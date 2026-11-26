-- HSGo Stats 1.5.0 rank, scoreboard and leaderboard-command settings upgrade.
-- Set this value to the exact ServerId from HsGoStats.json before running.
SET @hsgo_server_id = 'hsgo-public-1';

START TRANSACTION;

UPDATE hsgo_plugin_settings
SET setting_json = JSON_SET(
      setting_json,
      '$.tiers', JSON_ARRAY(
        JSON_OBJECT('id',1,'name','Recruit','minXP',0,'color','#B3B2B2'),
        JSON_OBJECT('id',2,'name','Soldier','minXP',1000,'color','#BF8C47'),
        JSON_OBJECT('id',3,'name','Trooper','minXP',3000,'color','#FFFFFF'),
        JSON_OBJECT('id',4,'name','Veteran','minXP',6000,'color','#F8A815'),
        JSON_OBJECT('id',5,'name','Warrior','minXP',10000,'color','#58C3E4'),
        JSON_OBJECT('id',6,'name','Warlord','minXP',15000,'color','#8693C9'),
        JSON_OBJECT('id',7,'name','Phantom','minXP',22500,'color','#E8375D'),
        JSON_OBJECT('id',8,'name','Supreme','minXP',32500,'color','#8575B4'),
        JSON_OBJECT('id',9,'name','Conquer','minXP',45000,'color','#EC651C'),
        JSON_OBJECT('id',10,'name','Immortal','minXP',60000,'color','#E52421')
      ),
      '$.scoreboard', JSON_EXTRACT(
        '{"enabled":true,"skillGroupBaseId":19010001,"refreshSeconds":5,"useActiveSeason":true,"fallbackToGlobal":true}', '$'
      )
    ),
    revision = revision + 1,
    updated_by = 'migration-1.5.0',
    updated_at = UTC_TIMESTAMP(6)
-- BINARY keeps the server-id match exact and avoids connection/table collation conflicts.
WHERE BINARY server_id = BINARY @hsgo_server_id AND setting_key = 'rating';

UPDATE hsgo_plugin_settings
SET setting_json = JSON_SET(
      setting_json,
      '$.leaderboard', JSON_EXTRACT(
        '{"enabled":true,"aliases":["!siralama"],"cooldownSeconds":10}', '$'
      )
    ),
    revision = revision + 1,
    updated_by = 'migration-1.5.0',
    updated_at = UTC_TIMESTAMP(6)
WHERE BINARY server_id = BINARY @hsgo_server_id AND setting_key = 'chat_commands';

COMMIT;

-- Both rows must be present. Review the JSON before restarting/reloading the plugin.
SELECT setting_key, setting_json, revision, updated_by, updated_at
FROM hsgo_plugin_settings
WHERE BINARY server_id = BINARY @hsgo_server_id AND setting_key IN ('rating','chat_commands')
ORDER BY setting_key;
