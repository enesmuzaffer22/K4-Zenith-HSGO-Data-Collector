using HsGoStats.Configuration;
using HsGoStats.Domain;
using MySqlConnector;

namespace HsGoStats.Persistence;

public sealed class MySqlStore
{
    private readonly HsGoStatsConfig _bootstrap;
    private HsGoStatsRuntimeSettings _settings = new();
    private readonly string _connectionString;

    public MySqlStore(HsGoStatsConfig config)
    {
        _bootstrap = config;
        var sslMode = Enum.Parse<MySqlSslMode>(config.Database.SslMode, true);
        _connectionString = new MySqlConnectionStringBuilder
        {
            Server = config.Database.Host,
            Port = config.Database.Port,
            Database = config.Database.Name,
            UserID = config.Database.Username,
            Password = config.Database.Password,
            SslMode = sslMode,
            ConnectionTimeout = (uint)config.Database.ConnectionTimeoutSeconds,
            DefaultCommandTimeout = 30,
            CharacterSet = "utf8mb4",
            Pooling = true,
            MaximumPoolSize = 10,
            AllowUserVariables = false
        }.ConnectionString;
    }

    public void ApplyRuntimeSettings(HsGoStatsRuntimeSettings settings) => _settings = settings;

    public async Task ClampTpFloorAsync(CancellationToken token)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(token);
        await SetUtcSessionAsync(connection, token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        var targets = new (string Table, string Column, long Minimum)[]
        {
            ("hsgo_player_global_hsr", "hsr", _settings.Rating.MinimumXP),
            ("hsgo_player_season_hsr", "hsr", _settings.Rating.MinimumXP)
        };
        foreach (var target in targets)
        {
            var sql = $"UPDATE {target.Table} SET {target.Column}=GREATEST({target.Column},@minimum),updated_at=UTC_TIMESTAMP(6) WHERE server_id=@server AND {target.Column}<@minimum";
            await using var command = new MySqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@minimum", target.Minimum);
            command.Parameters.AddWithValue("@server", _bootstrap.ServerId);
            await command.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
    }

    public async Task<IReadOnlyList<string>> InitializeSchemaAsync(CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await SetUtcSessionAsync(connection, cancellationToken);
        var warnings = await InspectLegacyMissionSchemaAsync(connection, cancellationToken);
        foreach (var sql in SchemaSql.Commands)
        {
            await using var command = new MySqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        return warnings;
    }

    public async Task SyncRatingTiersAsync(CancellationToken token)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(token);
        await SetUtcSessionAsync(connection, token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using (var disable = new MySqlCommand("UPDATE hsgo_rating_tiers SET enabled=0,updated_at=UTC_TIMESTAMP(6) WHERE server_id=@server", connection, transaction))
        {
            disable.Parameters.AddWithValue("@server", _bootstrap.ServerId);
            await disable.ExecuteNonQueryAsync(token);
        }
        const string sql = """
            INSERT INTO hsgo_rating_tiers(server_id,tier_id,name,min_xp,color,enabled,updated_at)
            VALUES(@server,@id,@name,@min,@color,1,UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE name=VALUES(name),min_xp=VALUES(min_xp),color=VALUES(color),enabled=1,updated_at=VALUES(updated_at)
            """;
        foreach (var tier in _settings.Rating.Enabled ? _settings.Rating.Tiers : [])
        {
            await using var command = new MySqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@server", _bootstrap.ServerId);
            command.Parameters.AddWithValue("@id", tier.Id);
            command.Parameters.AddWithValue("@name", tier.Name);
            command.Parameters.AddWithValue("@min", tier.MinXP);
            command.Parameters.AddWithValue("@color", tier.Color);
            await command.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
    }

    private static async Task<IReadOnlyList<string>> InspectLegacyMissionSchemaAsync(MySqlConnection connection, CancellationToken token)
    {
        var warnings = new List<string>();
        const string tablesSql = "SELECT table_name FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name LIKE 'hsgo_weekly_%'";
        var tables = new List<string>();
        await using (var command = new MySqlCommand(tablesSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token)) tables.Add(reader.GetString(0));
        var known = new HashSet<string>(["hsgo_weekly_windows", "hsgo_weekly_assignments"], StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables.Where(x => !known.Contains(x)))
            warnings.Add($"Unrecognized legacy/test weekly table '{table}' was found and left untouched.");
        if (tables.Contains("hsgo_weekly_assignments", StringComparer.OrdinalIgnoreCase))
        {
            const string columnsSql = "SELECT column_name FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='hsgo_weekly_assignments'";
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var command = new MySqlCommand(columnsSql, connection);
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) columns.Add(reader.GetString(0));
            var required = new[] { "definition_json", "rewarded_at", "rewarded_season_id" };
            var missing = required.Where(x => !columns.Contains(x)).ToArray();
            if (missing.Length > 0) warnings.Add($"Legacy/test hsgo_weekly_assignments schema is missing {string.Join(", ", missing)}; no data or table was changed.");
        }
        if (tables.Contains("hsgo_weekly_windows", StringComparer.OrdinalIgnoreCase) &&
            !await HasColumnAsync(connection, "hsgo_weekly_windows", "season_id", token))
            warnings.Add("Legacy hsgo_weekly_windows is missing season_id; no data or table was changed.");
        if (await TableExistsAsync(connection, "hsgo_seasons", token) &&
            !await HasColumnAsync(connection, "hsgo_seasons", "is_configured", token))
            warnings.Add("Legacy hsgo_seasons is missing is_configured; no data or table was changed.");
        return warnings;
    }

    private static async Task<bool> TableExistsAsync(MySqlConnection connection, string table, CancellationToken token)
    {
        await using var command = new MySqlCommand("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@table", connection);
        command.Parameters.AddWithValue("@table", table);
        return Convert.ToInt32(await command.ExecuteScalarAsync(token)) > 0;
    }

    private static async Task<bool> HasColumnAsync(MySqlConnection connection, string table, string column, CancellationToken token)
    {
        await using var command = new MySqlCommand("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name=@table AND column_name=@column", connection);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        return Convert.ToInt32(await command.ExecuteScalarAsync(token)) > 0;
    }

    public async Task<IReadOnlyList<SeasonLeaderboardBlacklistRule>> SyncSeasonLeaderboardBlacklistAsync(
        IEnumerable<ulong> configuredSteamIds,
        int effectiveFromSeasonNumber,
        CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await SetUtcSessionAsync(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var configured = configuredSteamIds.ToHashSet();
        var openRules = new Dictionary<ulong, int>();
        const string openSql = """
            SELECT steam_id64, effective_from_season_number
            FROM hsgo_season_leaderboard_blacklist
            WHERE server_id=@server_id AND effective_until_season_number IS NULL
            FOR UPDATE
            """;
        await using (var selectOpen = new MySqlCommand(openSql, connection, transaction))
        {
            selectOpen.Parameters.AddWithValue("@server_id", _bootstrap.ServerId);
            await using var reader = await selectOpen.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) openRules[reader.GetUInt64(0)] = reader.GetInt32(1);
        }

        const string insertSql = """
            INSERT INTO hsgo_season_leaderboard_blacklist
              (server_id, steam_id64, effective_from_season_number, effective_until_season_number)
            VALUES (@server_id, @steam_id64, @effective_from, NULL)
            ON DUPLICATE KEY UPDATE effective_until_season_number=NULL, updated_at=UTC_TIMESTAMP(6)
            """;
        foreach (var steamId in configured.Where(steamId => !openRules.ContainsKey(steamId)))
        {
            await using var insert = new MySqlCommand(insertSql, connection, transaction);
            insert.Parameters.AddWithValue("@server_id", _bootstrap.ServerId);
            insert.Parameters.AddWithValue("@steam_id64", steamId);
            insert.Parameters.AddWithValue("@effective_from", effectiveFromSeasonNumber);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        const string closeSql = """
            UPDATE hsgo_season_leaderboard_blacklist
            SET effective_until_season_number=GREATEST(effective_from_season_number,@effective_until), updated_at=UTC_TIMESTAMP(6)
            WHERE server_id=@server_id AND steam_id64=@steam_id64 AND effective_until_season_number IS NULL
            """;
        foreach (var steamId in openRules.Keys.Where(steamId => !configured.Contains(steamId)))
        {
            await using var close = new MySqlCommand(closeSql, connection, transaction);
            close.Parameters.AddWithValue("@server_id", _bootstrap.ServerId);
            close.Parameters.AddWithValue("@steam_id64", steamId);
            close.Parameters.AddWithValue("@effective_until", effectiveFromSeasonNumber);
            await close.ExecuteNonQueryAsync(cancellationToken);
        }

        var rules = new List<SeasonLeaderboardBlacklistRule>();
        const string rulesSql = """
            SELECT steam_id64, effective_from_season_number, effective_until_season_number
            FROM hsgo_season_leaderboard_blacklist
            WHERE server_id=@server_id
            ORDER BY steam_id64, effective_from_season_number
            """;
        await using (var selectRules = new MySqlCommand(rulesSql, connection, transaction))
        {
            selectRules.Parameters.AddWithValue("@server_id", _bootstrap.ServerId);
            await using var reader = await selectRules.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rules.Add(new(
                    reader.GetUInt64(0),
                    reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2)));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return rules;
    }

    public async Task<int> WriteBatchAsync(IReadOnlyCollection<NormalizedStatEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0) return 0;
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await SetUtcSessionAsync(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var applied = 0;
        foreach (var item in events)
        {
            if (!await ClaimEventAsync(connection, transaction, item, cancellationToken)) continue;
            await UpsertPlayerAsync(connection, transaction, item, cancellationToken);
            if (item.ApplyGlobal)
            {
                await UpsertStatsAsync(connection, transaction, "hsgo_player_global_stats", false, item, cancellationToken);
                if (_settings.Tracking.PerMap && item.MapName is not null) await UpsertDimensionAsync(connection, transaction, "hsgo_map_stats", "map_name", "global", 0, item.MapName, item, cancellationToken);
                if (_settings.Tracking.PerWeapon && item.WeaponName is not null) await UpsertDimensionAsync(connection, transaction, "hsgo_weapon_stats", "weapon_name", "global", 0, item.WeaponName, item, cancellationToken);
            }
            if (item.ApplySeason && item.SeasonId is long seasonId)
            {
                await UpsertStatsAsync(connection, transaction, "hsgo_player_season_stats", true, item, cancellationToken);
                if (_settings.Tracking.PerMap && item.MapName is not null) await UpsertDimensionAsync(connection, transaction, "hsgo_map_stats", "map_name", "season", seasonId, item.MapName, item, cancellationToken);
                if (_settings.Tracking.PerWeapon && item.WeaponName is not null) await UpsertDimensionAsync(connection, transaction, "hsgo_weapon_stats", "weapon_name", "season", seasonId, item.WeaponName, item, cancellationToken);
            }
            applied++;
        }
        await transaction.CommitAsync(cancellationToken);
        return applied;
    }

    public MySqlConnection CreateConnection() => new(_connectionString);

    public static async Task SetUtcSessionAsync(MySqlConnection connection, CancellationToken token)
    {
        await using var command = new MySqlCommand("SET time_zone = '+00:00'", connection);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<bool> ClaimEventAsync(MySqlConnection connection, MySqlTransaction transaction, NormalizedStatEvent item, CancellationToken token)
    {
        const string sql = """
            INSERT IGNORE INTO hsgo_ingested_events
              (event_id, season_id, server_id, event_type, scoring_version, created_at)
            VALUES (UUID_TO_BIN(@event_id), @season_id, @server_id, @event_type, @scoring_version, @created_at)
            """;
        await using var command = new MySqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@event_id", item.EventId.ToString("D"));
        command.Parameters.AddWithValue("@season_id", item.SeasonId is null ? DBNull.Value : item.SeasonId);
        command.Parameters.AddWithValue("@server_id", item.ServerId);
        command.Parameters.AddWithValue("@event_type", item.EventType);
        command.Parameters.AddWithValue("@scoring_version", item.ScoringVersion);
        command.Parameters.AddWithValue("@created_at", item.ObservedAt.UtcDateTime);
        return await command.ExecuteNonQueryAsync(token) == 1;
    }

    private static async Task UpsertPlayerAsync(MySqlConnection connection, MySqlTransaction transaction, NormalizedStatEvent item, CancellationToken token)
    {
        const string sql = """
            INSERT INTO hsgo_players (steam_id64, last_name, first_seen_at, last_seen_at, updated_at)
            VALUES (@steam_id64, @last_name, @at, @at, @at)
            ON DUPLICATE KEY UPDATE
              last_name = COALESCE(VALUES(last_name), last_name),
              last_seen_at = GREATEST(last_seen_at, VALUES(last_seen_at)),
              updated_at = VALUES(updated_at)
            """;
        await using var command = new MySqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@steam_id64", item.Player.SteamId64);
        command.Parameters.AddWithValue("@last_name", item.Player.Name is null ? DBNull.Value : item.Player.Name);
        command.Parameters.AddWithValue("@at", item.ObservedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task UpsertStatsAsync(MySqlConnection connection, MySqlTransaction transaction, string table, bool seasonal, NormalizedStatEvent item, CancellationToken token)
    {
        var identityColumns = seasonal ? "season_id, server_id, steam_id64" : "server_id, steam_id64";
        var identityValues = seasonal ? "@season_id, @server_id, @steam_id64" : "@server_id, @steam_id64";
        var sql = $"""
            INSERT INTO {table} ({identityColumns}, kills, deaths, assists, headshot_kills, damage, score,
              rounds_played, rounds_won, rounds_lost, playtime_seconds, shots, hits, bomb_plants, bomb_defuses, updated_at)
            VALUES ({identityValues}, @kills, @deaths, @assists, @headshots, @damage, @score,
              @rounds_played, @rounds_won, @rounds_lost, @playtime, @shots, @hits, @plants, @defuses, @updated_at)
            ON DUPLICATE KEY UPDATE
              kills = kills + VALUES(kills), deaths = deaths + VALUES(deaths), assists = assists + VALUES(assists),
              headshot_kills = headshot_kills + VALUES(headshot_kills), damage = damage + VALUES(damage), score = score + VALUES(score),
              rounds_played = rounds_played + VALUES(rounds_played), rounds_won = rounds_won + VALUES(rounds_won),
              rounds_lost = rounds_lost + VALUES(rounds_lost), playtime_seconds = playtime_seconds + VALUES(playtime_seconds),
              shots = shots + VALUES(shots), hits = hits + VALUES(hits), bomb_plants = bomb_plants + VALUES(bomb_plants),
              bomb_defuses = bomb_defuses + VALUES(bomb_defuses), updated_at = VALUES(updated_at)
            """;
        await using var command = new MySqlCommand(sql, connection, transaction);
        AddStatParameters(command, item);
        if (seasonal) command.Parameters.AddWithValue("@season_id", item.SeasonId);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task UpsertDimensionAsync(MySqlConnection connection, MySqlTransaction transaction, string table, string column, string scope, long periodId, string value, NormalizedStatEvent item, CancellationToken token)
    {
        var sql = $"""
            INSERT INTO {table} (scope, period_id, server_id, steam_id64, {column}, kills, deaths, assists, headshot_kills,
              damage, score, rounds_played, rounds_won, rounds_lost, playtime_seconds, shots, hits, bomb_plants, bomb_defuses, updated_at)
            VALUES (@scope, @period_id, @server_id, @steam_id64, @dimension, @kills, @deaths, @assists, @headshots,
              @damage, @score, @rounds_played, @rounds_won, @rounds_lost, @playtime, @shots, @hits, @plants, @defuses, @updated_at)
            ON DUPLICATE KEY UPDATE
              kills = kills + VALUES(kills), deaths = deaths + VALUES(deaths), assists = assists + VALUES(assists),
              headshot_kills = headshot_kills + VALUES(headshot_kills), damage = damage + VALUES(damage), score = score + VALUES(score),
              rounds_played = rounds_played + VALUES(rounds_played), rounds_won = rounds_won + VALUES(rounds_won),
              rounds_lost = rounds_lost + VALUES(rounds_lost), playtime_seconds = playtime_seconds + VALUES(playtime_seconds),
              shots = shots + VALUES(shots), hits = hits + VALUES(hits), bomb_plants = bomb_plants + VALUES(bomb_plants),
              bomb_defuses = bomb_defuses + VALUES(bomb_defuses), updated_at = VALUES(updated_at)
            """;
        await using var command = new MySqlCommand(sql, connection, transaction);
        AddStatParameters(command, item);
        command.Parameters.AddWithValue("@scope", scope);
        command.Parameters.AddWithValue("@period_id", periodId);
        command.Parameters.AddWithValue("@dimension", value.Length > 128 ? value[..128] : value);
        await command.ExecuteNonQueryAsync(token);
    }

    private static void AddStatParameters(MySqlCommand command, NormalizedStatEvent item)
    {
        var d = item.Delta;
        command.Parameters.AddWithValue("@server_id", item.ServerId);
        command.Parameters.AddWithValue("@steam_id64", item.Player.SteamId64);
        command.Parameters.AddWithValue("@kills", d.Kills);
        command.Parameters.AddWithValue("@deaths", d.Deaths);
        command.Parameters.AddWithValue("@assists", d.Assists);
        command.Parameters.AddWithValue("@headshots", d.HeadshotKills);
        command.Parameters.AddWithValue("@damage", d.Damage);
        command.Parameters.AddWithValue("@score", d.Score);
        command.Parameters.AddWithValue("@rounds_played", d.RoundsPlayed);
        command.Parameters.AddWithValue("@rounds_won", d.RoundsWon);
        command.Parameters.AddWithValue("@rounds_lost", d.RoundsLost);
        command.Parameters.AddWithValue("@playtime", d.PlaytimeSeconds);
        command.Parameters.AddWithValue("@shots", d.Shots);
        command.Parameters.AddWithValue("@hits", d.Hits);
        command.Parameters.AddWithValue("@plants", d.BombPlants);
        command.Parameters.AddWithValue("@defuses", d.BombDefuses);
        command.Parameters.AddWithValue("@updated_at", item.ObservedAt.UtcDateTime);
    }
}
