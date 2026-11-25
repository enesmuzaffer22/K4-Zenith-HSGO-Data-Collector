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
    public override string ModuleVersion => "1.4.1";
    public override string ModuleDescription => "Standalone TP, weekly mission, season and MySQL statistics collector.";

    public HsGoStatsConfig Config { get; set; } = new();

    private HsGoStatsRuntimeSettings _settings = new();
    private ConfigValidationResult _validation = new([], []);
    private readonly SeasonState _seasonState = new();
    private readonly SeasonLeaderboardBlacklistState _seasonLeaderboardBlacklistState = new();
    private readonly Dictionary<ulong, PlaySession> _playSessions = [];
    private readonly Dictionary<ulong, RoundParticipant> _roundParticipants = [];
    private HashSet<ulong> _configuredSeasonLeaderboardBlacklist = [];
    private CancellationTokenSource? _lifetime;
    private MySqlStore? _store;
    private PluginSettingsRepository? _settingsRepository;
    private SeasonRepository? _seasonRepository;
    private EventPipeline? _pipeline;
    private ScoringPolicy? _scoring;
    private HsrScoringPolicy? _hsrScoring;
    private WeeklyMissionRepository? _missionRepository;
    private WeeklyMissionPipeline? _missionPipeline;
    private PlayerSessionRepository? _sessionRepository;
    private PlayerSessionPipeline? _sessionPipeline;
    private readonly Dictionary<ulong, string> _lastGrenadeThrown = [];
    private PlayerIdentity? _bombPlanter;
    private string? _currentMap;
    private volatile bool _ready;
    private int _refreshing;
    private long _settingsRevision;
    private long _rejectedSettingsRevision;
    private readonly HashSet<string> _registeredSeasonCommands = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _registeredMissionCommands = new(StringComparer.OrdinalIgnoreCase);
    private long _connectCallbacks;
    private long _roundStartCallbacks;
    private long _roundEndCallbacks;
    private long _deathCallbacks;
    private long _hurtCallbacks;
    private long _playtimeTicks;
    private long _observeChecks;
    private long _observeRejectedNotReady;
    private long _observeRejectedDisabled;
    private long _observeRejectedWarmup;

    public void OnConfigParsed(HsGoStatsConfig config)
    {
        Config = config;
        _validation = ConfigValidator.ValidateBootstrap(config);
    }

    public override void Load(bool hotReload)
    {
        AddCommand("css_hsgo_status", "HSGo collector runtime diagnostics", OnStatusCommand);
        foreach (var warning in _validation.Warnings) Logger.LogWarning("HSGo config: {Warning}", warning);
        if (!_validation.IsValid)
        {
            foreach (var error in _validation.Errors) Logger.LogError("HSGo config: {Error}", error);
            Logger.LogError("HSGo statistics collection is disabled until the bootstrap JSON configuration is corrected and the server/plugin is restarted.");
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

        _lifetime = new CancellationTokenSource();
        _store = new MySqlStore(Config);
        _settingsRepository = new PluginSettingsRepository(_store, Config);

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
        var now = DateTimeOffset.UtcNow;
        foreach (var session in _playSessions.Values)
            if (session.DatabaseOpened) EnqueueSession(session, PlayerSessionSignalType.Close, now);
        _ready = false;
        _pipeline?.StopAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
        _missionPipeline?.StopAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
        _sessionPipeline?.StopAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
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
            var loaded = await _settingsRepository!.LoadOrSeedAsync(Config.CreateRuntimeSeed(), token);
            _validation = ConfigValidator.Validate(Config, loaded.Settings);
            foreach (var warning in _validation.Warnings) Logger.LogWarning("HSGo database config: {Warning}", warning);
            if (!_validation.IsValid)
            {
                foreach (var error in _validation.Errors) Logger.LogError("HSGo database config: {Error}", error);
                Logger.LogError("HSGo collection remains disabled. Correct hsgo_plugin_settings in the admin panel and reload the plugin.");
                return;
            }
            _settings = loaded.Settings;
            _settingsRevision = loaded.Revision;
            _store.ApplyRuntimeSettings(_settings);
            var blacklist = _settings.Season.LeaderboardBlacklistSteamIds
                .Select(value => ulong.TryParse(value, out var steamId) ? steamId : 0)
                .Where(steamId => steamId != 0)
                .ToHashSet();
            Volatile.Write(ref _configuredSeasonLeaderboardBlacklist, blacklist);
            _scoring = new ScoringPolicy(_settings.Scoring);
            _hsrScoring = new HsrScoringPolicy(_settings.GameXp);
            _seasonRepository = new SeasonRepository(_store, _settings, _scoring);
            var spoolPath = Path.Combine(ModuleDirectory, "data", "pending-events.jsonl");
            _pipeline = new EventPipeline(_store, new DurableSpool(spoolPath, _settings.Persistence.PersistPendingEventsToDisk, message => Logger.LogWarning("{Message}", message)), _settings.Persistence,
                message => Logger.LogInformation("{Message}", message),
                (message, ex) => { if (ex is null) Logger.LogError("{Message}", message); else Logger.LogError(ex, "{Message}", message); });
            _missionRepository = new WeeklyMissionRepository(_store, _settings, Config.ServerId);
            _missionPipeline = new WeeklyMissionPipeline(_missionRepository, _settings.Persistence,
                Path.Combine(ModuleDirectory, "data", "pending-missions.jsonl"),
                (message, ex) => { if (ex is null) Logger.LogError("{Message}", message); else Logger.LogError(ex, "{Message}", message); });
            _sessionRepository = new PlayerSessionRepository(_store, Config.ServerId);
            _sessionPipeline = new PlayerSessionPipeline(_sessionRepository, _settings.Persistence,
                Path.Combine(ModuleDirectory, "data", "pending-sessions.jsonl"),
                (message, ex) => { if (ex is null) Logger.LogError("{Message}", message); else Logger.LogError(ex, "{Message}", message); });
            await _sessionPipeline.StartAsync(token);
            await _sessionRepository.CloseOrphanedSessionsAsync(DateTimeOffset.UtcNow, token);
            await _store.ClampTpFloorAsync(token);
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
            Server.NextFrame(() =>
            {
                RegisterConfiguredCommands();
                var observed = DateTimeOffset.UtcNow;
                foreach (var player in Utilities.GetPlayers())
                    if (TryIdentity(player, out var identity))
                    {
                        EnsurePlaySession(player, observed);
                        EnqueueMission(identity, "Ensure", at: observed);
                    }
            });
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
                await RefreshDatabaseSettingsAsync(_lifetime.Token);
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

    private async Task RefreshDatabaseSettingsAsync(CancellationToken token)
    {
        var revision = await _settingsRepository!.GetRevisionTokenAsync(token);
        if (revision == _settingsRevision || revision == _rejectedSettingsRevision) return;
        var loaded = await _settingsRepository!.LoadOrSeedAsync(Config.CreateRuntimeSeed(), token);
        if (loaded.Revision == _settingsRevision || loaded.Revision == _rejectedSettingsRevision) return;
        var validation = ConfigValidator.Validate(Config, loaded.Settings);
        if (!validation.IsValid)
        {
            _rejectedSettingsRevision = loaded.Revision;
            foreach (var error in validation.Errors) Logger.LogError("Rejected database config revision {Revision}: {Error}", loaded.Revision, error);
            return;
        }

        var persistenceChanged = !PersistenceMatches(_settings.Persistence, loaded.Settings.Persistence);
        _settings.Enabled = loaded.Settings.Enabled;
        _settings.Season = loaded.Settings.Season;
        _settings.Scoring = loaded.Settings.Scoring;
        _settings.GameXp = loaded.Settings.GameXp;
        _settings.Rating = loaded.Settings.Rating;
        _settings.WeeklyMissions = loaded.Settings.WeeklyMissions;
        _settings.ChatCommands = loaded.Settings.ChatCommands;
        _settings.Tracking = loaded.Settings.Tracking;
        _settings.Bans = loaded.Settings.Bans;
        _settings.Logging = loaded.Settings.Logging;
        _store!.ApplyRuntimeSettings(_settings);
        _scoring = new ScoringPolicy(_settings.Scoring);
        _hsrScoring = new HsrScoringPolicy(_settings.GameXp);
        _seasonRepository = new SeasonRepository(_store, _settings, _scoring);
        Volatile.Write(ref _configuredSeasonLeaderboardBlacklist, _settings.Season.LeaderboardBlacklistSteamIds
            .Select(value => ulong.TryParse(value, out var steamId) ? steamId : 0)
            .Where(steamId => steamId != 0).ToHashSet());
        await _store.ClampTpFloorAsync(token);
        await _store.SyncRatingTiersAsync(token);
        Server.NextFrame(RegisterConfiguredCommands);
        _settingsRevision = loaded.Revision;
        _rejectedSettingsRevision = 0;
        foreach (var warning in validation.Warnings) Logger.LogWarning("HSGo database config revision {Revision}: {Warning}", loaded.Revision, warning);
        if (persistenceChanged) Logger.LogWarning("Persistence settings changed in the database and will take effect after a plugin/server reload; other revision {Revision} settings are active.", loaded.Revision);
        Logger.LogInformation("Applied HSGo database config revision token {Revision}.", loaded.Revision);
    }

    private static bool PersistenceMatches(PersistenceConfig left, PersistenceConfig right) =>
        left.BatchIntervalSeconds == right.BatchIntervalSeconds && left.MaxBatchSize == right.MaxBatchSize &&
        left.MaxQueuedEvents == right.MaxQueuedEvents && left.RetryMaxAttempts == right.RetryMaxAttempts &&
        left.RetryInitialDelaySeconds == right.RetryInitialDelaySeconds &&
        left.PersistPendingEventsToDisk == right.PersistPendingEventsToDisk;

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
        Interlocked.Increment(ref _connectCallbacks);
        EnsurePlaySession(@event.Userid, DateTimeOffset.UtcNow);
        if (TryIdentity(@event.Userid, out var identity)) EnqueueMission(identity, "Ensure");
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var steamId = @event.Xuid != 0 ? @event.Xuid : @event.Userid?.SteamID ?? 0;
        if (steamId != 0 && _playSessions.Remove(steamId, out var session))
        {
            var now = DateTimeOffset.UtcNow;
            FlushPlaytime(session, now);
            if (session.DatabaseOpened && !EnqueueSession(session, PlayerSessionSignalType.Close, now))
                _playSessions[steamId] = session;
        }
        _roundParticipants.Remove(steamId);
        return HookResult.Continue;
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        Interlocked.Increment(ref _roundStartCallbacks);
        _roundParticipants.Clear();
        _lastGrenadeThrown.Clear();
        _bombPlanter = null;
        if (_settings.WeeklyMissions.Enabled && _settings.ChatCommands.Missions.Enabled && _settings.ChatCommands.RoundStartReminder.Enabled &&
            ActiveSeason(DateTimeOffset.UtcNow) is not null &&
            (_settings.ChatCommands.RoundStartReminder.IncludeWarmup || !IsWarmup()))
        {
            foreach (var player in Utilities.GetPlayers())
                if (TryIdentity(player, out _)) player!.PrintToChat(_settings.ChatCommands.RoundStartReminder.Message);
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
        Interlocked.Increment(ref _roundEndCallbacks);
        if (!CanObserveStats()) return HookResult.Continue;
        var winner = (CsTeam)@event.Winner;
        foreach (var participant in _roundParticipants.Values)
        {
            var won = participant.Team == winner;
            if (_settings.Tracking.Rounds)
            {
                var delta = new StatDelta(
                    Score: won && _settings.Tracking.WinsAndLosses ? _scoring!.RoundWin : 0,
                    RoundsPlayed: 1,
                    RoundsWon: _settings.Tracking.WinsAndLosses && won ? 1 : 0,
                    RoundsLost: _settings.Tracking.WinsAndLosses && !won ? 1 : 0);
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
        Interlocked.Increment(ref _deathCallbacks);
        if (!CanObserveStats() || !TryIdentity(@event.Userid, out var victim)) return HookResult.Continue;
        var attackerValid = TryIdentity(@event.Attacker, out var attacker);
        var suicide = attackerValid && attacker.SteamId64 == victim.SteamId64;
        var teamKill = attackerValid && !suicide && @event.Attacker!.Team == @event.Userid!.Team;
        var ignoredTeamKill = teamKill && _settings.Tracking.ExcludeTeamKills;

        if (_settings.Tracking.Deaths && !ignoredTeamKill)
        {
            var deathScore = teamKill ? 0 : _scoring!.Death(suicide);
            Enqueue(suicide ? "suicide" : attackerValid ? "death" : "world_death", victim,
                new StatDelta(Deaths: 1, Score: deathScore), NormalizeWeapon(@event.Weapon));
        }
        if (!teamKill) EnqueueMission(victim, "HsrOnly", hsrDelta: _hsrScoring!.Death(suicide));

        if (_roundParticipants.TryGetValue(victim.SteamId64, out var victimRound)) victimRound.Alive = false;

        if (attackerValid && !suicide && !teamKill)
        {
            var headshot = _settings.Tracking.Headshots && @event.Headshot;
            var weapon = NormalizeWeapon(@event.Weapon);
            var hsr = _hsrScoring!.Kill(weapon, @event.Headshot, @event.Noscope, @event.Thrusmoke, @event.Penetrated > 0, @event.Attackerblind, @event.Attackerinair);
            if (_settings.Tracking.Kills)
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
            if (_settings.Tracking.Assists)
                Enqueue("assist", assister, new StatDelta(Assists: 1, Score: _scoring!.Assist), NormalizeWeapon(@event.Weapon));
            EnqueueMission(assister, "Assist", hsrDelta: _hsrScoring!.Assist);
            if (_roundParticipants.TryGetValue(assister.SteamId64, out var assisterRound)) assisterRound.Assists++;
        }

        DetectClutchState();

        return HookResult.Continue;
    }

    private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        Interlocked.Increment(ref _hurtCallbacks);
        if (!CanObserveStats() || !TryIdentity(@event.Attacker, out var attacker) || !TryIdentity(@event.Userid, out var victim) || attacker.SteamId64 == victim.SteamId64)
            return HookResult.Continue;
        var teamDamage = @event.Attacker!.Team == @event.Userid!.Team;
        if (teamDamage && _settings.Tracking.ExcludeTeamKills) return HookResult.Continue;
        var damage = _settings.Tracking.Damage ? Math.Max(0, @event.DmgHealth) : 0;
        var hits = _settings.Tracking.ShotsAndHits ? 1 : 0;
        var weapon = NormalizeWeapon(@event.Weapon);
        if (damage != 0 || hits != 0) Enqueue("damage", attacker, new StatDelta(Damage: damage, Hits: hits), weapon);
        var missionDamage = Math.Max(0, @event.DmgHealth);
        if (!teamDamage && missionDamage > 0) EnqueueMission(attacker, "Damage", missionDamage, weapon);
        return HookResult.Continue;
    }

    private HookResult OnWeaponFire(EventWeaponFire @event, GameEventInfo info)
    {
        if (CanObserveStats() && _settings.Tracking.ShotsAndHits && TryIdentity(@event.Userid, out var player))
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
        Interlocked.Increment(ref _playtimeTicks);
        var now = DateTimeOffset.UtcNow;
        var current = new HashSet<ulong>();
        foreach (var player in Utilities.GetPlayers())
        {
            if (!TryIdentity(player, out var identity)) continue;
            current.Add(identity.SteamId64);
            if (!_playSessions.TryGetValue(identity.SteamId64, out var session))
            {
                EnsurePlaySession(player, now);
                continue;
            }
            EnsurePlaySession(player, now);
            FlushPlaytime(session, now);
            session.Team = player!.Team;
            session.LastObservedAt = now;
            if (session.DatabaseOpened && now - session.LastSessionHeartbeatAt >= TimeSpan.FromMinutes(1) &&
                EnqueueSession(session, PlayerSessionSignalType.Heartbeat, now))
                session.LastSessionHeartbeatAt = now;
        }
        foreach (var stale in _playSessions.Keys.Where(x => !current.Contains(x)).ToArray())
        {
            var session = _playSessions[stale];
            FlushPlaytime(session, now);
            if (!session.DatabaseOpened || EnqueueSession(session, PlayerSessionSignalType.Close, now))
                _playSessions.Remove(stale);
        }
    }

    private void FlushPlaytime(PlaySession session, DateTimeOffset now)
    {
        var seconds = Math.Clamp((long)(now - session.LastObservedAt).TotalSeconds, 0, 60);
        session.LastObservedAt = now;
        if (seconds == 0 || !_settings.Tracking.Playtime || !IsPlayingTeam(session.Team) || !CanObserveStats()) return;
        Enqueue("playtime", session.Player, new StatDelta(PlaytimeSeconds: seconds), null, now);
    }

    private void EnsurePlaySession(CCSPlayerController? player, DateTimeOffset now)
    {
        if (!TryIdentity(player, out var identity)) return;
        if (!_playSessions.TryGetValue(identity.SteamId64, out var session))
        {
            session = new(identity, player!.Team, now);
            _playSessions[identity.SteamId64] = session;
        }
        if (_ready && _settings.Enabled && !session.DatabaseOpened && EnqueueSession(session, PlayerSessionSignalType.Open, now))
        {
            session.DatabaseOpened = true;
            session.LastSessionHeartbeatAt = now;
        }
    }

    private bool EnqueueSession(PlaySession session, PlayerSessionSignalType type, DateTimeOffset observed)
    {
        if (_sessionPipeline is null) return false;
        var accepted = _sessionPipeline.TryEnqueue(new(Guid.NewGuid(), session.SessionId, observed, session.Player, type));
        if (!accepted) Logger.LogError("Player session queue is full; {Type} for {SteamId} was dropped.", type, session.Player.SteamId64);
        return accepted;
    }

    private bool CanObserveStats()
    {
        Interlocked.Increment(ref _observeChecks);
        if (!_ready)
        {
            Interlocked.Increment(ref _observeRejectedNotReady);
            return false;
        }
        if (!_settings.Enabled)
        {
            Interlocked.Increment(ref _observeRejectedDisabled);
            return false;
        }
        if (_settings.Tracking.ExcludeWarmup && IsWarmup())
        {
            Interlocked.Increment(ref _observeRejectedWarmup);
            return false;
        }
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
            ((_settings.Tracking.ExcludeBots || _settings.WeeklyMissions.ExcludeBots) && player.IsBot)) return false;
        var name = _settings.Logging.IncludePlayerNames ? Truncate(player.PlayerName, 128) : null;
        identity = new(player.SteamID, name);
        return true;
    }

    private void Enqueue(string type, PlayerIdentity player, StatDelta delta, string? weapon, DateTimeOffset? at = null)
    {
        if (delta.IsEmpty || _pipeline is null || _scoring is null) return;
        var observed = at ?? DateTimeOffset.UtcNow;
        var season = _settings.Season.Enabled ? _seasonState.ActiveAt(observed) : null;
        var excludeFromSeason = season is not null && _seasonLeaderboardBlacklistState.IsExcluded(player.SteamId64, season.Number);
        var targets = TrackingTargets.Resolve(_settings.Season.Enabled, _settings.Season.PreseasonTracking, season is not null, excludeFromSeason);
        if (!targets.Any) return;
        _pipeline.TryEnqueue(new(Guid.NewGuid(), type, observed, Config.ServerId, player, season?.Id,
            targets.ApplyGlobal, targets.ApplySeason, _scoring.Version, _settings.Tracking.PerMap ? _currentMap : null,
            _settings.Tracking.PerWeapon ? weapon : null, delta));
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
        if (!_ready || (!_settings.WeeklyMissions.Enabled && !_settings.Rating.Enabled) || _missionPipeline is null) return;
        if (!_settings.WeeklyMissions.Enabled && signal.HsrDelta == 0) return;
        if (!_missionPipeline.TryEnqueue(signal)) Logger.LogError("Weekly mission queue is full; event {EventId} was dropped.", signal.EventId);
    }

    private StoredSeason? ActiveSeason(DateTimeOffset at) => _settings.Season.Enabled ? _seasonState.ActiveAt(at) : null;
    private bool CanGrantSeason(PlayerIdentity player, StoredSeason? season) =>
        season is not null && !_seasonLeaderboardBlacklistState.IsExcluded(player.SteamId64, season.Number);

    private void DetectClutchState()
    {
        var aliveT = _roundParticipants.Values.Where(x => x.Alive && x.Team == CsTeam.Terrorist).ToArray();
        var aliveCt = _roundParticipants.Values.Where(x => x.Alive && x.Team == CsTeam.CounterTerrorist).ToArray();
        if (aliveT.Length == 1 && aliveCt.Length >= 2) aliveT[0].Clutch = true;
        if (aliveCt.Length == 1 && aliveT.Length >= 2) aliveCt[0].Clutch = true;
    }

    private void OnStatusCommand(CCSPlayerController? player, CommandInfo info)
    {
        var players = Utilities.GetPlayers().Where(value => value is { IsValid: true }).ToArray();
        var humans = players.Count(value => !value!.IsBot && !value.IsHLTV);
        var bots = players.Count(value => value!.IsBot);
        var steamReady = players.Count(value => value!.SteamID != 0);
        var playing = players.Count(value => IsPlayingTeam(value!.Team));
        var eligible = players.Count(value => TryIdentity(value, out _));
        var eligiblePlaying = players.Count(value => IsPlayingTeam(value!.Team) && TryIdentity(value, out _));
        var active = ActiveSeason(DateTimeOffset.UtcNow);

        info.ReplyToCommand($"[HsGo] version={ModuleVersion} ready={_ready} enabled={_settings.Enabled} warmup={IsWarmup()} map={_currentMap ?? "<unknown>"} activeSeason={active?.Name ?? "none"} settingsRevision={_settingsRevision}");
        info.ReplyToCommand($"[HsGo] players total={players.Length} humans={humans} bots={bots} steamReady={steamReady} playing={playing} eligible={eligible} eligiblePlaying={eligiblePlaying} trackedSessions={_playSessions.Count}");
        info.ReplyToCommand($"[HsGo] callbacks connect={Interlocked.Read(ref _connectCallbacks)} roundStart={Interlocked.Read(ref _roundStartCallbacks)} roundEnd={Interlocked.Read(ref _roundEndCallbacks)} death={Interlocked.Read(ref _deathCallbacks)} hurt={Interlocked.Read(ref _hurtCallbacks)} playtimeTicks={Interlocked.Read(ref _playtimeTicks)}");
        info.ReplyToCommand($"[HsGo] observe checks={Interlocked.Read(ref _observeChecks)} rejectedNotReady={Interlocked.Read(ref _observeRejectedNotReady)} rejectedDisabled={Interlocked.Read(ref _observeRejectedDisabled)} rejectedWarmup={Interlocked.Read(ref _observeRejectedWarmup)}");
        info.ReplyToCommand(_pipeline is null
            ? "[HsGo] statsPipeline=not-created"
            : $"[HsGo] statsPipeline={WorkerState(_pipeline.IsRunning, _pipeline.LastFailure)} enqueued={_pipeline.EnqueuedEvents} persisted={_pipeline.PersistedEvents} dropped={_pipeline.DroppedEvents}");
        info.ReplyToCommand(_sessionPipeline is null
            ? "[HsGo] sessionPipeline=not-created"
            : $"[HsGo] sessionPipeline={WorkerState(_sessionPipeline.IsRunning, _sessionPipeline.LastFailure)} enqueued={_sessionPipeline.EnqueuedEvents} persisted={_sessionPipeline.PersistedEvents}");
        info.ReplyToCommand(_missionPipeline is null
            ? "[HsGo] missionPipeline=not-created"
            : $"[HsGo] missionPipeline={WorkerState(_missionPipeline.IsRunning, _missionPipeline.LastFailure)} enqueued={_missionPipeline.EnqueuedEvents} persisted={_missionPipeline.PersistedEvents}");
    }

    private static string WorkerState(bool running, string? failure) =>
        running ? "running" : failure is null ? "stopped" : $"failed ({failure})";

    private void RegisterConfiguredCommands()
    {
        if (_settings.ChatCommands.Season.Enabled)
            foreach (var alias in _settings.ChatCommands.Season.Aliases.Select(CommandName).Distinct(StringComparer.OrdinalIgnoreCase))
                if (_registeredSeasonCommands.Add(alias)) AddCommand(alias, "Aktif HsGo sezonunu gösterir", OnSeasonCommand);
        if (_settings.ChatCommands.Missions.Enabled)
            foreach (var alias in _settings.ChatCommands.Missions.Aliases.Select(CommandName).Distinct(StringComparer.OrdinalIgnoreCase))
                if (_registeredMissionCommands.Add(alias)) AddCommand(alias, "Haftalık görevleri gösterir", OnMissionsCommand);
    }

    private void OnMissionsCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!_settings.WeeklyMissions.Enabled || !_settings.ChatCommands.Missions.Enabled)
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
                        CommandReply(player, $"[HsGo] K{mission.Category} #{mission.MissionId}: {mission.Description} ({mission.Progress}/{mission.Target}) | +{mission.RewardXp} TP{(mission.Completed ? " | TAMAMLANDI" : "")}");
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
        if (!_settings.ChatCommands.Season.Enabled)
        {
            info.ReplyToCommand("[HsGo] Sezon komutu kapalı.");
            return;
        }
        if (!_settings.Rating.Enabled)
        {
            info.ReplyToCommand("[HsGo] TP sistemi kapalı.");
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
                await _missionRepository.ProcessAsync(new MissionSignal(Guid.NewGuid(), now, identity, "Ensure",
                    season.Id, true, CanGrantSeason(identity, season),
                    ActiveSeasonStartsAt: season.StartsAtUtc, ActiveSeasonEndsAt: season.EndsAtUtc), _lifetime.Token);
                var view = await _missionRepository.GetSeasonViewAsync(season, identity.SteamId64, _lifetime.Token);
                Server.NextFrame(() =>
                {
                    if (player is null || !player.IsValid || view is null) return;
                    var rating = RatingResolver.Resolve(view.SeasonTp, _settings.Rating);
                    var rank = view.Rank is null ? "Henüz sıralamada değilsin" : $"Sıralama: #{view.Rank}/{view.RankedPlayers}";
                    CommandReply(player, $"[HsGo] {view.Name} | Genel TP: {view.GlobalTp:N0} | Sezon TP: {view.SeasonTp:N0} ({rating.Tier.Name})");
                    CommandReply(player, $"[HsGo] {rank} | Kalan: {FormatRemaining(view.EndsAtUtc - DateTimeOffset.UtcNow)}");
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
        if (_settings.ChatCommands.ReplyPrivately) player.PrintToChat(message);
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
        var zone = TimeZoneInfo.FindSystemTimeZoneById(_settings.Season.Timezone);
        return $"{TimeZoneInfo.ConvertTime(value, zone):dd.MM.yyyy HH:mm} ({_settings.Season.Timezone})";
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
        public Guid SessionId { get; } = Guid.NewGuid();
        public PlayerIdentity Player { get; } = player;
        public CsTeam Team { get; set; } = team;
        public DateTimeOffset LastObservedAt { get; set; } = lastObservedAt;
        public DateTimeOffset LastSessionHeartbeatAt { get; set; } = lastObservedAt;
        public bool DatabaseOpened { get; set; }
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
