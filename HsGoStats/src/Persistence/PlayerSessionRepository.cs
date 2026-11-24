using HsGoStats.Domain;
using MySqlConnector;

namespace HsGoStats.Persistence;

public sealed class PlayerSessionRepository(MySqlStore store, string serverId)
{
    public async Task CloseOrphanedSessionsAsync(DateTimeOffset now, CancellationToken token)
    {
        await using var connection = store.CreateConnection();
        await connection.OpenAsync(token);
        await MySqlStore.SetUtcSessionAsync(connection, token);
        const string sql = """
            UPDATE hsgo_player_sessions
            SET disconnected_at_utc=last_seen_at_utc,
                duration_seconds=GREATEST(0,TIMESTAMPDIFF(SECOND,connected_at_utc,last_seen_at_utc)),
                close_reason='plugin_restart',updated_at=@now
            WHERE server_id=@server AND disconnected_at_utc IS NULL
            """;
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@server", serverId);
        command.Parameters.AddWithValue("@now", now.UtcDateTime);
        await command.ExecuteNonQueryAsync(token);
    }

    public async Task ProcessBatchAsync(IReadOnlyCollection<PlayerSessionSignal> signals, CancellationToken token)
    {
        if (signals.Count == 0) return;
        await using var connection = store.CreateConnection();
        await connection.OpenAsync(token);
        await MySqlStore.SetUtcSessionAsync(connection, token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        foreach (var signal in signals.OrderBy(x => x.ObservedAt))
        {
            await UpsertPlayerAsync(connection, transaction, signal, token);
            if (signal.Type == PlayerSessionSignalType.Open) await OpenAsync(connection, transaction, signal, token);
            else if (signal.Type == PlayerSessionSignalType.Heartbeat) await HeartbeatAsync(connection, transaction, signal, token);
            else await CloseAsync(connection, transaction, signal, token);
        }
        await transaction.CommitAsync(token);
    }

    public async Task<ServerActivity24Hours> GetLast24HoursAsync(DateTimeOffset now, CancellationToken token)
    {
        await using var connection = store.CreateConnection();
        await connection.OpenAsync(token);
        await MySqlStore.SetUtcSessionAsync(connection, token);
        const string sql = """
            SELECT
              COUNT(DISTINCT CASE WHEN connected_at_utc<@now AND COALESCE(disconnected_at_utc,last_seen_at_utc)>=@from THEN steam_id64 END),
              SUM(CASE WHEN connected_at_utc>=@from AND connected_at_utc<@now THEN 1 ELSE 0 END),
              COALESCE(AVG(CASE WHEN connected_at_utc>=@from AND connected_at_utc<@now
                THEN GREATEST(0,TIMESTAMPDIFF(MICROSECOND,connected_at_utc,LEAST(COALESCE(disconnected_at_utc,last_seen_at_utc),@now))/1000000.0) END),0)
            FROM hsgo_player_sessions
            WHERE server_id=@server AND connected_at_utc<@now AND COALESCE(disconnected_at_utc,last_seen_at_utc)>=@from
            """;
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@server", serverId);
        command.Parameters.AddWithValue("@from", now.AddHours(-24).UtcDateTime);
        command.Parameters.AddWithValue("@now", now.UtcDateTime);
        await using var reader = await command.ExecuteReaderAsync(token);
        await reader.ReadAsync(token);
        return new(reader.GetInt64(0), reader.IsDBNull(1) ? 0 : Convert.ToInt64(reader.GetValue(1)), reader.GetDouble(2));
    }

    private async Task OpenAsync(MySqlConnection connection, MySqlTransaction transaction, PlayerSessionSignal signal, CancellationToken token)
    {
        const string sql = """
            INSERT IGNORE INTO hsgo_player_sessions
              (session_id,server_id,steam_id64,connected_at_utc,last_seen_at_utc,updated_at)
            VALUES (UUID_TO_BIN(@session),@server,@steam,@at,@at,@at)
            """;
        await using var command = Command(sql, connection, transaction, signal);
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task HeartbeatAsync(MySqlConnection connection, MySqlTransaction transaction, PlayerSessionSignal signal, CancellationToken token)
    {
        const string sql = """
            UPDATE hsgo_player_sessions
            SET last_seen_at_utc=GREATEST(last_seen_at_utc,@at),updated_at=@at
            WHERE session_id=UUID_TO_BIN(@session) AND server_id=@server AND disconnected_at_utc IS NULL
            """;
        await using var command = Command(sql, connection, transaction, signal);
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task CloseAsync(MySqlConnection connection, MySqlTransaction transaction, PlayerSessionSignal signal, CancellationToken token)
    {
        const string sql = """
            UPDATE hsgo_player_sessions
            SET last_seen_at_utc=GREATEST(last_seen_at_utc,@at),disconnected_at_utc=@at,
                duration_seconds=GREATEST(0,TIMESTAMPDIFF(SECOND,connected_at_utc,@at)),
                close_reason='disconnect',updated_at=@at
            WHERE session_id=UUID_TO_BIN(@session) AND server_id=@server AND disconnected_at_utc IS NULL
            """;
        await using var command = Command(sql, connection, transaction, signal);
        await command.ExecuteNonQueryAsync(token);
    }

    private MySqlCommand Command(string sql, MySqlConnection connection, MySqlTransaction transaction, PlayerSessionSignal signal)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@session", signal.SessionId.ToString("D"));
        command.Parameters.AddWithValue("@server", serverId);
        command.Parameters.AddWithValue("@steam", signal.Player.SteamId64);
        command.Parameters.AddWithValue("@at", signal.ObservedAt.UtcDateTime);
        return command;
    }

    private static async Task UpsertPlayerAsync(MySqlConnection connection, MySqlTransaction transaction,
        PlayerSessionSignal signal, CancellationToken token)
    {
        const string sql = """
            INSERT INTO hsgo_players(steam_id64,last_name,first_seen_at,last_seen_at,updated_at)
            VALUES(@steam,@name,@at,@at,@at)
            ON DUPLICATE KEY UPDATE last_name=COALESCE(VALUES(last_name),last_name),
              last_seen_at=GREATEST(last_seen_at,VALUES(last_seen_at)),updated_at=VALUES(updated_at)
            """;
        await using var command = new MySqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@steam", signal.Player.SteamId64);
        command.Parameters.AddWithValue("@name", signal.Player.Name is null ? DBNull.Value : signal.Player.Name);
        command.Parameters.AddWithValue("@at", signal.ObservedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(token);
    }
}
