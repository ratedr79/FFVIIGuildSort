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
    /// <summary>Limited "Battle Tower: Singularity" events; floors are event solo battles.</summary>
    public List<EosProgressStat> SingularityTowers { get; set; } = new();
    public List<EosDamageChallengeStat> DamageChallenges { get; set; } = new();

    public List<EosParty> Parties { get; set; } = new();
    public List<EosChocobo> Chocobos { get; set; } = new();
    public List<EosChocoboExpedition> ChocoboExpeditions { get; set; } = new();
    public List<EosMateriaOwned> MateriaOwned { get; set; } = new();
    /// <summary>Sub stat labels that appear on at least one owned materia, in display order.</summary>
    public List<string> MateriaStatColumns { get; set; } = new();
    public List<EosDungeonStat> CriterionDungeons { get; set; } = new();
    public List<EosProgressStat> EscalationChallenges { get; set; } = new();
    /// <summary>Every Crash battle that awards a Crash badge, cleared or not.</summary>
    public List<EosCrashBattle> CrashBattles { get; set; } = new();
    public List<EosSeasonPass> SeasonPasses { get; set; } = new();
    public EosAchievements Achievements { get; set; } = new();

    public EosHighwind Highwind { get; set; } = new();
    public EosGuildInfo Guild { get; set; } = new();
    /// <summary>Every memoria in the game data, owned or not.</summary>
    public List<EosMemoriaStat> Memoria { get; set; } = new();
    /// <summary>Every special skill (limit breaks, summon skills) and overaccel skill, owned or not.</summary>
    public List<EosSkillStat> Skills { get; set; } = new();
    /// <summary>Non-repeating missions the player has progress on.</summary>
    public List<EosMissionStat> Missions { get; set; } = new();

    public EosBattleStats Battles { get; set; } = new();
    public List<EosGrowthBoard> GrowthBoards { get; set; } = new();
    /// <summary>Shop exchanges (not real-money packs), one row per shop item bought.</summary>
    public List<EosShopExchange> ShopExchanges { get; set; } = new();
    public List<EosWishlist> Wishlists { get; set; } = new();
    public List<EosBoxDraw> BoxDraws { get; set; } = new();
    public EosLogins Logins { get; set; } = new();
    /// <summary>Different players met for the first time in co-op.</summary>
    public long? CoopPlayersMet { get; set; }
    /// <summary>Limited-time shop packs the game offered the player, one row per pack.</summary>
    public List<EosLimitedOffer> LimitedOffers { get; set; } = new();
    /// <summary>Highwind Paint Cans held (a "big item" stored as significand × 10^exponent).</summary>
    public long? PaintCans { get; set; }

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
    public List<EosDungeonBattle> Battles { get; set; } = new();
}

public sealed class EosDungeonBattle
{
    public int Idx { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Enemy { get; set; } = string.Empty;
    /// <summary>Highest boss enhancement level beaten, when the battle has been won.</summary>
    public int? MaxEnhancement { get; set; }
    public long Wins { get; set; }
}

public sealed class EosCrashBattle
{
    public string Name { get; set; } = string.Empty;
    public bool Coop { get; set; }
    public bool Attempted { get; set; }
    public long Wins { get; set; }
    public long HighScore { get; set; }
    public bool Cleared => Wins > 0;
}

public sealed class EosEventStage
{
    public string Name { get; set; } = string.Empty;
    public string? Team { get; set; }
    public DateTime? BestSet { get; set; }
    public bool WithMemoria { get; set; }
    public int Modifiers { get; set; }
    public long HighScore { get; set; }
    public long Clears { get; set; }
    /// <summary>Stage level of the best run (Crisis events only): the default level plus the selected modifiers.</summary>
    public int? StageLevel { get; set; }
}

public sealed class EosGuildFight
{
    public int Stars { get; set; }
    public string Boss { get; set; } = string.Empty;
    public long HighScore { get; set; }
    public long PracticeBest { get; set; }
    /// <summary>Current guild totals, when the player is still in the guild that fought it.</summary>
    public long? GuildDefeats { get; set; }
    public long? GuildScore { get; set; }
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
    /// <summary>Per-stage bests (Crisis and Damage Ranking events).</summary>
    public List<EosEventStage> Stages { get; set; } = new();
    /// <summary>Per-boss results (guild battles).</summary>
    public List<EosGuildFight> Fights { get; set; } = new();
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
    public DateTime? FirstObtained { get; set; }
    public DateTime? LastObtained { get; set; }
}

public sealed class EosWeaponStat
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Character { get; set; } = string.Empty;
    public int GachaPulls { get; set; }
    public bool Owned { get; set; } = true;
    /// <summary>Stars: 3-5 for standard weapons, 6 for ultimate weapons (RarityType 101).</summary>
    public int Stars { get; set; }
    public int Level { get; set; }
    public int MaxLevel { get; set; }
    /// <summary>Overboost level 0-10 (WeaponUpgradeType 1).</summary>
    public int Overboost { get; set; }
    /// <summary>The extra +1..+20 after OB10 (WeaponUpgradeType 2).</summary>
    public int OverboostPlus { get; set; }
    /// <summary>This weapon's own "Parts" item (Weapon.WeaponMedalItemId) currently held; null if the weapon has none.</summary>
    public long? Parts { get; set; }
    public long PartsObtained { get; set; }
    public string OverboostLabel => Stars == 6 ? "" : OverboostPlus > 0 ? $"OB10 +{OverboostPlus}" : Overboost > 0 ? $"OB{Overboost}" : "";
    /// <summary>Sort key: OB level then the +N stage.</summary>
    public int OverboostOrder => Overboost * 100 + OverboostPlus;
    public DateTime? FirstObtained { get; set; }
    /// <summary>Times this weapon was taken from a weapon voucher exchange shop.</summary>
    public long VoucherExchanges { get; set; }
    public DateTime? LastVoucherExchange { get; set; }
    /// <summary>Customizations this weapon can have (Heart/Spade/Diamond), with which are unlocked and which is active.</summary>
    public List<EosWeaponCustomization> Customizations { get; set; } = new();
}

public sealed class EosWeaponCustomization
{
    /// <summary>WeaponEvolveType: 1 Heart, 2 Spade, 3 Diamond, 4 special.</summary>
    public int Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    /// <summary>What it changes, e.g. "New ability: Thunderstrike" or "Adds passive: …".</summary>
    public string Effect { get; set; } = string.Empty;
    public bool Unlocked { get; set; }
    public bool Active { get; set; }
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

public sealed class EosHighwind
{
    public long IdleCollections { get; set; }
    public List<EosNamedCount> Parts { get; set; } = new();
    public List<EosHighwindKeyItem> KeyItems { get; set; } = new();
    public int KeyItemsTotal { get; set; }
}

public sealed class EosHighwindKeyItem
{
    public string Name { get; set; } = string.Empty;
    public int Upgrades { get; set; }
    public int MaxUpgrades { get; set; }
    public DateTime? Obtained { get; set; }
}

public sealed class EosGuildInfo
{
    public DateTime? Created { get; set; }
    public int TimesLeftAGuild { get; set; }
    public List<EosNamedCount> Bonuses { get; set; } = new();
    public Dictionary<string, int> BonusMax { get; set; } = new();
    public List<EosGuildAchievement> Achievements { get; set; } = new();
}

public sealed class EosGuildAchievement
{
    public string Description { get; set; } = string.Empty;
    public long Progress { get; set; }
    public long Goal { get; set; }
    public bool Complete => Goal > 0 && Progress >= Goal;
}

public sealed class EosMemoriaStat
{
    public string Name { get; set; } = string.Empty;
    public int Stars { get; set; }
    public string? Source { get; set; }
    public bool Owned { get; set; }
    public int Fragments { get; set; }
    public int FragmentsNeeded { get; set; }
    public DateTime? Obtained { get; set; }
    /// <summary>Analysis level from AnalysisPoint; null when not owned.</summary>
    public int? Level { get; set; }
    public int MaxLevel { get; set; }
}

public sealed class EosGrowthBoard
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Boards { get; set; }
    public int NodesUnlocked { get; set; }
    public int NodesTotal { get; set; }
    public long Hp { get; set; }
    public long PhysicalAttack { get; set; }
    public long MagicalAttack { get; set; }
    public long PhysicalDefense { get; set; }
    public long MagicalDefense { get; set; }
    public long Healing { get; set; }
}

public sealed class EosLogins
{
    /// <summary>Days the daily login bonus was claimed.</summary>
    public long DaysLoggedIn { get; set; }
    /// <summary>Days from account creation to the last daily login.</summary>
    public long DaysAvailable { get; set; }
    public DateTime? LastLogin { get; set; }
    /// <summary>Limited login bonus campaigns with at least one claim.</summary>
    public int Campaigns { get; set; }
    public long CampaignLogins { get; set; }
}

public sealed class EosLimitedOffer
{
    public string Name { get; set; } = string.Empty;
    public DateTime? FirstOffered { get; set; }
    public DateTime? LastOffered { get; set; }
    public int TimesOffered { get; set; }
    public long PriceCrystals { get; set; }
    public long Bought { get; set; }
    public long CrystalsSpent => PriceCrystals * Bought;
}

public sealed class EosShopExchange
{
    public string Name { get; set; } = string.Empty;
    public string Shop { get; set; } = string.Empty;
    public long Count { get; set; }
    public DateTime? LastPurchased { get; set; }
}

public sealed class EosWishlist
{
    public long Id { get; set; }
    /// <summary>Draws that used this wishlist.</summary>
    public string Banners { get; set; } = string.Empty;
    /// <summary>Latest pull on any of those draws; the export has no date for the choice itself.</summary>
    public DateTime? LastPulled { get; set; }
    public List<EosWishPick> Picks { get; set; } = new();
    public int Changed => Picks.Count(p => p.Changed);
}

public sealed class EosWishPick
{
    public int Slot { get; set; }
    public string Weapon { get; set; } = string.Empty;
    public string Character { get; set; } = string.Empty;
    /// <summary>True when the player replaced the game's default pick for this slot.</summary>
    public bool Changed { get; set; }
}

public sealed class EosBoxDraw
{
    public string Event { get; set; } = string.Empty;
    public string Box { get; set; } = string.Empty;
    public long EventId { get; set; }
    public long BoxesReset { get; set; }
    public long Draws { get; set; }
    public string Ticket { get; set; } = string.Empty;
    public long TicketsUsed { get; set; }
    public DateTime? LastTicket { get; set; }
}

public sealed class EosSkillStat
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string For { get; set; } = string.Empty;
    public bool Owned { get; set; }
}

public sealed class EosMissionStat
{
    public string Category { get; set; } = string.Empty;
    public string Mission { get; set; } = string.Empty;
    public long Progress { get; set; }
    public long Goal { get; set; }
    public bool Complete { get; set; }
}

/// <summary>Lifetime battle wins. Only sources that don't overlap are counted in the totals.</summary>
public sealed class EosBattleStats
{
    public long SoloWins { get; set; }
    public long CoopWins { get; set; }
    public long TotalWins => SoloWins + CoopWins;
    public int StoryBattlesCleared { get; set; }
    public List<EosBattleMode> ByMode { get; set; } = new();
    public List<EosEventBattles> Events { get; set; } = new();
    public List<EosBattleWin> TopBattles { get; set; } = new();
    public List<EosAreaBattles> Areas { get; set; } = new();
}

public sealed class EosBattleMode
{
    public string Name { get; set; } = string.Empty;
    public bool Coop { get; set; }
    public long Wins { get; set; }
    public int Battles { get; set; }
}

public sealed class EosEventBattles
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long SoloWins { get; set; }
    public long CoopWins { get; set; }
    public long TotalWins => SoloWins + CoopWins;
    public int Battles { get; set; }
}

public sealed class EosBattleWin
{
    public string Name { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public bool Coop { get; set; }
    public long Wins { get; set; }
    public long HighScore { get; set; }
}

public sealed class EosAreaBattles
{
    public string Category { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Coop { get; set; }
    public long Wins { get; set; }
    public int Battles { get; set; }
    public long HighScore { get; set; }
}
