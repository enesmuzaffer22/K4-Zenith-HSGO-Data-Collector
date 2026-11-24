using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
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
    public override string ModuleVersion => "1.2.0";
    public override string ModuleDescription => "Standalone HSR, weekly mission, season and MySQL statistics collector.";

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
    private HsrScoringPolicy? _hsrScoring;
    private WeeklyMissionRepository? _missionRepository;
    private WeeklyMissionPipeline? _missionPipeline;
    private readonly Dictionary<ulong, string> _lastGrenadeThrown = [];
    private PlayerIdentity? _bombPlanter;
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
        RegisterEventHandler<EventBombExploded>(OnBombExploded, HookMode.Post);
        RegisterEventHandler<EventGrenadeThrown>(OnGrenadeThrown, HookMode.Post);
        RegisterEventHandler<EventFlashbangDetonate>(OnFlashbangDetonate, HookMode.Post);
        RegisterEventHandler<EventHegrenadeDetonate>(OnHeGrenadeDetonate, HookMode.Post);
        RegisterEventHandler<EventSmokegrenadeDetonate>(OnSmokeGrenadeDetonate, HookMode.Post);
        RegisterEventHandler<EventMolotovDetonate>(OnMolotovDetonate, HookMode.Post);
        RegisterEventHandler<EventPlayerBlind>(OnPlayerBlind, HookMode.Post);
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnMapEnd>(OnMapEnd);

        _scoring = new ScoringPolicy(Config.Scoring);
        _hsrScoring = new HsrScoringPolicy(Config.GameXp);
        _lifetime = new CancellationTokenSource();
        _store = new MySqlStore(Config);
        _seasonRepository = new SeasonRepository(_store, Config, _scoring);
        var spoolPath = Path.Combine(ModuleDirectory, "data", "pending-events.jsonl");
        _pipeline = new EventPipeline(_store, new DurableSpool(spoolPath, Config.Persistence.PersistPendingEventsToDisk, message => Logger.LogWarning("{Message}", message)), Config.Persistence,
            message => Logger.LogInformation("{Message}", message),
            (message, ex) => { if (ex is null) Logger.LogError("{Message}", message); else Logger.LogError(ex, "{Message}", message); });
        _missionRepository = new WeeklyMissionRepository(_store, Config);
        _missionPipeline = new WeeklyMissionPipeline(_missionRepository, Config.Persistence,
            Path.Combine(ModuleDirectory, "data", "pending-missions.jsonl"),
            (message, ex) => { if (ex is null) Logger.LogError("{Message}", message); else Logger.LogError(ex, "{Message}", message); });

        RegisterConfiguredCommands();

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
        _missionPipeline?.StopAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _playSessions.Clear();
        _roundParticipants.Clear();
        _lastGrenadeThrown.Clear();
    }

    private async Task InitializeAsync(CancellationToken token)
    {
        try
        {
            var schemaWarnings = await _store!.InitializeSchemaAsync(token);
            foreach (var warning in schemaWarnings) Logger.LogWarning("HSGo schema: {Warning}", warning);
            await _store.SyncRatingTiersAsync(token);
            var now = DateTimeOffset.UtcNow;
            var refresh = await _seasonRepository!.RefreshAsync(now, token);
            _seasonState.Replace(refresh.Seasons);
            var effectiveFrom = GetNextBlacklistSeasonNumber(refresh.Seasons, now);
            var blacklistRules = await _store.SyncSeasonLeaderboardBlacklistAsync(
                Volatile.Read(ref _configuredSeasonLeaderboardBlacklist), effectiveFrom, token);
            _seasonLeaderboardBlacklistState.Replace(blacklistRules);
            foreach (var warning in refresh.Warnings) Logger.LogWarning("HSGo season: {Warning}", warning);
            _pipeline!.Start();
            _missionPipeline!.Start();
            _ready = true;
            var active = _seasonState.ActiveAt(now);
            var activeBlacklistCount = active is null ? 0 : _seasonLeaderboardBlacklistState.CountAt(active.Number);
            Logger.LogInformation("HSGo Stats is ready. ServerId={ServerId}, active season={Season}, configured future blacklist={ConfiguredCount}, active season blacklist={ActiveCount}, ban enforcement=disabled.",
                Config.ServerId, active?.Name ?? "none", Volatile.Read(ref _configuredSeasonLeaderboardBlacklist).Count, activeBlacklistCount);
            foreach (var player in Utilities.GetPlayers())
                if (TryIdentity(player, out var identity)) EnqueueMission(identity, "Ensure", at: now);
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
        _lastGrenadeThrown.Clear();
        _bombPlanter = null;
        var now = DateTimeOffset.UtcNow;
        foreach (var player in Utilities.GetPlayers()) EnsurePlaySession(player, now);
    }

    private void OnMapEnd()
    {
        TickPlaytime();
        _roundParticipants.Clear();
        _lastGrenadeThrown.Clear();
        _bombPlanter = null;
        _currentMap = null;
    }

    private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        EnsurePlaySession(@event.Userid, DateTimeOffset.UtcNow);
        if (TryIdentity(@event.Userid, out var identity)) EnqueueMission(identity, "Ensure");
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
        _lastGrenadeThrown.Clear();
        _bombPlanter = null;
        if (Config.WeeklyMissions.Enabled && Config.ChatCommands.Missions.Enabled && Config.ChatCommands.RoundStartReminder.Enabled &&
            ActiveSeason(DateTimeOffset.UtcNow) is not null &&
            (Config.ChatCommands.RoundStartReminder.IncludeWarmup || !IsWarmup()))
        {
            foreach (var player in Utilities.GetPlayers())
                if (TryIdentity(player, out _)) player!.PrintToChat(Config.ChatCommands.RoundStartReminder.Message);
        }
        if (!CanObserveStats()) return HookResult.Continue;
        foreach (var player in Utilities.GetPlayers())
        {
            if (!TryIdentity(player, out var identity) || !IsPlayingTeam(player!.Team)) continue;
            _roundParticipants[identity.SteamId64] = new(identity, player.Team);
        }
        DetectClutchState();
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        if (!CanObserveStats()) return HookResult.Continue;
        var winner = (CsTeam)@event.Winner;
        foreach (var participant in _roundParticipants.Values)
        {
            var won = participant.Team == winner;
            if (Config.Tracking.Rounds)
            {
                var delta = new StatDelta(
                    Score: won && Config.Tracking.WinsAndLosses ? _scoring!.RoundWin : 0,
                    RoundsPlayed: 1,
                    RoundsWon: Config.Tracking.WinsAndLosses && won ? 1 : 0,
                    RoundsLost: Config.Tracking.WinsAndLosses && !won ? 1 : 0);
                Enqueue("round_end", participant.Player, delta, null);
            }
            var observed = DateTimeOffset.UtcNow;
            var season = ActiveSeason(observed);
            EnqueueMission(new MissionSignal(Guid.NewGuid(), observed, participant.Player, "RoundEnd",
                season?.Id, season is not null, CanGrantSeason(participant.Player, season),
                ActiveSeasonStartsAt: season?.StartsAtUtc, ActiveSeasonEndsAt: season?.EndsAtUtc,
                Won: won, Alive: participant.Alive, Planted: participant.Planted, Defused: participant.Defused,
                Clutch: participant.Clutch, Team: TeamName(participant.Team), RoundKills: participant.Kills,
                RoundHeadshotKills: participant.HeadshotKills, RoundAssists: participant.Assists,
                RoundFlashBlinds: participant.FlashBlinds, RoundWeaponKills: participant.WeaponKills));
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
        var ignoredTeamKill = teamKill && Config.Tracking.ExcludeTeamKills;

        if (Config.Tracking.Deaths && !ignoredTeamKill)
        {
            var deathScore = teamKill ? 0 : _scoring!.Death(suicide);
            Enqueue(suicide ? "suicide" : attackerValid ? "death" : "world_death", victim,
                new StatDelta(Deaths: 1, Score: deathScore), NormalizeWeapon(@event.Weapon));
        }
        if (!teamKill) EnqueueMission(victim, "HsrOnly", hsrDelta: _hsrScoring!.Death(suicide));

        if (_roundParticipants.TryGetValue(victim.SteamId64, out var victimRound)) victimRound.Alive = false;

        if (attackerValid && !suicide && !teamKill)
        {
            var headshot = Config.Tracking.Headshots && @event.Headshot;
            var weapon = NormalizeWeapon(@event.Weapon);
            var hsr = _hsrScoring!.Kill(weapon, @event.Headshot, @event.Noscope, @event.Thrusmoke, @event.Penetrated > 0, @event.Attackerblind, @event.Attackerinair);
            if (Config.Tracking.Kills)
                Enqueue("kill", attacker, new StatDelta(Kills: 1, HeadshotKills: headshot ? 1 : 0, Score: _scoring!.Kill(headshot)), weapon);
            EnqueueMission(attacker, "Kill", weapon: weapon, headshot: @event.Headshot, noScope: @event.Noscope,
                throughSmoke: @event.Thrusmoke, penetrated: @event.Penetrated > 0, attackerBlind: @event.Attackerblind,
                attackerInAir: @event.Attackerinair, hsrDelta: hsr);
            if (_roundParticipants.TryGetValue(attacker.SteamId64, out var attackerRound))
            {
                attackerRound.Kills++;
                if (@event.Headshot) attackerRound.HeadshotKills++;
                if (weapon is not null) attackerRound.WeaponKills[weapon] = attackerRound.WeaponKills.GetValueOrDefault(weapon) + 1;
            }
        }

        if (!teamKill && TryIdentity(@event.Assister, out var assister) && assister.SteamId64 != victim.SteamId64 && (!attackerValid || assister.SteamId64 != attacker.SteamId64))
        {
            if (Config.Tracking.Assists)
                Enqueue("assist", assister, new StatDelta(Assists: 1, Score: _scoring!.Assist), NormalizeWeapon(@event.Weapon));
            EnqueueMission(assister, "Assist", hsrDelta: _hsrScoring!.Assist);
            if (_roundParticipants.TryGetValue(assister.SteamId64, out var assisterRound)) assisterRound.Assists++;
        }

        DetectClutchState();

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
        var weapon = NormalizeWeapon(@event.Weapon);
        if (damage != 0 || hits != 0) Enqueue("damage", attacker, new StatDelta(Damage: damage, Hits: hits), weapon);
        var missionDamage = Math.Max(0, @event.DmgHealth);
        if (!teamDamage && missionDamage > 0) EnqueueMission(attacker, "Damage", missionDamage, weapon);
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
        {
            _bombPlanter = player;
            Enqueue("bomb_plant", player, new StatDelta(Score: _scoring!.BombPlant, BombPlants: 1), "c4");
            EnqueueMission(player, "BombPlant", hsrDelta: _hsrScoring!.BombPlant);
            if (_roundParticipants.TryGetValue(player.SteamId64, out var round)) round.Planted = true;
        }
        return HookResult.Continue;
    }

    private HookResult OnBombDefused(EventBombDefused @event, GameEventInfo info)
    {
        if (CanObserveStats() && TryIdentity(@event.Userid, out var player))
        {
            Enqueue("bomb_defuse", player, new StatDelta(Score: _scoring!.BombDefuse, BombDefuses: 1), "c4");
            EnqueueMission(player, "BombDefuse", hsrDelta: _hsrScoring!.BombDefuse);
            if (_roundParticipants.TryGetValue(player.SteamId64, out var round)) round.Defused = true;
        }
        return HookResult.Continue;
    }

    private HookResult OnBombExploded(EventBombExploded @event, GameEventInfo info)
    {
        if (!CanObserveStats()) return HookResult.Continue;
        PlayerIdentity? player = _bombPlanter;
        if (player is null && TryIdentity(@event.Userid, out var eventPlayer)) player = eventPlayer;
        if (player is not null)
        {
            EnqueueMission(player, "OwnBombExplode", hsrDelta: _hsrScoring!.BombExplode);
        }
        return HookResult.Continue;
    }

    private HookResult OnGrenadeThrown(EventGrenadeThrown @event, GameEventInfo info)
    {
        if (CanObserveStats() && TryIdentity(@event.Userid, out var player))
        {
            var weapon = NormalizeWeapon(@event.Weapon);
            if (weapon is not null) _lastGrenadeThrown[player.SteamId64] = weapon;
        }
        return HookResult.Continue;
    }

    private HookResult OnFlashbangDetonate(EventFlashbangDetonate @event, GameEventInfo info)
    {
        if (CanObserveStats() && TryIdentity(@event.Userid, out var player)) EnqueueMission(player, "GrenadeUsed", weapon: "flashbang");
        return HookResult.Continue;
    }

    private HookResult OnHeGrenadeDetonate(EventHegrenadeDetonate @event, GameEventInfo info)
    {
        if (CanObserveStats() && TryIdentity(@event.Userid, out var player)) EnqueueMission(player, "GrenadeUsed", weapon: "hegrenade");
        return HookResult.Continue;
    }

    private HookResult OnSmokeGrenadeDetonate(EventSmokegrenadeDetonate @event, GameEventInfo info)
    {
        if (CanObserveStats() && TryIdentity(@event.Userid, out var player)) EnqueueMission(player, "GrenadeUsed", weapon: "smokegrenade");
        return HookResult.Continue;
    }

    private HookResult OnMolotovDetonate(EventMolotovDetonate @event, GameEventInfo info)
    {
        if (CanObserveStats() && TryIdentity(@event.Userid, out var player))
        {
            var weapon = _lastGrenadeThrown.GetValueOrDefault(player.SteamId64);
            if (weapon is not ("molotov" or "incgrenade")) weapon = @event.Userid!.Team == CsTeam.Terrorist ? "molotov" : "incgrenade";
            EnqueueMission(player, "GrenadeUsed", weapon: weapon);
        }
        return HookResult.Continue;
    }

    private HookResult OnPlayerBlind(EventPlayerBlind @event, GameEventInfo info)
    {
        if (!CanObserveStats() || !TryIdentity(@event.Attacker, out var attacker) || !TryIdentity(@event.Userid, out var victim) ||
            attacker.SteamId64 == victim.SteamId64 || @event.Attacker!.Team == @event.Userid!.Team) return HookResult.Continue;
        EnqueueMission(attacker, "FlashEnemyBlinded");
        if (_roundParticipants.TryGetValue(attacker.SteamId64, out var round)) round.FlashBlinds++;
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
        if (player is null || !player.IsValid || player.SteamID == 0 || player.IsHLTV ||
            ((Config.Tracking.ExcludeBots || Config.WeeklyMissions.ExcludeBots) && player.IsBot)) return false;
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

    private void EnqueueMission(PlayerIdentity player, string eventName, int amount = 1, string? weapon = null,
        bool headshot = false, bool noScope = false, bool throughSmoke = false, bool penetrated = false,
        bool attackerBlind = false, bool attackerInAir = false, DateTimeOffset? at = null, long hsrDelta = 0)
    {
        var observed = at ?? DateTimeOffset.UtcNow;
        var season = ActiveSeason(observed);
        EnqueueMission(new MissionSignal(Guid.NewGuid(), observed, player, eventName, season?.Id,
            season is not null, CanGrantSeason(player, season), ActiveSeasonStartsAt: season?.StartsAtUtc,
            ActiveSeasonEndsAt: season?.EndsAtUtc, HsrDelta: hsrDelta, Amount: amount,
            Weapon: weapon, Headshot: headshot, NoScope: noScope, ThroughSmoke: throughSmoke,
            Penetrated: penetrated, AttackerBlind: attackerBlind, AttackerInAir: attackerInAir));
    }

    private void EnqueueMission(MissionSignal signal)
    {
        if (!_ready || (!Config.WeeklyMissions.Enabled && !Config.Rating.Enabled) || _missionPipeline is null) return;
        if (!Config.WeeklyMissions.Enabled && signal.HsrDelta == 0) return;
        if (!_missionPipeline.TryEnqueue(signal)) Logger.LogError("Weekly mission queue is full; event {EventId} was dropped.", signal.EventId);
    }

    private StoredSeason? ActiveSeason(DateTimeOffset at) => Config.Season.Enabled ? _seasonState.ActiveAt(at) : null;
    private bool CanGrantSeason(PlayerIdentity player, StoredSeason? season) =>
        season is not null && !_seasonLeaderboardBlacklistState.IsExcluded(player.SteamId64, season.Number);

    private void DetectClutchState()
    {
        var aliveT = _roundParticipants.Values.Where(x => x.Alive && x.Team == CsTeam.Terrorist).ToArray();
        var aliveCt = _roundParticipants.Values.Where(x => x.Alive && x.Team == CsTeam.CounterTerrorist).ToArray();
        if (aliveT.Length == 1 && aliveCt.Length >= 2) aliveT[0].Clutch = true;
        if (aliveCt.Length == 1 && aliveT.Length >= 2) aliveCt[0].Clutch = true;
    }

    private void RegisterConfiguredCommands()
    {
        if (Config.ChatCommands.Season.Enabled)
            foreach (var alias in Config.ChatCommands.Season.Aliases.Select(CommandName).Distinct(StringComparer.OrdinalIgnoreCase))
                AddCommand(alias, "Aktif HsGo sezonunu gösterir", OnSeasonCommand);
        if (Config.ChatCommands.Missions.Enabled)
            foreach (var alias in Config.ChatCommands.Missions.Aliases.Select(CommandName).Distinct(StringComparer.OrdinalIgnoreCase))
                AddCommand(alias, "Haftalık görevleri gösterir", OnMissionsCommand);
    }

    private void OnMissionsCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!Config.WeeklyMissions.Enabled || !Config.ChatCommands.Missions.Enabled)
        {
            info.ReplyToCommand("[HsGo] Haftalık görev sistemi kapalı.");
            return;
        }
        if (!TryIdentity(player, out var identity) || _missionRepository is null || _lifetime is null)
        {
            info.ReplyToCommand("[HsGo] Bu komut yalnızca bağlı insan oyuncular tarafından kullanılabilir.");
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var observed = DateTimeOffset.UtcNow;
                var season = ActiveSeason(observed);
                await _missionRepository.ProcessAsync(new MissionSignal(Guid.NewGuid(), observed, identity, "Ensure",
                    season?.Id, season is not null, CanGrantSeason(identity, season),
                    ActiveSeasonStartsAt: season?.StartsAtUtc, ActiveSeasonEndsAt: season?.EndsAtUtc), _lifetime.Token);
                var missions = await _missionRepository.GetProgressAsync(identity.SteamId64, DateTimeOffset.UtcNow, _lifetime.Token);
                Server.NextFrame(() =>
                {
                    if (player is null || !player.IsValid) return;
                    if (missions.Count == 0)
                    {
                        CommandReply(player, ActiveSeason(DateTimeOffset.UtcNow) is not null
                            ? "[HsGo] Görevlerin hazırlanıyor; birkaç saniye sonra tekrar dene."
                            : "[HsGo] Aktif sezon olmadığı için bu hafta görev atanmadı.");
                        return;
                    }
                    CommandReply(player, $"[HsGo] Haftalık görevler | Kalan: {FormatRemaining(missions[0].EndsAtUtc - DateTimeOffset.UtcNow)}");
                    foreach (var mission in missions)
                        CommandReply(player, $"[HsGo] K{mission.Category} #{mission.MissionId}: {mission.Description} ({mission.Progress}/{mission.Target}) | +{mission.RewardXp} XP{(mission.Completed ? " | TAMAMLANDI" : "")}");
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Weekly missions command failed for {SteamId}", identity.SteamId64);
                Server.NextFrame(() => { if (player is not null && player.IsValid) CommandReply(player, "[HsGo] Görevler şu anda yüklenemedi; lütfen tekrar dene."); });
            }
        });
    }

    private void OnSeasonCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!Config.ChatCommands.Season.Enabled)
        {
            info.ReplyToCommand("[HsGo] Sezon komutu kapalı.");
            return;
        }
        if (!Config.Rating.Enabled)
        {
            info.ReplyToCommand("[HsGo] HSR sistemi kapalı.");
            return;
        }
        if (!TryIdentity(player, out var identity) || _missionRepository is null || _lifetime is null)
        {
            info.ReplyToCommand("[HsGo] Bu komut yalnızca bağlı insan oyuncular tarafından kullanılabilir.");
            return;
        }
        var now = DateTimeOffset.UtcNow;
        var season = _seasonState.ActiveAt(now);
        if (season is null)
        {
            var next = _seasonState.NextAfter(now);
            CommandReply(player!, next is null ? "[HsGo] Aktif sezon yok." : $"[HsGo] Aktif sezon yok. Sonraki sezon: {next.Name} | Başlangıç: {FormatSeasonDate(next.StartsAtUtc)}");
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var view = await _missionRepository.GetSeasonViewAsync(season, identity.SteamId64, _lifetime.Token);
                Server.NextFrame(() =>
                {
                    if (player is null || !player.IsValid || view is null) return;
                    var rating = RatingResolver.Resolve(view.Hsr, Config.Rating);
                    var rank = view.Rank is null ? "Henüz sıralamada değilsin" : $"Sıralama: #{view.Rank}/{view.RankedPlayers}";
                    CommandReply(player, $"[HsGo] {view.Name} | {view.Hsr:N0} HSR ({rating.Tier.Name}) | {rank} | Kalan: {FormatRemaining(view.EndsAtUtc - DateTimeOffset.UtcNow)}");
                    CommandReply(player, $"[HsGo] Başlangıç: {FormatSeasonDate(view.StartsAtUtc)} | Bitiş: {FormatSeasonDate(view.EndsAtUtc)}");
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Season command failed for {SteamId}", identity.SteamId64);
                Server.NextFrame(() => { if (player is not null && player.IsValid) CommandReply(player, "[HsGo] Sezon bilgisi şu anda yüklenemedi; lütfen tekrar dene."); });
            }
        });
    }

    private static string CommandName(string alias)
    {
        var value = alias.Trim().TrimStart('!', '/');
        return value.StartsWith("css_", StringComparison.OrdinalIgnoreCase) ? value : "css_" + value;
    }

    private void CommandReply(CCSPlayerController player, string message)
    {
        if (Config.ChatCommands.ReplyPrivately) player.PrintToChat(message);
        else Server.PrintToChatAll(message);
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return "0 dakika";
        if (remaining.TotalDays >= 1) return $"{(int)remaining.TotalDays} gün {remaining.Hours} saat";
        if (remaining.TotalHours >= 1) return $"{(int)remaining.TotalHours} saat {remaining.Minutes} dakika";
        return $"{Math.Max(1, remaining.Minutes)} dakika";
    }

    private string FormatSeasonDate(DateTimeOffset value)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(Config.Season.Timezone);
        return $"{TimeZoneInfo.ConvertTime(value, zone):dd.MM.yyyy HH:mm} ({Config.Season.Timezone})";
    }

    private static bool IsPlayingTeam(CsTeam team) => team is CsTeam.Terrorist or CsTeam.CounterTerrorist;
    private static string TeamName(CsTeam team) => team == CsTeam.Terrorist ? "T" : team == CsTeam.CounterTerrorist ? "CT" : "";
    private int GetNextBlacklistSeasonNumber(IReadOnlyList<StoredSeason> seasons, DateTimeOffset now)
    {
        var current = seasons.FirstOrDefault(season => season.Contains(now));
        if (current is not null) return checked(current.Number + 1);
        var next = seasons.FirstOrDefault(season => season.StartsAtUtc > now);
        if (next is not null) return next.Number;
        return seasons.Count > 0 ? checked(seasons.Max(season => season.Number) + 1) : 1;
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

    private sealed class RoundParticipant(PlayerIdentity player, CsTeam team)
    {
        public PlayerIdentity Player { get; } = player;
        public CsTeam Team { get; } = team;
        public bool Alive { get; set; } = true;
        public int Kills { get; set; }
        public int HeadshotKills { get; set; }
        public int Assists { get; set; }
        public int FlashBlinds { get; set; }
        public bool Planted { get; set; }
        public bool Defused { get; set; }
        public bool Clutch { get; set; }
        public Dictionary<string, int> WeaponKills { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
