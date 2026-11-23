-- HSGo Stats schema v1. No DROP, TRUNCATE, or non-hsgo_ table is used.
SET NAMES utf8mb4;
SET time_zone = '+00:00';
CREATE TABLE IF NOT EXISTS hsgo_schema_migrations (version INT NOT NULL PRIMARY KEY, applied_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS hsgo_players (steam_id64 BIGINT UNSIGNED NOT NULL PRIMARY KEY, last_name VARCHAR(128) NULL, first_seen_at DATETIME(6) NOT NULL, last_seen_at DATETIME(6) NOT NULL, updated_at DATETIME(6) NOT NULL) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS hsgo_seasons (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, season_number INT NOT NULL, name VARCHAR(128) NOT NULL,
  status ENUM('scheduled','active','completed') NOT NULL, starts_at_utc DATETIME(6) NOT NULL, ends_at_utc DATETIME(6) NOT NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6), activated_at DATETIME(6) NULL, completed_at DATETIME(6) NULL,
  scoring_version VARCHAR(32) NOT NULL, updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  active_guard TINYINT GENERATED ALWAYS AS (IF(status='active',1,NULL)) STORED,
  UNIQUE KEY uq_hsgo_season_number(season_number), UNIQUE KEY uq_hsgo_season_start(starts_at_utc), UNIQUE KEY uq_hsgo_single_active(active_guard),
  KEY ix_hsgo_season_status_time(status,starts_at_utc,ends_at_utc), CONSTRAINT ck_hsgo_season_window CHECK(ends_at_utc>starts_at_utc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS hsgo_season_settings (setting_key VARCHAR(128) NOT NULL PRIMARY KEY, setting_value LONGTEXT NOT NULL, changed_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS hsgo_season_leaderboard_blacklist (
  server_id VARCHAR(64) NOT NULL, steam_id64 BIGINT UNSIGNED NOT NULL,
  effective_from_season_number INT NOT NULL, effective_until_season_number INT NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6), updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY(server_id,steam_id64,effective_from_season_number),
  KEY ix_hsgo_season_blacklist_lookup(server_id,steam_id64,effective_from_season_number,effective_until_season_number),
  CONSTRAINT ck_hsgo_season_blacklist_window CHECK(effective_until_season_number IS NULL OR effective_until_season_number>=effective_from_season_number)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS hsgo_player_global_stats (
  server_id VARCHAR(64) NOT NULL, steam_id64 BIGINT UNSIGNED NOT NULL,
  kills BIGINT NOT NULL DEFAULT 0, deaths BIGINT NOT NULL DEFAULT 0, assists BIGINT NOT NULL DEFAULT 0, headshot_kills BIGINT NOT NULL DEFAULT 0,
  damage BIGINT NOT NULL DEFAULT 0, score BIGINT NOT NULL DEFAULT 0, rounds_played BIGINT NOT NULL DEFAULT 0, rounds_won BIGINT NOT NULL DEFAULT 0,
  rounds_lost BIGINT NOT NULL DEFAULT 0, playtime_seconds BIGINT NOT NULL DEFAULT 0, shots BIGINT NOT NULL DEFAULT 0, hits BIGINT NOT NULL DEFAULT 0,
  bomb_plants BIGINT NOT NULL DEFAULT 0, bomb_defuses BIGINT NOT NULL DEFAULT 0, updated_at DATETIME(6) NOT NULL,
  PRIMARY KEY(server_id,steam_id64), KEY ix_hsgo_global_score(server_id,score), KEY ix_hsgo_global_kills(server_id,kills)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS hsgo_player_season_stats (
  season_id BIGINT UNSIGNED NOT NULL, server_id VARCHAR(64) NOT NULL, steam_id64 BIGINT UNSIGNED NOT NULL,
  kills BIGINT NOT NULL DEFAULT 0, deaths BIGINT NOT NULL DEFAULT 0, assists BIGINT NOT NULL DEFAULT 0, headshot_kills BIGINT NOT NULL DEFAULT 0,
  damage BIGINT NOT NULL DEFAULT 0, score BIGINT NOT NULL DEFAULT 0, rounds_played BIGINT NOT NULL DEFAULT 0, rounds_won BIGINT NOT NULL DEFAULT 0,
  rounds_lost BIGINT NOT NULL DEFAULT 0, playtime_seconds BIGINT NOT NULL DEFAULT 0, shots BIGINT NOT NULL DEFAULT 0, hits BIGINT NOT NULL DEFAULT 0,
  bomb_plants BIGINT NOT NULL DEFAULT 0, bomb_defuses BIGINT NOT NULL DEFAULT 0, updated_at DATETIME(6) NOT NULL,
  PRIMARY KEY(season_id,server_id,steam_id64), KEY ix_hsgo_season_score(season_id,server_id,score), KEY ix_hsgo_season_kills(season_id,server_id,kills)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS hsgo_map_stats (
  scope ENUM('global','season') NOT NULL, period_id BIGINT UNSIGNED NOT NULL DEFAULT 0, server_id VARCHAR(64) NOT NULL, steam_id64 BIGINT UNSIGNED NOT NULL, map_name VARCHAR(128) NOT NULL,
  kills BIGINT NOT NULL DEFAULT 0, deaths BIGINT NOT NULL DEFAULT 0, assists BIGINT NOT NULL DEFAULT 0, headshot_kills BIGINT NOT NULL DEFAULT 0, damage BIGINT NOT NULL DEFAULT 0, score BIGINT NOT NULL DEFAULT 0,
  rounds_played BIGINT NOT NULL DEFAULT 0, rounds_won BIGINT NOT NULL DEFAULT 0, rounds_lost BIGINT NOT NULL DEFAULT 0, playtime_seconds BIGINT NOT NULL DEFAULT 0,
  shots BIGINT NOT NULL DEFAULT 0, hits BIGINT NOT NULL DEFAULT 0, bomb_plants BIGINT NOT NULL DEFAULT 0, bomb_defuses BIGINT NOT NULL DEFAULT 0, updated_at DATETIME(6) NOT NULL,
  PRIMARY KEY(scope,period_id,server_id,steam_id64,map_name), KEY ix_hsgo_map_leaderboard(scope,period_id,server_id,kills)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS hsgo_weapon_stats (
  scope ENUM('global','season') NOT NULL, period_id BIGINT UNSIGNED NOT NULL DEFAULT 0, server_id VARCHAR(64) NOT NULL, steam_id64 BIGINT UNSIGNED NOT NULL, weapon_name VARCHAR(128) NOT NULL,
  kills BIGINT NOT NULL DEFAULT 0, deaths BIGINT NOT NULL DEFAULT 0, assists BIGINT NOT NULL DEFAULT 0, headshot_kills BIGINT NOT NULL DEFAULT 0, damage BIGINT NOT NULL DEFAULT 0, score BIGINT NOT NULL DEFAULT 0,
  rounds_played BIGINT NOT NULL DEFAULT 0, rounds_won BIGINT NOT NULL DEFAULT 0, rounds_lost BIGINT NOT NULL DEFAULT 0, playtime_seconds BIGINT NOT NULL DEFAULT 0,
  shots BIGINT NOT NULL DEFAULT 0, hits BIGINT NOT NULL DEFAULT 0, bomb_plants BIGINT NOT NULL DEFAULT 0, bomb_defuses BIGINT NOT NULL DEFAULT 0, updated_at DATETIME(6) NOT NULL,
  PRIMARY KEY(scope,period_id,server_id,steam_id64,weapon_name), KEY ix_hsgo_weapon_leaderboard(scope,period_id,server_id,kills)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS hsgo_ingested_events (
  event_id BINARY(16) NOT NULL PRIMARY KEY, season_id BIGINT UNSIGNED NULL, server_id VARCHAR(64) NOT NULL, event_type VARCHAR(48) NOT NULL,
  scoring_version VARCHAR(32) NOT NULL, created_at DATETIME(6) NOT NULL, ingested_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  KEY ix_hsgo_ingested_created(created_at), KEY ix_hsgo_ingested_season(season_id,server_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS hsgo_bans (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, steam_id64 BIGINT UNSIGNED NOT NULL, player_name_snapshot VARCHAR(128) NULL, reason VARCHAR(1024) NOT NULL,
  admin_steam_id64 BIGINT UNSIGNED NULL, admin_name_snapshot VARCHAR(128) NULL, issued_at DATETIME(6) NOT NULL, expires_at DATETIME(6) NULL,
  revoked_at DATETIME(6) NULL, revoked_by_steam_id64 BIGINT UNSIGNED NULL, revoke_reason VARCHAR(1024) NULL,
  source ENUM('manual_import','authorized_sync','verified_game_event') NOT NULL, external_ban_id VARCHAR(191) NULL, server_id VARCHAR(64) NOT NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6), updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  UNIQUE KEY uq_hsgo_ban_source_external(source,external_ban_id), KEY ix_hsgo_ban_player_issued(steam_id64,issued_at), KEY ix_hsgo_ban_active(expires_at,revoked_at), KEY ix_hsgo_ban_server(server_id,issued_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
INSERT IGNORE INTO hsgo_schema_migrations(version) VALUES(1),(2);
