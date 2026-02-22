using Robust.Shared.GameStates;
using Robust.Shared.Maths;
using Robust.Shared.ViewVariables;

namespace Content.Shared._DV.VentCrawling.Components;

/// <summary>
/// Marks a vent/pipe node as crawlable and defines directional connectivity.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VentCrawlableComponent : Component
{
    /// <summary>
    /// Container that stores entities while traversing this node.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string ContainerId = "VentCrawlingContainer";

    /// <summary>
    /// Runtime-generated directions that can be traversed from this node.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public Direction Connections = Direction.Invalid;
}
