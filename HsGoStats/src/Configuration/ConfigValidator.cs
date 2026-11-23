using MySqlConnector;

namespace HsGoStats.Configuration;

public sealed record ConfigValidationResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

public static class ConfigValidator
{
    public static ConfigValidationResult Validate(HsGoStatsConfig config)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (config.Version != 1) errors.Add("ConfigVersion must be 1.");
        if (string.IsNullOrWhiteSpace(config.ServerId) || config.ServerId.Length > 64)
            errors.Add("ServerId must contain 1-64 characters.");
        if (config.Season.DurationDays is < 1 or > 3660)
            errors.Add("Season.DurationDays must be between 1 and 3660.");
        if (!config.Season.NameFormat.Contains("{number}", StringComparison.Ordinal))
            errors.Add("Season.NameFormat must contain {number}.");
        if (!DateTimeOffset.TryParse(config.Season.StartDate, out _))
            errors.Add("Season.StartDate must be an ISO-8601 date with an offset.");
        else if (!HasExplicitOffset(config.Season.StartDate))
            errors.Add("Season.StartDate must include Z or an explicit UTC offset.");
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(config.Season.Timezone); }
        catch { errors.Add($"Season.Timezone '{config.Season.Timezone}' is not available on this host."); }
        if (config.Season.SeasonOverrideEndDate is not null &&
            !DateTimeOffset.TryParse(config.Season.SeasonOverrideEndDate, out _))
            errors.Add("Season.SeasonOverrideEndDate must be null or a valid ISO-8601 date with an offset.");
        var blacklistSteamIds = new HashSet<ulong>();
        foreach (var value in config.Season.LeaderboardBlacklistSteamIds)
        {
            if (!ulong.TryParse(value, out var steamId) || steamId == 0)
                errors.Add($"Season.LeaderboardBlacklistSteamIds contains an invalid SteamID64: '{value}'.");
            else if (!blacklistSteamIds.Add(steamId))
                warnings.Add($"Season.LeaderboardBlacklistSteamIds contains duplicate SteamID64 '{value}'.");
        }

        if (string.IsNullOrWhiteSpace(config.Database.Host)) errors.Add("Database.Host is required.");
        if (string.IsNullOrWhiteSpace(config.Database.Name)) errors.Add("Database.Name is required.");
        if (config.Database.Username is "" or "CHANGE_ME") errors.Add("Database.Username must be configured.");
        if (config.Database.Password is "" or "CHANGE_ME") errors.Add("Database.Password must be configured.");
        if (!Enum.TryParse<MySqlSslMode>(config.Database.SslMode, true, out _))
            errors.Add("Database.SslMode is not supported by MySqlConnector.");
        if (config.Database.ConnectionTimeoutSeconds is < 1 or > 120)
            errors.Add("Database.ConnectionTimeoutSeconds must be between 1 and 120.");

        if (config.Persistence.BatchIntervalSeconds is < 1 or > 300) errors.Add("Persistence.BatchIntervalSeconds must be between 1 and 300.");
        if (config.Persistence.MaxBatchSize is < 1 or > 5000) errors.Add("Persistence.MaxBatchSize must be between 1 and 5000.");
        if (config.Persistence.MaxQueuedEvents is < 100 or > 1_000_000) errors.Add("Persistence.MaxQueuedEvents must be between 100 and 1000000.");
        if (config.Persistence.RetryMaxAttempts is < 1 or > 20) errors.Add("Persistence.RetryMaxAttempts must be between 1 and 20.");
        if (config.Scoring.BombDropPenaltyMaxPerRound is < 0 or > 10) errors.Add("Scoring.BombDropPenaltyMaxPerRound must be between 0 and 10.");

        if (config.Bans.AutoImportFromExistingAdminPlugin) errors.Add("Bans.AutoImportFromExistingAdminPlugin is not supported in 1.0.0.");
        if (config.Bans.TrackNativeBanEventsIfVerified) errors.Add("Bans.TrackNativeBanEventsIfVerified is not supported in 1.0.0.");
        if (config.Bans.ExposeBanEnforcement) errors.Add("Bans.ExposeBanEnforcement is not supported; this collector never enforces bans.");
        if (!config.Tracking.ExcludeSuicidesFromKills) warnings.Add("Tracking.ExcludeSuicidesFromKills=false is ignored: suicides are never credited as kills.");
        if (config.Scoring.FriendlyFirePenalty != 0) warnings.Add("Scoring.FriendlyFirePenalty is not applied in 1.0.0; team kills never earn kill points.");
        if (config.Scoring.BombDrop != 0 || config.Scoring.BombPickup != 0) warnings.Add("Manual C4 drop/pickup scoring is disabled because the available game events cannot reliably distinguish manual and automatic drops.");
        if (!config.Persistence.PersistPendingEventsToDisk) warnings.Add("Pending events can be lost on process crash because the durable spool is disabled.");
        if (config.Database.SslMode.Equals("None", StringComparison.OrdinalIgnoreCase)) warnings.Add("Database TLS is disabled. Confirm that the provider network is private and trusted.");

        return new(errors, warnings);
    }

    private static bool HasExplicitOffset(string value)
    {
        value = value.Trim();
        return value.EndsWith('Z') ||
               (value.Length >= 6 && (value[^6] == '+' || value[^6] == '-') && value[^3] == ':');
    }
}
