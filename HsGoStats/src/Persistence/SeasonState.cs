namespace HsGoStats.Persistence;

public sealed class SeasonState
{
    private StoredSeason[] _seasons = [];

    public void Replace(IEnumerable<StoredSeason> seasons) => Volatile.Write(ref _seasons, seasons.OrderBy(x => x.StartsAtUtc).ToArray());

    public StoredSeason? At(DateTimeOffset instant)
    {
        var snapshot = Volatile.Read(ref _seasons);
        return snapshot.FirstOrDefault(x => x.Contains(instant));
    }

    public StoredSeason? ActiveAt(DateTimeOffset instant)
    {
        var season = At(instant);
        return season?.Status is "active" or "scheduled" ? season : null;
    }

    public StoredSeason? NextAfter(DateTimeOffset instant) =>
        Volatile.Read(ref _seasons).FirstOrDefault(x => x.StartsAtUtc > instant);
}
