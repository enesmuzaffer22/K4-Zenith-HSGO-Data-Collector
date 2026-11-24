using System.Globalization;
using HsGoStats.Configuration;
using HsGoStats.Domain;
using MySqlConnector;

namespace HsGoStats.Persistence;

public sealed record StoredSeason(long Id, int Number, string Name, string Status, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, string ScoringVersion)
{
    public bool Contains(DateTimeOffset instant) => instant >= StartsAtUtc && instant < EndsAtUtc;
}

public sealed record SeasonRefreshResult(IReadOnlyList<StoredSeason> Seasons, IReadOnlyList<string> Warnings);

public sealed class SeasonRepository(MySqlStore store, HsGoStatsRuntimeSettings config, ScoringPolicy scoring)
{
    private const string LockName = "hsgo_stats_manual_season_schedule";

    public async Task<SeasonRefreshResult> RefreshAsync(DateTimeOffset now, CancellationToken token)
    {
        if (!config.Season.Enabled) return new([], []);
        var warnings = new List<string>();
        await using var connection = store.CreateConnection();
        await connection.OpenAsync(token);
        await MySqlStore.SetUtcSessionAsync(connection, token);
        if (!await AcquireLockAsync(connection, token)) throw new InvalidOperationException("Could not acquire the manual season schedule lock.");
        try
        {
            await using var transaction = await connection.BeginTransactionAsync(token);
            await MarkAllUnconfiguredAsync(connection, transaction, token);
            for (var index = 0; index < config.Season.Schedule.Count; index++)
                await SyncSeasonAsync(connection, transaction, index + 1, config.Season.Schedule[index], now, warnings, token);
            await UpdateStatusesAsync(connection, transaction, now, token);
            var ignored = await CountUnconfiguredAsync(connection, transaction, token);
            if (ignored > 0) warnings.Add($"{ignored} legacy/unconfigured season row(s) were preserved but excluded from the active manual schedule.");
            await transaction.CommitAsync(token);
            return new(await LoadSeasonsAsync(connection, null, token), warnings);
        }
        finally
        {
            await ReleaseLockAsync(connection, token);
        }
    }

    private async Task SyncSeasonAsync(MySqlConnection connection, MySqlTransaction transaction, int number,
        SeasonScheduleEntryConfig requested, DateTimeOffset now, List<string> warnings, CancellationToken token)
    {
        var start = DateTimeOffset.Parse(requested.StartsAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
        var end = DateTimeOffset.Parse(requested.EndsAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
        var name = requested.Name.Trim();
        var existing = await LoadSeasonByNumberAsync(connection, transaction, number, token);
        if (existing is null)
        {
            const string insertSql = """
                INSERT INTO hsgo_seasons
                  (season_number,name,status,starts_at_utc,ends_at_utc,scoring_version,is_configured)
                VALUES (@number,@name,'scheduled',@start,@end,@scoring,1)
                """;
            await using var insert = new MySqlCommand(insertSql, connection, transaction);
            insert.Parameters.AddWithValue("@number", number);
            insert.Parameters.AddWithValue("@name", name);
            insert.Parameters.AddWithValue("@start", start.UtcDateTime);
            insert.Parameters.AddWithValue("@end", end.UtcDateTime);
            insert.Parameters.AddWithValue("@scoring", scoring.Version);
            await insert.ExecuteNonQueryAsync(token);
            return;
        }

        if (existing.Status == "completed" && existing.EndsAtUtc <= now)
        {
            await SetConfiguredAsync(connection, transaction, existing.Id, token);
            if (existing.Name != name || existing.StartsAtUtc != start || existing.EndsAtUtc != end)
                warnings.Add($"Completed season {number} differs from config; historical DB name and boundaries remain authoritative.");
            return;
        }

        if (existing.Status == "active" && existing.StartsAtUtc != start)
        {
            warnings.Add($"Active season {number} start cannot be moved; existing start {existing.StartsAtUtc:O} remains authoritative.");
            start = existing.StartsAtUtc;
        }

        const string updateSql = """
            UPDATE hsgo_seasons
            SET name=@name,starts_at_utc=@start,ends_at_utc=@end,scoring_version=@scoring,
                is_configured=1,updated_at=UTC_TIMESTAMP(6)
            WHERE id=@id
            """;
        await using var update = new MySqlCommand(updateSql, connection, transaction);
        update.Parameters.AddWithValue("@name", name);
        update.Parameters.AddWithValue("@start", start.UtcDateTime);
        update.Parameters.AddWithValue("@end", end.UtcDateTime);
        update.Parameters.AddWithValue("@scoring", scoring.Version);
        update.Parameters.AddWithValue("@id", existing.Id);
        await update.ExecuteNonQueryAsync(token);
    }

    private static async Task MarkAllUnconfiguredAsync(MySqlConnection connection, MySqlTransaction transaction, CancellationToken token)
    {
        await using var command = new MySqlCommand("UPDATE hsgo_seasons SET is_configured=0 WHERE is_configured<>0", connection, transaction);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task SetConfiguredAsync(MySqlConnection connection, MySqlTransaction transaction, long id, CancellationToken token)
    {
        await using var command = new MySqlCommand("UPDATE hsgo_seasons SET is_configured=1 WHERE id=@id", connection, transaction);
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<int> CountUnconfiguredAsync(MySqlConnection connection, MySqlTransaction transaction, CancellationToken token)
    {
        await using var command = new MySqlCommand("SELECT COUNT(*) FROM hsgo_seasons WHERE is_configured=0", connection, transaction);
        return Convert.ToInt32(await command.ExecuteScalarAsync(token));
    }

    private static async Task UpdateStatusesAsync(MySqlConnection connection, MySqlTransaction transaction, DateTimeOffset now, CancellationToken token)
    {
        var statements = new[]
        {
            "UPDATE hsgo_seasons SET status='completed',completed_at=COALESCE(completed_at,@now),updated_at=@now WHERE is_configured=1 AND ends_at_utc<=@now AND status<>'completed'",
            "UPDATE hsgo_seasons SET status='scheduled',updated_at=@now WHERE is_configured=1 AND starts_at_utc>@now AND status<>'scheduled'",
            "UPDATE hsgo_seasons SET status='active',activated_at=COALESCE(activated_at,@now),updated_at=@now WHERE is_configured=1 AND starts_at_utc<=@now AND ends_at_utc>@now AND status<>'active'"
        };
        foreach (var sql in statements)
        {
            await using var command = new MySqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@now", now.UtcDateTime);
            await command.ExecuteNonQueryAsync(token);
        }
    }

    private static async Task<StoredSeason?> LoadSeasonByNumberAsync(MySqlConnection connection, MySqlTransaction transaction, int number, CancellationToken token)
    {
        const string sql = "SELECT id,season_number,name,status,starts_at_utc,ends_at_utc,scoring_version FROM hsgo_seasons WHERE season_number=@number FOR UPDATE";
        await using var command = new MySqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@number", number);
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? ReadSeason(reader) : null;
    }

    private static async Task<List<StoredSeason>> LoadSeasonsAsync(MySqlConnection connection, MySqlTransaction? transaction, CancellationToken token)
    {
        const string sql = "SELECT id,season_number,name,status,starts_at_utc,ends_at_utc,scoring_version FROM hsgo_seasons WHERE is_configured=1 ORDER BY starts_at_utc";
        await using var command = new MySqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(token);
        var result = new List<StoredSeason>();
        while (await reader.ReadAsync(token)) result.Add(ReadSeason(reader));
        return result;
    }

    private static StoredSeason ReadSeason(MySqlDataReader reader) => new(
        reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
        new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc)),
        new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc)), reader.GetString(6));

    private static async Task<bool> AcquireLockAsync(MySqlConnection connection, CancellationToken token)
    {
        await using var command = new MySqlCommand("SELECT GET_LOCK(@name,10)", connection);
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
