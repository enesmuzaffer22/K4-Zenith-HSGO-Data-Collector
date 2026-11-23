namespace HsGoStats.Domain;

public sealed record PlayerIdentity(ulong SteamId64, string? Name);

public sealed record StatDelta(
    long Kills = 0,
    long Deaths = 0,
    long Assists = 0,
    long HeadshotKills = 0,
    long Damage = 0,
    long Score = 0,
    long RoundsPlayed = 0,
    long RoundsWon = 0,
    long RoundsLost = 0,
    long PlaytimeSeconds = 0,
    long Shots = 0,
    long Hits = 0,
    long BombPlants = 0,
    long BombDefuses = 0)
{
    public bool IsEmpty => this == new StatDelta();
}

public sealed record NormalizedStatEvent(
    Guid EventId,
    string EventType,
    DateTimeOffset ObservedAt,
    string ServerId,
    PlayerIdentity Player,
    long? SeasonId,
    bool ApplyGlobal,
    bool ApplySeason,
    string ScoringVersion,
    string? MapName,
    string? WeaponName,
    StatDelta Delta);

public sealed record TrackingTargets(bool ApplyGlobal, bool ApplySeason)
{
    public bool Any => ApplyGlobal || ApplySeason;

    public static TrackingTargets Resolve(bool seasonEnabled, bool preseasonTracking, bool hasActiveSeason, bool excludeFromSeason = false)
    {
        if (seasonEnabled && hasActiveSeason) return new(true, !excludeFromSeason);
        return preseasonTracking ? new(true, false) : new(false, false);
    }
}
