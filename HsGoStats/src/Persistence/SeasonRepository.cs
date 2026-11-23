using HsGoStats.Configuration;
using HsGoStats.Domain;
using MySqlConnector;

namespace HsGoStats.Persistence;

public sealed record StoredSeason(long Id, int Number, string Name, string Status, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, string ScoringVersion)
{
    public bool Contains(DateTimeOffset instant) => instant >= StartsAtUtc && instant < EndsAtUtc;
}

public sealed record SeasonRefreshResult(IReadOnlyList<StoredSeason> Seasons, IReadOnlyList<string> Warnings);

public sealed class SeasonRepository(MySqlStore store, HsGoStatsConfig config, ScoringPolicy scoring)
{
    private const string LockName = "hsgo_stats_season_transition";

    public async Task<SeasonRefreshResult> RefreshAsync(DateTimeOffset now, CancellationToken token)
    {
        var warnings = new List<string>();
        await using var connection = store.CreateConnection();
        await connection.OpenAsync(token);
        await MySqlStore.SetUtcSessionAsync(connection, token);
        if (!await AcquireLockAsync(connection, token)) throw new InvalidOperationException("Could not acquire the season transition lock.");
        try
        {
            await using var transaction = await connection.BeginTransactionAsync(token);
            var settings = await LoadSettingsAsync(connection, transaction, token);
            var calendar = await EnsureCalendarSettingsAsync(connection, transaction, settings, warnings, token);
            var seasons = await LoadSeasonsAsync(connection, transaction, token);

            if (config.Season.Enabled)
            {
                if (seasons.Count == 0)
                {
                    var first = calendar.ByIndex(0);
                    await InsertSeasonAsync(connection, transaction, first, scoring.Version, token);
                    seasons = await LoadSeasonsAsync(connection, transaction, token);
                }

                await UpdateStatusesAsync(connection, transaction, now, token);
                seasons = await LoadSeasonsAsync(connection, transaction, token);
                await ApplyOverrideOnceAsync(connection, transaction, seasons, now, warnings, token);
                seasons = await LoadSeasonsAsync(connection, transaction, token);

                if (config.Season.AutoTransition)
                {
                    while (seasons.Count > 0 && now >= seasons[^1].EndsAtUtc)
                    {
                        var last = seasons[^1];
                        await InsertNextSeasonAsync(connection, transaction, last, calendar.DurationDays, calendar.NameFormat, token);
                        seasons = await LoadSeasonsAsync(connection, transaction, token);
                    }
                    if (seasons.Count > 0 && now >= seasons[^1].StartsAtUtc)
                    {
                        await InsertNextSeasonAsync(connection, transaction, seasons[^1], calendar.DurationDays, calendar.NameFormat, token);
                    }
                }

                await UpdateStatusesAsync(connection, transaction, now, token);
            }

            await transaction.CommitAsync(token);
            seasons = await LoadSeasonsAsync(connection, null, token);
            return new(seasons, warnings);
        }
        finally
        {
            await ReleaseLockAsync(connection, token);
        }
    }

    private async Task<SeasonCalendar> EnsureCalendarSettingsAsync(MySqlConnection connection, MySqlTransaction transaction, Dictionary<string, string> settings, List<string> warnings, CancellationToken token)
    {
        var requested = SeasonCalendar.FromConfig(config.Season);
        if (settings.Count == 0)
        {
            await SaveSettingAsync(connection, transaction, "calendar.start_utc", requested.AnchorUtc.ToString("O"), token);
            await SaveSettingAsync(connection, transaction, "calendar.duration_days", requested.DurationDays.ToString(), token);
            await SaveSettingAsync(connection, transaction, "calendar.initial_number", requested.InitialNumber.ToString(), token);
            await SaveSettingAsync(connection, transaction, "calendar.name_format", requested.NameFormat, token);
            return requested;
        }

        var persisted = new SeasonCalendar(
            DateTimeOffset.Parse(settings["calendar.start_utc"]),
            int.Parse(settings["calendar.duration_days"]),
            int.Parse(settings["calendar.initial_number"]),
            settings["calendar.name_format"]);
        if (persisted.AnchorUtc != requested.AnchorUtc || persisted.DurationDays != requested.DurationDays || persisted.InitialNumber != requested.InitialNumber || persisted.NameFormat != requested.NameFormat)
            warnings.Add("Season bootstrap values differ from the persisted calendar; existing DB values remain authoritative.");
        return persisted;
    }

    private async Task ApplyOverrideOnceAsync(MySqlConnection connection, MySqlTransaction transaction, List<StoredSeason> seasons, DateTimeOffset now, List<string> warnings, CancellationToken token)
    {
        if (config.Season.SeasonOverrideEndDate is null) return;
        var active = seasons.SingleOrDefault(x => x.Contains(now));
        var requested = DateTimeOffset.Parse(config.Season.SeasonOverrideEndDate).ToUniversalTime();
        if (active is null) { warnings.Add("SeasonOverrideEndDate was ignored because there is no active season."); return; }
        if (requested <= now || requested <= active.StartsAtUtc) { warnings.Add("SeasonOverrideEndDate was rejected because it is not a safe future end time."); return; }
        var auditKey = $"override.{active.Id}.{requested:O}";
        await using var audit = new MySqlCommand("INSERT IGNORE INTO hsgo_season_settings(setting_key, setting_value) VALUES (@key, @value)", connection, transaction);
        audit.Parameters.AddWithValue("@key", auditKey);
        audit.Parameters.AddWithValue("@value", $"old={active.EndsAtUtc:O};new={requested:O}");
        if (await audit.ExecuteNonQueryAsync(token) == 0) return;

        await using var update = new MySqlCommand("UPDATE hsgo_seasons SET ends_at_utc=@end, updated_at=UTC_TIMESTAMP(6) WHERE id=@id AND status='active'", connection, transaction);
        update.Parameters.AddWithValue("@end", requested.UtcDateTime);
        update.Parameters.AddWithValue("@id", active.Id);
        if (await update.ExecuteNonQueryAsync(token) != 1) throw new InvalidOperationException("Season override lost a concurrent update.");
        await using var delete = new MySqlCommand("DELETE FROM hsgo_seasons WHERE status='scheduled' AND starts_at_utc >= @old_end", connection, transaction);
        delete.Parameters.AddWithValue("@old_end", active.EndsAtUtc.UtcDateTime);
        await delete.ExecuteNonQueryAsync(token);
        warnings.Add($"Applied one-time end override to season {active.Number}; future scheduled rows were regenerated from the new boundary.");
    }

    private static async Task InsertNextSeasonAsync(MySqlConnection connection, MySqlTransaction transaction, StoredSeason last, int durationDays, string nameFormat, CancellationToken token)
    {
        var number = checked(last.Number + 1);
        var next = new SeasonPeriod(number, nameFormat.Replace("{number}", number.ToString(), StringComparison.Ordinal), last.EndsAtUtc, last.EndsAtUtc.AddDays(durationDays));
        await InsertSeasonAsync(connection, transaction, next, last.ScoringVersion, token);
    }

    private static async Task InsertSeasonAsync(MySqlConnection connection, MySqlTransaction transaction, SeasonPeriod season, string scoringVersion, CancellationToken token)
    {
        const string sql = """
            INSERT IGNORE INTO hsgo_seasons (season_number, name, status, starts_at_utc, ends_at_utc, scoring_version)
            VALUES (@number, @name, 'scheduled', @start, @end, @scoring)
            """;
        await using var command = new MySqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@number", season.Number);
        command.Parameters.AddWithValue("@name", season.Name);
        command.Parameters.AddWithValue("@start", season.StartsAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@end", season.EndsAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@scoring", scoringVersion);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task UpdateStatusesAsync(MySqlConnection connection, MySqlTransaction transaction, DateTimeOffset now, CancellationToken token)
    {
        var statements = new[]
        {
            "UPDATE hsgo_seasons SET status='completed', completed_at=COALESCE(completed_at,@now), updated_at=@now WHERE ends_at_utc<=@now AND status<>'completed'",
            "UPDATE hsgo_seasons SET status='scheduled', updated_at=@now WHERE starts_at_utc>@now AND status<>'scheduled'",
            "UPDATE hsgo_seasons SET status='active', activated_at=COALESCE(activated_at,@now), updated_at=@now WHERE starts_at_utc<=@now AND ends_at_utc>@now AND status<>'active'"
        };
        foreach (var sql in statements)
        {
            await using var command = new MySqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@now", now.UtcDateTime);
            await command.ExecuteNonQueryAsync(token);
        }
    }

    private static async Task<List<StoredSeason>> LoadSeasonsAsync(MySqlConnection connection, MySqlTransaction? transaction, CancellationToken token)
    {
        const string sql = "SELECT id, season_number, name, status, starts_at_utc, ends_at_utc, scoring_version FROM hsgo_seasons ORDER BY starts_at_utc";
        await using var command = new MySqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(token);
        var result = new List<StoredSeason>();
        while (await reader.ReadAsync(token))
        {
            result.Add(new(reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc)),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc)), reader.GetString(6)));
        }
        return result;
    }

    private static async Task<Dictionary<string, string>> LoadSettingsAsync(MySqlConnection connection, MySqlTransaction transaction, CancellationToken token)
    {
        await using var command = new MySqlCommand("SELECT setting_key, setting_value FROM hsgo_season_settings WHERE setting_key LIKE 'calendar.%'", connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(token);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(token)) result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }

    private static async Task SaveSettingAsync(MySqlConnection connection, MySqlTransaction transaction, string key, string value, CancellationToken token)
    {
        await using var command = new MySqlCommand("INSERT INTO hsgo_season_settings(setting_key, setting_value) VALUES (@key, @value)", connection, transaction);
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@value", value);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<bool> AcquireLockAsync(MySqlConnection connection, CancellationToken token)
    {
        await using var command = new MySqlCommand("SELECT GET_LOCK(@name, 10)", connection);
        command.Parameters.AddWithValue("@name", LockName);
        return Convert.ToInt32(await command.ExecuteScalarAsync(token)) == 1;
    }

    private static async Task ReleaseLockAsync(MySqlConnection connection, CancellationToken token)
    {
        await using var command = new MySqlCommand("SELECT RELEASE_LOCK(@name)", connection);
        command.Parameters.AddWithValue("@name", LockName);
        await command.ExecuteScalarAsync(token);
    }
}
