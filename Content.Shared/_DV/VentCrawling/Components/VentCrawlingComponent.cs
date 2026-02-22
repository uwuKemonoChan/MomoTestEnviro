using Robust.Shared.GameStates;
using Content.Shared.Movement.Systems;

namespace Content.Shared._DV.VentCrawling.Components;

/// <summary>
/// Runtime state for entities currently vent crawling.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VentCrawlingComponent : Component
{
    /// <summary>
    /// The vent node we are currently in.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? CurrentNode;

    /// <summary>
    /// Snapshot of pre-crawl movement state for clean restoration.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool PreviousCanMove = true;

    /// <summary>
    /// Snapshot of whether the entity was standing before crawling began.
    /// Null means no standing component/state was resolved.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool? PreviousStanding;

    /// <summary>
    /// Set while deactivation cleanup is running to avoid re-entrancy.
    /// </summary>
    [DataField]
    public bool Deactivating;

    /// <summary>
    /// The last directional buttons snapshot used to gate single-step traversal.
    /// </summary>
    [DataField]
    public MoveButtons LastButtons;
}
