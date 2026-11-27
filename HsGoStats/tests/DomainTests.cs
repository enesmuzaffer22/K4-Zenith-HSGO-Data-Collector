using HsGoStats.Configuration;
using HsGoStats.Domain;
using HsGoStats.Persistence;
using System.Text.Json;
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

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, false)]
    [InlineData(0, 1, false)]
    [InlineData(1, 1, true)]
    [InlineData(4, 5, true)]
    public void PointsRequireAtLeastOnePlayerOnEachTeam(int terroristPlayers, int counterTerroristPlayers, bool expected)
    {
        Assert.Equal(expected, PointAwardEligibility.HasOpposingPlayers(terroristPlayers, counterTerroristPlayers));
    }

    [Fact]
    public void InvalidSeasonBlacklistSteamIdIsRejected()
    {
        var config = ValidConfig();
        var settings = new HsGoStatsRuntimeSettings();
        settings.Season.LeaderboardBlacklistSteamIds.Add("not-a-steamid");
        var result = ConfigValidator.Validate(config, settings);
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
        var settings = new HsGoStatsRuntimeSettings();
        settings.Season.Schedule =
        [
            new("Kış Ligi", "2026-12-01T12:30:00+03:00", "2026-12-20T18:00:00+03:00"),
            new("Yeni Yıl Kupası", "2027-01-05T00:00:00+03:00", "2027-02-14T21:45:00+03:00")
        ];

        var result = ConfigValidator.Validate(config, settings);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public void OverlappingManualSeasonsAreRejected()
    {
        var config = ValidConfig();
        var settings = new HsGoStatsRuntimeSettings();
        settings.Season.Schedule =
        [
            new("Bir", "2026-12-01T00:00:00+03:00", "2027-01-01T00:00:00+03:00"),
            new("İki", "2026-12-31T00:00:00+03:00", "2027-02-01T00:00:00+03:00")
        ];

        var result = ConfigValidator.Validate(config, settings);

        Assert.Contains(result.Errors, value => value.Contains("overlaps"));
    }

    [Fact]
    public void ManualSeasonDatesRequireExplicitOffsets()
    {
        var config = ValidConfig();
        var settings = new HsGoStatsRuntimeSettings();
        settings.Season.Schedule = [new("Ofsetsiz", "2026-12-01T00:00:00", "2026-12-31T00:00:00")];

        var result = ConfigValidator.Validate(config, settings);

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
    public void LocalBootstrapConfigDoesNotExposeRuntimeSections()
    {
        Assert.NotNull(typeof(HsGoStatsConfig).GetProperty(nameof(HsGoStatsConfig.Database)));
        Assert.NotNull(typeof(HsGoStatsConfig).GetProperty(nameof(HsGoStatsConfig.SettingsStore)));
        Assert.Null(typeof(HsGoStatsConfig).GetProperty("Season"));
        Assert.Null(typeof(HsGoStatsConfig).GetProperty("WeeklyMissions"));
        Assert.Null(typeof(HsGoStatsConfig).GetProperty("Scoring"));
    }

    [Fact]
    public void LegacyJsonRuntimeSectionsSeedDatabaseSettings()
    {
        const string json = """
            {
              "ServerId":"legacy-server",
              "Enabled":false,
              "Season":{"Enabled":true,"Timezone":"UTC","PreseasonTracking":false}
            }
            """;

        var bootstrap = JsonSerializer.Deserialize<HsGoStatsConfig>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { PropertyNameCaseInsensitive = true })!;
        var seed = bootstrap.CreateRuntimeSeed();

        Assert.False(seed.Enabled);
        Assert.Equal("UTC", seed.Season.Timezone);
        Assert.False(seed.Season.PreseasonTracking);
    }

    [Fact]
    public void PlayerSessionSignalRoundTripsThroughDurableJson()
    {
        var original = new PlayerSessionSignal(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.Parse("2026-11-24T12:00:00Z"),
            new PlayerIdentity(76561198000000001, "Test"), PlayerSessionSignalType.Heartbeat);

        var restored = JsonSerializer.Deserialize<PlayerSessionSignal>(JsonSerializer.Serialize(original, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(original, restored);
    }

    [Fact]
    public void SchemaContainsDatabaseSettingsAndPlayerSessions()
    {
        var type = typeof(MySqlStore).Assembly.GetType("HsGoStats.Persistence.SchemaSql")!;
        var commands = (string[])type.GetField("Commands", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)!.GetValue(null)!;
        var schema = string.Join("\n", commands);
        Assert.Contains("CREATE TABLE IF NOT EXISTS hsgo_plugin_settings", schema);
        Assert.Contains("CREATE TABLE IF NOT EXISTS hsgo_player_sessions", schema);
        Assert.Contains("VALUES (1), (2), (3), (4), (5)", schema);
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
        var settings = new HsGoStatsRuntimeSettings();
        var excluded = settings.WeeklyMissions.ExcludedMissionWeapons.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(settings.WeeklyMissions.Catalog.SelectMany(x => x.Weapons), excluded.Contains);
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
    [InlineData(-50, "Silver I")]
    [InlineData(0, "Silver I")]
    [InlineData(499, "Silver I")]
    [InlineData(500, "Silver II")]
    [InlineData(999, "Silver II")]
    [InlineData(1000, "Silver III")]
    [InlineData(1749, "Silver III")]
    [InlineData(1750, "Silver IV")]
    [InlineData(2749, "Silver IV")]
    [InlineData(2750, "Silver Elite")]
    [InlineData(3999, "Silver Elite")]
    [InlineData(4000, "Silver Elite Master")]
    [InlineData(5499, "Silver Elite Master")]
    [InlineData(5500, "Gold Nova I")]
    [InlineData(7499, "Gold Nova I")]
    [InlineData(7500, "Gold Nova II")]
    [InlineData(9999, "Gold Nova II")]
    [InlineData(10000, "Gold Nova III")]
    [InlineData(12999, "Gold Nova III")]
    [InlineData(13000, "Gold Nova Master")]
    [InlineData(16499, "Gold Nova Master")]
    [InlineData(16500, "Master Guardian I")]
    [InlineData(20499, "Master Guardian I")]
    [InlineData(20500, "Master Guardian II")]
    [InlineData(24999, "Master Guardian II")]
    [InlineData(25000, "Master Guardian Elite")]
    [InlineData(29999, "Master Guardian Elite")]
    [InlineData(30000, "Distinguished Master Guardian")]
    [InlineData(35999, "Distinguished Master Guardian")]
    [InlineData(36000, "Legendary Eagle")]
    [InlineData(42999, "Legendary Eagle")]
    [InlineData(43000, "Legendary Eagle Master")]
    [InlineData(50999, "Legendary Eagle Master")]
    [InlineData(51000, "Supreme Master First Class")]
    [InlineData(59999, "Supreme Master First Class")]
    [InlineData(60000, "Global Elite")]
    [InlineData(90000, "Global Elite")]
    public void RatingBoundariesResolveCorrectly(long xp, string expected)
    {
        Assert.Equal(expected, RatingResolver.Resolve(xp, new RatingConfig()).Tier.Name);
    }

    [Theory]
    [InlineData(200, "Silver I", "Silver II", 300)]
    [InlineData(500, "Silver II", "Silver III", 500)]
    [InlineData(59_999, "Supreme Master First Class", "Global Elite", 1)]
    public void RatingReportsDistanceToNextLevel(long xp, string current, string next, long remaining)
    {
        var rating = RatingResolver.Resolve(xp, new RatingConfig());

        Assert.Equal(current, rating.Tier.Name);
        Assert.Equal(next, rating.NextTier!.Name);
        Assert.Equal(remaining, rating.XpToNext);
    }

    [Fact]
    public void HighestRatingHasNoNextLevel()
    {
        var rating = RatingResolver.Resolve(60_000, new RatingConfig());

        Assert.Equal("Global Elite", rating.Tier.Name);
        Assert.Null(rating.NextTier);
        Assert.Null(rating.XpToNext);
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
    public void ScoreboardUsesSeasonTpWhenAnActiveSeasonHasAPlayerBalance()
    {
        var snapshot = new ScoreboardTpSnapshot(1, 20_000, 6_000);
        Assert.Equal(6_000, ScoreboardRatingSelector.SelectTp(snapshot, true, new ScoreboardRankConfig()));
    }

    [Fact]
    public void ScoreboardFallsBackToGlobalTpOutsideSeasonOrBeforeSeasonBalanceExists()
    {
        var config = new ScoreboardRankConfig();
        Assert.Equal(20_000, ScoreboardRatingSelector.SelectTp(new(1, 20_000, null), true, config));
        Assert.Equal(20_000, ScoreboardRatingSelector.SelectTp(new(1, 20_000, 6_000), false, config));
    }

    [Fact]
    public void ScoreboardCanRefuseGlobalFallbackDuringAnActiveSeason()
    {
        var config = new ScoreboardRankConfig { FallbackToGlobal = false };
        Assert.Null(ScoreboardRatingSelector.SelectTp(new(1, 20_000, null), true, config));
    }

    [Fact]
    public void DefaultRatingContainsAllEighteenCompetitiveTiersInOrder()
    {
        var tiers = new RatingConfig().Tiers;
        Assert.Equal(18, tiers.Count);
        Assert.Equal(new[]
        {
            "Silver I", "Silver II", "Silver III", "Silver IV", "Silver Elite", "Silver Elite Master",
            "Gold Nova I", "Gold Nova II", "Gold Nova III", "Gold Nova Master", "Master Guardian I",
            "Master Guardian II", "Master Guardian Elite", "Distinguished Master Guardian", "Legendary Eagle",
            "Legendary Eagle Master", "Supreme Master First Class", "Global Elite"
        }, tiers.Select(x => x.Name));
        Assert.Equal(Enumerable.Range(1, 18), tiers.Select(x => x.Id));
        Assert.Equal(1, new ScoreboardRankConfig().SkillGroupBaseId);
    }

    [Fact]
    public void InvalidScoreboardSkillGroupRangeAndRefreshAreRejected()
    {
        var settings = new HsGoStatsRuntimeSettings();
        settings.Rating.Scoreboard.SkillGroupBaseId = 18;
        settings.Rating.Scoreboard.RefreshSeconds = 1;
        var result = ConfigValidator.Validate(ValidConfig(), settings);
        Assert.Contains(result.Errors, value => value.Contains("SkillGroupBaseId"));
        Assert.Contains(result.Errors, value => value.Contains("RefreshSeconds"));
    }

    [Fact]
    public void CustomScoreboardSkillGroupRangeRemainsSupported()
    {
        var settings = new HsGoStatsRuntimeSettings();
        settings.Rating.Scoreboard.SkillGroupBaseId = 19_010_001;

        var result = ConfigValidator.Validate(ValidConfig(), settings);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
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
        var result = ConfigValidator.Validate(config, new HsGoStatsRuntimeSettings());
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public void DefaultTpEconomyStartsAtTwoHundredAndFloorsAtZero()
    {
        var settings = new HsGoStatsRuntimeSettings();

        Assert.Equal(200, settings.Rating.StartingXP);
        Assert.Equal(0, settings.Rating.MinimumXP);
    }

    [Fact]
    public void TpEconomyRejectsStartingBalanceBelowItsFloor()
    {
        var config = ValidConfig();
        var settings = new HsGoStatsRuntimeSettings();
        settings.Rating.StartingXP = -1;

        var result = ConfigValidator.Validate(config, settings);

        Assert.Contains(result.Errors, value => value.Contains("StartingXP"));
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
