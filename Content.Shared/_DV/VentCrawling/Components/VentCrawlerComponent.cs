using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._DV.VentCrawling.Components;

/// <summary>
/// Marks an entity as capable of entering the vent crawling state.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VentCrawlerComponent : Component
{
    /// <summary>
    /// Action prototype used to exit vent crawling.
    /// </summary>
    [DataField]
    public EntProtoId ExitAction = "ActionVentCrawlExit";

    /// <summary>
    /// Runtime entity for the exit action.
    /// </summary>
    [DataField]
    public EntityUid? ExitActionEntity;

    /// <summary>
    /// Delay before entering vents.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan EnterDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Delay before exiting vents.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan ExitDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Whether the entity should be forced down while vent crawling is active.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool ForceDownOnEnter = true;

    /// <summary>
    /// Whether the entity should stand back up after vent crawling ends.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool ForceStandOnExit = true;
}
