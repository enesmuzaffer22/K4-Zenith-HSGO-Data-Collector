namespace HsGoStats.Persistence;

public sealed record SeasonLeaderboardBlacklistRule(
    ulong SteamId64,
    int EffectiveFromSeasonNumber,
    int? EffectiveUntilSeasonNumber);

public sealed class SeasonLeaderboardBlacklistState
{
    private SeasonLeaderboardBlacklistRule[] _rules = [];

    public void Replace(IEnumerable<SeasonLeaderboardBlacklistRule> rules) =>
        Volatile.Write(ref _rules, rules.ToArray());

    public bool IsExcluded(ulong steamId64, int seasonNumber)
    {
        var snapshot = Volatile.Read(ref _rules);
        return snapshot.Any(rule =>
            rule.SteamId64 == steamId64 &&
            seasonNumber >= rule.EffectiveFromSeasonNumber &&
            (rule.EffectiveUntilSeasonNumber is null || seasonNumber < rule.EffectiveUntilSeasonNumber));
    }

    public int CountAt(int seasonNumber)
    {
        var snapshot = Volatile.Read(ref _rules);
        return snapshot.Count(rule =>
            seasonNumber >= rule.EffectiveFromSeasonNumber &&
            (rule.EffectiveUntilSeasonNumber is null || seasonNumber < rule.EffectiveUntilSeasonNumber));
    }
}
