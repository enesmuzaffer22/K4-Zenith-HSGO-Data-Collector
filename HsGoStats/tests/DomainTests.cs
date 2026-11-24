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
    public void ManualSeasonScheduleAllowsCustomNamesAndGaps()
    {
        var config = ValidConfig();
        config.Season.Schedule =
        [
            new("Kış Ligi", "2026-12-01T12:30:00+03:00", "2026-12-20T18:00:00+03:00"),
            new("Yeni Yıl Kupası", "2027-01-05T00:00:00+03:00", "2027-02-14T21:45:00+03:00")
        ];

        var result = ConfigValidator.Validate(config);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public void OverlappingManualSeasonsAreRejected()
    {
        var config = ValidConfig();
        config.Season.Schedule =
        [
            new("Bir", "2026-12-01T00:00:00+03:00", "2027-01-01T00:00:00+03:00"),
            new("İki", "2026-12-31T00:00:00+03:00", "2027-02-01T00:00:00+03:00")
        ];

        var result = ConfigValidator.Validate(config);

        Assert.Contains(result.Errors, value => value.Contains("overlaps"));
    }

    [Fact]
    public void ManualSeasonDatesRequireExplicitOffsets()
    {
        var config = ValidConfig();
        config.Season.Schedule = [new("Ofsetsiz", "2026-12-01T00:00:00", "2026-12-31T00:00:00")];

        var result = ConfigValidator.Validate(config);

        Assert.Contains(result.Errors, value => value.Contains("explicit offset"));
    }

    [Fact]
    public void ManualSeasonStateHonorsExactBoundariesAndGaps()
    {
        var state = new SeasonState();
        state.Replace(
        [
            new(1, 1, "Bir", "active", DateTimeOffset.Parse("2026-12-01T00:00:00Z"), DateTimeOffset.Parse("2026-12-10T00:00:00Z"), "v1"),
            new(2, 2, "İki", "scheduled", DateTimeOffset.Parse("2026-12-15T12:00:00Z"), DateTimeOffset.Parse("2026-12-20T18:00:00Z"), "v1")
        ]);

        Assert.Null(state.ActiveAt(DateTimeOffset.Parse("2026-11-30T23:59:59Z")));
        Assert.Equal("Bir", state.ActiveAt(DateTimeOffset.Parse("2026-12-01T00:00:00Z"))!.Name);
        Assert.Null(state.ActiveAt(DateTimeOffset.Parse("2026-12-10T00:00:00Z")));
        Assert.Null(state.ActiveAt(DateTimeOffset.Parse("2026-12-14T00:00:00Z")));
        Assert.Equal("İki", state.ActiveAt(DateTimeOffset.Parse("2026-12-15T12:00:00Z"))!.Name);
        Assert.Null(state.ActiveAt(DateTimeOffset.Parse("2026-12-20T18:00:00Z")));
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

    [Fact]
    public void WeeklyCatalogContainsExactProductSet()
    {
        var catalog = MissionCatalogDefaults.Create();
        Assert.Equal(100, catalog.Count);
        Assert.Equal(Enumerable.Range(1, 100), catalog.Select(x => x.Id).Order());
        Assert.Equal(17_145, catalog.Sum(x => x.RewardXp));
        Assert.Equal(9, catalog.Select(x => x.Category).Distinct().Count());
    }

    [Fact]
    public void RestrictedMissionWeaponsAreNotUsed()
    {
        var config = new HsGoStatsConfig();
        var excluded = config.WeeklyMissions.ExcludedMissionWeapons.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(config.WeeklyMissions.Catalog.SelectMany(x => x.Weapons), excluded.Contains);
    }

    [Fact]
    public void SpecialWeaponMissionRulesMatchSpecification()
    {
        var catalog = MissionCatalogDefaults.Create().ToDictionary(x => x.Id);
        Assert.Equal(new[] { "mp9", "mac10", "mp7", "mp5sd", "ump45" }, catalog[33].Weapons);
        Assert.True(catalog[34].Headshot);
        Assert.Equal(2, catalog[35].MinimumRoundKills);
        Assert.True(catalog[51].Scoped);
        Assert.False(catalog[51].NoScope);
        Assert.Equal(2, catalog[52].MinimumRoundKills);
    }

    [Theory]
    [InlineData(0, "Iron")]
    [InlineData(-50, "Iron")]
    [InlineData(999, "Iron")]
    [InlineData(1000, "Bronze")]
    [InlineData(2999, "Bronze")]
    [InlineData(3000, "Silver")]
    [InlineData(59999, "Master")]
    [InlineData(60000, "Legend")]
    [InlineData(90000, "Legend")]
    public void RatingBoundariesResolveCorrectly(long xp, string expected)
    {
        Assert.Equal(expected, RatingResolver.Resolve(xp, new RatingConfig()).Tier.Name);
    }

    [Fact]
    public void SpecialKillsUseHighestSingleXpValue()
    {
        var policy = new HsrScoringPolicy(new GameXpConfig());
        Assert.Equal(25, policy.Kill("awp", headshot: true, noScope: true, throughSmoke: true, penetrated: true, attackerBlind: true, attackerInAir: true));
        Assert.Equal(20, policy.Kill("knife", headshot: true, noScope: false, throughSmoke: false, penetrated: false, attackerBlind: false, attackerInAir: false));
        Assert.Equal(7, policy.Kill("ak47", headshot: true, noScope: false, throughSmoke: false, penetrated: false, attackerBlind: false, attackerInAir: false));
    }

    [Fact]
    public void WeeklyWindowUsesIstanbulMondayBoundary()
    {
        var config = new WeeklyRotationConfig();
        var before = WeeklyWindowCalculator.At(DateTimeOffset.Parse("2026-11-22T23:59:59+03:00"), config);
        var boundary = WeeklyWindowCalculator.At(DateTimeOffset.Parse("2026-11-23T00:00:00+03:00"), config);
        Assert.Equal(DateTimeOffset.Parse("2026-11-16T00:00:00+03:00").ToUniversalTime(), before.StartsAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-11-23T00:00:00+03:00").ToUniversalTime(), boundary.StartsAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-11-30T00:00:00+03:00").ToUniversalTime(), boundary.EndsAtUtc);
    }

    [Fact]
    public void FirstWeeklyWindowStartsExactlyWithSeason()
    {
        var config = new WeeklyRotationConfig();
        var seasonStart = DateTimeOffset.Parse("2026-11-25T12:30:00+03:00");
        var seasonEnd = DateTimeOffset.Parse("2026-12-20T18:00:00+03:00");

        var window = WeeklyWindowCalculator.At(seasonStart, config, seasonStart, seasonEnd);

        Assert.Equal(seasonStart, window.StartsAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-11-30T00:00:00+03:00").ToUniversalTime(), window.EndsAtUtc);
    }

    [Fact]
    public void LastWeeklyWindowEndsExactlyWithSeason()
    {
        var config = new WeeklyRotationConfig();
        var seasonStart = DateTimeOffset.Parse("2026-11-25T12:30:00+03:00");
        var seasonEnd = DateTimeOffset.Parse("2026-12-04T18:00:00+03:00");

        var window = WeeklyWindowCalculator.At(DateTimeOffset.Parse("2026-12-01T00:00:00+03:00"), config, seasonStart, seasonEnd);

        Assert.Equal(DateTimeOffset.Parse("2026-11-30T00:00:00+03:00").ToUniversalTime(), window.StartsAtUtc);
        Assert.Equal(seasonEnd, window.EndsAtUtc);
    }

    [Fact]
    public void WeeklyWindowCannotExistOutsideSeason()
    {
        var config = new WeeklyRotationConfig();
        var seasonStart = DateTimeOffset.Parse("2026-12-01T00:00:00+03:00");
        var seasonEnd = DateTimeOffset.Parse("2026-12-31T00:00:00+03:00");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WeeklyWindowCalculator.At(seasonEnd, config, seasonStart, seasonEnd));
    }

    [Fact]
    public void CompleteDefaultProductConfigIsValidWithCredentials()
    {
        var config = new HsGoStatsConfig();
        config.Database.Username = "test";
        config.Database.Password = "test";
        var result = ConfigValidator.Validate(config);
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public void ScopedMissionRejectsNoScopeAndAcceptsScopedKill()
    {
        var mission = MissionCatalogDefaults.Create().Single(x => x.Id == 51);
        Assert.False(MissionEvaluator.Evaluate(mission, Signal("Kill", "awp", noScope: true)).Matched);
        Assert.True(MissionEvaluator.Evaluate(mission, Signal("Kill", "awp", noScope: false)).Matched);
    }

    [Fact]
    public void QualifiedRoundMissionsAdvanceAtMostOncePerRound()
    {
        var catalog = MissionCatalogDefaults.Create().ToDictionary(x => x.Id);
        var rifleRound = Signal("RoundEnd", roundWeapons: new Dictionary<string, int> { ["ak47"] = 2 });
        var scoutRound = Signal("RoundEnd", roundWeapons: new Dictionary<string, int> { ["ssg08"] = 2 });
        var rifle = MissionEvaluator.Evaluate(catalog[35], rifleRound);
        var scout = MissionEvaluator.Evaluate(catalog[52], scoutRound);
        Assert.True(rifle.Matched); Assert.Equal(1, rifle.Amount); Assert.False(rifle.Replace);
        Assert.True(scout.Matched); Assert.Equal(1, scout.Amount); Assert.False(scout.Replace);
    }

    [Fact]
    public void RoundFlashMissionReplacesRatherThanAccumulatesProgress()
    {
        var mission = MissionCatalogDefaults.Create().Single(x => x.Id == 70);
        var result = MissionEvaluator.Evaluate(mission, Signal("RoundEnd", flashBlinds: 1));
        Assert.True(result.Matched);
        Assert.True(result.Replace);
        Assert.Equal(1, result.Amount);
    }

    private static MissionSignal Signal(string eventName, string? weapon = null, bool noScope = false,
        IReadOnlyDictionary<string, int>? roundWeapons = null, int flashBlinds = 0) =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, new PlayerIdentity(1, null), eventName, 1, true, true,
            Weapon: weapon, NoScope: noScope, RoundFlashBlinds: flashBlinds, RoundWeaponKills: roundWeapons);

    private static HsGoStatsConfig ValidConfig()
    {
        var config = new HsGoStatsConfig();
        config.Database.Username = "test";
        config.Database.Password = "test";
        return config;
    }
}
