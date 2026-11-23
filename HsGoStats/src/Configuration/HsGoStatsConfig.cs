using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace HsGoStats.Configuration;

public sealed class HsGoStatsConfig : BasePluginConfig
{
    public bool Enabled { get; set; } = true;
    public string ServerId { get; set; } = "hsgo-public-1";
    public SeasonConfig Season { get; set; } = new();
    public ScoringConfig Scoring { get; set; } = new();
    public DatabaseConfig Database { get; set; } = new();
    public TrackingConfig Tracking { get; set; } = new();
    public BansConfig Bans { get; set; } = new();
    public PersistenceConfig Persistence { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();
}

public sealed class SeasonConfig
{
    public bool Enabled { get; set; } = true;
    public bool AutoTransition { get; set; } = true;
    public int DurationDays { get; set; } = 30;
    public string NameFormat { get; set; } = "Season {number}";
    public int InitialSeasonNumber { get; set; } = 1;
    public string StartDate { get; set; } = "2026-12-01T00:00:00+03:00";
    public string Timezone { get; set; } = "Europe/Istanbul";
    public bool PreseasonTracking { get; set; } = true;
    public string? SeasonOverrideEndDate { get; set; }
    public List<string> LeaderboardBlacklistSteamIds { get; set; } = [];
}

public sealed class ScoringConfig
{
    public int Kill { get; set; } = 10;
    public int HeadshotBonus { get; set; } = 5;
    public int Assist { get; set; } = 4;
    public int Death { get; set; } = -3;
    public int BombPlant { get; set; } = 5;
    public int BombDefuse { get; set; } = 10;
    public int RoundWin { get; set; } = 3;
    public int BombDrop { get; set; } = -3;
    public int BombPickup { get; set; }
    public int Suicide { get; set; } = -10;
    public int FriendlyFirePenalty { get; set; }
    public int BombDropPenaltyMaxPerRound { get; set; } = 1;
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
