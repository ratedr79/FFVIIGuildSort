using System;
using System.Collections.Generic;

namespace FFVIIEverCrisisAnalyzer.Models;

public sealed class GuildSurveyPlannerViewModel
{
    public required string SelectedElement { get; init; }
    public required string SelectedAbilityType { get; init; }
    public required IReadOnlyList<string> AvailableElements { get; init; }
    public required IReadOnlyList<string> AvailableAbilityTypes { get; init; }
    public required IReadOnlyList<GuildSurveyCharacterPlan> CharacterPlans { get; init; }
    public required DateTimeOffset GeneratedAtUtc { get; init; }
}

public sealed class GuildSurveyCharacterPlan
{
    public required string Character { get; init; }
    public required CharacterRole Role { get; init; }
    public required IReadOnlyList<GuildSurveyGearSuggestion> OutfitSuggestions { get; init; }
    public required IReadOnlyList<GuildSurveyGearSuggestion> DpsWeaponSuggestions { get; init; }
    public required IReadOnlyList<GuildSurveyGearSuggestion> UtilityWeaponSuggestions { get; init; }

    public bool HasAnySuggestions => OutfitSuggestions.Count > 0 || DpsWeaponSuggestions.Count > 0 || UtilityWeaponSuggestions.Count > 0;
}

public sealed class GuildSurveyGearSuggestion
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Character { get; init; }
    public required string ImageUrl { get; init; }
    public required string PreviewImageUrl { get; init; }
    public required string Element { get; init; }
    public required string AbilityType { get; init; }
    public required string EquipmentType { get; init; }
    public required double DamagePercent { get; init; }
    public required string Summary { get; init; }
    public required string DetailText { get; init; }
    public required IReadOnlyList<string> Highlights { get; init; }
    public required bool IsSelfOnlyEnhancement { get; init; }
    public required double Score { get; init; }
}
