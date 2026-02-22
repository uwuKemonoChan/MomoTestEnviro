using Content.Shared._DV.VentCrawling.Components;
using Content.Shared._DV.VentCrawling.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Standing;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Shared._DV.VentCrawling.Systems;

/// <summary>
/// Shared lifecycle management for vent crawling state.
/// Traversal is intentionally not implemented in this scaffold.
/// </summary>
public sealed class SharedVentCrawlingSystem : EntitySystem
{
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VentCrawlingComponent, ComponentShutdown>(OnVentCrawlingShutdown);
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

        var target = Transform(node.Owner).Coordinates.Offset(offset);
        var entities = _lookup.GetEntitiesInRange(target, 0.2f);

        foreach (var entity in entities)
        {
            if (entity == node.Owner)
                continue;

            if (!TryComp<VentCrawlableComponent>(entity, out var comp))
                continue;

            // Must allow entry from opposite direction.
            if ((comp.Connections & direction.GetOpposite()) == 0)
                continue;

            adjacentUid = entity;
            adjacentComp = comp;
            return true;
        }

        return false;
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
