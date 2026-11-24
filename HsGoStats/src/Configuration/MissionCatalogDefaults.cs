namespace HsGoStats.Configuration;

public static class MissionCatalogDefaults
{
    private static readonly string[] Pistols = ["glock", "usp_silencer", "hkp2000", "deagle", "p250", "fiveseven", "tec9", "elite", "cz75a", "revolver"];
    private static readonly string[] AllowedSmgs = ["mp9", "mac10", "mp7", "mp5sd", "ump45"];
    private static readonly string[] AllowedRifles = ["ak47", "m4a1", "m4a1_silencer", "galilar", "famas", "aug", "sg556"];

    public static List<MissionDefinition> Create() =>
    [
        D(1,1,"Kill","Toplam 10 düşman öldür",10,40), D(2,1,"Kill","Toplam 25 düşman öldür",25,100),
        D(3,1,"Kill","Toplam 50 düşman öldür",50,200), D(4,1,"Kill","Toplam 100 düşman öldür",100,400),
        D(5,1,"RoundKills","Tek bir raundda 2 kill al",2,25), D(6,1,"RoundKills","Tek bir raundda 3 kill al",3,60),
        D(7,1,"RoundKills","Tek bir raundda 4 kill al",4,120), D(8,1,"RoundKills","Tek bir raundda ACE yap (5 kill)",5,250),
        D(9,1,"Assist","Toplam 20 asist yap",20,100), D(10,1,"Assist","Toplam 50 asist yap",50,250),

        D(11,2,"Kill","Toplam 5 headshot kill al",5,40,headshot:true), D(12,2,"Kill","Toplam 15 headshot kill al",15,100,headshot:true),
        D(13,2,"Kill","Toplam 30 headshot kill al",30,200,headshot:true), D(14,2,"Kill","Toplam 75 headshot kill al",75,450,headshot:true),
        D(15,2,"Kill","Toplam 150 headshot kill al",150,800,headshot:true),
        D(16,2,"RoundHeadshotKills","Tek bir raundda 2 headshot kill al",2,50), D(17,2,"RoundHeadshotKills","Tek bir raundda 3 headshot kill al",3,100),
        D(18,2,"Kill","AK-47 ile 20 headshot kill al",20,160,["ak47"],headshot:true),
        D(19,2,"Kill","M4A4 veya M4A1-S ile 20 headshot kill al",20,160,["m4a1","m4a1_silencer"],headshot:true),
        D(20,2,"Kill","Tabancalarla 15 headshot kill al",15,130,Pistols,headshot:true),

        D(21,3,"Kill","AK-47 ile 15 kill al",15,100,["ak47"]), D(22,3,"Kill","AK-47 ile 50 kill al",50,300,["ak47"]),
        D(23,3,"Kill","M4A4 ile 20 kill al",20,140,["m4a1"]), D(24,3,"Kill","M4A1-S ile 20 kill al",20,140,["m4a1_silencer"]),
        D(25,3,"Kill","Galil AR ile 15 kill al",15,110,["galilar"]), D(26,3,"Kill","FAMAS ile 15 kill al",15,110,["famas"]),
        D(27,3,"Kill","AUG ile 15 kill al",15,120,["aug"]), D(28,3,"Kill","SG 553 ile 15 kill al",15,120,["sg556"]),
        D(29,3,"Kill","MP9 ile 20 kill al",20,140,["mp9"]), D(30,3,"Kill","MAC-10 ile 20 kill al",20,140,["mac10"]),
        D(31,3,"Kill","MP7 veya MP5-SD ile 20 kill al",20,140,["mp7","mp5sd"]), D(32,3,"Kill","UMP-45 ile 15 kill al",15,120,["ump45"]),
        D(33,3,"Kill","SMG sınıfındaki izinli silahlarla toplam 25 kill al",25,170,AllowedSmgs),
        D(34,3,"Kill","İzinli saldırı tüfekleriyle toplam 15 headshot kill al",15,130,AllowedRifles,headshot:true),
        D(35,3,"RoundKillThreshold","İzinli saldırı tüfekleriyle en az 2 kill aldığın 5 farklı raund tamamla",5,140,AllowedRifles,minKills:2),

        D(36,4,"Kill","Glock-18 ile 15 kill al",15,100,["glock"]), D(37,4,"Kill","USP-S ile 15 kill al",15,100,["usp_silencer"]),
        D(38,4,"Kill","P2000 ile 15 kill al",15,100,["hkp2000"]), D(39,4,"Kill","Desert Eagle ile 10 kill al",10,100,["deagle"]),
        D(40,4,"Kill","Desert Eagle ile 30 kill al",30,250,["deagle"]), D(41,4,"Kill","P250 ile 15 kill al",15,120,["p250"]),
        D(42,4,"Kill","Five-SeveN ile 15 kill al",15,120,["fiveseven"]), D(43,4,"Kill","Tec-9 ile 15 kill al",15,120,["tec9"]),
        D(44,4,"Kill","Dual Berettas ile 15 kill al",15,130,["elite"]), D(45,4,"Kill","CZ75-Auto veya R8 Revolver ile 15 kill al",15,140,["cz75a","revolver"]),

        D(46,5,"Kill","AWP ile 10 kill al",10,90,["awp"]), D(47,5,"Kill","AWP ile 30 kill al",30,220,["awp"]),
        D(48,5,"Kill","AWP ile 75 kill al",75,500,["awp"]), D(49,5,"Kill","SSG 08 ile 10 kill al",10,110,["ssg08"]),
        D(50,5,"Kill","SSG 08 ile 30 kill al",30,270,["ssg08"]), D(51,5,"Kill","AWP veya SSG 08 ile dürbün açıkken toplam 20 kill al",20,150,["awp","ssg08"],scoped:true),
        D(52,5,"RoundKillThreshold","SSG 08 ile en az 2 kill aldığın 3 farklı raund tamamla",3,150,["ssg08"],minKills:2),
        D(53,5,"Kill","AWP ile 10 headshot kill al",10,180,["awp"],headshot:true), D(54,5,"Kill","SSG 08 ile 10 headshot kill al",10,200,["ssg08"],headshot:true),
        D(55,5,"RoundWeaponKills","Tek bir raundda AWP ile 2 kill al",2,50,["awp"]),

        D(56,6,"GrenadeUsed","10 Flashbang kullan",10,50,["flashbang"]), D(57,6,"GrenadeUsed","25 Flashbang kullan",25,100,["flashbang"]),
        D(58,6,"FlashEnemyBlinded","Flashbang ile toplam 10 düşmanı kör et",10,100), D(59,6,"FlashEnemyBlinded","Flashbang ile toplam 30 düşmanı kör et",30,250),
        D(60,6,"GrenadeUsed","10 HE Grenade kullan",10,50,["hegrenade"]), D(61,6,"Damage","HE Grenade ile toplam 200 hasar ver",200,150,["hegrenade"]),
        D(62,6,"Damage","HE Grenade ile toplam 500 hasar ver",500,300,["hegrenade"]), D(63,6,"Kill","HE Grenade ile 3 kill al",3,140,["hegrenade"]),
        D(64,6,"GrenadeUsed","10 Smoke Grenade kullan",10,50,["smokegrenade"]), D(65,6,"GrenadeUsed","30 Smoke Grenade kullan",30,120,["smokegrenade"]),
        D(66,6,"GrenadeUsed","10 Molotof kullan",10,60,["molotov"]), D(67,6,"Damage","Molotof veya yangın bombasıyla toplam 200 hasar ver",200,160,["inferno","molotov","incgrenade"]),
        D(68,6,"Kill","Molotof veya yangın bombasıyla 5 kill al",5,200,["inferno","molotov","incgrenade"]),
        D(69,6,"GrenadeUsed","10 Incendiary Grenade kullan",10,60,["incgrenade"]), D(70,6,"RoundFlashBlinds","Tek bir raundda Flashbang ile en az 2 düşmanı kör et",2,60),

        D(71,7,"BombPlant","Toplam 3 kez C4 kur",3,60), D(72,7,"BombPlant","Toplam 10 kez C4 kur",10,180), D(73,7,"BombPlant","Toplam 25 kez C4 kur",25,400),
        D(74,7,"OwnBombExplode","Kendi kurduğun C4'ün 3 kez patlamasını sağla",3,100), D(75,7,"OwnBombExplode","Kendi kurduğun C4'ün 10 kez patlamasını sağla",10,300),
        D(76,7,"BombDefuse","Toplam 3 kez C4 çöz",3,100), D(77,7,"BombDefuse","Toplam 10 kez C4 çöz",10,300), D(78,7,"BombDefuse","Toplam 20 kez C4 çöz",20,550),
        D(79,7,"RoundPlantWin","C4 kurduğun 5 raundu T tarafında kazan",5,160), D(80,7,"RoundDefuseWin","C4 çözdüğün 5 raundu CT tarafında kazan",5,180),

        D(81,8,"RoundWin","Toplam 5 raund kazan",5,50), D(82,8,"RoundWin","Toplam 15 raund kazan",15,120), D(83,8,"RoundWin","Toplam 30 raund kazan",30,250),
        D(84,8,"RoundWin","T tarafında 10 raund kazan",10,100,team:"T"), D(85,8,"RoundWin","CT tarafında 10 raund kazan",10,100,team:"CT"),
        D(86,8,"RoundWinAlive","Ölmeden tamamladığın 5 raundu kazan",5,80), D(87,8,"RoundWinAlive","Ölmeden tamamladığın 15 raundu kazan",15,220),
        D(88,8,"RoundWinKills","En az 2 kill aldığın 5 raundu kazan",5,180,minKills:2), D(89,8,"RoundWinKills","En az 3 kill aldığın bir raundu kazan",1,100,minKills:3),
        D(90,8,"RoundWinAssists","En az 1 asist yaptığın 10 raundu kazan",10,160,minAssists:1),

        D(91,9,"Kill","Bıçak ile 3 kill al",3,150,knife:true), D(92,9,"Kill","Bıçak ile 10 kill al",10,450,knife:true),
        D(93,9,"Kill","Zeus x27 ile 3 kill al",3,150,["taser"]), D(94,9,"Kill","Zeus x27 ile 10 kill al",10,450,["taser"]),
        D(95,9,"Kill","AWP veya SSG 08 ile 5 NoScope kill al",5,200,["awp","ssg08"],noScope:true),
        D(96,9,"Kill","Smoke arkasından 5 kill al",5,180,throughSmoke:true), D(97,9,"Kill","Duvar arkasından 5 kill al",5,220,penetrated:true),
        D(98,9,"Kill","Flash etkisindeyken 5 kill al",5,200,attackerBlind:true), D(99,9,"Kill","Havadayken 3 kill al",3,250,attackerInAir:true),
        D(100,9,"ClutchWin","Tek başına en az 2 rakibe karşı kaldığın bir raundu kazan (1v2 clutch)",1,300)
    ];

    private static MissionDefinition D(int id, int category, string eventName, string description, int target, int reward,
        IEnumerable<string>? weapons = null, bool headshot = false, bool noScope = false, bool scoped = false,
        bool throughSmoke = false, bool penetrated = false, bool attackerBlind = false, bool attackerInAir = false,
        bool knife = false, int minKills = 0, int minAssists = 0, string? team = null) => new()
    {
        Id = id, Category = category, Event = eventName, Description = description, Target = target, RewardXp = reward,
        Weapons = weapons?.ToList() ?? [], Headshot = headshot, NoScope = noScope, Scoped = scoped,
        ThroughSmoke = throughSmoke, Penetrated = penetrated, AttackerBlind = attackerBlind,
        AttackerInAir = attackerInAir, Knife = knife, MinimumRoundKills = minKills,
        MinimumRoundAssists = minAssists, Team = team
    };
}
