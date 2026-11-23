using HsGoStats.Configuration;
using HsGoStats.Domain;
using HsGoStats.Persistence;
using Xunit;

namespace HsGoStats.Tests;

public sealed class DomainTests
{
    [Theory]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, true, false)]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, false, true, true, true)]
    public void TrackingTruthTable(bool enabled, bool preseason, bool active, bool global, bool season)
    {
        var actual = TrackingTargets.Resolve(enabled, preseason, active);
        Assert.Equal(global, actual.ApplyGlobal);
        Assert.Equal(season, actual.ApplySeason);
    }

    [Fact]
    public void SeasonBlacklistKeepsGlobalButSuppressesSeasonTarget()
    {
        var actual = TrackingTargets.Resolve(seasonEnabled: true, preseasonTracking: true, hasActiveSeason: true, excludeFromSeason: true);
        Assert.True(actual.ApplyGlobal);
        Assert.False(actual.ApplySeason);
    }

    [Fact]
    public void InvalidSeasonBlacklistSteamIdIsRejected()
    {
        var config = new HsGoStatsConfig();
        config.Season.LeaderboardBlacklistSteamIds.Add("not-a-steamid");
        var result = ConfigValidator.Validate(config);
        Assert.Contains(result.Errors, value => value.Contains("LeaderboardBlacklistSteamIds"));
    }

    [Fact]
    public void BlacklistRuleLeavesPastSeasonAndStartsWithNextSeason()
    {
        var state = new SeasonLeaderboardBlacklistState();
        state.Replace([new SeasonLeaderboardBlacklistRule(76561198000000001, 2, null)]);
        Assert.False(state.IsExcluded(76561198000000001, 1));
        Assert.True(state.IsExcluded(76561198000000001, 2));
        Assert.True(state.IsExcluded(76561198000000001, 3));
    }

    [Fact]
    public void ThirtyDayCalendarHasExclusiveBoundaries()
    {
        var calendar = new SeasonCalendar(DateTimeOffset.Parse("2026-12-01T00:00:00+03:00"), 30, 1, "Season {number}");
        Assert.Null(calendar.At(DateTimeOffset.Parse("2026-11-30T23:59:59+03:00")));
        Assert.Equal(1, calendar.At(DateTimeOffset.Parse("2026-12-01T00:00:00+03:00"))!.Number);
        Assert.Equal(2, calendar.At(DateTimeOffset.Parse("2026-12-31T00:00:00+03:00"))!.Number);
        Assert.Equal(3, calendar.At(DateTimeOffset.Parse("2027-01-30T00:00:00+03:00"))!.Number);
    }

    [Fact]
    public void SuicideUsesSinglePenaltyButStillAllowsDeathCounter()
    {
        var policy = new ScoringPolicy(new ScoringConfig());
        var delta = new StatDelta(Deaths: 1, Score: policy.Death(suicide: true));
        Assert.Equal(1, delta.Deaths);
        Assert.Equal(-10, delta.Score);
    }

    [Fact]
    public void KillAndHeadshotAreAdditive()
    {
        Assert.Equal(15, new ScoringPolicy(new ScoringConfig()).Kill(headshot: true));
    }

    [Fact]
    public void DefaultConfigRequiresCredentials()
    {
        var result = ConfigValidator.Validate(new HsGoStatsConfig());
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, value => value.Contains("Username"));
        Assert.Contains(result.Errors, value => value.Contains("Password"));
    }
}
