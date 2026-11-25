using System.Globalization;
using HsGoStats.Configuration;

namespace HsGoStats.Domain;

public sealed record WeeklyWindow(DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, string Timezone);

public static class WeeklyWindowCalculator
{
    public static WeeklyWindow At(DateTimeOffset instant, WeeklyRotationConfig config)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(config.Timezone);
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        if (!TimeOnly.TryParseExact(config.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var rotationTime))
            throw new InvalidOperationException("Weekly mission rotation time must use HH:mm.");

        var daysBack = ((int)local.DayOfWeek - (int)config.DayOfWeek + 7) % 7;
        var startDate = local.Date.AddDays(-daysBack);
        var startLocal = startDate.Add(rotationTime.ToTimeSpan());
        if (local.DateTime < startLocal) startLocal = startLocal.AddDays(-7);
        var endLocal = startLocal.AddDays(7);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified), zone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified), zone);
        return new(new DateTimeOffset(startUtc), new DateTimeOffset(endUtc), config.Timezone);
    }

    public static WeeklyWindow At(DateTimeOffset instant, WeeklyRotationConfig config, DateTimeOffset seasonStart, DateTimeOffset seasonEnd)
    {
        if (instant < seasonStart || instant >= seasonEnd) throw new ArgumentOutOfRangeException(nameof(instant), "Instant is outside the active season.");
        var weekly = At(instant, config);
        var start = weekly.StartsAtUtc < seasonStart ? seasonStart : weekly.StartsAtUtc;
        var end = weekly.EndsAtUtc > seasonEnd ? seasonEnd : weekly.EndsAtUtc;
        return new(start, end, weekly.Timezone);
    }
}

public sealed record MissionSignal(
    Guid EventId,
    DateTimeOffset ObservedAt,
    PlayerIdentity Player,
    string Event,
    long? ActiveSeasonId,
    bool HasActiveSeason,
    bool GrantSeason,
    DateTimeOffset? ActiveSeasonStartsAt = null,
    DateTimeOffset? ActiveSeasonEndsAt = null,
    long HsrDelta = 0,
    int Amount = 1,
    string? Weapon = null,
    bool Headshot = false,
    bool NoScope = false,
    bool ThroughSmoke = false,
    bool Penetrated = false,
    bool AttackerBlind = false,
    bool AttackerInAir = false,
    bool Won = false,
    bool Alive = false,
    bool Planted = false,
    bool Defused = false,
    bool Clutch = false,
    string? Team = null,
    int RoundKills = 0,
    int RoundHeadshotKills = 0,
    int RoundAssists = 0,
    int RoundFlashBlinds = 0,
    IReadOnlyDictionary<string, int>? RoundWeaponKills = null);

public sealed record MissionProgressView(int MissionId, int Category, string Description, int Progress, int Target, int RewardXp, bool Completed, DateTimeOffset EndsAtUtc);
public sealed record SeasonCommandView(string Name, int Number, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc,
    long GlobalTp, long SeasonTp, long? Rank, long RankedPlayers);

public sealed record MissionProgressDelta(bool Matched, int Amount, bool Replace);

public static class MissionEvaluator
{
    public static MissionProgressDelta Evaluate(MissionDefinition definition, MissionSignal signal)
    {
        if (signal.Event == "RoundEnd")
        {
            return definition.Event switch
            {
                "RoundKills" => new(true, signal.RoundKills, true),
                "RoundHeadshotKills" => new(true, signal.RoundHeadshotKills, true),
                "RoundWeaponKills" => new(true, SumWeapons(definition, signal), true),
                "RoundKillThreshold" => new(SumWeapons(definition, signal) >= definition.MinimumRoundKills, 1, false),
                "RoundFlashBlinds" => new(true, signal.RoundFlashBlinds, true),
                "RoundPlantWin" => new(signal.Won && signal.Planted, 1, false),
                "RoundDefuseWin" => new(signal.Won && signal.Defused, 1, false),
                "RoundWin" => new(signal.Won && TeamMatches(definition, signal), 1, false),
                "RoundWinAlive" => new(signal.Won && signal.Alive, 1, false),
                "RoundWinKills" => new(signal.Won && signal.RoundKills >= definition.MinimumRoundKills, 1, false),
                "RoundWinAssists" => new(signal.Won && signal.RoundAssists >= definition.MinimumRoundAssists, 1, false),
                "ClutchWin" => new(signal.Won && signal.Clutch, 1, false),
                _ => new(false, 0, false)
            };
        }
        if (!definition.Event.Equals(signal.Event, StringComparison.Ordinal)) return new(false, 0, false);
        if (definition.Weapons.Count > 0 && (signal.Weapon is null || !definition.Weapons.Contains(signal.Weapon, StringComparer.OrdinalIgnoreCase))) return new(false, 0, false);
        if (definition.Headshot && !signal.Headshot || definition.NoScope && !signal.NoScope || definition.Scoped && signal.NoScope ||
            definition.ThroughSmoke && !signal.ThroughSmoke || definition.Penetrated && !signal.Penetrated ||
            definition.AttackerBlind && !signal.AttackerBlind || definition.AttackerInAir && !signal.AttackerInAir ||
            definition.Knife && !WeaponRules.IsKnife(signal.Weapon)) return new(false, 0, false);
        return new(true, signal.Amount, false);
    }

    private static int SumWeapons(MissionDefinition definition, MissionSignal signal) => definition.Weapons.Count == 0
        ? signal.RoundKills
        : definition.Weapons.Sum(w => signal.RoundWeaponKills?.GetValueOrDefault(w) ?? 0);
    private static bool TeamMatches(MissionDefinition definition, MissionSignal signal) => definition.Team is null || definition.Team.Equals(signal.Team, StringComparison.OrdinalIgnoreCase);
}
