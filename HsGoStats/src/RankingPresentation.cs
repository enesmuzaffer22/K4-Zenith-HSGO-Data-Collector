using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using HsGoStats.Configuration;
using HsGoStats.Domain;
using HsGoStats.Persistence;
using Microsoft.Extensions.Logging;

namespace HsGoStats;

public sealed partial class HsGoStatsPlugin
{
    private readonly HashSet<string> _registeredLeaderboardCommands = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _registeredLevelCommands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ulong, DateTimeOffset> _leaderboardCooldowns = [];
    private readonly HashSet<ulong> _scoreboardApplied = [];
    private readonly Dictionary<ulong, int> _scoreboardSkillGroups = [];
    private const int ScoreboardCompetitiveWins = 777;
    private DateTimeOffset _nextScoreboardRefresh = DateTimeOffset.MinValue;
    private int _scoreboardRefreshing;
    private bool _scoreboardBlocked;
    private long _scoreboardRepairs;

    private void RegisterLeaderboardCommands()
    {
        if (!_settings.ChatCommands.Leaderboard.Enabled) return;
        foreach (var alias in _settings.ChatCommands.Leaderboard.Aliases.Select(CommandName).Distinct(StringComparer.OrdinalIgnoreCase))
            if (_registeredLeaderboardCommands.Add(alias))
                AddCommand(alias, "Genel ve aktif sezon TP sıralamasını gösterir", OnLeaderboardCommand);
    }

    private void RegisterLevelCommands()
    {
        if (!_settings.ChatCommands.Level.Enabled) return;
        foreach (var alias in _settings.ChatCommands.Level.Aliases.Select(CommandName).Distinct(StringComparer.OrdinalIgnoreCase))
            if (_registeredLevelCommands.Add(alias))
                AddCommand(alias, "Mevcut seviyeyi ve sonraki seviyeye kalan TP'yi gösterir", OnLevelCommand);
    }

    private void OnLevelCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!_settings.ChatCommands.Level.Enabled)
        {
            info.ReplyToCommand("[HsGo] Seviye komutu kapalı.");
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

        var observed = DateTimeOffset.UtcNow;
        var season = ActiveSeason(observed);
        _ = Task.Run(async () =>
        {
            try
            {
                await _missionRepository.ProcessAsync(new MissionSignal(Guid.NewGuid(), observed, identity, "Ensure",
                    season?.Id, season is not null, CanGrantSeason(identity, season),
                    ActiveSeasonStartsAt: season?.StartsAtUtc, ActiveSeasonEndsAt: season?.EndsAtUtc), _lifetime.Token);
                var snapshot = (await _missionRepository.GetScoreboardTpAsync(
                    [identity.SteamId64], season, _lifetime.Token)).Single();
                var tp = ScoreboardRatingSelector.SelectTp(snapshot, season is not null, _settings.Rating.Scoreboard)
                    ?? snapshot.GlobalTp;
                var rating = RatingResolver.Resolve(tp, _settings.Rating);

                Server.NextFrame(() =>
                {
                    if (player is null || !player.IsValid) return;
                    var remaining = rating.NextTier is null
                        ? $"{ChatColors.Gold}En yüksek seviyedesin.{ChatColors.Default}"
                        : $"Bir sonraki seviye için {ChatColors.Green}{rating.XpToNext:N0} TP{ChatColors.Default} kaldı ({TierLabel(rating.NextTier)})";
                    CommandReply(player,
                        $"{HsGoPrefix()}{ChatColors.Gold}Seviye:{ChatColors.Default} {TierLabel(rating.Tier)} — {remaining}");
                });
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Level command failed for {SteamId}", identity.SteamId64);
                Server.NextFrame(() =>
                {
                    if (player is not null && player.IsValid)
                        CommandReply(player, $"{HsGoPrefix()}{ChatColors.LightRed}Seviye bilgisi şu anda yüklenemedi; lütfen tekrar deneyin.{ChatColors.Default}");
                });
            }
        });
    }

    private void OnLeaderboardCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!_settings.ChatCommands.Leaderboard.Enabled)
        {
            info.ReplyToCommand("[HsGo] Sıralama komutu kapalı.");
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
        var cooldown = _settings.ChatCommands.Leaderboard.CooldownSeconds;
        if (cooldown > 0 && _leaderboardCooldowns.TryGetValue(identity.SteamId64, out var nextUse) && nextUse > now)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling((nextUse - now).TotalSeconds));
            CommandReply(player!, $"{HsGoPrefix()}Sıralamayı tekrar görüntülemek için {ChatColors.Green}{seconds}{ChatColors.Default} saniye bekleyin.");
            return;
        }
        _leaderboardCooldowns[identity.SteamId64] = now.AddSeconds(cooldown);
        var season = ActiveSeason(now);

        _ = Task.Run(async () =>
        {
            try
            {
                var view = await _missionRepository.GetLeaderboardAsync(season, identity.SteamId64, 10, _lifetime.Token);
                Server.NextFrame(() =>
                {
                    if (player is null || !player.IsValid) return;
                    PrintLeaderboardScope(player, "GENEL TP — İLK 10", view.Global);
                    if (view.Season is not null)
                        PrintLeaderboardScope(player, $"{view.SeasonName} — İLK 10", view.Season);
                    else
                        CommandReply(player, $"{HsGoPrefix()}{ChatColors.Grey}Aktif sezon bulunmuyor.{ChatColors.Default}");

                    CommandReply(player, $"{HsGoPrefix()}{ChatColors.Gold}SENİN DURUMUN{ChatColors.Default}");
                    PrintPersonalRank(player, "Genel", view.Global);
                    if (view.Season is not null) PrintPersonalRank(player, "Sezon", view.Season);
                });
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Leaderboard command failed for {SteamId}", identity.SteamId64);
                Server.NextFrame(() =>
                {
                    if (player is not null && player.IsValid)
                        CommandReply(player, $"{HsGoPrefix()}{ChatColors.LightRed}Sıralama şu anda yüklenemedi; lütfen tekrar deneyin.{ChatColors.Default}");
                });
            }
        });
    }

    private void PrintLeaderboardScope(CCSPlayerController player, string title, LeaderboardScopeView scope)
    {
        CommandReply(player, $"{HsGoPrefix()}{ChatColors.Gold}{title}{ChatColors.Default}");
        if (scope.Entries.Count == 0)
        {
            CommandReply(player, $"{HsGoPrefix()}{ChatColors.Grey}Henüz sıralamaya giren oyuncu yok.{ChatColors.Default}");
            return;
        }
        foreach (var entry in scope.Entries)
        {
            var tier = RatingResolver.Resolve(entry.Tp, _settings.Rating).Tier;
            CommandReply(player,
                $"{ChatColors.Silver}#{entry.Rank}{ChatColors.Default} {SanitizePlayerName(entry.Name)} — " +
                $"{ChatColors.Green}{entry.Tp:N0} TP{ChatColors.Default} — {TierLabel(tier)}");
        }
    }

    private void PrintPersonalRank(CCSPlayerController player, string label, LeaderboardScopeView scope)
    {
        if (scope.PlayerRank is null || scope.PlayerTp is null)
        {
            CommandReply(player, $"{ChatColors.Gold}{label}:{ChatColors.Default} {ChatColors.Grey}Henüz sıralamada değilsin.{ChatColors.Default}");
            return;
        }
        var tier = RatingResolver.Resolve(scope.PlayerTp.Value, _settings.Rating).Tier;
        CommandReply(player,
            $"{ChatColors.Gold}{label}:{ChatColors.Default} {ChatColors.Silver}#{scope.PlayerRank}/{scope.RankedPlayers}{ChatColors.Default} — " +
            $"{ChatColors.Green}{scope.PlayerTp.Value:N0} TP{ChatColors.Default} — {TierLabel(tier)}");
    }

    private void RequestScoreboardRefresh(bool immediate = false)
    {
        if (!_ready || _missionRepository is null || _lifetime is null || _scoreboardBlocked) return;
        if (!_settings.Rating.Enabled || !_settings.Rating.Scoreboard.Enabled)
        {
            if (_scoreboardApplied.Count > 0) ClearAppliedScoreboardRanks();
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!immediate && now < _nextScoreboardRefresh) return;
        if (Interlocked.Exchange(ref _scoreboardRefreshing, 1) != 0) return;
        _nextScoreboardRefresh = now.AddSeconds(_settings.Rating.Scoreboard.RefreshSeconds);
        var steamIds = Utilities.GetPlayers()
            .Where(player => TryIdentity(player, out _))
            .Select(player => player!.SteamID)
            .Distinct()
            .ToArray();
        var activeSeason = _settings.Rating.Scoreboard.UseActiveSeason ? ActiveSeason(now) : null;

        _ = Task.Run(async () =>
        {
            try
            {
                var snapshots = await _missionRepository.GetScoreboardTpAsync(steamIds, activeSeason, _lifetime.Token);
                Server.NextFrame(() => ApplyScoreboardSnapshots(snapshots, activeSeason is not null));
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception ex) { Logger.LogError(ex, "HSGo scoreboard TP refresh failed; previous rank icons remain in use."); }
            finally { Interlocked.Exchange(ref _scoreboardRefreshing, 0); }
        });
    }

    private void ApplyScoreboardSnapshots(IReadOnlyList<ScoreboardTpSnapshot> snapshots, bool seasonActive)
    {
        if (!_ready || !_settings.Rating.Enabled || !_settings.Rating.Scoreboard.Enabled) return;
        try
        {
            foreach (var snapshot in snapshots)
            {
                var player = Utilities.GetPlayerFromSteamId(snapshot.SteamId64);
                if (player is null || !player.IsValid || player.IsBot || player.IsHLTV) continue;
                var tp = ScoreboardRatingSelector.SelectTp(snapshot, seasonActive, _settings.Rating.Scoreboard);
                if (tp is null) continue;
                var tier = RatingResolver.Resolve(tp.Value, _settings.Rating).Tier;
                var tierIndex = _settings.Rating.Tiers.FindIndex(value => value.Id == tier.Id);
                if (tierIndex < 0) continue;
                var skillGroup = checked(_settings.Rating.Scoreboard.SkillGroupBaseId + tierIndex);
                _scoreboardSkillGroups[snapshot.SteamId64] = skillGroup;
                if (player.CompetitiveWins == ScoreboardCompetitiveWins && player.CompetitiveRankType == 12 && player.CompetitiveRanking == skillGroup)
                {
                    _scoreboardApplied.Add(snapshot.SteamId64);
                    continue;
                }
                ApplyScoreboardRank(player, skillGroup);
                _scoreboardApplied.Add(snapshot.SteamId64);
            }
        }
        catch (Exception ex)
        {
            _scoreboardBlocked = true;
            Logger.LogError(ex, "HSGo could not write native scoreboard rank fields. CounterStrikeSharp may be blocking this under FollowCS2ServerGuidelines; HSGo will not change that setting automatically.");
        }
    }

    private void ClearAppliedScoreboardRanks()
    {
        if (_scoreboardApplied.Count == 0) return;
        try
        {
            foreach (var steamId in _scoreboardApplied.ToArray())
            {
                var player = Utilities.GetPlayerFromSteamId(steamId);
                if (player is null || !player.IsValid) continue;
                player.CompetitiveWins = 0;
                player.CompetitiveRankType = 12;
                player.CompetitiveRanking = 0;
                Utilities.SetStateChanged(player, "CCSPlayerController", "m_iCompetitiveWins");
                Utilities.SetStateChanged(player, "CCSPlayerController", "m_iCompetitiveRankType");
                Utilities.SetStateChanged(player, "CCSPlayerController", "m_iCompetitiveRanking");
            }
        }
        catch (Exception ex) { Logger.LogWarning(ex, "HSGo could not clear previously applied scoreboard ranks during disable/unload."); }
        finally
        {
            _scoreboardApplied.Clear();
            _scoreboardSkillGroups.Clear();
        }
    }

    private void MaintainScoreboardRanks()
    {
        if (!_ready || _scoreboardBlocked || !_settings.Rating.Enabled || !_settings.Rating.Scoreboard.Enabled ||
            _scoreboardSkillGroups.Count == 0)
            return;

        try
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player is null || !player.IsValid || player.IsBot || player.IsHLTV ||
                    !_scoreboardSkillGroups.TryGetValue(player.SteamID, out var skillGroup) ||
                    (player.CompetitiveWins == ScoreboardCompetitiveWins && player.CompetitiveRankType == 12 && player.CompetitiveRanking == skillGroup))
                    continue;

                ApplyScoreboardRank(player, skillGroup);
                Interlocked.Increment(ref _scoreboardRepairs);
            }
        }
        catch (Exception ex)
        {
            _scoreboardBlocked = true;
            Logger.LogError(ex, "HSGo could not maintain native scoreboard rank fields; scoreboard rank writes are disabled until settings or the plugin are reloaded.");
        }
    }

    private static void ApplyScoreboardRank(CCSPlayerController player, int skillGroup)
    {
        player.CompetitiveWins = ScoreboardCompetitiveWins;
        player.CompetitiveRankType = 12;
        player.CompetitiveRanking = skillGroup;
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_iCompetitiveWins");
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_iCompetitiveRankType");
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_iCompetitiveRanking");
    }

    private static string HsGoPrefix() => $"{ChatColors.Gold}[HsGo]{ChatColors.Default} ";

    private static string TierLabel(RatingTierConfig tier) =>
        $"{TierChatColor(tier.Id)}{tier.Name}{ChatColors.Default}";

    private static char TierChatColor(int tierId) => tierId switch
    {
        <= 4 => ChatColors.Grey,
        <= 6 => ChatColors.White,
        <= 10 => ChatColors.Gold,
        <= 12 => ChatColors.LightBlue,
        <= 14 => ChatColors.BlueGrey,
        <= 16 => ChatColors.Purple,
        17 => ChatColors.Magenta,
        18 => ChatColors.Red,
        _ => ChatColors.Default
    };

    private static string SanitizePlayerName(string name)
    {
        var clean = new string(name.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (clean.Length == 0) return "Bilinmeyen oyuncu";
        return clean.Length <= 24 ? clean : clean[..21] + "...";
    }
}
