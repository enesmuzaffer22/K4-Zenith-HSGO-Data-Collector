using System.Text.Json;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace HsGoStats.Configuration;

public sealed class HsGoStatsConfig : BasePluginConfig
{
    public string ServerId { get; set; } = "hsgo-public-1";
    public DatabaseSettingsStoreConfig SettingsStore { get; set; } = new();
    public DatabaseConfig Database { get; set; } = new();
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? LegacyRuntimeSettings { get; set; }

    public HsGoStatsRuntimeSettings CreateRuntimeSeed()
    {
        var result = new HsGoStatsRuntimeSettings();
        if (LegacyRuntimeSettings is null) return result;
        result.Enabled = ReadLegacy("Enabled", result.Enabled);
        result.Season = ReadLegacy("Season", result.Season);
        result.Scoring = ReadLegacy("Scoring", result.Scoring);
        result.GameXp = ReadLegacy("GameXp", result.GameXp);
        result.Rating = ReadLegacy("Rating", result.Rating);
        result.WeeklyMissions = ReadLegacy("WeeklyMissions", result.WeeklyMissions);
        result.ChatCommands = ReadLegacy("ChatCommands", result.ChatCommands);
        result.Tracking = ReadLegacy("Tracking", result.Tracking);
        result.Bans = ReadLegacy("Bans", result.Bans);
        result.Persistence = ReadLegacy("Persistence", result.Persistence);
        result.Logging = ReadLegacy("Logging", result.Logging);
        return result;
    }

    private T ReadLegacy<T>(string key, T fallback)
    {
        var row = LegacyRuntimeSettings!.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(row.Key)) return fallback;
        return row.Value.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Legacy JSON section '{key}' cannot be null.");
    }
}

public sealed class DatabaseSettingsStoreConfig
{
    public bool SeedMissingRows { get; set; } = true;
}

public sealed class HsGoStatsRuntimeSettings
{
    public bool Enabled { get; set; } = true;
    public SeasonConfig Season { get; set; } = new();
    public ScoringConfig Scoring { get; set; } = new();
    public GameXpConfig GameXp { get; set; } = new();
    public RatingConfig Rating { get; set; } = new();
    public WeeklyMissionsConfig WeeklyMissions { get; set; } = new();
    public ChatCommandsConfig ChatCommands { get; set; } = new();
    public TrackingConfig Tracking { get; set; } = new();
    public BansConfig Bans { get; set; } = new();
    public PersistenceConfig Persistence { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();
}

public sealed class SeasonConfig
{
    public bool Enabled { get; set; } = true;
    public string Timezone { get; set; } = "Europe/Istanbul";
    public bool PreseasonTracking { get; set; } = true;
    public List<SeasonScheduleEntryConfig> Schedule { get; set; } =
    [
        new("Season 1", "2027-12-01T00:00:00+03:00", "2027-12-31T00:00:00+03:00")
    ];
    public List<string> LeaderboardBlacklistSteamIds { get; set; } = [];
}

public sealed record SeasonScheduleEntryConfig(string Name, string StartsAt, string EndsAt);

public sealed class ScoringConfig
{
    public int Kill { get; set; } = 10;
    public int HeadshotBonus { get; set; } = 5;
    public int Assist { get; set; } = 4;
    public int Death { get; set; } = -3;
    public int BombPlant { get; set; } = 5;
    public int BombDefuse { get; set; } = 10;
    public int RoundWin { get; set; } = 3;
    public int BombDrop { get; set; }
    public int BombPickup { get; set; }
    public int Suicide { get; set; } = -10;
    public int FriendlyFirePenalty { get; set; }
    public int BombDropPenaltyMaxPerRound { get; set; }
}

public sealed class GameXpConfig
{
    public int NormalKill { get; set; } = 5;
    public int HeadshotKill { get; set; } = 7;
    public int Assist { get; set; } = 2;
    public int KnifeKill { get; set; } = 20;
    public int NoScopeKill { get; set; } = 10;
    public int WallbangKill { get; set; } = 15;
    public int ThroughSmokeKill { get; set; } = 10;
    public int AirborneKill { get; set; } = 25;
    public int BlindKill { get; set; } = 10;
    public int ZeusKill { get; set; } = 20;
    public int HeKill { get; set; } = 5;
    public int MolotovKill { get; set; } = 5;
    public int BombPlant { get; set; } = 5;
    public int BombDefuse { get; set; } = 10;
    public int BombExplode { get; set; } = 10;
    public int Death { get; set; } = -1;
    public int Suicide { get; set; } = -10;
}

public sealed class RatingConfig
{
    public bool Enabled { get; set; } = true;
    public int StartingXP { get; set; } = 200;
    public int MinimumXP { get; set; }
    public bool AllowDemotion { get; set; } = true;
    public List<RatingTierConfig> Tiers { get; set; } =
    [
        new(1, "Iron", 0, "#9CA3AF"),
        new(2, "Bronze", 1_000, "#CD8A50"),
        new(3, "Silver", 3_000, "#E2E8F0"),
        new(4, "Gold", 7_000, "#EAB308"),
        new(5, "Platinum", 12_500, "#22D3EE"),
        new(6, "Diamond", 20_000, "#6366F1"),
        new(7, "Master", 35_000, "#D946EF"),
        new(8, "Legend", 60_000, "#EF4444")
    ];
}

public sealed record RatingTierConfig(int Id, string Name, long MinXP, string Color);

public sealed class WeeklyMissionsConfig
{
    public bool Enabled { get; set; } = true;
    public WeeklyRotationConfig Rotation { get; set; } = new();
    public MissionRewardsConfig Rewards { get; set; } = new();
    public List<string> ExcludedMissionWeapons { get; set; } =
    [
        "m249", "negev", "xm1014", "nova", "mag7", "sawedoff",
        "g3sg1", "scar20", "p90", "bizon"
    ];
    public bool ExcludeBots { get; set; } = true;
    public List<MissionDefinition> Catalog { get; set; } = MissionCatalogDefaults.Create();
}

public sealed class WeeklyRotationConfig
{
    public string Timezone { get; set; } = "Europe/Istanbul";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Monday;
    public string Time { get; set; } = "00:00";
    public int TasksPerCategory { get; set; } = 1;
    public bool AllowRepeatAcrossWeeks { get; set; } = true;
}

public sealed class MissionRewardsConfig
{
    public bool GrantGeneralXP { get; set; } = true;
    public bool GrantActiveSeasonXP { get; set; } = true;
}

public sealed class ChatCommandsConfig
{
    public ChatCommandConfig Season { get; set; } = new(true, ["!sezon"]);
    public ChatCommandConfig Missions { get; set; } = new(true, ["!gorevler"]);
    public RoundStartReminderConfig RoundStartReminder { get; set; } = new();
    public bool ReplyPrivately { get; set; } = true;
}

public sealed class ChatCommandConfig(bool enabled, List<string> aliases)
{
    public bool Enabled { get; set; } = enabled;
    public List<string> Aliases { get; set; } = aliases;
}

public sealed class RoundStartReminderConfig
{
    public bool Enabled { get; set; } = true;
    public string Message { get; set; } = "!gorevler yazarak haftalık görevlerinizi takip edebilirsiniz.";
    public bool IncludeWarmup { get; set; }
}

public sealed class MissionDefinition
{
    public int Id { get; set; }
    public int Category { get; set; }
    public string Event { get; set; } = "Kill";
    public string Description { get; set; } = "";
    public int Target { get; set; }
    public int RewardXp { get; set; }
    public List<string> Weapons { get; set; } = [];
    public bool Headshot { get; set; }
    public bool NoScope { get; set; }
    public bool Scoped { get; set; }
    public bool ThroughSmoke { get; set; }
    public bool Penetrated { get; set; }
    public bool AttackerBlind { get; set; }
    public bool AttackerInAir { get; set; }
    public bool Knife { get; set; }
    public int MinimumRoundKills { get; set; }
    public int MinimumRoundAssists { get; set; }
    public string? Team { get; set; }
}

public sealed class DatabaseConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public uint Port { get; set; } = 3306;
    public string Name { get; set; } = "hsgo_stats";
    public string Username { get; set; } = "CHANGE_ME";
    public string Password { get; set; } = "CHANGE_ME";
    public string SslMode { get; set; } = "Preferred";
    public int ConnectionTimeoutSeconds { get; set; } = 5;
}

public sealed class TrackingConfig
{
    public bool Kills { get; set; } = true;
    public bool Deaths { get; set; } = true;
    public bool Assists { get; set; } = true;
    public bool Headshots { get; set; } = true;
    public bool Damage { get; set; } = true;
    public bool ShotsAndHits { get; set; }
    public bool Rounds { get; set; } = true;
    public bool WinsAndLosses { get; set; } = true;
    public bool Playtime { get; set; } = true;
    public bool PerMap { get; set; } = true;
    public bool PerWeapon { get; set; } = true;
    public bool ExcludeBots { get; set; } = true;
    public bool ExcludeWarmup { get; set; } = true;
    public bool ExcludeTeamKills { get; set; } = true;
    public bool ExcludeSuicidesFromKills { get; set; } = true;
}

public sealed class BansConfig
{
    public bool Enabled { get; set; } = true;
    public bool AutoImportFromExistingAdminPlugin { get; set; }
    public bool AllowManualSqlImport { get; set; } = true;
    public bool TrackNativeBanEventsIfVerified { get; set; }
    public bool ExposeBanEnforcement { get; set; }
}

public sealed class PersistenceConfig
{
    public int BatchIntervalSeconds { get; set; } = 10;
    public int MaxBatchSize { get; set; } = 100;
    public int MaxQueuedEvents { get; set; } = 5000;
    public int RetryMaxAttempts { get; set; } = 5;
    public int RetryInitialDelaySeconds { get; set; } = 2;
    public bool PersistPendingEventsToDisk { get; set; } = true;
}

public sealed class LoggingConfig
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public HsGoLogLevel Level { get; set; } = HsGoLogLevel.Information;
    public bool IncludePlayerNames { get; set; }
}

public enum HsGoLogLevel { Debug, Information, Warning, Error }
