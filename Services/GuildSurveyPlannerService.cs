using System;
using System.Collections.Generic;
using System.Linq;
using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services;

public sealed class GuildSurveyPlannerService
{
    private static readonly string[] Elements = new[] { "Earth", "Fire", "Ice", "Lightning", "Water", "Wind" };
    private static readonly string[] AbilityTypes = new[] { "Physical", "Magical" };

    private readonly WeaponSearchDataService _weaponData;

    public GuildSurveyPlannerService(WeaponSearchDataService weaponData)
    {
        _weaponData = weaponData;
    }

    public GuildSurveyPlannerViewModel Build(string? element, string? abilityType)
    {
        var selectedElement = NormalizeElement(element);
        var selectedAbilityType = NormalizeAbilityType(abilityType);
        var allItems = _weaponData.GetWeapons()
            .Where(item => !string.IsNullOrWhiteSpace(item.Character))
            .ToList();
        var orderedCharacters = GetOrderedCharacters(allItems);

        var characterPlans = orderedCharacters
            .Select(character => BuildCharacterPlan(character, allItems, selectedElement, selectedAbilityType))
            .ToList();

        return new GuildSurveyPlannerViewModel
        {
            SelectedElement = selectedElement,
            SelectedAbilityType = selectedAbilityType,
            AvailableElements = Elements,
            AvailableAbilityTypes = AbilityTypes,
            CharacterPlans = characterPlans,
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static string NormalizeElement(string? element)
    {
        return Elements.FirstOrDefault(candidate => candidate.Equals(element?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? Elements[0];
    }

    private static string NormalizeAbilityType(string? abilityType)
    {
        return AbilityTypes.FirstOrDefault(candidate => candidate.Equals(abilityType?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? AbilityTypes[0];
    }

    private static List<string> GetOrderedCharacters(IReadOnlyList<WeaponSearchItem> allItems)
    {
        var names = CharacterRoleRegistry.Roles.Keys.ToList();

        foreach (var character in allItems
                     .Select(item => item.Character)
                     .Where(character => !string.IsNullOrWhiteSpace(character))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(character => character, StringComparer.OrdinalIgnoreCase))
        {
            if (!names.Contains(character, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(character);
            }
        }

        return names;
    }

    private static GuildSurveyCharacterPlan BuildCharacterPlan(
        string character,
        IReadOnlyList<WeaponSearchItem> allItems,
        string selectedElement,
        string selectedAbilityType)
    {
        var characterItems = allItems
            .Where(item => string.Equals(item.Character, character, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var outfitSuggestions = characterItems
            .Where(IsCostume)
            .Select(item => BuildOutfitSuggestion(item, selectedElement, selectedAbilityType))
            .Where(suggestion => suggestion != null)
            .Cast<GuildSurveyGearSuggestion>()
            .OrderByDescending(suggestion => suggestion.Score)
            .ThenBy(suggestion => suggestion.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var dpsSuggestions = characterItems
            .Where(item => !IsCostume(item))
            .Select(item => BuildDpsWeaponSuggestion(item, selectedElement, selectedAbilityType))
            .Where(suggestion => suggestion != null)
            .Cast<GuildSurveyGearSuggestion>()
            .OrderByDescending(suggestion => suggestion.Score)
            .ThenByDescending(suggestion => suggestion.DamagePercent)
            .ThenBy(suggestion => suggestion.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var dpsIds = new HashSet<string>(dpsSuggestions.Select(suggestion => suggestion.Id), StringComparer.OrdinalIgnoreCase);

        var utilitySuggestions = characterItems
            .Where(item => !IsCostume(item))
            .Select(item => BuildUtilityWeaponSuggestion(item, selectedElement, selectedAbilityType))
            .Where(suggestion => suggestion != null)
            .Cast<GuildSurveyGearSuggestion>()
            .Where(suggestion => !dpsIds.Contains(suggestion.Id))
            .OrderByDescending(suggestion => suggestion.Score)
            .ThenBy(suggestion => suggestion.Name, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        return new GuildSurveyCharacterPlan
        {
            Character = character,
            Role = CharacterRoleRegistry.GetRoleOrDefault(character),
            OutfitSuggestions = outfitSuggestions,
            DpsWeaponSuggestions = dpsSuggestions,
            UtilityWeaponSuggestions = utilitySuggestions
        };
    }

    private static GuildSurveyGearSuggestion? BuildOutfitSuggestion(WeaponSearchItem outfit, string selectedElement, string selectedAbilityType)
    {
        var passiveNames = outfit.MaxPassiveSkills
            .Select(passive => passive.SkillName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var searchTexts = GetSearchTexts(outfit);
        var reasons = new List<string>();
        var highlights = new List<string>();
        double score = 0;

        var hasElementArcanum = searchTexts.Any(text => ContainsText(text, selectedElement) && ContainsText(text, "Arcanum"));
        var hasElementMastery = searchTexts.Any(text => ContainsText(text, selectedElement) && ContainsText(text, "Mastery"));
        var hasElementPotency = searchTexts.Any(text => ContainsText(text, selectedElement) && (ContainsText(text, "Pot.") || ContainsText(text, "Damage Bonus") || ContainsText(text, "Damage Up") || ContainsText(text, "Weapon Boost")));
        var hasOmniElemental = searchTexts.Any(text => ContainsText(text, "Boost Elem. Pot. Arcanum") || ContainsText(text, "Elem. Pot. Arcanum") || ContainsText(text, "Elem. Pot."));
        var isPhysicalBattle = selectedAbilityType.Equals("Physical", StringComparison.OrdinalIgnoreCase);
        var hasRelevantAttackAll = isPhysicalBattle
            ? searchTexts.Any(text => ContainsText(text, "Boost ATK (All Allies)") || ContainsText(text, "Boost PATK (All Allies)") || (ContainsText(text, "PATK") && ContainsText(text, "All")))
            : searchTexts.Any(text => ContainsText(text, "Boost ATK (All Allies)") || ContainsText(text, "Boost MATK (All Allies)") || (ContainsText(text, "MATK") && ContainsText(text, "All")));
        var hasRelevantAbilityPotency = isPhysicalBattle
            ? searchTexts.Any(text => ContainsText(text, "Boost Ability Pot.") || ContainsText(text, "Boost Phys. Ability Pot."))
            : searchTexts.Any(text => ContainsText(text, "Boost Ability Pot.") || ContainsText(text, "Boost Mag. Ability Pot."));

        if (hasElementArcanum)
        {
            score += 360;
            reasons.Add($"{selectedElement} arcanum outfit");
            highlights.Add($"{selectedElement} Arcanum");
        }

        if (hasElementMastery)
        {
            score += 280;
            reasons.Add($"{selectedElement} mastery outfit");
            highlights.Add($"{selectedElement} Mastery");
        }

        if (hasElementPotency)
        {
            score += 220;
            reasons.Add($"{selectedElement} elemental potency support");
            highlights.Add($"{selectedElement} Potency");
        }

        if (hasOmniElemental)
        {
            score += hasElementArcanum || hasElementMastery || hasElementPotency ? 90 : 300;
            reasons.Add("omni elemental outfit");
            highlights.Add("Omni Elemental");
        }

        if (hasRelevantAttackAll)
        {
            score += 120;
            reasons.Add(isPhysicalBattle
                ? "team PATK or ATK support outfit"
                : "team MATK or ATK support outfit");
            highlights.Add(isPhysicalBattle ? "ATK / PATK All" : "ATK / MATK All");
        }

        if (hasRelevantAbilityPotency)
        {
            score += 110;
            reasons.Add(isPhysicalBattle
                ? "physical ability potency outfit"
                : "magical ability potency outfit");
            highlights.Add(isPhysicalBattle ? "Phys. Ability Pot." : "Mag. Ability Pot.");
        }

        if (score <= 0)
        {
            return null;
        }

        var detailLines = passiveNames
            .Where(name => highlights.Any(highlight => ContainsText(name, highlight))
                || ContainsText(name, selectedElement)
                || ContainsText(name, "Arcanum")
                || ContainsText(name, "Mastery")
                || ContainsText(name, "Elem. Pot.")
                || ContainsText(name, "Ability Pot.")
                || ContainsText(name, "Boost ATK")
                || ContainsText(name, "Boost PATK")
                || ContainsText(name, "Boost MATK"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        if (detailLines.Count == 0)
        {
            detailLines = passiveNames.Take(3).ToList();
        }

        return new GuildSurveyGearSuggestion
        {
            Id = outfit.Id,
            Name = outfit.Name,
            Character = outfit.Character,
            ImageUrl = outfit.ImageUrl,
            PreviewImageUrl = outfit.PreviewImageUrl,
            Element = outfit.Element,
            AbilityType = outfit.AbilityType,
            EquipmentType = outfit.EquipmentType,
            DamagePercent = outfit.DamagePercent,
            Summary = string.Join(" • ", reasons.Distinct(StringComparer.OrdinalIgnoreCase)),
            DetailText = string.Join(" • ", detailLines),
            Highlights = highlights.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            IsSelfOnlyEnhancement = false,
            Score = score
        };
    }

    private static GuildSurveyGearSuggestion? BuildDpsWeaponSuggestion(WeaponSearchItem weapon, string selectedElement, string selectedAbilityType)
    {
        var matchesElement = string.Equals(weapon.Element, selectedElement, StringComparison.OrdinalIgnoreCase);
        var matchesAbilityType = MatchesBattleAbilityType(weapon.AbilityType, selectedAbilityType);

        if (!matchesElement || !matchesAbilityType || weapon.DamagePercent <= 0)
        {
            return null;
        }

        var highlights = new List<string> { selectedElement, selectedAbilityType };
        var detailParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(weapon.AbilityText))
        {
            detailParts.Add(weapon.AbilityText);
        }

        foreach (var passive in weapon.MaxPassiveSkills.Select(passive => passive.SkillName).Where(name => !string.IsNullOrWhiteSpace(name)).Take(2))
        {
            detailParts.Add(passive);
        }

        return new GuildSurveyGearSuggestion
        {
            Id = weapon.Id,
            Name = weapon.Name,
            Character = weapon.Character,
            ImageUrl = weapon.ImageUrl,
            PreviewImageUrl = weapon.PreviewImageUrl,
            Element = weapon.Element,
            AbilityType = weapon.AbilityType,
            EquipmentType = weapon.EquipmentType,
            DamagePercent = weapon.DamagePercent,
            Summary = $"Battle-fit {selectedElement} {selectedAbilityType.ToLowerInvariant()} DPS option",
            DetailText = string.Join(" • ", detailParts.Where(part => !string.IsNullOrWhiteSpace(part)).Distinct(StringComparer.OrdinalIgnoreCase).Take(2)),
            Highlights = highlights,
            IsSelfOnlyEnhancement = false,
            Score = 500 + weapon.DamagePercent
        };
    }

    private static GuildSurveyGearSuggestion? BuildUtilityWeaponSuggestion(WeaponSearchItem weapon, string selectedElement, string selectedAbilityType)
    {
        var isPhysicalBattle = selectedAbilityType.Equals("Physical", StringComparison.OrdinalIgnoreCase);
        var battleBuffTag = isPhysicalBattle ? "PATK Up" : "MATK Up";
        var battleBreakTag = isPhysicalBattle ? "PDEF Down" : "MDEF Down";
        var battleDamageBonusTag = isPhysicalBattle ? "Phys. Damage Bonus" : "Mag. Damage Bonus";
        var battleWeaponBoostTag = isPhysicalBattle ? "Phys. Weapon Boost" : "Mag. Weapon Boost";
        var battleDamageReceivedUpTags = isPhysicalBattle
            ? new[] { "Phys. Dmg. Rcvd. Up", "All-Tgt. Phys. Dmg. Rcvd. Up", "Status Ailment: All-Tgt. Phys. Dmg. Rcvd. Up", "Single-Tgt. Phys. Dmg. Rcvd. Up", "Status Ailment: Single-Tgt. Phys. Dmg. Rcvd. Up" }
            : new[] { "Mag. Dmg. Rcvd. Up", "All-Tgt. Mag. Dmg. Rcvd. Up", "Status Ailment: All-Tgt. Mag. Dmg. Rcvd. Up", "Single-Tgt. Mag. Dmg. Rcvd. Up", "Status Ailment: Single-Tgt. Mag. Dmg. Rcvd. Up" };
        var battleAtbConservationTag = isPhysicalBattle ? "Phys. ATB Conservation Effect" : "Mag. ATB Conservation Effect";
        var hasSelfOnlySupportEffect = IsSelfOnlySupportEffect(weapon);
        var searchTexts = GetSearchTexts(weapon);
        var reasons = new List<string>();
        var highlights = new List<string>();
        double score = 0;

        if (HasAnyText(searchTexts, $"{selectedElement} Resistance Down"))
        {
            score += 260;
            reasons.Add($"{selectedElement} resistance down");
            highlights.Add($"{selectedElement} Resist Down");
        }

        if (HasAnyText(searchTexts, $"{selectedElement} Damage Bonus"))
        {
            score += hasSelfOnlySupportEffect ? 175 : 215;
            reasons.Add(hasSelfOnlySupportEffect ? $"self-only {selectedElement} damage bonus" : $"{selectedElement} damage bonus support");
            highlights.Add($"{selectedElement} Damage Bonus");
        }

        if (HasAnyText(searchTexts, $"{selectedElement} Damage Up"))
        {
            score += hasSelfOnlySupportEffect ? 170 : 210;
            reasons.Add(hasSelfOnlySupportEffect ? $"self-only {selectedElement} damage up" : $"{selectedElement} damage up support");
            highlights.Add($"{selectedElement} Damage Up");
        }

        if (HasAnyText(searchTexts, $"{selectedElement} Weapon Boost"))
        {
            score += hasSelfOnlySupportEffect ? 180 : 220;
            reasons.Add(hasSelfOnlySupportEffect ? $"self-only {selectedElement} weapon boost" : $"{selectedElement} weapon boost support");
            highlights.Add($"{selectedElement} Weapon Boost");
        }

        if (HasAnyText(searchTexts, $"Amp. {selectedElement} Abilities"))
        {
            score += 160;
            reasons.Add($"{selectedElement} passive ability amp");
            highlights.Add($"Amp. {selectedElement}");
        }

        if (HasEffectTag(weapon, battleBuffTag))
        {
            score += hasSelfOnlySupportEffect ? 120 : 165;
            reasons.Add(hasSelfOnlySupportEffect ? $"self-only {battleBuffTag.ToLowerInvariant()} enhancer" : $"{battleBuffTag} support");
            highlights.Add(battleBuffTag);
        }

        if (HasEffectTag(weapon, battleBreakTag))
        {
            score += 170;
            reasons.Add($"{battleBreakTag} setup");
            highlights.Add(battleBreakTag);
        }

        if (HasAnyText(searchTexts, battleDamageBonusTag))
        {
            score += 155;
            reasons.Add($"{selectedAbilityType.ToLowerInvariant()} damage bonus support");
            highlights.Add(battleDamageBonusTag);
        }

        if (HasAnyText(searchTexts, battleWeaponBoostTag))
        {
            score += hasSelfOnlySupportEffect ? 135 : 175;
            reasons.Add(hasSelfOnlySupportEffect ? $"self-only {selectedAbilityType.ToLowerInvariant()} weapon boost" : $"{selectedAbilityType.ToLowerInvariant()} weapon boost support");
            highlights.Add(battleWeaponBoostTag);
        }

        if (HasAnyText(searchTexts, battleDamageReceivedUpTags))
        {
            score += 190;
            reasons.Add($"{selectedAbilityType.ToLowerInvariant()} damage amplification");
            highlights.Add(isPhysicalBattle ? "Phys. Damage Taken Up" : "Mag. Damage Taken Up");
        }

        if (HasEffectTag(weapon, "Exploit Weakness"))
        {
            score += hasSelfOnlySupportEffect ? 140 : 185;
            reasons.Add(hasSelfOnlySupportEffect ? "self-only exploit weakness setup" : "Exploit Weakness support");
            highlights.Add("Exploit Weakness");
        }

        if (HasAnyText(searchTexts, "Enfeeble", "Status Ailment: Enfeeble"))
        {
            score += 170;
            reasons.Add("Enfeeble utility");
            highlights.Add("Enfeeble");
        }

        if (HasEffectTag(weapon, "Haste"))
        {
            score += hasSelfOnlySupportEffect ? 110 : 160;
            reasons.Add(hasSelfOnlySupportEffect ? "self-only haste enhancer" : "Haste support");
            highlights.Add("Haste");
        }

        if (HasEffectTag(weapon, "Applied Stats Debuff Tier Increased"))
        {
            score += 150;
            reasons.Add("debuff amplification");
            highlights.Add("Debuff Amp");
        }

        if (HasEffectTag(weapon, "Applied Stats Buff Tier Increased"))
        {
            score += hasSelfOnlySupportEffect ? 105 : 145;
            reasons.Add(hasSelfOnlySupportEffect ? "self buff amplification" : "buff amplification");
            highlights.Add("Buff Amp");
        }

        if (HasEffectTag(weapon, "Enliven"))
        {
            score += hasSelfOnlySupportEffect ? 105 : 140;
            reasons.Add(hasSelfOnlySupportEffect ? "self-only enliven enhancer" : "Enliven support");
            highlights.Add("Enliven");
        }

        if (HasAnyText(searchTexts, battleAtbConservationTag))
        {
            score += 145;
            reasons.Add($"{selectedAbilityType.ToLowerInvariant()} ATB conservation");
            highlights.Add(isPhysicalBattle ? "Phys. ATB Save" : "Mag. ATB Save");
        }

        if (HasAnyText(searchTexts, "command gauge"))
        {
            score += 120;
            reasons.Add("command gauge help");
            highlights.Add("Command Gauge");
        }

        if (score <= 0)
        {
            return null;
        }

        var detailText = string.IsNullOrWhiteSpace(weapon.AbilityText)
            ? string.Join(" • ", weapon.MaxPassiveSkills.Select(passive => passive.SkillName).Where(name => !string.IsNullOrWhiteSpace(name)).Take(2))
            : weapon.AbilityText;

        return new GuildSurveyGearSuggestion
        {
            Id = weapon.Id,
            Name = weapon.Name,
            Character = weapon.Character,
            ImageUrl = weapon.ImageUrl,
            PreviewImageUrl = weapon.PreviewImageUrl,
            Element = weapon.Element,
            AbilityType = weapon.AbilityType,
            EquipmentType = weapon.EquipmentType,
            DamagePercent = weapon.DamagePercent,
            Summary = string.Join(" • ", reasons.Distinct(StringComparer.OrdinalIgnoreCase).Take(3)),
            DetailText = detailText,
            Highlights = highlights.Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList(),
            IsSelfOnlyEnhancement = hasSelfOnlySupportEffect,
            Score = score + (MatchesBattleAbilityType(weapon.AbilityType, selectedAbilityType) ? 20 : 0)
        };
    }

    private static List<string> GetSearchTexts(WeaponSearchItem item)
    {
        return new[]
            {
                item.AbilityText,
                item.MaxAbilityDescription,
                item.Range,
                item.Element,
                item.AbilityType,
                string.Join(" | ", item.EffectTags),
                string.Join(" | ", item.MaxPassiveSkills.Select(passive => passive.SkillName)),
                string.Join(" | ", item.MaxPassiveSkills.SelectMany(passive => passive.Effects.Select(effect => effect.Label))),
                string.Join(" | ", item.MaxPassiveSkills.SelectMany(passive => passive.Effects.Select(effect => effect.Description))),
                string.Join(" | ", item.Customizations.Select(customization => customization.Description)),
                string.Join(" | ", item.Customizations.Select(customization => customization.PassiveSkillName)),
                string.Join(" | ", item.Customizations.SelectMany(customization => customization.PassiveEffects.Select(effect => effect.Label))),
                string.Join(" | ", item.Customizations.SelectMany(customization => customization.PassiveEffects.Select(effect => effect.Description)))
            }
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool HasEffectTag(WeaponSearchItem item, string tag)
    {
        return item.EffectTags.Any(effectTag => effectTag.Equals(tag, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasAnyText(IEnumerable<string> searchTexts, params string[] values)
    {
        return values.Any(value => searchTexts.Any(text => ContainsText(text, value)));
    }

    private static bool MatchesBattleAbilityType(string weaponAbilityType, string selectedAbilityType)
    {
        if (selectedAbilityType.Equals("Physical", StringComparison.OrdinalIgnoreCase))
        {
            return weaponAbilityType.Equals("Phys.", StringComparison.OrdinalIgnoreCase)
                || weaponAbilityType.Equals("Physical", StringComparison.OrdinalIgnoreCase);
        }

        return weaponAbilityType.Equals("Mag.", StringComparison.OrdinalIgnoreCase)
            || weaponAbilityType.Equals("Magical", StringComparison.OrdinalIgnoreCase)
            || weaponAbilityType.Equals("Magic", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSelfOnlySupportEffect(WeaponSearchItem weapon)
    {
        return string.Equals(weapon.Range?.Trim(), "Self", StringComparison.OrdinalIgnoreCase)
            || ContainsText(weapon.Range, "Self")
            || ContainsText(weapon.AbilityText, "[Rng.: Self]")
            || ContainsText(weapon.MaxAbilityDescription, "[Rng.: Self]")
            || ContainsText(weapon.AbilityText, "[Range: Self]")
            || ContainsText(weapon.MaxAbilityDescription, "[Range: Self]")
            || ContainsText(weapon.AbilityText, "Range: Self");
    }

    private static bool ContainsText(string? source, string? value)
    {
        return !string.IsNullOrWhiteSpace(source)
            && !string.IsNullOrWhiteSpace(value)
            && source.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCostume(WeaponSearchItem item)
    {
        return item.EquipmentType.Equals("Costume", StringComparison.OrdinalIgnoreCase);
    }
}
