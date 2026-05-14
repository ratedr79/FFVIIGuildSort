using System;
using System.Collections.Generic;

namespace FFVIIEverCrisisAnalyzer.Models
{
    public enum GuildBattleSheetRecommendationMode
    {
        Traditional,
        Character
    }

    public sealed class GuildBattleSheetConfiguration
    {
        public int SchemaVersion { get; init; } = 1;
        public List<GuildBattleSheetBattleDefinition> Battles { get; init; } = new();
    }

    public sealed class GuildBattleSheetBattleDefinition
    {
        public string Id { get; init; } = string.Empty;
        public string Month { get; init; } = string.Empty;
        public int Year { get; init; }
        public string Element { get; init; } = string.Empty;
        public string AbilityType { get; init; } = string.Empty;
        public string Rank { get; init; } = string.Empty;
        public string Writeup { get; init; } = string.Empty;
        public List<string> TopPicks { get; init; } = new();
        public List<string> HiddenWeapons { get; init; } = new();
        public List<string> ConditionalMechanics { get; init; } = new();
    }

    public sealed class GuildBattleSheetBattleSummary
    {
        public required string Id { get; init; }
        public required string DisplayLabel { get; init; }
        public required string Element { get; init; }
        public required string AbilityType { get; init; }
        public string Rank { get; init; } = string.Empty;
        public int SortKey { get; init; }
    }

    public sealed class GuildBattleSheetEntry
    {
        public required string WeaponId { get; init; }
        public required string Name { get; init; }
        public required string Character { get; init; }
        public required string ImageUrl { get; init; }
        public required string PreviewImageUrl { get; init; }
        public required string Element { get; init; }
        public required string AbilityType { get; init; }
        public required string AbilityText { get; init; }
        public required string EquipmentType { get; init; }
        public required List<string> Highlights { get; init; }
        public required List<string> MatchReasons { get; init; }
        public required string InclusionSummary { get; init; }
        public required List<string> RelevantCustomizationDetails { get; init; }
        public required List<string> EffectTags { get; init; }
        public required List<string> RAbilityNames { get; init; }
        public required List<string> RAbilityDetails { get; init; }
        public bool IsTopPick { get; init; }
        public bool HasCustomizations { get; init; }
        public double DamagePercent { get; init; }
        public double Score { get; init; }
        public int PatkAtMaxLevel { get; init; }
        public int MatkAtMaxLevel { get; init; }
        public int HealAtMaxLevel { get; init; }
    }

    public sealed class GuildBattleSheetSection
    {
        public required string Title { get; init; }
        public required string EmptyMessage { get; init; }
        public required List<GuildBattleSheetEntry> Entries { get; init; }
    }

    public sealed class GuildBattleSheetViewModel
    {
        public required GuildBattleSheetBattleDefinition SelectedBattle { get; init; }
        public required GuildBattleSheetBattleSummary SelectedBattleSummary { get; init; }
        public required List<GuildBattleSheetBattleSummary> AvailableBattles { get; init; }
        public GuildBattleSheetRecommendationMode RecommendationMode { get; init; }
        public required List<GuildBattleSheetEntry> MainRecommendations { get; init; }
        public required GuildBattleSheetSection PotencySection { get; init; }
        public required GuildBattleSheetSection SubWeaponSection { get; init; }
        public required GuildBattleSheetSection ResistanceDownSection { get; init; }
        public required GuildBattleSheetSection DamageUpSection { get; init; }
        public required GuildBattleSheetSection DamageBonusSection { get; init; }
        public required List<string> WriteupLines { get; init; }
        public bool UsesManualWriteup { get; init; }
        public bool DebugMode { get; init; }
        public DateTimeOffset GeneratedAtUtc { get; init; }
    }
}
