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
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(config.Season.Timezone); }
        catch { errors.Add($"Season.Timezone '{config.Season.Timezone}' is not available on this host."); }
        var seasonSchedule = config.Season.Schedule ?? [];
        if (config.Season.Schedule is null) errors.Add("Season.Schedule cannot be null.");
        if (config.Season.Enabled && seasonSchedule.Count == 0) warnings.Add("Season.Schedule is empty; no season or weekly mission window can become active.");
        var seasonNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DateTimeOffset? previousEnd = null;
        foreach (var (season, index) in seasonSchedule.Select((value, index) => (value, index)))
        {
            var label = $"Season.Schedule[{index}]";
            if (string.IsNullOrWhiteSpace(season.Name) || season.Name.Length > 128) errors.Add($"{label}.Name must contain 1-128 characters.");
            else if (!seasonNames.Add(season.Name.Trim())) errors.Add($"{label}.Name '{season.Name}' is duplicated.");
            if (!TryExplicitDate(season.StartsAt, out var starts)) errors.Add($"{label}.StartsAt must be ISO-8601 with Z or an explicit offset.");
            if (!TryExplicitDate(season.EndsAt, out var ends)) errors.Add($"{label}.EndsAt must be ISO-8601 with Z or an explicit offset.");
            if (starts is not null && ends is not null)
            {
                if (ends.Value <= starts.Value) errors.Add($"{label}.EndsAt must be after StartsAt.");
                if (previousEnd is not null && starts.Value < previousEnd.Value) errors.Add($"{label} overlaps the previous season or is not ordered chronologically.");
                previousEnd = ends.Value;
            }
        }
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
        var positiveGameXp = new[] { config.GameXp.NormalKill, config.GameXp.HeadshotKill, config.GameXp.Assist, config.GameXp.KnifeKill,
            config.GameXp.NoScopeKill, config.GameXp.WallbangKill, config.GameXp.ThroughSmokeKill, config.GameXp.AirborneKill,
            config.GameXp.BlindKill, config.GameXp.ZeusKill, config.GameXp.HeKill, config.GameXp.MolotovKill,
            config.GameXp.BombPlant, config.GameXp.BombDefuse, config.GameXp.BombExplode };
        if (positiveGameXp.Any(x => x < 0)) errors.Add("GameXp reward values cannot be negative.");
        if (config.GameXp.Death > 0 || config.GameXp.Suicide > 0) errors.Add("GameXp death and suicide values cannot be positive.");

        if (config.Rating.MinimumXP < 0) errors.Add("Rating.MinimumXP cannot be negative.");
        if (config.Rating.Tiers.Count == 0) errors.Add("Rating.Tiers must contain at least one tier.");
        else
        {
            var ordered = config.Rating.Tiers.OrderBy(x => x.MinXP).ToArray();
            if (ordered[0].MinXP != config.Rating.MinimumXP) errors.Add("The first rating tier must start at Rating.MinimumXP.");
            if (ordered.Select(x => x.Id).Distinct().Count() != ordered.Length) errors.Add("Rating tier IDs must be unique.");
            if (ordered.Select(x => x.MinXP).Distinct().Count() != ordered.Length) errors.Add("Rating tier thresholds must be unique.");
            if (!ordered.SequenceEqual(config.Rating.Tiers)) errors.Add("Rating tiers must be sorted by MinXP.");
            foreach (var tier in ordered)
            {
                if (string.IsNullOrWhiteSpace(tier.Name)) errors.Add($"Rating tier {tier.Id} requires a name.");
                if (!System.Text.RegularExpressions.Regex.IsMatch(tier.Color, "^#[0-9A-Fa-f]{6}$")) errors.Add($"Rating tier {tier.Id} has an invalid HEX color.");
            }
        }

        try { _ = TimeZoneInfo.FindSystemTimeZoneById(config.WeeklyMissions.Rotation.Timezone); }
        catch { errors.Add($"WeeklyMissions.Rotation.Timezone '{config.WeeklyMissions.Rotation.Timezone}' is not available on this host."); }
        if (!TimeOnly.TryParseExact(config.WeeklyMissions.Rotation.Time, "HH:mm", out _)) errors.Add("WeeklyMissions.Rotation.Time must use HH:mm.");
        if (config.WeeklyMissions.Rotation.TasksPerCategory != 1) errors.Add("WeeklyMissions.Rotation.TasksPerCategory must be 1 for the nine-category product contract.");
        var catalog = config.WeeklyMissions.Catalog;
        var allowedMissionEvents = new HashSet<string>(
            ["Kill", "Assist", "RoundKills", "RoundHeadshotKills", "RoundWeaponKills", "RoundKillThreshold",
             "GrenadeUsed", "FlashEnemyBlinded", "Damage", "RoundFlashBlinds", "BombPlant", "OwnBombExplode",
             "BombDefuse", "RoundPlantWin", "RoundDefuseWin", "RoundWin", "RoundWinAlive", "RoundWinKills",
             "RoundWinAssists", "ClutchWin"], StringComparer.Ordinal);
        if (catalog.Count != 100) errors.Add("WeeklyMissions.Catalog must contain exactly 100 missions.");
        if (catalog.Select(x => x.Id).Distinct().Count() != catalog.Count) errors.Add("Weekly mission IDs must be unique.");
        foreach (var mission in catalog)
        {
            if (mission.Id is < 1 or > 100) errors.Add($"Weekly mission ID {mission.Id} is outside 1-100.");
            if (mission.Category is < 1 or > 9) errors.Add($"Weekly mission {mission.Id} has an invalid category.");
            if (mission.Target <= 0) errors.Add($"Weekly mission {mission.Id} must have a positive target.");
            if (mission.RewardXp <= 0) errors.Add($"Weekly mission {mission.Id} must have a positive reward.");
            if (string.IsNullOrWhiteSpace(mission.Description) || string.IsNullOrWhiteSpace(mission.Event)) errors.Add($"Weekly mission {mission.Id} requires an event and description.");
            else if (!allowedMissionEvents.Contains(mission.Event)) errors.Add($"Weekly mission {mission.Id} has an unsupported event '{mission.Event}'.");
            if (mission.NoScope && mission.Scoped) errors.Add($"Weekly mission {mission.Id} cannot require both scoped and NoScope kills.");
        }
        foreach (var category in Enumerable.Range(1, 9))
            if (!catalog.Any(x => x.Category == category)) errors.Add($"Weekly mission category {category} has no valid mission.");
        var excluded = config.WeeklyMissions.ExcludedMissionWeapons.Select(NormalizeWeapon).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var mission in catalog.Where(x => x.Weapons.Any(w => excluded.Contains(NormalizeWeapon(w)))))
            errors.Add($"Weekly mission {mission.Id} uses an excluded weapon.");
        if (catalog.Sum(x => x.RewardXp) != 17_145) warnings.Add($"Weekly mission reward total is {catalog.Sum(x => x.RewardXp)}, not the product baseline 17145.");

        ValidateAliases(config.ChatCommands.Season.Aliases, "ChatCommands.Season", errors);
        ValidateAliases(config.ChatCommands.Missions.Aliases, "ChatCommands.Missions", errors);

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

    private static bool HasExplicitOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim();
        return value.EndsWith('Z') ||
               (value.Length >= 6 && (value[^6] == '+' || value[^6] == '-') && value[^3] == ':');
    }

    private static bool TryExplicitDate(string? value, out DateTimeOffset? parsed)
    {
        parsed = null;
        if (!DateTimeOffset.TryParse(value, out var result) || !HasExplicitOffset(value)) return false;
        parsed = result.ToUniversalTime();
        return true;
    }

    private static string NormalizeWeapon(string value) => value.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase) ? value[7..] : value;

    private static void ValidateAliases(IEnumerable<string> aliases, string name, List<string> errors)
    {
        foreach (var alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias)) errors.Add($"{name} contains an empty alias.");
            if (alias.Any(ch => ch > 127)) errors.Add($"{name} aliases must be ASCII.");
        }
    }
}
