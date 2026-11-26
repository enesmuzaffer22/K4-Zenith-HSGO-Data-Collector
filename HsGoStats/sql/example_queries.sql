-- Bind every :parameter in server-side code. Never concatenate user input.
SELECT id, season_number, name, status, starts_at_utc, ends_at_utc FROM hsgo_seasons WHERE is_configured=1 ORDER BY starts_at_utc DESC;
SELECT id, season_number, name, starts_at_utc, ends_at_utc FROM hsgo_seasons WHERE is_configured=1 AND status='active' AND starts_at_utc<=UTC_TIMESTAMP() AND ends_at_utc>UTC_TIMESTAMP() LIMIT 1;
SELECT p.steam_id64,p.last_name,g.* FROM hsgo_players p LEFT JOIN hsgo_player_global_stats g ON g.steam_id64=p.steam_id64 AND g.server_id=:server_id WHERE p.steam_id64=:steam_id64;
SELECT p.steam_id64,p.last_name,s.score,s.kills,s.deaths,s.assists,s.headshot_kills,s.playtime_seconds FROM hsgo_player_season_stats s JOIN hsgo_players p USING(steam_id64) JOIN hsgo_seasons se ON se.id=s.season_id WHERE s.season_id=:season_id AND s.server_id=:server_id AND NOT EXISTS (SELECT 1 FROM hsgo_season_leaderboard_blacklist b WHERE b.server_id=s.server_id AND b.steam_id64=s.steam_id64 AND se.season_number>=b.effective_from_season_number AND (b.effective_until_season_number IS NULL OR se.season_number<b.effective_until_season_number)) ORDER BY s.score DESC,s.kills DESC LIMIT 10;
SELECT p.steam_id64,p.last_name,g.score,g.kills,g.deaths,g.assists,g.headshot_kills,g.playtime_seconds FROM hsgo_player_global_stats g JOIN hsgo_players p USING(steam_id64) WHERE g.server_id=:server_id ORDER BY g.score DESC,g.kills DESC LIMIT 10;
SELECT p.steam_id64,p.last_name,s.kills,s.deaths,s.headshot_kills,ROUND(s.kills/GREATEST(1,s.deaths),2) AS kd,ROUND(100*s.headshot_kills/GREATEST(1,s.kills),2) AS hs_percent FROM hsgo_player_season_stats s JOIN hsgo_players p USING(steam_id64) JOIN hsgo_seasons se ON se.id=s.season_id WHERE s.season_id=:season_id AND s.server_id=:server_id AND NOT EXISTS (SELECT 1 FROM hsgo_season_leaderboard_blacklist b WHERE b.server_id=s.server_id AND b.steam_id64=s.steam_id64 AND se.season_number>=b.effective_from_season_number AND (b.effective_until_season_number IS NULL OR se.season_number<b.effective_until_season_number)) ORDER BY s.kills DESC LIMIT 10;
SELECT steam_id64,effective_from_season_number,effective_until_season_number,created_at,updated_at FROM hsgo_season_leaderboard_blacklist WHERE server_id=:server_id ORDER BY steam_id64,effective_from_season_number;
SELECT map_name,kills,deaths,assists,headshot_kills,damage,rounds_played FROM hsgo_map_stats WHERE scope=:scope AND period_id=:period_id AND server_id=:server_id AND steam_id64=:steam_id64 ORDER BY kills DESC;
SELECT weapon_name,kills,headshot_kills,damage,shots,hits FROM hsgo_weapon_stats WHERE scope=:scope AND period_id=:period_id AND server_id=:server_id AND steam_id64=:steam_id64 ORDER BY kills DESC;
SELECT id,steam_id64,player_name_snapshot,reason,issued_at,expires_at,server_id FROM hsgo_bans WHERE revoked_at IS NULL AND (expires_at IS NULL OR expires_at>UTC_TIMESTAMP()) ORDER BY issued_at DESC;
SELECT id,reason,issued_at,expires_at,revoked_at,revoke_reason,source,CASE WHEN revoked_at IS NOT NULL THEN 'revoked' WHEN expires_at IS NOT NULL AND expires_at<=UTC_TIMESTAMP() THEN 'expired' ELSE 'active' END AS status FROM hsgo_bans WHERE steam_id64=:steam_id64 ORDER BY issued_at DESC;
SELECT id,steam_id64,reason,issued_at,expires_at,revoked_at FROM hsgo_bans WHERE revoked_at IS NOT NULL OR (expires_at IS NOT NULL AND expires_at<=UTC_TIMESTAMP()) ORDER BY issued_at DESC LIMIT :limit;

-- General HSR with the config-synchronized tier and color.
SELECT h.steam_id64,h.hsr,
  (SELECT t.name FROM hsgo_rating_tiers t WHERE t.server_id=h.server_id AND t.enabled=1 AND t.min_xp<=h.hsr ORDER BY t.min_xp DESC LIMIT 1) AS tier_name,
  (SELECT t.color FROM hsgo_rating_tiers t WHERE t.server_id=h.server_id AND t.enabled=1 AND t.min_xp<=h.hsr ORDER BY t.min_xp DESC LIMIT 1) AS tier_color
FROM hsgo_player_global_hsr h WHERE h.server_id=:server_id AND h.steam_id64=:steam_id64;

-- General TP top 10 with dynamic tier metadata. Do not hard-code tier names in the website.
SELECT h.steam_id64,COALESCE(NULLIF(p.last_name,''),CAST(h.steam_id64 AS CHAR)) AS player_name,h.hsr,
  (SELECT t.name FROM hsgo_rating_tiers t WHERE t.server_id=h.server_id AND t.enabled=1 AND t.min_xp<=h.hsr ORDER BY t.min_xp DESC LIMIT 1) AS tier_name,
  (SELECT t.color FROM hsgo_rating_tiers t WHERE t.server_id=h.server_id AND t.enabled=1 AND t.min_xp<=h.hsr ORDER BY t.min_xp DESC LIMIT 1) AS tier_color
FROM hsgo_player_global_hsr h LEFT JOIN hsgo_players p USING(steam_id64)
WHERE h.server_id=:server_id
ORDER BY h.hsr DESC,h.steam_id64 ASC LIMIT 10;

-- Active-season HSR leaderboard. The blacklist is effective by season number.
SELECT h.steam_id64,COALESCE(NULLIF(p.last_name,''),CAST(h.steam_id64 AS CHAR)) AS player_name,h.hsr,
  (SELECT t.name FROM hsgo_rating_tiers t WHERE t.server_id=h.server_id AND t.enabled=1 AND t.min_xp<=h.hsr ORDER BY t.min_xp DESC LIMIT 1) AS tier_name,
  (SELECT t.color FROM hsgo_rating_tiers t WHERE t.server_id=h.server_id AND t.enabled=1 AND t.min_xp<=h.hsr ORDER BY t.min_xp DESC LIMIT 1) AS tier_color
FROM hsgo_player_season_hsr h JOIN hsgo_players p USING(steam_id64) JOIN hsgo_seasons se ON se.id=h.season_id
WHERE h.season_id=:season_id AND h.server_id=:server_id
  AND NOT EXISTS (SELECT 1 FROM hsgo_season_leaderboard_blacklist b WHERE b.server_id=h.server_id AND b.steam_id64=h.steam_id64
    AND se.season_number>=b.effective_from_season_number AND (b.effective_until_season_number IS NULL OR se.season_number<b.effective_until_season_number))
ORDER BY h.hsr DESC,h.steam_id64 LIMIT :limit;

-- Current weekly assignments. definition_json is the immutable assignment-time catalog snapshot.
SELECT a.category,a.mission_id,a.definition_json,a.progress,a.target,a.reward_xp,a.completed_at,a.rewarded_at,w.starts_at_utc,w.ends_at_utc
FROM hsgo_weekly_assignments a JOIN hsgo_weekly_windows w ON w.id=a.window_id
WHERE a.server_id=:server_id AND a.steam_id64=:steam_id64 AND w.starts_at_utc<=UTC_TIMESTAMP() AND w.ends_at_utc>UTC_TIMESTAMP()
ORDER BY a.category;

-- Admin panel: list database-backed plugin settings. Parse setting_json according to setting_key.
SELECT setting_key,setting_json,description,revision,updated_by,updated_at
FROM hsgo_plugin_settings WHERE server_id=:server_id ORDER BY setting_key;

-- Admin panel: optimistic update. The plugin notices revision changes within roughly 30 seconds.
UPDATE hsgo_plugin_settings
SET setting_json=:setting_json,revision=revision+1,updated_by=:admin_identity,updated_at=UTC_TIMESTAMP(6)
WHERE server_id=:server_id AND setting_key=:setting_key AND revision=:expected_revision
  AND JSON_VALID(:setting_json);

-- Server activity during the trailing 24 hours.
-- average_session_seconds is the average elapsed duration of sessions that started in that window.
SELECT
  COUNT(DISTINCT CASE
    WHEN connected_at_utc<UTC_TIMESTAMP() AND COALESCE(disconnected_at_utc,last_seen_at_utc)>=UTC_TIMESTAMP()-INTERVAL 24 HOUR
    THEN steam_id64 END) AS unique_players_24h,
  SUM(CASE WHEN connected_at_utc>=UTC_TIMESTAMP()-INTERVAL 24 HOUR AND connected_at_utc<UTC_TIMESTAMP() THEN 1 ELSE 0 END) AS sessions_started_24h,
  COALESCE(AVG(CASE
    WHEN connected_at_utc>=UTC_TIMESTAMP()-INTERVAL 24 HOUR AND connected_at_utc<UTC_TIMESTAMP()
    THEN GREATEST(0,TIMESTAMPDIFF(MICROSECOND,connected_at_utc,LEAST(COALESCE(disconnected_at_utc,last_seen_at_utc),UTC_TIMESTAMP()))/1000000.0)
    END),0) AS average_session_seconds_24h
FROM hsgo_player_sessions
WHERE server_id=:server_id AND connected_at_utc<UTC_TIMESTAMP()
  AND COALESCE(disconnected_at_utc,last_seen_at_utc)>=UTC_TIMESTAMP()-INTERVAL 24 HOUR;
