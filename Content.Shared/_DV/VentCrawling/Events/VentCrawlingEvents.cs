using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._DV.VentCrawling.Events;

/// <summary>
/// Raised when an entity attempts to enter vent crawling.
/// </summary>
[ByRefEvent]
public record struct VentEnterAttemptEvent(EntityUid User, EntityUid Vent)
{
    public bool Cancelled;
}

/// <summary>
/// Completes transition into vent crawling.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class VentEnterDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
/// Completes transition out of vent crawling.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class VentExitDoAfterEvent : SimpleDoAfterEvent;
