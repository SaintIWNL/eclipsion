namespace Content.Shared._Rat.Cloning;

/// <summary>
/// Raised on a freshly spawned clone body so systems can tag derived entities (e.g. organs).
/// </summary>
[ByRefEvent]
public record struct CloneBodySpawnedForOrganMarkingEvent;
