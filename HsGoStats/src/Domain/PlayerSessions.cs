namespace HsGoStats.Domain;

public enum PlayerSessionSignalType { Open, Heartbeat, Close }

public sealed record PlayerSessionSignal(
    Guid EventId,
    Guid SessionId,
    DateTimeOffset ObservedAt,
    PlayerIdentity Player,
    PlayerSessionSignalType Type);

public sealed record ServerActivity24Hours(
    long UniquePlayers,
    long SessionsStarted,
    double AverageSessionSeconds);
