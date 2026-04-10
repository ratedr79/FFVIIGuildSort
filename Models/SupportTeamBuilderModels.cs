using System;
using System.Collections.Generic;
using System.Linq;

namespace FFVIIEverCrisisAnalyzer.Models
{
    public enum SupportFilterRange
    {
        All,
        SingleTargetOrAll,
        SelfOrSingleTargetOrAll
    }

    public enum SupportPotencyTier
    {
        Low = 1,
        Mid = 2,
        High = 3,
        ExtraHigh = 4
    }

    public sealed class SupportTeamFilter
    {
        public string EffectType { get; set; } = string.Empty;
        public SupportFilterRange Range { get; set; } = SupportFilterRange.SingleTargetOrAll;
        public SupportPotencyTier MinBasePotency { get; set; } = SupportPotencyTier.Low;
        public SupportPotencyTier MinMaxPotency { get; set; } = SupportPotencyTier.Low;

        public bool IsValid => !string.IsNullOrWhiteSpace(EffectType);
    }

    public sealed class SupportTeamRequest
    {
        public List<SupportTeamFilter> Filters { get; set; } = new();
        public int MaxCharacterCount { get; set; } = 2;
        public HashSet<string> MustHaveCharacters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ExcludeCharacters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> OwnedObByWeaponId { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> OwnedOutfitById { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class SupportTeamBuilderOptionData
    {
        public List<string> EffectTypes { get; set; } = new();
        public List<string> Characters { get; set; } = new();
        public Dictionary<string, bool> EffectHasPotency { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<SupportTeamPreset> Presets { get; set; } = new();
    }

    public sealed class SupportTeamPreset
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Effects { get; set; } = new();
    }

    public sealed class SupportTeamPresetConfiguration
    {
        public List<SupportTeamPreset> Presets { get; set; } = new();
    }

    public sealed class SupportWeaponMatch
    {
        public WeaponSearchItem Weapon { get; set; } = null!;
        public string WeaponId { get; set; } = string.Empty;
        public string WeaponName { get; set; } = string.Empty;
        public string Character { get; set; } = string.Empty;
        public string Range { get; set; } = string.Empty;
        public string AbilityText { get; set; } = string.Empty;
        public SupportPotencyTier BasePotency { get; set; } = SupportPotencyTier.Low;
        public SupportPotencyTier MaxPotency { get; set; } = SupportPotencyTier.Low;
        public int OwnedObSelection { get; set; } = 4;
    }

    public sealed class SupportFilterResult
    {
        public SupportTeamFilter Filter { get; set; } = new();
        public List<SupportWeaponMatch> MatchingWeapons { get; set; } = new();
        public List<SupportOutfitMatch> MatchingOutfits { get; set; } = new();
    }

    public sealed class SupportOutfitMatch
    {
        public WeaponSearchItem Outfit { get; set; } = null!;
        public string OutfitId { get; set; } = string.Empty;
        public string OutfitName { get; set; } = string.Empty;
        public string Character { get; set; } = string.Empty;
        public string Range { get; set; } = string.Empty;
        public string AbilityText { get; set; } = string.Empty;
        public SupportPotencyTier BasePotency { get; set; } = SupportPotencyTier.Low;
        public SupportPotencyTier MaxPotency { get; set; } = SupportPotencyTier.Low;
        public int OwnedSelection { get; set; } = 1;
    }

    public sealed class SupportEquippedWeapon
    {
        public WeaponSearchItem Weapon { get; set; } = null!;
        public int OwnedObSelection { get; set; }
        public List<SupportTeamFilter> MatchedFilters { get; set; } = new();
        public SupportPotencyTier BasePotency { get; set; } = SupportPotencyTier.Low;
        public SupportPotencyTier MaxPotency { get; set; } = SupportPotencyTier.Low;
    }

    public sealed class SupportCharacterAssignment
    {
        public string Name { get; set; } = string.Empty;
        public SupportEquippedWeapon? MainHand { get; set; }
        public SupportEquippedWeapon? OffHand { get; set; }
        public SupportEquippedOutfit? Outfit { get; set; }
    }

    public sealed class SupportEquippedOutfit
    {
        public WeaponSearchItem Outfit { get; set; } = null!;
        public int OwnedSelection { get; set; }
        public List<SupportTeamFilter> MatchedFilters { get; set; } = new();
        public SupportPotencyTier BasePotency { get; set; } = SupportPotencyTier.Low;
        public SupportPotencyTier MaxPotency { get; set; } = SupportPotencyTier.Low;
    }

    public sealed class SupportTeamResult
    {
        public Dictionary<string, SupportCharacterAssignment> Characters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public int MaxPotenciesScore { get; set; }
        public int BasePotenciesScore { get; set; }
        public int CharacterCountScore { get; set; }
        public int WeaponCountScore { get; set; }
        public int TeamScoreComposite => (MaxPotenciesScore * 1000) + (CharacterCountScore * 100) + (WeaponCountScore * 10) + BasePotenciesScore;

        public IEnumerable<SupportEquippedWeapon> GetEquippedWeapons()
        {
            foreach (var character in Characters.Values)
            {
                if (character.MainHand != null)
                {
                    yield return character.MainHand;
                }

                if (character.OffHand != null)
                {
                    yield return character.OffHand;
                }
            }
        }

        public IEnumerable<SupportEquippedOutfit> GetEquippedOutfits()
        {
            foreach (var character in Characters.Values)
            {
                if (character.Outfit != null)
                {
                    yield return character.Outfit;
                }
            }
        }

        public HashSet<string> GetWeaponNames() => GetEquippedWeapons().Select(w => w.Weapon.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> GetOutfitNames() => GetEquippedOutfits().Select(o => o.Outfit.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class SupportTeamBuilderResponse
    {
        public List<SupportFilterResult> FilterResults { get; set; } = new();
        public List<SupportTeamResult> Teams { get; set; } = new();
    }
}
