using HsGoStats.Configuration;

namespace HsGoStats.Domain;

public sealed class HsrScoringPolicy(GameXpConfig config)
{
    public int Assist => config.Assist;
    public int Death(bool suicide) => suicide ? config.Suicide : config.Death;
    public int BombPlant => config.BombPlant;
    public int BombDefuse => config.BombDefuse;
    public int BombExplode => config.BombExplode;

    public int Kill(string? weapon, bool headshot, bool noScope, bool throughSmoke, bool penetrated, bool attackerBlind, bool attackerInAir)
    {
        var values = new List<int> { headshot ? config.HeadshotKill : config.NormalKill };
        if (WeaponRules.IsKnife(weapon)) values.Add(config.KnifeKill);
        if (noScope) values.Add(config.NoScopeKill);
        if (penetrated) values.Add(config.WallbangKill);
        if (throughSmoke) values.Add(config.ThroughSmokeKill);
        if (attackerInAir) values.Add(config.AirborneKill);
        if (attackerBlind) values.Add(config.BlindKill);
        if (weapon == "taser") values.Add(config.ZeusKill);
        if (weapon == "hegrenade") values.Add(config.HeKill);
        if (weapon is "inferno" or "molotov" or "incgrenade") values.Add(config.MolotovKill);
        return values.Max();
    }
}

public sealed record RatingSnapshot(long Xp, RatingTierConfig Tier, RatingTierConfig? NextTier, long? XpToNext, double? Progress);

public static class RatingResolver
{
    public static RatingSnapshot Resolve(long xp, RatingConfig config)
    {
        xp = Math.Max(config.MinimumXP, xp);
        var tiers = config.Tiers.OrderBy(x => x.MinXP).ToArray();
        var tier = tiers.Last(x => xp >= x.MinXP);
        var next = tiers.FirstOrDefault(x => x.MinXP > tier.MinXP);
        if (next is null) return new(xp, tier, null, null, null);
        var width = next.MinXP - tier.MinXP;
        return new(xp, tier, next, Math.Max(0, next.MinXP - xp), width == 0 ? 1 : Math.Clamp((double)(xp - tier.MinXP) / width, 0, 1));
    }
}

public static class WeaponRules
{
    public static bool IsKnife(string? weapon) => weapon is not null &&
        (weapon.Equals("knife", StringComparison.OrdinalIgnoreCase) || weapon.StartsWith("knife_", StringComparison.OrdinalIgnoreCase) ||
         weapon.Equals("bayonet", StringComparison.OrdinalIgnoreCase));
}
