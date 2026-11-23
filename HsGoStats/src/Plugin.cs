using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using HsGoStats.Configuration;
using HsGoStats.Domain;
using HsGoStats.Persistence;
using Microsoft.Extensions.Logging;

namespace HsGoStats;

[MinimumApiVersion(379)]
public sealed class HsGoStatsPlugin : BasePlugin, IPluginConfig<HsGoStatsConfig>
{
    public override string ModuleName => "HSGo Stats Collector";
    public override string ModuleAuthor => "HSGo / K4-Zenith contributors";
    public override string ModuleVersion => "1.0.1";
    public override string ModuleDescription => "Invisible, standalone MySQL statistics and ban archive collector.";

    public HsGoStatsConfig Config { get; set; } = new();

    private ConfigValidationResult _validation = new([], []);
    private readonly SeasonState _seasonState = new();
    private readonly SeasonLeaderboardBlacklistState _seasonLeaderboardBlacklistState = new();
    private readonly Dictionary<ulong, PlaySession> _playSessions = [];
    private readonly Dictionary<ulong, RoundParticipant> _roundParticipants = [];
    private HashSet<ulong> _configuredSeasonLeaderboardBlacklist = [];
    private CancellationTokenSource? _lifetime;
    private MySqlStore? _store;
    private SeasonRepository? _seasonRepository;
    private EventPipeline? _pipeline;
    private ScoringPolicy? _scoring;
    private string? _currentMap;
    private volatile bool _ready;
    private int _refreshing;

    public void OnConfigParsed(HsGoStatsConfig config)
    {
        Config = config;
        _validation = ConfigValidator.Validate(config);
        var blacklist = config.Season.LeaderboardBlacklistSteamIds
            .Select(value => ulong.TryParse(value, out var steamId) ? steamId : 0)
            .Where(steamId => steamId != 0)
            .ToHashSet();
        Volatile.Write(ref _configuredSeasonLeaderboardBlacklist, blacklist);
    }

    public override void Load(bool hotReload)
    {
        foreach (var warning in _validation.Warnings) Logger.LogWarning("HSGo config: {Warning}", warning);
        if (!_validation.IsValid)
        {
            foreach (var error in _validation.Errors) Logger.LogError("HSGo config: {Error}", error);
            Logger.LogError("HSGo statistics collection is disabled until the JSON configuration is corrected and the server/plugin is restarted.");
            return;
        }

        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull, HookMode.Post);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect, HookMode.Pre);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath, HookMode.Post);
        RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt, HookMode.Post);
        RegisterEventHandler<EventWeaponFire>(OnWeaponFire, HookMode.Post);
        RegisterEventHandler<EventRoundStart>(OnRoundStart, HookMode.Post);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd, HookMode.Post);
        RegisterEventHandler<EventBombPlanted>(OnBombPlanted, HookMode.Post);
        RegisterEventHandler<EventBombDefused>(OnBombDefused, HookMode.Post);
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnMapEnd>(OnMapEnd);

        _scoring = new ScoringPolicy(Config.Scoring);
        _lifetime = new CancellationTokenSource();
        _store = new MySqlStore(Config);
        _seasonRepository = new SeasonRepository(_store, Config, _scoring);
        var spoolPath = Path.Combine(ModuleDirectory, "data", "pending-events.jsonl");
        _pipeline = new EventPipeline(_store, new DurableSpool(spoolPath, Config.Persistence.PersistPendingEventsToDisk, message => Logger.LogWarning("{Message}", message)), Config.Persistence,
            message => Logger.LogInformation("{Message}", message),
            (message, ex) => { if (ex is null) Logger.LogError("{Message}", message); else Logger.LogError(ex, "{Message}", message); });

        AddTimer(10.0f, TickPlaytime, TimerFlags.REPEAT);
        AddTimer(30.0f, RequestSeasonRefresh, TimerFlags.REPEAT);
        _ = Task.Run(() => InitializeAsync(_lifetime.Token));

        if (hotReload)
        {
            foreach (var player in Utilities.GetPlayers()) EnsurePlaySession(player, DateTimeOffset.UtcNow);
        }
    }

    public override void Unload(bool hotReload)
    {
        TickPlaytime();
        _ready = false;
        _pipeline?.StopAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _playSessions.Clear();
        _roundParticipants.Clear();
    }

    private async Task InitializeAsync(CancellationToken token)
    {
        try
        {
            await _store!.InitializeSchemaAsync(token);
            var now = DateTimeOffset.UtcNow;
            var refresh = await _seasonRepository!.RefreshAsync(now, token);
            _seasonState.Replace(refresh.Seasons);
            var effectiveFrom = GetNextBlacklistSeasonNumber(refresh.Seasons, now);
            var blacklistRules = await _store.SyncSeasonLeaderboardBlacklistAsync(
                Volatile.Read(ref _configuredSeasonLeaderboardBlacklist), effectiveFrom, token);
            _seasonLeaderboardBlacklistState.Replace(blacklistRules);
            foreach (var warning in refresh.Warnings) Logger.LogWarning("HSGo season: {Warning}", warning);
            _pipeline!.Start();
            _ready = true;
            var active = _seasonState.ActiveAt(now);
            var activeBlacklistCount = active is null ? 0 : _seasonLeaderboardBlacklistState.CountAt(active.Number);
            Logger.LogInformation("HSGo Stats is ready. ServerId={ServerId}, active season={Season}, configured future blacklist={ConfiguredCount}, active season blacklist={ActiveCount}, ban enforcement=disabled.",
                Config.ServerId, active?.Name ?? "none", Volatile.Read(ref _configuredSeasonLeaderboardBlacklist).Count, activeBlacklistCount);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _ready = false;
            Logger.LogError(ex, "HSGo Stats could not initialize MySQL/schema. Gameplay is unaffected; collection remains disabled. Run sql/schema.sql manually if this user has no DDL permission.");
        }
    }

    private void RequestSeasonRefresh()
    {
        if (!_ready || _seasonRepository is null || _lifetime is null || Interlocked.Exchange(ref _refreshing, 1) != 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var result = await _seasonRepository.RefreshAsync(now, _lifetime.Token);
                _seasonState.Replace(result.Seasons);
                var effectiveFrom = GetNextBlacklistSeasonNumber(result.Seasons, now);
                var blacklistRules = await _store!.SyncSeasonLeaderboardBlacklistAsync(
                    Volatile.Read(ref _configuredSeasonLeaderboardBlacklist), effectiveFrom, _lifetime.Token);
                _seasonLeaderboardBlacklistState.Replace(blacklistRules);
                foreach (var warning in result.Warnings) Logger.LogWarning("HSGo season: {Warning}", warning);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception ex) { Logger.LogError(ex, "HSGo season refresh failed; the last immutable season snapshot remains in use."); }
            finally { Interlocked.Exchange(ref _refreshing, 0); }
        });
    }

    private void OnMapStart(string mapName)
    {
        _currentMap = NormalizeDimension(mapName);
        _roundParticipants.Clear();
        var now = DateTimeOffset.UtcNow;
        foreach (var player in Utilities.GetPlayers()) EnsurePlaySession(player, now);
    }

    private void OnMapEnd()
    {
        TickPlaytime();
        _roundParticipants.Clear();
        _currentMap = null;
    }

    private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        EnsurePlaySession(@event.Userid, DateTimeOffset.UtcNow);
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var steamId = @event.Xuid != 0 ? @event.Xuid : @event.Userid?.SteamID ?? 0;
        if (steamId != 0 && _playSessions.Remove(steamId, out var session)) FlushPlaytime(session, DateTimeOffset.UtcNow);
        _roundParticipants.Remove(steamId);
        return HookResult.Continue;
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _roundParticipants.Clear();
        if (!CanObserveStats()) return HookResult.Continue;
        foreach (var player in Utilities.GetPlayers())
        {
            if (!TryIdentity(player, out var identity) || !IsPlayingTeam(player!.Team)) continue;
            _roundParticipants[identity.SteamId64] = new(identity, player.Team);
        }
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        if (!CanObserveStats() || !Config.Tracking.Rounds) return HookResult.Continue;
        var winner = (CsTeam)@event.Winner;
        foreach (var participant in _roundParticipants.Values)
        {
            var won = participant.Team == winner;
            var delta = new StatDelta(
                Score: won && Config.Tracking.WinsAndLosses ? _scoring!.RoundWin : 0,
                RoundsPlayed: 1,
                RoundsWon: Config.Tracking.WinsAndLosses && won ? 1 : 0,
                RoundsLost: Config.Tracking.WinsAndLosses && !won ? 1 : 0);
            Enqueue("round_end", participant.Player, delta, null);
        }
        _roundParticipants.Clear();
        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        if (!CanObserveStats() || !TryIdentity(@event.Userid, out var victim)) return HookResult.Continue;
        var attackerValid = TryIdentity(@event.Attacker, out var attacker);
        var suicide = attackerValid && attacker.SteamId64 == victim.SteamId64;
        var teamKill = attackerValid && !suicide && @event.Attacker!.Team == @event.Userid!.Team;
        if (teamKill && Config.Tracking.ExcludeTeamKills) return HookResult.Continue;

        if (Config.Tracking.Deaths)
        {
            var deathScore = teamKill ? 0 : _scoring!.Death(suicide);
            Enqueue(suicide ? "suicide" : attackerValid ? "death" : "world_death", victim, new StatDelta(Deaths: 1, Score: deathScore), NormalizeWeapon(@event.Weapon));
        }

        if (attackerValid && !suicide && !teamKill && Config.Tracking.Kills)
        {
            var headshot = Config.Tracking.Headshots && @event.Headshot;
            Enqueue("kill", attacker, new StatDelta(Kills: 1, HeadshotKills: headshot ? 1 : 0, Score: _scoring!.Kill(headshot)), NormalizeWeapon(@event.Weapon));
        }

        if (!teamKill && TryIdentity(@event.Assister, out var assister) && assister.SteamId64 != victim.SteamId64 && (!attackerValid || assister.SteamId64 != attacker.SteamId64) && Config.Tracking.Assists)
            Enqueue("assist", assister, new StatDelta(Assists: 1, Score: _scoring!.Assist), NormalizeWeapon(@event.Weapon));

        return HookResult.Continue;
    }

    private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        if (!CanObserveStats() || !TryIdentity(@event.Attacker, out var attacker) || !TryIdentity(@event.Userid, out var victim) || attacker.SteamId64 == victim.SteamId64)
            return HookResult.Continue;
        var teamDamage = @event.Attacker!.Team == @event.Userid!.Team;
        if (teamDamage && Config.Tracking.ExcludeTeamKills) return HookResult.Continue;
        var damage = Config.Tracking.Damage ? Math.Max(0, @event.DmgHealth) : 0;
        var hits = Config.Tracking.ShotsAndHits ? 1 : 0;
        if (damage != 0 || hits != 0) Enqueue("damage", attacker, new StatDelta(Damage: damage, Hits: hits), NormalizeWeapon(@event.Weapon));
        return HookResult.Continue;
    }

    private HookResult OnWeaponFire(EventWeaponFire @event, GameEventInfo info)
    {
        if (CanObserveStats() && Config.Tracking.ShotsAndHits && TryIdentity(@event.Userid, out var player))
            Enqueue("shot", player, new StatDelta(Shots: 1), NormalizeWeapon(@event.Weapon));
        return HookResult.Continue;
    }

    private HookResult OnBombPlanted(EventBombPlanted @event, GameEventInfo info)
    {
        if (CanObserveStats() && TryIdentity(@event.Userid, out var player))
            Enqueue("bomb_plant", player, new StatDelta(Score: _scoring!.BombPlant, BombPlants: 1), "c4");
        return HookResult.Continue;
    }

    private HookResult OnBombDefused(EventBombDefused @event, GameEventInfo info)
    {
        if (CanObserveStats() && TryIdentity(@event.Userid, out var player))
            Enqueue("bomb_defuse", player, new StatDelta(Score: _scoring!.BombDefuse, BombDefuses: 1), "c4");
        return HookResult.Continue;
    }

    private void TickPlaytime()
    {
        var now = DateTimeOffset.UtcNow;
        var current = new HashSet<ulong>();
        foreach (var player in Utilities.GetPlayers())
        {
            if (!TryIdentity(player, out var identity)) continue;
            current.Add(identity.SteamId64);
            if (!_playSessions.TryGetValue(identity.SteamId64, out var session))
            {
                _playSessions[identity.SteamId64] = new(identity, player!.Team, now);
                continue;
            }
            FlushPlaytime(session, now);
            session.Team = player!.Team;
            session.LastObservedAt = now;
        }
        foreach (var stale in _playSessions.Keys.Where(x => !current.Contains(x)).ToArray())
        {
            FlushPlaytime(_playSessions[stale], now);
            _playSessions.Remove(stale);
        }
    }

    private void FlushPlaytime(PlaySession session, DateTimeOffset now)
    {
        var seconds = Math.Clamp((long)(now - session.LastObservedAt).TotalSeconds, 0, 60);
        session.LastObservedAt = now;
        if (seconds == 0 || !Config.Tracking.Playtime || !IsPlayingTeam(session.Team) || !CanObserveStats()) return;
        Enqueue("playtime", session.Player, new StatDelta(PlaytimeSeconds: seconds), null, now);
    }

    private void EnsurePlaySession(CCSPlayerController? player, DateTimeOffset now)
    {
        if (!TryIdentity(player, out var identity)) return;
        _playSessions.TryAdd(identity.SteamId64, new(identity, player!.Team, now));
    }

    private bool CanObserveStats()
    {
        if (!_ready || !Config.Enabled) return false;
        if (Config.Tracking.ExcludeWarmup && IsWarmup()) return false;
        return true;
    }

    private static bool IsWarmup()
    {
        var proxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        return proxy?.GameRules?.WarmupPeriod == true;
    }

    private bool TryIdentity(CCSPlayerController? player, out PlayerIdentity identity)
    {
        identity = default!;
        if (player is null || !player.IsValid || player.SteamID == 0 || player.IsHLTV || (Config.Tracking.ExcludeBots && player.IsBot)) return false;
        var name = Config.Logging.IncludePlayerNames ? Truncate(player.PlayerName, 128) : null;
        identity = new(player.SteamID, name);
        return true;
    }

    private void Enqueue(string type, PlayerIdentity player, StatDelta delta, string? weapon, DateTimeOffset? at = null)
    {
        if (delta.IsEmpty || _pipeline is null || _scoring is null) return;
        var observed = at ?? DateTimeOffset.UtcNow;
        var season = Config.Season.Enabled ? _seasonState.ActiveAt(observed) : null;
        var excludeFromSeason = season is not null && _seasonLeaderboardBlacklistState.IsExcluded(player.SteamId64, season.Number);
        var targets = TrackingTargets.Resolve(Config.Season.Enabled, Config.Season.PreseasonTracking, season is not null, excludeFromSeason);
        if (!targets.Any) return;
        _pipeline.TryEnqueue(new(Guid.NewGuid(), type, observed, Config.ServerId, player, season?.Id,
            targets.ApplyGlobal, targets.ApplySeason, _scoring.Version, Config.Tracking.PerMap ? _currentMap : null,
            Config.Tracking.PerWeapon ? weapon : null, delta));
    }

    private static bool IsPlayingTeam(CsTeam team) => team is CsTeam.Terrorist or CsTeam.CounterTerrorist;
    private int GetNextBlacklistSeasonNumber(IReadOnlyList<StoredSeason> seasons, DateTimeOffset now)
    {
        var current = seasons.FirstOrDefault(season => season.Contains(now));
        if (current is not null) return checked(current.Number + 1);
        var next = seasons.FirstOrDefault(season => season.StartsAtUtc > now);
        if (next is not null) return next.Number;
        return seasons.Count > 0 ? checked(seasons.Max(season => season.Number) + 1) : Config.Season.InitialSeasonNumber;
    }

    private static string? NormalizeWeapon(string? weapon) => NormalizeDimension(weapon?.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase) == true ? weapon[7..] : weapon);
    private static string? NormalizeDimension(string? value) => string.IsNullOrWhiteSpace(value) ? null : Truncate(value.Trim().ToLowerInvariant(), 128);
    private static string? Truncate(string? value, int length) => value is null || value.Length <= length ? value : value[..length];

    private sealed class PlaySession(PlayerIdentity player, CsTeam team, DateTimeOffset lastObservedAt)
    {
        public PlayerIdentity Player { get; } = player;
        public CsTeam Team { get; set; } = team;
        public DateTimeOffset LastObservedAt { get; set; } = lastObservedAt;
    }

    private sealed record RoundParticipant(PlayerIdentity Player, CsTeam Team);
}
