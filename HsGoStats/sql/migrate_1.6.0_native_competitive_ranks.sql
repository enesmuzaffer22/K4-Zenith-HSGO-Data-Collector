-- HSGo Stats 1.6.0 native CS2 competitive rank upgrade.
-- Set this value to the exact ServerId from HsGoStats.json before running.
SET @hsgo_server_id = 'hsgo-public-1';

START TRANSACTION;

UPDATE hsgo_plugin_settings
SET setting_json = JSON_SET(
      setting_json,
      '$.tiers', JSON_ARRAY(
        JSON_OBJECT('id',1,'name','Silver I','minXP',0,'color','#B3B2B2'),
        JSON_OBJECT('id',2,'name','Silver II','minXP',500,'color','#B3B2B2'),
        JSON_OBJECT('id',3,'name','Silver III','minXP',1000,'color','#B3B2B2'),
        JSON_OBJECT('id',4,'name','Silver IV','minXP',1750,'color','#B3B2B2'),
        JSON_OBJECT('id',5,'name','Silver Elite','minXP',2750,'color','#D6D6D6'),
        JSON_OBJECT('id',6,'name','Silver Elite Master','minXP',4000,'color','#D6D6D6'),
        JSON_OBJECT('id',7,'name','Gold Nova I','minXP',5500,'color','#F8A815'),
        JSON_OBJECT('id',8,'name','Gold Nova II','minXP',7500,'color','#F8A815'),
        JSON_OBJECT('id',9,'name','Gold Nova III','minXP',10000,'color','#F8A815'),
        JSON_OBJECT('id',10,'name','Gold Nova Master','minXP',13000,'color','#F8A815'),
        JSON_OBJECT('id',11,'name','Master Guardian I','minXP',16500,'color','#58C3E4'),
        JSON_OBJECT('id',12,'name','Master Guardian II','minXP',20500,'color','#58C3E4'),
        JSON_OBJECT('id',13,'name','Master Guardian Elite','minXP',25000,'color','#8693C9'),
        JSON_OBJECT('id',14,'name','Distinguished Master Guardian','minXP',30000,'color','#8693C9'),
        JSON_OBJECT('id',15,'name','Legendary Eagle','minXP',36000,'color','#8575B4'),
        JSON_OBJECT('id',16,'name','Legendary Eagle Master','minXP',43000,'color','#8575B4'),
        JSON_OBJECT('id',17,'name','Supreme Master First Class','minXP',51000,'color','#E8375D'),
        JSON_OBJECT('id',18,'name','Global Elite','minXP',60000,'color','#E52421')
      ),
      '$.scoreboard', JSON_EXTRACT(
        '{"enabled":true,"skillGroupBaseId":1,"refreshSeconds":5,"useActiveSeason":true,"fallbackToGlobal":true}', '$'
      )
    ),
    revision = revision + 1,
    updated_by = 'migration-1.6.0',
    updated_at = UTC_TIMESTAMP(6)
WHERE BINARY server_id = BINARY @hsgo_server_id AND setting_key = 'rating';

COMMIT;

-- Exactly one row must be returned. Review the JSON before restarting/reloading the plugin.
SELECT setting_key, setting_json, revision, updated_by, updated_at
FROM hsgo_plugin_settings
WHERE BINARY server_id = BINARY @hsgo_server_id AND setting_key = 'rating';
