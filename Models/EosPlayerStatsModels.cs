namespace FFVIIEverCrisisAnalyzer.Models;

/// <summary>
/// Readable stats derived from an EOS account export (the raw AccountInfo JSON).
/// Numbers mirror the community stats summary shared on Discord.
/// </summary>
public sealed class EosPlayerStats
{
    public string PlayerName { get; set; } = string.Empty;
    public string? ProfileMessage { get; set; }
    public string? GuildName { get; set; }

    public EosCurrency Gil { get; set; } = new();
    public long RedCrystals { get; set; }
    public long BlueCrystals { get; set; }
    public long TotalCrystals => RedCrystals + BlueCrystals;

    public long TotalPulls { get; set; }
    public long TotalFreePulls { get; set; }
    public long TotalCrystalPulls { get; set; }
    public long EstimatedCrystalsUsedForPulls { get; set; }
    public int TotalHomeBackgroundsOwned { get; set; }
    public long TotalCrystalAdsViewed { get; set; }
    public long TotalHighwindCactuars { get; set; }
    public long TotalHighwindGoldCactuars { get; set; }
    public long TotalHighwindGoldBombs { get; set; }

    public List<EosDrawStat> Draws { get; set; } = new();
    public List<EosItemStat> Items { get; set; } = new();
    /// <summary>Every weapon in the game data, owned or not.</summary>
    public List<EosWeaponStat> Weapons { get; set; } = new();
    /// <summary>Every outfit in the game data, owned or not.</summary>
    public List<EosOutfitStat> Outfits { get; set; } = new();
    public List<EosMateriaRecipeStat> MateriaRecipes { get; set; } = new();
    public EosMateriaOverview Materia { get; set; } = new();

    public List<string> Warnings { get; set; } = new();

    // Account
    public int PlayerRank { get; set; }
    public int MaxPlayerRank { get; set; }
    public DateTime? AccountCreated { get; set; }
    public long? MaxPower { get; set; }
    public int? GuildLevel { get; set; }
    public DateTime? GuildJoined { get; set; }

    public List<EosCharacterStat> Characters { get; set; } = new();
    public List<EosCollectionStat> Collection { get; set; } = new();
    public List<EosRecordGroup> RecordGroups { get; set; } = new();
    public List<EosBadge> Badges { get; set; } = new();
    public List<EosEventRank> CrisisEvents { get; set; } = new();
    public List<EosEventRank> DamageRankings { get; set; } = new();
    public List<EosEventRank> GuildBattles { get; set; } = new();
    public List<EosEventRank> ScoreDungeons { get; set; } = new();
    public List<EosProgressStat> Towers { get; set; } = new();
    public List<EosDamageChallengeStat> DamageChallenges { get; set; } = new();

    public List<EosParty> Parties { get; set; } = new();
    public List<EosChocobo> Chocobos { get; set; } = new();
    public List<EosChocoboExpedition> ChocoboExpeditions { get; set; } = new();
    public List<EosMateriaOwned> MateriaOwned { get; set; } = new();
    /// <summary>Sub stat labels that appear on at least one owned materia, in display order.</summary>
    public List<string> MateriaStatColumns { get; set; } = new();
    public List<EosDungeonStat> CriterionDungeons { get; set; } = new();
    public List<EosProgressStat> EscalationChallenges { get; set; } = new();
    public List<EosSeasonPass> SeasonPasses { get; set; } = new();
    public EosAchievements Achievements { get; set; } = new();

    /// <summary>Only filled when the player opts in to showing purchases.</summary>
    public EosPurchases? Purchases { get; set; }
}

public sealed class EosParty
{
    public string Name { get; set; } = string.Empty;
    public long CombatPower { get; set; }
    public List<EosPartyMember> Members { get; set; } = new();
}

public sealed class EosPartyMember
{
    public string Character { get; set; } = string.Empty;
    public string? MainWeapon { get; set; }
    public string? Outfit { get; set; }
}

public sealed class EosChocobo
{
    public int Rarity { get; set; }
    public int Rank { get; set; }
    public int RankLimit { get; set; }
    /// <summary>Stat balance in percent: speed, stamina, intelligence, adaptability.</summary>
    public int[] Balance { get; set; } = new int[4];
    public DateTime? Obtained { get; set; }
    public bool Locked { get; set; }
}

public sealed class EosChocoboExpedition
{
    public string Area { get; set; } = string.Empty;
    public string? Character { get; set; }
    public DateTime? Started { get; set; }
}

public sealed class EosMateriaOwned
{
    public string Name { get; set; } = string.Empty;
    public int Stars { get; set; }
    public bool Locked { get; set; }
    public DateTime? Obtained { get; set; }
    /// <summary>Sub stats by label, e.g. "PATK %" = 3.6 or "MDEF" = 6.</summary>
    public Dictionary<string, decimal> Stats { get; set; } = new();
}

public sealed class EosDungeonStat
{
    public string Name { get; set; } = string.Empty;
    public int Difficulty { get; set; }
    public string BestRank { get; set; } = string.Empty;
    public int RankOrder { get; set; }
    public long HighScore { get; set; }
    public long Clears { get; set; }
    public string? Team { get; set; }
    public DateTime? LastImproved { get; set; }
}

public sealed class EosSeasonPass
{
    public string Name { get; set; } = string.Empty;
    public DateTime? Ended { get; set; }
    public int Steps { get; set; }
    public int FreeClaimed { get; set; }
    public int PremiumClaimed { get; set; }
    public bool HasPremium { get; set; }
}

public sealed class EosAchievements
{
    public int Unlocked { get; set; }
    public int Total { get; set; }
    public DateTime? First { get; set; }
    public DateTime? Latest { get; set; }
    /// <summary>Unlocks per year, oldest first.</summary>
    public List<EosNamedCount> ByYear { get; set; } = new();
}

public sealed class EosPurchases
{
    public List<EosPaidPurchase> Paid { get; set; } = new();
    public List<EosShopSummary> Shops { get; set; } = new();
    public decimal TotalListPriceUsd => Paid.Sum(p => p.PriceUsd * p.Count);
    public long TotalCrystals => Paid.Sum(p => p.Crystals * p.Count);
    public long TotalPacks => Paid.Sum(p => (long)p.Count);
}

public sealed class EosPaidPurchase
{
    public string Name { get; set; } = string.Empty;
    public string Shop { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal PriceUsd { get; set; }
    public long Crystals { get; set; }
    public DateTime? LastPurchased { get; set; }
}

public sealed class EosShopSummary
{
    public string Name { get; set; } = string.Empty;
    public int ItemsBought { get; set; }
    public long TotalPurchases { get; set; }
    public DateTime? LastPurchased { get; set; }
}

public sealed class EosCharacterStat
{
    public string Name { get; set; } = string.Empty;
    public int Level { get; set; }
    public int MaxLevel { get; set; }
    public int CostumesOwned { get; set; }
    public int CostumesTotal { get; set; }
    public int WeaponsOwned { get; set; }
    public int WeaponsTotal { get; set; }
    public long WeaponPulls { get; set; }
}

public sealed class EosCollectionStat
{
    public string Name { get; set; } = string.Empty;
    public int Owned { get; set; }
    public int Total { get; set; }
    public int Percent => Total == 0 ? 0 : (int)Math.Floor(100.0 * Math.Min(Owned, Total) / Total);
}

public sealed class EosRecordGroup
{
    public string Name { get; set; } = string.Empty;
    public List<EosNamedCount> Records { get; set; } = new();
}

public sealed class EosNamedCount
{
    public string Name { get; set; } = string.Empty;
    public long Value { get; set; }
    /// <summary>The game stores capped scores as int.MaxValue.</summary>
    public bool IsMaxed => Value >= int.MaxValue;
}

public sealed class EosEventRank
{
    public string Name { get; set; } = string.Empty;
    public long EventBaseId { get; set; }
    public DateTime? EndDate { get; set; }
    public int FinalRank { get; set; }
    public long Points { get; set; }
    public int HighStage { get; set; }
    /// <summary>Best single-battle damage (damage ranking events only).</summary>
    public long BestDamage { get; set; }
    /// <summary>Party used for the most recent personal best (damage ranking events only).</summary>
    public string? BestTeam { get; set; }
    /// <summary>Final rank of the player's current guild, when the export has it (guild battles only).</summary>
    public int? CurrentGuildRank { get; set; }
    /// <summary>Placement was good enough to earn a ranking badge.</summary>
    public bool EarnedBadge { get; set; }
}

public sealed class EosBadge
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long Count { get; set; }
    /// <summary>For ranking badges: the placements that earned it.</summary>
    public List<string> EarnedFrom { get; set; } = new();
}

public sealed class EosProgressStat
{
    public string Name { get; set; } = string.Empty;
    public int Cleared { get; set; }
    public int Total { get; set; }
}

public sealed class EosDamageChallengeStat
{
    public string Name { get; set; } = string.Empty;
    public long HighScore { get; set; }
    public long Attempts { get; set; }
    public DateTime? LastImproved { get; set; }
    public bool IsMaxed => HighScore >= int.MaxValue;
}

public sealed class EosCurrency
{
    public long Count { get; set; }
    public long TotalObtained { get; set; }
}

public sealed class EosDrawStat
{
    public long Id { get; set; }
    public int Type { get; set; }
    public string TypeLabel { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long TotalPulls { get; set; }
    public long CrystalPulls { get; set; }
    public int TotalStampPages { get; set; }
    public int TotalStamps { get; set; }
    public long EstimatedCrystalsUsed { get; set; }
    /// <summary>When the player last pulled on this draw (the export has no first-pull date).</summary>
    public DateTime? LastPulled { get; set; }
    /// <summary>Weapons the banner advertised, when the game data lists them.</summary>
    public string? FeaturedWeapons { get; set; }
}

public sealed class EosItemStat
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long Count { get; set; }
    public long TotalObtained { get; set; }
}

public sealed class EosWeaponStat
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Character { get; set; } = string.Empty;
    public int GachaPulls { get; set; }
    public bool Owned { get; set; } = true;
}

public sealed class EosOutfitStat
{
    public string Name { get; set; } = string.Empty;
    public string Character { get; set; } = string.Empty;
    public bool Owned { get; set; }
}

public sealed class EosMateriaRecipeStat
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long TotalCrafted { get; set; }
}

public sealed class EosMateriaOverview
{
    /// <summary>Index 0 = 1★ ... index 4 = 5★.</summary>
    public long[] CraftedByStar { get; set; } = new long[5];
    public long[] ObtainedByStar { get; set; } = new long[5];
    public long TotalCrafted => CraftedByStar.Sum();
    public long TotalObtained => ObtainedByStar.Sum();
}
