using System.Security.Cryptography;
using System.Text.Json;
using HsGoStats.Configuration;
using HsGoStats.Domain;
using MySqlConnector;

namespace HsGoStats.Persistence;

public sealed class WeeklyMissionRepository(MySqlStore store, HsGoStatsConfig config)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task ProcessAsync(MissionSignal signal, CancellationToken token)
        => await ProcessBatchAsync([signal], token);

    public async Task ProcessBatchAsync(IReadOnlyCollection<MissionSignal> signals, CancellationToken token)
    {
        if ((!config.WeeklyMissions.Enabled && !config.Rating.Enabled) || signals.Count == 0) return;
        await using var connection = store.CreateConnection();
        await connection.OpenAsync(token);
        await MySqlStore.SetUtcSessionAsync(connection, token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        foreach (var signal in signals) await ProcessInTransactionAsync(connection, transaction, signal, token);
        await transaction.CommitAsync(token);
    }

    private async Task ProcessInTransactionAsync(MySqlConnection connection, MySqlTransaction transaction, MissionSignal signal, CancellationToken token)
    {
        if (!await ClaimAsync(connection, transaction, signal, token)) return;
        await UpsertPlayerAsync(connection, transaction, signal, token);
        if (config.Rating.Enabled && signal.HsrDelta != 0)
            await ApplyGameplayHsrAsync(connection, transaction, signal, token);
        if (!config.WeeklyMissions.Enabled) return;
        if (!signal.HasActiveSeason || signal.ActiveSeasonId is null ||
            signal.ActiveSeasonStartsAt is null || signal.ActiveSeasonEndsAt is null) return;

        var window = await FindWindowAsync(connection, transaction, signal.ActiveSeasonId.Value, signal.ObservedAt, token);
        if (window is null)
        {
            var calculated = WeeklyWindowCalculator.At(signal.ObservedAt, config.WeeklyMissions.Rotation,
                signal.ActiveSeasonStartsAt.Value, signal.ActiveSeasonEndsAt.Value);
            await InsertWindowAsync(connection, transaction, signal.ActiveSeasonId.Value, calculated, token);
            window = await FindWindowAsync(connection, transaction, signal.ActiveSeasonId.Value, signal.ObservedAt, token);
        }

        if (window is not null)
            await EnsureAssignmentsAsync(connection, transaction, window.Value.Id, signal, token);

        if (window is not null && signal.Event != "Ensure")
            await ApplyProgressAsync(connection, transaction, window.Value.Id, signal, token);
    }

    public async Task<IReadOnlyList<MissionProgressView>> GetProgressAsync(ulong steamId, DateTimeOffset now, CancellationToken token)
    {
        var result = new List<MissionProgressView>();
        await using var connection = store.CreateConnection();
        await connection.OpenAsync(token);
        await MySqlStore.SetUtcSessionAsync(connection, token);
        const string sql = """
            SELECT a.mission_id,a.category,a.definition_json,a.progress,a.target,a.reward_xp,a.completed_at,w.ends_at_utc
            FROM hsgo_weekly_windows w
            JOIN hsgo_seasons s ON s.id=w.season_id AND s.is_configured=1 AND s.status='active'
            JOIN hsgo_weekly_assignments a ON a.window_id=w.id
            WHERE w.server_id=@server AND a.server_id=@server AND a.steam_id64=@steam
              AND w.starts_at_utc<=@now AND w.ends_at_utc>@now
            ORDER BY a.category
            """;
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@server", config.ServerId);
        command.Parameters.AddWithValue("@steam", steamId);
        command.Parameters.AddWithValue("@now", now.UtcDateTime);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var definition = JsonSerializer.Deserialize<MissionDefinition>(reader.GetString(2), JsonOptions)!;
            result.Add(new(reader.GetInt32(0), reader.GetInt32(1), definition.Description, reader.GetInt32(3), reader.GetInt32(4),
                reader.GetInt32(5), !reader.IsDBNull(6), Utc(reader.GetDateTime(7))));
        }
        return result;
    }

    public async Task<SeasonCommandView?> GetSeasonViewAsync(StoredSeason? season, ulong steamId, CancellationToken token)
    {
        if (season is null) return null;
        await using var connection = store.CreateConnection();
        await connection.OpenAsync(token);
        await MySqlStore.SetUtcSessionAsync(connection, token);
        const string hsrSql = "SELECT hsr FROM hsgo_player_season_hsr WHERE season_id=@season AND server_id=@server AND steam_id64=@steam";
        long hsr;
        await using (var hsrCommand = new MySqlCommand(hsrSql, connection))
        {
            hsrCommand.Parameters.AddWithValue("@season", season.Id);
            hsrCommand.Parameters.AddWithValue("@server", config.ServerId);
            hsrCommand.Parameters.AddWithValue("@steam", steamId);
            var value = await hsrCommand.ExecuteScalarAsync(token);
            if (value is null) return new(season.Name, season.Number, season.StartsAtUtc, season.EndsAtUtc, 0, null, 0);
            hsr = Convert.ToInt64(value);
        }

        const string rankSql = """
            SELECT
              1 + SUM(CASE WHEN h.hsr>@hsr OR (h.hsr=@hsr AND h.steam_id64<@steam) THEN 1 ELSE 0 END),
              COUNT(*)
            FROM hsgo_player_season_hsr h
            WHERE h.season_id=@season AND h.server_id=@server
              AND NOT EXISTS (
                SELECT 1 FROM hsgo_season_leaderboard_blacklist b
                WHERE b.server_id=h.server_id AND b.steam_id64=h.steam_id64
                  AND @number>=b.effective_from_season_number
                  AND (b.effective_until_season_number IS NULL OR @number<b.effective_until_season_number))
            """;
        await using var rankCommand = new MySqlCommand(rankSql, connection);
        rankCommand.Parameters.AddWithValue("@season", season.Id);
        rankCommand.Parameters.AddWithValue("@server", config.ServerId);
        rankCommand.Parameters.AddWithValue("@steam", steamId);
        rankCommand.Parameters.AddWithValue("@hsr", hsr);
        rankCommand.Parameters.AddWithValue("@number", season.Number);
        await using var rankReader = await rankCommand.ExecuteReaderAsync(token);
        await rankReader.ReadAsync(token);
        return new(season.Name, season.Number, season.StartsAtUtc, season.EndsAtUtc, hsr,
            rankReader.IsDBNull(0) ? null : Convert.ToInt64(rankReader.GetValue(0)), Convert.ToInt64(rankReader.GetValue(1)));
    }

    private async Task ApplyProgressAsync(MySqlConnection connection, MySqlTransaction transaction, long windowId, MissionSignal signal, CancellationToken token)
    {
        const string sql = """
            SELECT id,definition_json,target,reward_xp,progress
            FROM hsgo_weekly_assignments
            WHERE window_id=@window AND server_id=@server AND steam_id64=@steam AND completed_at IS NULL
            FOR UPDATE
            """;
        var rows = new List<(long Id, MissionDefinition Definition, int Target, int Reward, int Progress)>();
        await using (var command = new MySqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("@window", windowId);
            command.Parameters.AddWithValue("@server", config.ServerId);
            command.Parameters.AddWithValue("@steam", signal.Player.SteamId64);
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                rows.Add((reader.GetInt64(0), JsonSerializer.Deserialize<MissionDefinition>(reader.GetString(1), JsonOptions)!, reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)));
        }

        foreach (var row in rows)
        {
            var match = MissionEvaluator.Evaluate(row.Definition, signal);
            if (!match.Matched) continue;
            var next = match.Replace ? Math.Min(row.Target, Math.Max(0, match.Amount)) : Math.Min(row.Target, checked(row.Progress + Math.Max(0, match.Amount)));
            var completed = next >= row.Target;
            const string updateSql = """
                UPDATE hsgo_weekly_assignments
                SET progress=@progress,completed_at=IF(@completed=1,COALESCE(completed_at,@at),completed_at),
                    rewarded_at=IF(@completed=1,COALESCE(rewarded_at,@at),rewarded_at),
                    rewarded_season_id=IF(@completed=1 AND @season IS NOT NULL,COALESCE(rewarded_season_id,@season),rewarded_season_id),updated_at=@at
                WHERE id=@id AND completed_at IS NULL
                """;
            await using var update = new MySqlCommand(updateSql, connection, transaction);
            update.Parameters.AddWithValue("@progress", next);
            update.Parameters.AddWithValue("@completed", completed ? 1 : 0);
            update.Parameters.AddWithValue("@at", signal.ObservedAt.UtcDateTime);
            update.Parameters.AddWithValue("@season", signal.ActiveSeasonId is null ? DBNull.Value : signal.ActiveSeasonId);
            update.Parameters.AddWithValue("@id", row.Id);
            if (await update.ExecuteNonQueryAsync(token) != 1 || !completed) continue;
            if (config.Rating.Enabled && config.WeeklyMissions.Rewards.GrantGeneralXP)
                await ApplyRewardAsync(connection, transaction, "hsgo_player_global_hsr", false, signal, row.Reward, token);
            if (config.Rating.Enabled && config.WeeklyMissions.Rewards.GrantActiveSeasonXP && signal.HasActiveSeason && signal.GrantSeason && signal.ActiveSeasonId is not null)
                await ApplyRewardAsync(connection, transaction, "hsgo_player_season_hsr", true, signal, row.Reward, token);
        }
    }

    private async Task EnsureAssignmentsAsync(MySqlConnection connection, MySqlTransaction transaction, long windowId, MissionSignal signal, CancellationToken token)
    {
        foreach (var category in Enumerable.Range(1, 9))
        {
            var choices = config.WeeklyMissions.Catalog.Where(x => x.Category == category).ToArray();
            if (!config.WeeklyMissions.Rotation.AllowRepeatAcrossWeeks && choices.Length > 1)
            {
                const string previousSql = """
                    SELECT a.mission_id FROM hsgo_weekly_assignments a
                    JOIN hsgo_weekly_windows w ON w.id=a.window_id
                    WHERE a.server_id=@server AND a.steam_id64=@steam AND a.category=@category AND a.window_id<>@window
                    ORDER BY w.starts_at_utc DESC LIMIT 1
                    """;
                await using var previous = new MySqlCommand(previousSql, connection, transaction);
                previous.Parameters.AddWithValue("@server", config.ServerId);
                previous.Parameters.AddWithValue("@steam", signal.Player.SteamId64);
                previous.Parameters.AddWithValue("@category", category);
                previous.Parameters.AddWithValue("@window", windowId);
                var previousId = await previous.ExecuteScalarAsync(token);
                if (previousId is not null) choices = choices.Where(x => x.Id != Convert.ToInt32(previousId)).ToArray();
            }
            var selected = choices[RandomNumberGenerator.GetInt32(choices.Length)];
            const string sql = """
                INSERT IGNORE INTO hsgo_weekly_assignments
                  (window_id,server_id,steam_id64,category,mission_id,definition_json,target,reward_xp,created_at,updated_at)
                VALUES (@window,@server,@steam,@category,@mission,@definition,@target,@reward,@at,@at)
                """;
            await using var command = new MySqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@window", windowId);
            command.Parameters.AddWithValue("@server", config.ServerId);
            command.Parameters.AddWithValue("@steam", signal.Player.SteamId64);
            command.Parameters.AddWithValue("@category", category);
            command.Parameters.AddWithValue("@mission", selected.Id);
            command.Parameters.AddWithValue("@definition", JsonSerializer.Serialize(selected, JsonOptions));
            command.Parameters.AddWithValue("@target", selected.Target);
            command.Parameters.AddWithValue("@reward", selected.RewardXp);
            command.Parameters.AddWithValue("@at", signal.ObservedAt.UtcDateTime);
            await command.ExecuteNonQueryAsync(token);
        }
    }

    private async Task<(long Id, DateTimeOffset End)?> FindWindowAsync(MySqlConnection connection, MySqlTransaction transaction,
        long seasonId, DateTimeOffset at, CancellationToken token)
    {
        const string sql = "SELECT id,ends_at_utc FROM hsgo_weekly_windows WHERE season_id=@season AND server_id=@server AND starts_at_utc<=@at AND ends_at_utc>@at ORDER BY starts_at_utc DESC LIMIT 1 FOR UPDATE";
        await using var command = new MySqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@season", seasonId);
        command.Parameters.AddWithValue("@server", config.ServerId);
        command.Parameters.AddWithValue("@at", at.UtcDateTime);
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? (reader.GetInt64(0), Utc(reader.GetDateTime(1))) : null;
    }

    private async Task InsertWindowAsync(MySqlConnection connection, MySqlTransaction transaction, long seasonId, WeeklyWindow window, CancellationToken token)
    {
        const string sql = "INSERT IGNORE INTO hsgo_weekly_windows(season_id,server_id,starts_at_utc,ends_at_utc,timezone) VALUES(@season,@server,@start,@end,@zone)";
        await using var command = new MySqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@season", seasonId);
        command.Parameters.AddWithValue("@server", config.ServerId);
        command.Parameters.AddWithValue("@start", window.StartsAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@end", window.EndsAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@zone", window.Timezone);
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task ApplyRewardAsync(MySqlConnection connection, MySqlTransaction transaction, string table, bool seasonal, MissionSignal signal, int reward, CancellationToken token)
    {
        var identities = seasonal ? "season_id,server_id,steam_id64" : "server_id,steam_id64";
        var values = seasonal ? "@season,@server,@steam" : "@server,@steam";
        var sql = $"INSERT INTO {table}({identities},hsr,updated_at) VALUES({values},GREATEST(@minimum,@reward),@at) ON DUPLICATE KEY UPDATE hsr=GREATEST(@minimum,hsr+@reward),updated_at=VALUES(updated_at)";
        await using var command = new MySqlCommand(sql, connection, transaction);
        if (seasonal) command.Parameters.AddWithValue("@season", signal.ActiveSeasonId);
        command.Parameters.AddWithValue("@server", config.ServerId);
        command.Parameters.AddWithValue("@steam", signal.Player.SteamId64);
        command.Parameters.AddWithValue("@reward", reward);
        command.Parameters.AddWithValue("@minimum", config.Rating.MinimumXP);
        command.Parameters.AddWithValue("@at", signal.ObservedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task ApplyGameplayHsrAsync(MySqlConnection connection, MySqlTransaction transaction, MissionSignal signal, CancellationToken token)
    {
        var targets = TrackingTargets.Resolve(config.Season.Enabled, config.Season.PreseasonTracking, signal.HasActiveSeason, !signal.GrantSeason);
        if (targets.ApplyGlobal)
            await ApplyHsrDeltaAsync(connection, transaction, "hsgo_player_global_hsr", false, signal, token);
        if (targets.ApplySeason && signal.ActiveSeasonId is not null)
            await ApplyHsrDeltaAsync(connection, transaction, "hsgo_player_season_hsr", true, signal, token);
    }

    private async Task ApplyHsrDeltaAsync(MySqlConnection connection, MySqlTransaction transaction, string table, bool seasonal, MissionSignal signal, CancellationToken token)
    {
        var identities = seasonal ? "season_id,server_id,steam_id64" : "server_id,steam_id64";
        var values = seasonal ? "@season,@server,@steam" : "@server,@steam";
        var update = config.Rating.AllowDemotion ? "hsr+@delta" : "GREATEST(hsr,hsr+@delta)";
        var sql = $"INSERT INTO {table}({identities},hsr,updated_at) VALUES({values},GREATEST(@minimum,@delta),@at) ON DUPLICATE KEY UPDATE hsr=GREATEST(@minimum,{update}),updated_at=VALUES(updated_at)";
        await using var command = new MySqlCommand(sql, connection, transaction);
        if (seasonal) command.Parameters.AddWithValue("@season", signal.ActiveSeasonId);
        command.Parameters.AddWithValue("@server", config.ServerId);
        command.Parameters.AddWithValue("@steam", signal.Player.SteamId64);
        command.Parameters.AddWithValue("@delta", signal.HsrDelta);
        command.Parameters.AddWithValue("@minimum", config.Rating.MinimumXP);
        command.Parameters.AddWithValue("@at", signal.ObservedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task<bool> ClaimAsync(MySqlConnection connection, MySqlTransaction transaction, MissionSignal signal, CancellationToken token)
    {
        const string sql = "INSERT IGNORE INTO hsgo_mission_events(event_id,server_id,steam_id64,event_type,observed_at) VALUES(UUID_TO_BIN(@id),@server,@steam,@type,@at)";
        await using var command = new MySqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@id", signal.EventId.ToString("D"));
        command.Parameters.AddWithValue("@server", config.ServerId);
        command.Parameters.AddWithValue("@steam", signal.Player.SteamId64);
        command.Parameters.AddWithValue("@type", signal.Event);
        command.Parameters.AddWithValue("@at", signal.ObservedAt.UtcDateTime);
        return await command.ExecuteNonQueryAsync(token) == 1;
    }

    private static async Task UpsertPlayerAsync(MySqlConnection connection, MySqlTransaction transaction, MissionSignal signal, CancellationToken token)
    {
        const string sql = """
            INSERT INTO hsgo_players(steam_id64,last_name,first_seen_at,last_seen_at,updated_at) VALUES(@steam,@name,@at,@at,@at)
            ON DUPLICATE KEY UPDATE last_name=COALESCE(VALUES(last_name),last_name),last_seen_at=GREATEST(last_seen_at,VALUES(last_seen_at)),updated_at=VALUES(updated_at)
            """;
        await using var command = new MySqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@steam", signal.Player.SteamId64);
        command.Parameters.AddWithValue("@name", signal.Player.Name is null ? DBNull.Value : signal.Player.Name);
        command.Parameters.AddWithValue("@at", signal.ObservedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(token);
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
