using System.Security.Cryptography;
using System.Text;
using HsGoStats.Configuration;

namespace HsGoStats.Domain;

public sealed class ScoringPolicy(ScoringConfig config)
{
    public string Version { get; } = BuildVersion(config);
    public int Kill(bool headshot) => config.Kill + (headshot ? config.HeadshotBonus : 0);
    public int Assist => config.Assist;
    public int Death(bool suicide) => suicide ? config.Suicide : config.Death;
    public int BombPlant => config.BombPlant;
    public int BombDefuse => config.BombDefuse;
    public int RoundWin => config.RoundWin;
    public int BombDrop => config.BombDrop;
    public int BombPickup => config.BombPickup;

    private static string BuildVersion(ScoringConfig c)
    {
        var canonical = $"kill={c.Kill};hs={c.HeadshotBonus};assist={c.Assist};death={c.Death};plant={c.BombPlant};defuse={c.BombDefuse};roundwin={c.RoundWin};drop={c.BombDrop};pickup={c.BombPickup};suicide={c.Suicide};ff={c.FriendlyFirePenalty};dropmax={c.BombDropPenaltyMaxPerRound}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16].ToLowerInvariant();
    }
}
