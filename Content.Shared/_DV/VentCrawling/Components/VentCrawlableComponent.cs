using Robust.Shared.GameStates;
using Robust.Shared.Maths;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

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
    /// Directions that can be traversed from this node.
    /// </summary>
    [DataField(customTypeSerializer: typeof(FlagSerializer<Direction>)), AutoNetworkedField]
    public Direction Connections = Direction.Invalid;
}
