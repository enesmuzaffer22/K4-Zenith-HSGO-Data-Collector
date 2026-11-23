-- Manual archive import only. This DOES NOT ban or kick a player in CS2.
-- external_ban_id is required for repeat-safe imports from a named source.
INSERT INTO hsgo_bans
  (steam_id64,player_name_snapshot,reason,admin_steam_id64,admin_name_snapshot,issued_at,expires_at,source,external_ban_id,server_id)
VALUES
  (:steam_id64,:player_name,:reason,:admin_steam_id64,:admin_name,:issued_at_utc,:expires_at_utc,'manual_import',:external_ban_id,:server_id)
ON DUPLICATE KEY UPDATE updated_at=updated_at;

-- Revoke/archive; never DELETE history.
UPDATE hsgo_bans SET revoked_at=:revoked_at_utc,revoked_by_steam_id64=:revoked_by,revoke_reason=:revoke_reason,updated_at=UTC_TIMESTAMP(6)
WHERE source='manual_import' AND external_ban_id=:external_ban_id AND revoked_at IS NULL;
