using System.Text.Json;
using HsGoStats.Configuration;
using MySqlConnector;

namespace HsGoStats.Persistence;

public sealed record LoadedPluginSettings(HsGoStatsRuntimeSettings Settings, long Revision);

public sealed class PluginSettingsRepository(MySqlStore store, HsGoStatsConfig bootstrap)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly (string Key, string Description)[] Definitions =
    [
        ("enabled", "Master switch for statistics collection."),
        ("season", "Manual season names, boundaries and season leaderboard exclusions."),
        ("scoring", "Legacy score values."),
        ("game_xp", "Gameplay TP values."),
        ("rating", "TP tier thresholds and colors."),
        ("weekly_missions", "Weekly mission rotation, rewards and catalog."),
        ("chat_commands", "Player command and reminder settings."),
        ("tracking", "Event and dimension tracking switches."),
        ("bans", "Ban archive feature switches."),
        ("persistence", "Queue, batch, retry and spool settings."),
        ("logging", "Plugin logging and player-name storage settings.")
    ];

    public async Task<long> GetRevisionTokenAsync(CancellationToken token)
    {
        await using var connection = store.CreateConnection();
        await connection.OpenAsync(token);
        await MySqlStore.SetUtcSessionAsync(connection, token);
        await using var command = new MySqlCommand(
            "SELECT COALESCE(SUM(revision),0) FROM hsgo_plugin_settings WHERE server_id=@server", connection);
        command.Parameters.AddWithValue("@server", bootstrap.ServerId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token));
    }

    public async Task<LoadedPluginSettings> LoadOrSeedAsync(
        HsGoStatsRuntimeSettings defaults,
        CancellationToken token)
    {
        await using var connection = store.CreateConnection();
        await connection.OpenAsync(token);
        await MySqlStore.SetUtcSessionAsync(connection, token);
        await using var transaction = await connection.BeginTransactionAsync(token);

        if (bootstrap.SettingsStore.SeedMissingRows)
        {
            foreach (var definition in Definitions)
                await SeedAsync(connection, transaction, definition.Key, definition.Description,
                    SerializeSection(definition.Key, defaults), token);
        }

        var rows = new Dictionary<string, (string Json, long Revision)>(StringComparer.OrdinalIgnoreCase);
        const string selectSql = "SELECT setting_key,setting_json,revision FROM hsgo_plugin_settings WHERE server_id=@server FOR UPDATE";
        await using (var command = new MySqlCommand(selectSql, connection, transaction))
        {
            command.Parameters.AddWithValue("@server", bootstrap.ServerId);
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) rows[reader.GetString(0)] = (reader.GetString(1), reader.GetInt64(2));
        }

        var missing = Definitions.Select(x => x.Key).Where(key => !rows.ContainsKey(key)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Database plugin settings are incomplete for server '{bootstrap.ServerId}': {string.Join(", ", missing)}.");

        var settings = new HsGoStatsRuntimeSettings
        {
            Enabled = Deserialize<bool>(rows, "enabled"),
            Season = Deserialize<SeasonConfig>(rows, "season"),
            Scoring = Deserialize<ScoringConfig>(rows, "scoring"),
            GameXp = Deserialize<GameXpConfig>(rows, "game_xp"),
            Rating = Deserialize<RatingConfig>(rows, "rating"),
            WeeklyMissions = Deserialize<WeeklyMissionsConfig>(rows, "weekly_missions"),
            ChatCommands = Deserialize<ChatCommandsConfig>(rows, "chat_commands"),
            Tracking = Deserialize<TrackingConfig>(rows, "tracking"),
            Bans = Deserialize<BansConfig>(rows, "bans"),
            Persistence = Deserialize<PersistenceConfig>(rows, "persistence"),
            Logging = Deserialize<LoggingConfig>(rows, "logging")
        };
        await transaction.CommitAsync(token);
        return new(settings, checked(rows.Values.Sum(x => x.Revision)));
    }

    private async Task SeedAsync(MySqlConnection connection, MySqlTransaction transaction, string key,
        string description, string json, CancellationToken token)
    {
        const string sql = """
            INSERT IGNORE INTO hsgo_plugin_settings
              (server_id,setting_key,setting_json,description,revision,updated_by)
            VALUES (@server,@key,@json,@description,1,'plugin-bootstrap')
            """;
        await using var command = new MySqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@server", bootstrap.ServerId);
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@json", json);
        command.Parameters.AddWithValue("@description", description);
        await command.ExecuteNonQueryAsync(token);
    }

    private static T Deserialize<T>(IReadOnlyDictionary<string, (string Json, long Revision)> rows, string key) =>
        JsonSerializer.Deserialize<T>(rows[key].Json, JsonOptions)
        ?? throw new InvalidOperationException($"Database plugin setting '{key}' cannot be null.");

    private static string SerializeSection(string key, HsGoStatsRuntimeSettings settings) => key switch
    {
        "enabled" => JsonSerializer.Serialize(settings.Enabled, JsonOptions),
        "season" => JsonSerializer.Serialize(settings.Season, JsonOptions),
        "scoring" => JsonSerializer.Serialize(settings.Scoring, JsonOptions),
        "game_xp" => JsonSerializer.Serialize(settings.GameXp, JsonOptions),
        "rating" => JsonSerializer.Serialize(settings.Rating, JsonOptions),
        "weekly_missions" => JsonSerializer.Serialize(settings.WeeklyMissions, JsonOptions),
        "chat_commands" => JsonSerializer.Serialize(settings.ChatCommands, JsonOptions),
        "tracking" => JsonSerializer.Serialize(settings.Tracking, JsonOptions),
        "bans" => JsonSerializer.Serialize(settings.Bans, JsonOptions),
        "persistence" => JsonSerializer.Serialize(settings.Persistence, JsonOptions),
        "logging" => JsonSerializer.Serialize(settings.Logging, JsonOptions),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null)
    };
}
