using HsGoStats.Configuration;

namespace HsGoStats.Domain;

public sealed record SeasonPeriod(int Number, string Name, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc)
{
    public bool Contains(DateTimeOffset instant) => instant >= StartsAtUtc && instant < EndsAtUtc;
}

public sealed class SeasonCalendar
{
    public DateTimeOffset AnchorUtc { get; }
    public int DurationDays { get; }
    public int InitialNumber { get; }
    public string NameFormat { get; }

    public SeasonCalendar(DateTimeOffset anchor, int durationDays, int initialNumber, string nameFormat)
    {
        AnchorUtc = anchor.ToUniversalTime();
        DurationDays = durationDays;
        InitialNumber = initialNumber;
        NameFormat = nameFormat;
    }

    public static SeasonCalendar FromConfig(SeasonConfig config) =>
        new(DateTimeOffset.Parse(config.StartDate), config.DurationDays, config.InitialSeasonNumber, config.NameFormat);

    public SeasonPeriod? At(DateTimeOffset instant)
    {
        var utc = instant.ToUniversalTime();
        if (utc < AnchorUtc) return null;
        var periodTicks = TimeSpan.FromDays(DurationDays).Ticks;
        var index = (utc.Ticks - AnchorUtc.Ticks) / periodTicks;
        return ByIndex(checked((int)index));
    }

    public SeasonPeriod ByNumber(int number) => ByIndex(number - InitialNumber);

    public SeasonPeriod ByIndex(int index)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        var start = AnchorUtc.AddDays((long)DurationDays * index);
        var number = checked(InitialNumber + index);
        return new(number, NameFormat.Replace("{number}", number.ToString(), StringComparison.Ordinal), start, start.AddDays(DurationDays));
    }
}
