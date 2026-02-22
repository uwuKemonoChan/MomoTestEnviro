using Content.Shared._DV.VentCrawling.Components;
using Content.Shared._DV.VentCrawling.Events;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Movement.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Standing;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Shared._DV.VentCrawling.Systems;

/// <summary>
/// Shared lifecycle management for vent crawling state.
/// Includes enter/exit workflow and single-step adjacent traversal.
/// </summary>
public sealed class SharedVentCrawlingSystem : EntitySystem
{
    private static readonly Direction[] CardinalDirections =
    [
        Direction.North,
        Direction.East,
        Direction.South,
        Direction.West
    ];

    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VentCrawlerComponent, MapInitEvent>(OnCrawlerMapInit);
        SubscribeLocalEvent<VentCrawlableComponent, MapInitEvent>(OnNodeMapInit);
        SubscribeLocalEvent<VentCrawlableComponent, AnchorStateChangedEvent>(OnNodeAnchorChanged);
        SubscribeLocalEvent<VentCrawlableComponent, ComponentShutdown>(OnNodeShutdown);
        SubscribeLocalEvent<VentCrawlableComponent, InteractHandEvent>(OnVentInteractHand);
        SubscribeLocalEvent<VentCrawlerComponent, VentExitActionEvent>(OnVentExitAction);
        SubscribeLocalEvent<VentCrawlableComponent, VentEnterDoAfterEvent>(OnVentEnterDoAfter);
        SubscribeLocalEvent<VentCrawlerComponent, VentExitDoAfterEvent>(OnVentExitDoAfter);
        SubscribeLocalEvent<VentCrawlingComponent, ComponentShutdown>(OnVentCrawlingShutdown);
    }

    private void OnCrawlerMapInit(Entity<VentCrawlerComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.ExitActionEntity != null)
            return;

        _actions.AddAction(ent, ref ent.Comp.ExitActionEntity, ent.Comp.ExitAction);
        _actions.SetEnabled(ent.Comp.ExitActionEntity, false);
    }

    private void OnNodeMapInit(Entity<VentCrawlableComponent> ent, ref MapInitEvent args)
    {
        RefreshConnectionsAround(ent);
    }

    private void OnNodeAnchorChanged(Entity<VentCrawlableComponent> ent, ref AnchorStateChangedEvent args)
    {
        RefreshConnectionsAround(ent);
    }

    private void OnNodeShutdown(Entity<VentCrawlableComponent> ent, ref ComponentShutdown args)
    {
        RefreshConnectionsAround(ent);
    }

    private void OnVentInteractHand(Entity<VentCrawlableComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<VentCrawlerComponent>(args.User, out var crawler))
            return;

        if (HasComp<VentCrawlingComponent>(args.User))
            return;

        var doAfter = new DoAfterArgs(EntityManager,
            args.User,
            crawler.EnterDelay,
            new VentEnterDoAfterEvent(),
            ent,
            target: ent,
            used: args.User)
        {
            BreakOnMove = true,
            NeedHand = true
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            args.Handled = true;
    }

    private void OnVentEnterDoAfter(Entity<VentCrawlableComponent> ent, ref VentEnterDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        if (!TryActivate(args.Args.User, ent))
            return;

        args.Handled = true;
    }

    private void OnVentExitAction(Entity<VentCrawlerComponent> ent, ref VentExitActionEvent args)
    {
        if (args.Handled || !HasComp<VentCrawlingComponent>(ent))
            return;

        var doAfter = new DoAfterArgs(EntityManager,
            ent,
            ent.Comp.ExitDelay,
            new VentExitDoAfterEvent(),
            ent,
            target: ent,
            used: ent)
        {
            BreakOnMove = true,
            NeedHand = false
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            args.Handled = true;
    }

    private void OnVentExitDoAfter(Entity<VentCrawlerComponent> ent, ref VentExitDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        if (TryDeactivate(ent, ent.Comp))
            args.Handled = true;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<VentCrawlingComponent, InputMoverComponent>();

        while (query.MoveNext(out var uid, out var crawling, out var mover))
        {
            var directional = mover.HeldMoveButtons & MoveButtons.AnyDirection;

            if (directional == MoveButtons.None)
            {
                crawling.LastButtons = MoveButtons.None;
                continue;
            }

            if (directional == crawling.LastButtons)
                continue;

            crawling.LastButtons = directional;

            if (crawling.CurrentNode is not { } node)
                continue;

            if (!TryComp<VentCrawlableComponent>(node, out var nodeComp))
                continue;

            if (!TryGetDirection(directional, out var stepDirection))
                continue;

            if ((nodeComp.Connections & stepDirection) == 0)
                continue;

            if (TryGetAdjacentNode((node, nodeComp), stepDirection, out var nextNode, out var nextComp))
            {
                TransferNode(uid, crawling, (node, nodeComp), (nextNode, nextComp));
            }
        }
    }

    /// <summary>
    /// Activates vent crawling state and snapshots required state for restoration.
    /// </summary>
    public bool TryActivate(EntityUid uid, EntityUid node, VentCrawlerComponent? crawler = null, InputMoverComponent? mover = null)
    {
        if (!Resolve(uid, ref crawler, false))
            return false;

        if (HasComp<VentCrawlingComponent>(uid))
            return true;

        if (!TryComp<VentCrawlableComponent>(node, out var nodeComp))
            return false;

        var attempt = new VentEnterAttemptEvent(uid, node);
        RaiseLocalEvent(uid, ref attempt);
        if (attempt.Cancelled)
            return false;

        Resolve(uid, ref mover, false);

        var comp = EnsureComp<VentCrawlingComponent>(uid);
        comp.CurrentNode = node;
        comp.LastButtons = MoveButtons.None;

        var nodeContainer = _container.EnsureContainer<Container>(node, nodeComp.ContainerId);
        if (!_container.Insert(uid, nodeContainer))
            return false;

        if (mover != null)
        {
            comp.PreviousCanMove = mover.CanMove;
            mover.CanMove = false;
            Dirty(uid, mover);
        }

        _actions.SetEnabled(crawler.ExitActionEntity, true);
        _actions.SetToggled(crawler.ExitActionEntity, true);

        if (crawler.ForceDownOnEnter && TryComp<StandingStateComponent>(uid, out var standing))
        {
            comp.PreviousStanding = standing.Standing;
            _standing.Down(uid, standingState: standing);
        }

        Dirty(uid, comp);
        return true;
    }

    /// <summary>
    /// Deactivates vent crawling state and restores captured movement/standing state.
    /// </summary>
    public bool TryDeactivate(EntityUid uid, VentCrawlerComponent? crawler = null, VentCrawlingComponent? ventCrawling = null)
    {
        if (!Resolve(uid, ref ventCrawling, false))
            return false;

        if (ventCrawling.Deactivating)
            return false;

        Resolve(uid, ref crawler, false);

        RestoreState(uid, ventCrawling, crawler);
        RemCompDeferred<VentCrawlingComponent>(uid);
        return true;
    }

    private void OnVentCrawlingShutdown(Entity<VentCrawlingComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent.Owner) || ent.Comp.Deactivating)
            return;

        RestoreState(ent.Owner, ent.Comp, CompOrNull<VentCrawlerComponent>(ent.Owner));
    }

    private void RestoreState(EntityUid uid, VentCrawlingComponent ventCrawling, VentCrawlerComponent? crawler)
    {
        ventCrawling.Deactivating = true;

        if (ventCrawling.CurrentNode is { } node &&
            TryComp<VentCrawlableComponent>(node, out var nodeComp) &&
            _container.TryGetContainer(node, nodeComp.ContainerId, out var container))
        {
            _container.Remove(uid, container, reparent: true, force: true);
        }

        if (TryComp<InputMoverComponent>(uid, out var mover))
        {
            mover.CanMove = ventCrawling.PreviousCanMove;
            Dirty(uid, mover);
        }

        if (crawler?.ForceStandOnExit == true && ventCrawling.PreviousStanding == true)
        {
            _standing.Stand(uid, force: true);
        }

        if (crawler != null)
        {
            _actions.SetEnabled(crawler.ExitActionEntity, false);
            _actions.SetToggled(crawler.ExitActionEntity, false);
        }

        ventCrawling.CurrentNode = null;
        ventCrawling.LastButtons = MoveButtons.None;
        Dirty(uid, ventCrawling);
    }

    private bool TryGetDirection(MoveButtons buttons, out Direction direction)
    {
        // Prefer cardinal movement if multiple directions are held.
        if ((buttons & MoveButtons.Up) != 0)
        {
            direction = Direction.North;
            return true;
        }

        if ((buttons & MoveButtons.Down) != 0)
        {
            direction = Direction.South;
            return true;
        }

        if ((buttons & MoveButtons.Left) != 0)
        {
            direction = Direction.West;
            return true;
        }

        if ((buttons & MoveButtons.Right) != 0)
        {
            direction = Direction.East;
            return true;
        }

        direction = Direction.Invalid;
        return false;
    }

    private bool TryGetAdjacentNode(Entity<VentCrawlableComponent> node, Direction direction, out EntityUid adjacentUid, out VentCrawlableComponent adjacentComp)
    {
        adjacentUid = default;
        adjacentComp = default!;

        var offset = direction.ToVec();
        if (offset == Vector2i.Zero)
            return false;

        if (!TryGetAdjacentNodeRaw(node.Owner, direction, out adjacentUid, out adjacentComp))
            return false;

        // Must allow entry from opposite direction.
        return (adjacentComp.Connections & direction.GetOpposite()) != 0;
    }

    private void RefreshConnectionsAround(Entity<VentCrawlableComponent> center)
    {
        RecalculateConnections(center);

        foreach (var direction in CardinalDirections)
        {
            if (!TryGetAdjacentNodeRaw(center.Owner, direction, out var adjacentUid, out var adjacentComp))
                continue;

            RecalculateConnections((adjacentUid, adjacentComp));
        }
    }

    private bool TryGetAdjacentNodeRaw(EntityUid uid, Direction direction, out EntityUid adjacentUid, out VentCrawlableComponent adjacentComp)
    {
        adjacentUid = default;
        adjacentComp = default!;

        var target = Transform(uid).Coordinates.Offset(direction.ToVec());
        var entities = _lookup.GetEntitiesInRange(target, 0.2f);

        foreach (var entity in entities)
        {
            if (entity == uid)
                continue;

            if (!TryComp<VentCrawlableComponent>(entity, out var comp))
                continue;

            adjacentUid = entity;
            adjacentComp = comp;
            return true;
        }

        return false;
    }

    private void RecalculateConnections(Entity<VentCrawlableComponent> node)
    {
        if (!Transform(node).Anchored)
        {
            if (node.Comp.Connections != Direction.Invalid)
            {
                node.Comp.Connections = Direction.Invalid;
                Dirty(node);
            }

            return;
        }

        var nextConnections = Direction.Invalid;

        foreach (var direction in CardinalDirections)
        {
            var target = Transform(node.Owner).Coordinates.Offset(direction.ToVec());
            var entities = _lookup.GetEntitiesInRange(target, 0.2f);

            foreach (var entity in entities)
            {
                if (entity == node.Owner)
                    continue;

                if (!TryComp<VentCrawlableComponent>(entity, out var other) || !Transform(entity).Anchored)
                    continue;

                nextConnections |= direction;
                break;
            }
        }

        if (node.Comp.Connections == nextConnections)
            return;

        node.Comp.Connections = nextConnections;
        Dirty(node);
    }

    private void TransferNode(EntityUid uid,
        VentCrawlingComponent crawling,
        Entity<VentCrawlableComponent> from,
        Entity<VentCrawlableComponent> to)
    {
        var fromContainer = _container.EnsureContainer<Container>(from, from.Comp.ContainerId);
        var toContainer = _container.EnsureContainer<Container>(to, to.Comp.ContainerId);

        _container.Remove(uid, fromContainer, reparent: false, force: true);
        if (!_container.Insert(uid, toContainer))
        {
            _container.Insert(uid, fromContainer);
            return;
        }

        crawling.CurrentNode = to;
        Dirty(uid, crawling);
    }
}
