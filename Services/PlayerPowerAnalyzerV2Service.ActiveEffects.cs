using System.Globalization;
using System.Text.RegularExpressions;
using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed partial class PlayerPowerAnalyzerV2Service
    {
        private enum ActiveEffectTargetScope
        {
            Unknown,
            Self,
            SingleAlly,
            OtherAllies,
            AllAllies,
            SingleEnemy,
            AllEnemies
        }

        private sealed class DetectedActiveEffect
        {
            public string Key { get; set; } = string.Empty;
            public string FamilyKey { get; set; } = string.Empty;
            public string AxisKey { get; set; } = string.Empty;
            public string SourceName { get; set; } = string.Empty;
            public string SourceType { get; set; } = string.Empty;
            public string SourceText { get; set; } = string.Empty;
            public string SourceAbilityType { get; set; } = string.Empty;
            public string SourceElement { get; set; } = string.Empty;
            public double? PotencyPercent { get; set; }
            public double? DurationSeconds { get; set; }
            public double? ExtensionSeconds { get; set; }
            public ActiveEffectTargetScope TargetScope { get; set; }
            public bool IsAssumedMateria { get; set; }
        }

        private sealed class EffectFamilyLedgerEntry
        {
            public string FamilyKey { get; set; } = string.Empty;
            public List<DetectedActiveEffect> ExplicitEffects { get; set; } = new();
            public List<DetectedActiveEffect> AssumedEffects { get; set; } = new();
            public DetectedActiveEffect? PrimaryEffect { get; set; }
            public double BestPotencyFactor { get; set; } = 1.0;
            public double BestSustainFactor { get; set; } = 1.0;
            public double BestScopeMultiplier { get; set; } = 1.0;
            public bool HasExplicitCoverage => ExplicitEffects.Count > 0;
            public bool HasAssumedCoverage => AssumedEffects.Count > 0;
        }

        private sealed class EffectPackageScoreResult
        {
            public double Score { get; set; }
            public List<string> Notes { get; set; } = new();
        }

        private static IReadOnlyList<DetectedActiveEffect> DetectActiveEffects(
            IEnumerable<string> effectTags,
            string abilityText,
            PlayerPowerAnalyzerV2Request request,
            IEnumerable<string> bossImmunityKeys,
            string sourceType,
            string sourceName,
            string? sourceAbilityType,
            string? sourceElement,
            bool isAssumedMateria = false)
        {
            var blob = string.Join(" | ", effectTags.Where(t => !string.IsNullOrWhiteSpace(t)).Append(abilityText ?? string.Empty));
            if (string.IsNullOrWhiteSpace(blob))
            {
                return Array.Empty<DetectedActiveEffect>();
            }

            var effects = new List<DetectedActiveEffect>();
            if (request.EnemyWeakness != Element.None)
            {
                var element = request.EnemyWeakness.ToString();
                TryAddDetectedEffect(effects, blob, "elemental_resistance_down", $"{element} Resistance Down", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
                TryAddDetectedEffect(effects, blob, "elemental_resistance_down", $"Status Ailment: {element} Weakness", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
                TryAddDetectedEffect(effects, blob, "elemental_damage_up", $"{element} Damage Up", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
                TryAddDetectedEffect(effects, blob, "elemental_damage_received_up", $"{element} Damage Received Up", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
                TryAddDetectedEffect(effects, blob, "elemental_damage_bonus", $"{element} Damage Bonus", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
                TryAddDetectedEffect(effects, blob, "elemental_weapon_boost", $"{element} Weapon Boost", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
            }

            TryAddDetectedEffect(effects, blob, "phys_damage_bonus", "Phys. Damage Bonus", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
            TryAddDetectedEffect(effects, blob, "mag_damage_bonus", "Mag. Damage Bonus", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
            TryAddDetectedEffect(effects, blob, "phys_weapon_boost", "Phys. Weapon Boost", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
            TryAddDetectedEffect(effects, blob, "mag_weapon_boost", "Mag. Weapon Boost", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
            TryAddDetectedEffect(effects, blob, "patk_up", "PATK Up", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
            TryAddDetectedEffect(effects, blob, "matk_up", "MATK Up", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
            TryAddDetectedEffect(effects, blob, "pdef_up", "PDEF Up", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
            TryAddDetectedEffect(effects, blob, "mdef_up", "MDEF Up", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
            TryAddDetectedEffect(effects, blob, "patk_down", "PATK Down", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
            TryAddDetectedEffect(effects, blob, "matk_down", "MATK Down", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
            TryAddDetectedEffect(effects, blob, "pdef_down", "PDEF Down", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
            TryAddDetectedEffect(effects, blob, "mdef_down", "MDEF Down", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
            TryAddDetectedEffect(effects, blob, "stat_debuff_tier_increase", "Applied Stats Debuff Tier Increased", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
            TryAddDetectedEffect(effects, blob, "stat_debuff_tier_increase", "Stats Debuff Tier Increased", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
            TryAddDetectedEffect(effects, blob, "stat_buff_tier_increase", "Applied Stats Buff Tier Increased", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
            TryAddDetectedEffect(effects, blob, "stat_buff_tier_increase", "Stats Buff Tier Increased", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
            TryAddDetectedEffect(effects, blob, "haste", "Haste", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
            TryAddDetectedEffect(effects, blob, "exploit_weakness", "Exploit Weakness", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
            TryAddDetectedEffect(effects, blob, "enfeeble", "Enfeeble", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
            TryAddDetectedEffect(effects, blob, "enliven", "Enliven", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
            TryAddDetectedEffect(effects, blob, "torpor", "Torpor", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
            TryAddDetectedEffect(effects, blob, "healing_support", "Heal", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);
            TryAddDetectedEffect(effects, blob, "healing_support", "HP Recovery", sourceType, sourceName, sourceAbilityType, sourceElement, isAssumedMateria);

            return effects
                .Where(effect => !IsSuppressedByImmunity(effect.Key, bossImmunityKeys))
                .GroupBy(
                    effect => string.Join(
                        "|",
                        effect.Key,
                        effect.SourceName,
                        effect.SourceType,
                        effect.TargetScope,
                        effect.PotencyPercent?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        effect.DurationSeconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        effect.ExtensionSeconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private static void TryAddDetectedEffect(
            List<DetectedActiveEffect> effects,
            string blob,
            string key,
            string label,
            string sourceType,
            string sourceName,
            string? sourceAbilityType,
            string? sourceElement,
            bool isAssumedMateria)
        {
            if (!blob.Contains(label, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var snippet = ExtractEffectSnippet(blob, label);
            effects.Add(new DetectedActiveEffect
            {
                Key = key,
                FamilyKey = GetActiveEffectFamilyKey(key),
                AxisKey = GetActiveEffectAxisKey(key),
                SourceName = sourceName,
                SourceType = sourceType,
                SourceText = string.IsNullOrWhiteSpace(snippet) ? blob : snippet,
                SourceAbilityType = sourceAbilityType ?? string.Empty,
                SourceElement = sourceElement ?? string.Empty,
                PotencyPercent = TryParseMarker(PotencyMarkerRegex, snippet) ?? TryParseMarker(PotencyMarkerRegex, blob),
                DurationSeconds = TryParseMarker(DurationMarkerRegex, snippet) ?? TryParseMarker(DurationMarkerRegex, blob),
                ExtensionSeconds = TryParseMarker(ExtensionMarkerRegex, snippet) ?? TryParseMarker(ExtensionMarkerRegex, blob),
                TargetScope = ParseTargetScope(snippet, blob),
                IsAssumedMateria = isAssumedMateria
            });
        }

        private static string ExtractEffectSnippet(string blob, string label)
        {
            var index = blob.IndexOf(label, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return blob;
            }

            var start = 0;
            foreach (var separator in new[] { '|', '\n' })
            {
                var separatorIndex = blob.LastIndexOf(separator, index);
                if (separatorIndex >= 0)
                {
                    start = Math.Max(start, separatorIndex + 1);
                }
            }

            var end = blob.Length;
            foreach (var separator in new[] { '|', '\n', '.' })
            {
                var separatorIndex = blob.IndexOf(separator, index);
                if (separatorIndex >= 0)
                {
                    end = Math.Min(end, separatorIndex);
                }
            }

            if (end <= start)
            {
                return blob[index..].Trim();
            }

            return blob[start..end].Trim();
        }

        private static double? TryParseMarker(Regex regex, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var match = regex.Match(text);
            if (!match.Success)
            {
                return null;
            }

            return double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }

        private static ActiveEffectTargetScope ParseTargetScope(string snippet, string fallback)
        {
            var combined = string.IsNullOrWhiteSpace(snippet) ? fallback : $"{snippet} | {fallback}";
            var rangeValue = RangeMarkerRegex.Match(combined).Groups["value"].Value;
            if (rangeValue.Contains("All Allies", StringComparison.OrdinalIgnoreCase))
            {
                return ActiveEffectTargetScope.AllAllies;
            }

            if (rangeValue.Contains("Other Allies", StringComparison.OrdinalIgnoreCase))
            {
                return ActiveEffectTargetScope.OtherAllies;
            }

            if (rangeValue.Contains("Single Ally", StringComparison.OrdinalIgnoreCase))
            {
                return ActiveEffectTargetScope.SingleAlly;
            }

            if (rangeValue.Contains("All Enemies", StringComparison.OrdinalIgnoreCase))
            {
                return ActiveEffectTargetScope.AllEnemies;
            }

            if (rangeValue.Contains("Single Enemy", StringComparison.OrdinalIgnoreCase))
            {
                return ActiveEffectTargetScope.SingleEnemy;
            }

            if (rangeValue.Contains("Self", StringComparison.OrdinalIgnoreCase))
            {
                return ActiveEffectTargetScope.Self;
            }

            if (ContainsRangeMarker(combined, "All Allies"))
            {
                return ActiveEffectTargetScope.AllAllies;
            }

            if (ContainsRangeMarker(combined, "Other Allies"))
            {
                return ActiveEffectTargetScope.OtherAllies;
            }

            if (ContainsRangeMarker(combined, "Single Ally"))
            {
                return ActiveEffectTargetScope.SingleAlly;
            }

            if (ContainsRangeMarker(combined, "Self"))
            {
                return ActiveEffectTargetScope.Self;
            }

            if (combined.Contains("All Enemies", StringComparison.OrdinalIgnoreCase))
            {
                return ActiveEffectTargetScope.AllEnemies;
            }

            if (combined.Contains("Single Enemy", StringComparison.OrdinalIgnoreCase))
            {
                return ActiveEffectTargetScope.SingleEnemy;
            }

            return ActiveEffectTargetScope.Unknown;
        }

        private static string GetActiveEffectFamilyKey(string key)
        {
            return key switch
            {
                "phys_damage_bonus" or "mag_damage_bonus" or "elemental_damage_bonus" => "damage_bonus",
                "phys_weapon_boost" or "mag_weapon_boost" or "elemental_weapon_boost" => "weapon_boost",
                "patk_up" or "matk_up" => "attack_buff",
                "pdef_down" or "mdef_down" => "defense_debuff",
                "pdef_up" or "mdef_up" => "defense_buff",
                "patk_down" or "matk_down" => "attack_debuff",
                "elemental_resistance_down" => "elemental_resistance_debuff",
                "elemental_damage_up" => "damage_up",
                "elemental_damage_received_up" => "damage_received_up",
                "stat_buff_tier_increase" => "buff_amplifier",
                "stat_debuff_tier_increase" => "debuff_amplifier",
                _ => key
            };
        }

        private static string GetActiveEffectAxisKey(string key)
        {
            return key switch
            {
                "patk_up" or "pdef_down" or "patk_down" or "phys_damage_bonus" or "phys_weapon_boost" => "physical",
                "matk_up" or "mdef_down" or "matk_down" or "mag_damage_bonus" or "mag_weapon_boost" => "magical",
                "elemental_resistance_down" or "elemental_damage_up" or "elemental_damage_received_up" or "elemental_damage_bonus" or "elemental_weapon_boost" => "elemental",
                _ => "neutral"
            };
        }

        private double ScoreActiveEffectWithReferenceTuning(DetectedActiveEffect effect, CharacterRole role, PlayerPowerAnalyzerV2Request request, ReferenceTuningProfile referenceTuningProfile)
        {
            var score = ScoreEffectKeyWithReferenceTuning(effect.Key, role, request, false, referenceTuningProfile);
            score *= GetStructuredTargetingMultiplier(effect, role, request);
            score *= GetPotencyFactor(effect);
            score *= GetSustainFactor(effect);
            if (effect.IsAssumedMateria && MateriaAssumableFamilies.Contains(effect.FamilyKey))
            {
                score *= 0.7;
            }

            return score;
        }

        private static double GetPotencyFactor(DetectedActiveEffect effect)
        {
            if (!effect.PotencyPercent.HasValue)
            {
                return 1.0;
            }

            var potency = Math.Max(0d, effect.PotencyPercent.Value);
            return 0.8 + Math.Min(0.4, potency / 100d * 0.5);
        }

        private static double GetSustainFactor(DetectedActiveEffect effect)
        {
            var durationValue = effect.DurationSeconds.HasValue
                ? Math.Max(0d, (effect.DurationSeconds.Value - 15d) / 120d)
                : 0d;
            var extensionValue = effect.ExtensionSeconds.HasValue
                ? Math.Max(0d, effect.ExtensionSeconds.Value / 100d)
                : 0d;
            return 1.0 + Math.Min(0.22, durationValue + extensionValue);
        }

        private static IReadOnlyList<DetectedActiveEffect> GetDetectedEffectsForVariant(CharacterBuildCandidate variant, PlayerPowerAnalyzerV2Request request)
        {
            var effects = new List<DetectedActiveEffect>();
            AddDetectedEffectsFromSlot(effects, variant.MainWeapon, request);
            if (variant.OffHandWeapon != null)
            {
                AddDetectedEffectsFromSlot(effects, variant.OffHandWeapon, request);
            }

            if (variant.UltimateWeapon != null)
            {
                AddDetectedEffectsFromSlot(effects, variant.UltimateWeapon, request);
            }

            if (variant.MainOutfit != null)
            {
                AddDetectedEffectsFromSlot(effects, variant.MainOutfit, request);
            }

            return effects;
        }

        private static void AddDetectedEffectsFromSlot(List<DetectedActiveEffect> effects, PlayerPowerAnalyzerV2ItemSlot slot, PlayerPowerAnalyzerV2Request request)
        {
            effects.AddRange(DetectActiveEffects(
                Array.Empty<string>(),
                slot.AbilityText,
                request,
                request.BossImmunityKeys,
                slot.SlotName,
                slot.Name,
                slot.AbilityType,
                slot.Element));

            if (!string.IsNullOrWhiteSpace(slot.SelectedCustomization))
            {
                effects.AddRange(DetectActiveEffects(
                    Array.Empty<string>(),
                    slot.SelectedCustomization,
                    request,
                    request.BossImmunityKeys,
                    "Customization",
                    slot.Name,
                    slot.AbilityType,
                    slot.Element));
            }
        }

        private static EffectPackageScoreResult ScoreEffectPackage(
            IReadOnlyList<DetectedActiveEffect> explicitEffects,
            IReadOnlyList<CharacterBuildCandidate> baseVariants,
            PlayerPowerAnalyzerV2Request request)
        {
            var allEffects = explicitEffects.ToList();
            allEffects.AddRange(BuildAssumedMateriaEffects(baseVariants, request));
            if (allEffects.Count == 0)
            {
                return new EffectPackageScoreResult();
            }

            var ledger = BuildEffectFamilyLedger(allEffects, request);
            var result = new EffectPackageScoreResult();
            foreach (var entry in ledger.Values)
            {
                if (entry.PrimaryEffect == null)
                {
                    continue;
                }

                var familyScore = ScoreEffectKey(entry.PrimaryEffect.Key, CharacterRole.Support, request, false)
                    * entry.BestPotencyFactor
                    * entry.BestSustainFactor
                    * entry.BestScopeMultiplier;

                if (ShouldDiscountForAssumedMateria(entry, request))
                {
                    familyScore *= 0.9;
                }
                else if (!entry.HasExplicitCoverage)
                {
                    familyScore *= 0.38;
                }

                if (entry.ExplicitEffects.Count > 1)
                {
                    var breadthSupport = entry.ExplicitEffects
                        .Select(effect => GetActiveEffectTargetingMultiplier(effect.Key, CharacterRole.Support, request, effect.SourceText, effect.SourceAbilityType, effect.SourceElement))
                        .OrderByDescending(value => value)
                        .Skip(1)
                        .FirstOrDefault();
                    familyScore += Math.Min(28d, Math.Max(0d, (breadthSupport - 1.0) * 40d));
                }

                result.Score += familyScore;
            }

            var explicitOffensiveFamilies = ledger.Values
                .Where(entry => entry.HasExplicitCoverage && StageOneOffensiveFamilies.Contains(entry.FamilyKey))
                .Select(entry => entry.FamilyKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (explicitOffensiveFamilies.Count > 1)
            {
                result.Score += (explicitOffensiveFamilies.Count - 1) * 26;
                result.Notes.Add($"Cross-family offensive coverage: {string.Join(", ", explicitOffensiveFamilies.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))}");
            }

            var familySet = new HashSet<string>(explicitOffensiveFamilies, StringComparer.OrdinalIgnoreCase);
            if (familySet.Contains("weapon_boost") && familySet.Contains("damage_bonus"))
            {
                result.Score += 34;
            }

            if (familySet.Contains("attack_buff") && (familySet.Contains("defense_debuff") || familySet.Contains("elemental_resistance_debuff")))
            {
                result.Score += 28;
            }

            if (familySet.Contains("damage_up") && familySet.Contains("damage_received_up"))
            {
                result.Score += 24;
            }

            if (ledger.TryGetValue("buff_amplifier", out var buffAmplifier)
                && buffAmplifier.HasExplicitCoverage
                && ledger.TryGetValue("attack_buff", out var attackBuff)
                && (attackBuff.HasExplicitCoverage || attackBuff.HasAssumedCoverage))
            {
                result.Score += request.PreferredDamageType == DamageType.Magical ? 72 : 84;
            }

            if (ledger.TryGetValue("debuff_amplifier", out var debuffAmplifier)
                && debuffAmplifier.HasExplicitCoverage
                && ((ledger.TryGetValue("defense_debuff", out var defenseDebuff) && (defenseDebuff.HasExplicitCoverage || defenseDebuff.HasAssumedCoverage))
                    || (ledger.TryGetValue("elemental_resistance_debuff", out var elementalDebuff) && (elementalDebuff.HasExplicitCoverage || elementalDebuff.HasAssumedCoverage))))
            {
                result.Score += request.PreferredDamageType == DamageType.Magical ? 76 : 88;
            }

            if (ledger.TryGetValue("defense_debuff", out var explicitDefenseDebuff)
                && explicitDefenseDebuff.HasExplicitCoverage)
            {
                result.Score += 16;
            }

            return result;
        }

        private static string? BuildOffensiveAbilitySummary(
            IReadOnlyList<DetectedActiveEffect> explicitEffects,
            PlayerPowerAnalyzerV2Request request)
        {
            var relevantEffects = explicitEffects
                .Where(effect => !effect.IsAssumedMateria)
                .Where(effect => effect.FamilyKey is "attack_buff" or "damage_bonus" or "weapon_boost" or "damage_up" or "damage_received_up" or "defense_debuff" or "elemental_resistance_debuff" or "buff_amplifier" or "debuff_amplifier" or "exploit_weakness" or "enfeeble" or "enliven" or "torpor")
                .ToList();
            if (relevantEffects.Count == 0)
            {
                return null;
            }

            var ledger = BuildEffectFamilyLedger(relevantEffects, request);
            var packageParts = new List<string>();
            var supportParts = new List<string>();

            TryAddOffensiveSummaryPart(packageParts, ledger, "attack_buff", request);
            TryAddOffensiveSummaryPart(packageParts, ledger, "damage_bonus", request);
            TryAddOffensiveSummaryPart(packageParts, ledger, "weapon_boost", request);
            TryAddOffensiveSummaryPart(packageParts, ledger, "damage_up", request);
            TryAddOffensiveSummaryPart(packageParts, ledger, "damage_received_up", request);
            TryAddOffensiveSummaryPart(packageParts, ledger, "defense_debuff", request);
            TryAddOffensiveSummaryPart(packageParts, ledger, "elemental_resistance_debuff", request);

            TryAddOffensiveSummaryPart(supportParts, ledger, "exploit_weakness", request);
            TryAddConditionalOffensiveSummaryPart(supportParts, ledger, "buff_amplifier", request, hasSupportingFamily: HasExplicitFamilyCoverage(ledger, "attack_buff") || HasExplicitFamilyCoverage(ledger, "damage_bonus") || HasExplicitFamilyCoverage(ledger, "weapon_boost") || HasExplicitFamilyCoverage(ledger, "damage_up"));
            TryAddConditionalOffensiveSummaryPart(supportParts, ledger, "debuff_amplifier", request, hasSupportingFamily: HasExplicitFamilyCoverage(ledger, "defense_debuff") || HasExplicitFamilyCoverage(ledger, "elemental_resistance_debuff") || HasExplicitFamilyCoverage(ledger, "damage_received_up"));
            TryAddConditionalOffensiveSummaryPart(supportParts, ledger, "enfeeble", request, hasSupportingFamily: HasExplicitFamilyCoverage(ledger, "defense_debuff") || HasExplicitFamilyCoverage(ledger, "elemental_resistance_debuff") || HasExplicitFamilyCoverage(ledger, "damage_received_up"));
            TryAddConditionalOffensiveSummaryPart(supportParts, ledger, "enliven", request, hasSupportingFamily: HasExplicitFamilyCoverage(ledger, "attack_buff") || HasExplicitFamilyCoverage(ledger, "damage_bonus") || HasExplicitFamilyCoverage(ledger, "weapon_boost") || HasExplicitFamilyCoverage(ledger, "damage_up"));
            TryAddOffensiveSummaryPart(supportParts, ledger, "torpor", request);

            packageParts = packageParts
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            supportParts = supportParts
                .Where(part => !packageParts.Contains(part, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (packageParts.Count == 0 && supportParts.Count == 0)
            {
                return null;
            }

            var summary = packageParts.Count > 0
                ? $"Best-case offensive package: {JoinSummaryFragments(packageParts)}"
                : $"Best-case offensive package: {JoinSummaryFragments(supportParts)}";
            if (packageParts.Count > 0 && supportParts.Count > 0)
            {
                summary += $". Supported by {JoinSummaryFragments(supportParts)}";
            }

            return summary + ".";
        }

        private static void TryAddOffensiveSummaryPart(
            List<string> parts,
            IReadOnlyDictionary<string, EffectFamilyLedgerEntry> ledger,
            string familyKey,
            PlayerPowerAnalyzerV2Request request)
        {
            if (!ledger.TryGetValue(familyKey, out var entry) || !entry.HasExplicitCoverage || entry.PrimaryEffect == null)
            {
                return;
            }

            parts.Add(GetOffensiveSummaryText(entry.PrimaryEffect, request));
        }

        private static void TryAddConditionalOffensiveSummaryPart(
            List<string> parts,
            IReadOnlyDictionary<string, EffectFamilyLedgerEntry> ledger,
            string familyKey,
            PlayerPowerAnalyzerV2Request request,
            bool hasSupportingFamily)
        {
            if (!hasSupportingFamily)
            {
                return;
            }

            TryAddOffensiveSummaryPart(parts, ledger, familyKey, request);
        }

        private static bool HasExplicitFamilyCoverage(IReadOnlyDictionary<string, EffectFamilyLedgerEntry> ledger, string familyKey)
        {
            return ledger.TryGetValue(familyKey, out var entry) && entry.HasExplicitCoverage;
        }

        private static string GetOffensiveSummaryText(DetectedActiveEffect effect, PlayerPowerAnalyzerV2Request request)
        {
            var scopePrefix = effect.TargetScope switch
            {
                ActiveEffectTargetScope.AllAllies => "teamwide ",
                ActiveEffectTargetScope.OtherAllies or ActiveEffectTargetScope.SingleAlly => "ally-targeted ",
                ActiveEffectTargetScope.Self => "self-only ",
                _ => string.Empty
            };

            var label = effect.Key switch
            {
                "elemental_resistance_down" when request.EnemyWeakness != Element.None => $"{request.EnemyWeakness} Resistance Down",
                "elemental_damage_up" when request.EnemyWeakness != Element.None => $"{request.EnemyWeakness} Damage Up",
                "elemental_damage_received_up" when request.EnemyWeakness != Element.None => $"{request.EnemyWeakness} Damage Received Up",
                "elemental_damage_bonus" when request.EnemyWeakness != Element.None => $"{request.EnemyWeakness} Damage Bonus",
                "elemental_weapon_boost" when request.EnemyWeakness != Element.None => $"{request.EnemyWeakness} Weapon Boost",
                _ => ToLabel(effect.Key)
            };

            return scopePrefix + label;
        }

        private static string JoinSummaryFragments(IReadOnlyList<string> parts)
        {
            return parts.Count switch
            {
                0 => string.Empty,
                1 => parts[0],
                2 => parts[0] + " and " + parts[1],
                _ => string.Join(", ", parts.Take(parts.Count - 1)) + ", and " + parts[^1]
            };
        }

        private static double GetStructuredTargetingMultiplier(DetectedActiveEffect effect, CharacterRole role, PlayerPowerAnalyzerV2Request request)
        {
            var fallback = GetActiveEffectTargetingMultiplier(effect.Key, role, request, effect.SourceText, effect.SourceAbilityType, effect.SourceElement);
            return effect.TargetScope switch
            {
                ActiveEffectTargetScope.AllAllies => effect.Key switch
                {
                    "patk_up" => request.PreferredDamageType == DamageType.Magical ? 1.0 : 1.45,
                    "matk_up" => request.PreferredDamageType == DamageType.Magical ? 1.45 : 1.0,
                    "pdef_up" or "mdef_up" => role == CharacterRole.DPS ? 1.0 : 1.2,
                    "healing_support" => role == CharacterRole.Healer ? 1.18 : 1.08,
                    "exploit_weakness" => 1.24,
                    _ => 1.1
                },
                ActiveEffectTargetScope.OtherAllies => effect.Key switch
                {
                    "patk_up" => request.PreferredDamageType == DamageType.Magical ? 0.95 : 1.22,
                    "matk_up" => request.PreferredDamageType == DamageType.Magical ? 1.22 : 0.95,
                    _ => 1.05
                },
                ActiveEffectTargetScope.SingleAlly => effect.Key switch
                {
                    "patk_up" => request.PreferredDamageType == DamageType.Magical ? 0.85 : 1.08,
                    "matk_up" => request.PreferredDamageType == DamageType.Magical ? 1.08 : 0.85,
                    _ => 0.96
                },
                ActiveEffectTargetScope.Self when IsOffensiveSetupEffect(effect.Key) => role != CharacterRole.DPS
                    ? 0.2
                    : (request.PreferredDamageType != DamageType.Any
                        && !string.IsNullOrWhiteSpace(effect.SourceAbilityType)
                        && !MatchesRequestedDamageType(effect.SourceAbilityType, request.PreferredDamageType))
                        || (request.EnemyWeakness != Element.None
                            && !string.IsNullOrWhiteSpace(effect.SourceElement)
                            && !effect.SourceElement.Equals("None", StringComparison.OrdinalIgnoreCase)
                            && !MatchesRequestedElement(effect.SourceElement, request.EnemyWeakness))
                        ? 0.35
                        : effect.FamilyKey == "attack_buff" ? 0.94 : 1.0,
                ActiveEffectTargetScope.AllEnemies => fallback,
                ActiveEffectTargetScope.SingleEnemy => fallback,
                _ => fallback
            };
        }

        private static bool ShouldDiscountForAssumedMateria(EffectFamilyLedgerEntry entry, PlayerPowerAnalyzerV2Request request)
        {
            if (!entry.HasAssumedCoverage || !entry.HasExplicitCoverage || !MateriaAssumableFamilies.Contains(entry.FamilyKey))
            {
                return false;
            }

            var explicitPotency = entry.ExplicitEffects.Select(GetPotencyFactor).DefaultIfEmpty(1.0).Max();
            var assumedPotency = entry.AssumedEffects.Select(GetPotencyFactor).DefaultIfEmpty(1.0).Max();
            if (explicitPotency > assumedPotency + 0.06)
            {
                return false;
            }

            var explicitSustain = entry.ExplicitEffects.Select(GetSustainFactor).DefaultIfEmpty(1.0).Max();
            var assumedSustain = entry.AssumedEffects.Select(GetSustainFactor).DefaultIfEmpty(1.0).Max();
            if (explicitSustain > assumedSustain + 0.05)
            {
                return false;
            }

            var explicitScope = entry.ExplicitEffects
                .Select(effect => GetStructuredTargetingMultiplier(effect, CharacterRole.Support, request))
                .DefaultIfEmpty(1.0)
                .Max();
            var assumedScope = entry.AssumedEffects
                .Select(effect => GetStructuredTargetingMultiplier(effect, CharacterRole.Support, request))
                .DefaultIfEmpty(1.0)
                .Max();
            return explicitScope <= assumedScope + 0.05;
        }

        private static Dictionary<string, EffectFamilyLedgerEntry> BuildEffectFamilyLedger(
            IReadOnlyCollection<DetectedActiveEffect> effects,
            PlayerPowerAnalyzerV2Request request)
        {
            var ledger = new Dictionary<string, EffectFamilyLedgerEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var effect in effects)
            {
                if (!ledger.TryGetValue(effect.FamilyKey, out var entry))
                {
                    entry = new EffectFamilyLedgerEntry
                    {
                        FamilyKey = effect.FamilyKey
                    };
                    ledger[effect.FamilyKey] = entry;
                }

                if (effect.IsAssumedMateria)
                {
                    entry.AssumedEffects.Add(effect);
                }
                else
                {
                    entry.ExplicitEffects.Add(effect);
                }
            }

            foreach (var entry in ledger.Values)
            {
                var primaryCandidates = entry.HasExplicitCoverage ? entry.ExplicitEffects : entry.AssumedEffects;
                entry.PrimaryEffect = primaryCandidates
                    .OrderByDescending(effect => GetPotencyFactor(effect))
                    .ThenByDescending(effect => GetSustainFactor(effect))
                    .ThenByDescending(effect => GetStructuredTargetingMultiplier(effect, CharacterRole.Support, request))
                    .FirstOrDefault();
                entry.BestPotencyFactor = (entry.ExplicitEffects.Count > 0 ? entry.ExplicitEffects : entry.AssumedEffects)
                    .Select(GetPotencyFactor)
                    .DefaultIfEmpty(1.0)
                    .Max();
                entry.BestSustainFactor = (entry.ExplicitEffects.Count > 0 ? entry.ExplicitEffects : entry.AssumedEffects)
                    .Select(GetSustainFactor)
                    .DefaultIfEmpty(1.0)
                    .Max();
                entry.BestScopeMultiplier = (entry.ExplicitEffects.Count > 0 ? entry.ExplicitEffects : entry.AssumedEffects)
                    .Select(effect => GetStructuredTargetingMultiplier(effect, CharacterRole.Support, request))
                    .DefaultIfEmpty(1.0)
                    .Max();
            }

            return ledger;
        }

        private static IReadOnlyList<DetectedActiveEffect> BuildAssumedMateriaEffects(IReadOnlyList<CharacterBuildCandidate> baseVariants, PlayerPowerAnalyzerV2Request request)
        {
            var effects = new List<DetectedActiveEffect>();
            if (!CanAssumeStandardSynthDebuffSeedSetup(baseVariants))
            {
                return effects;
            }

            if (request.EnemyWeakness != Element.None)
            {
                effects.Add(CreateAssumedMateriaEffect(
                    "elemental_resistance_down",
                    request.EnemyWeakness.ToString() + " Resistance Down [Pot: 15%] [Rng: Single Enemy] [Dur: 24s]",
                    "Standard synth breach materia",
                    sourceElement: request.EnemyWeakness.ToString()));
            }

            if (request.PreferredDamageType == DamageType.Physical)
            {
                effects.Add(CreateAssumedMateriaEffect(
                    "pdef_down",
                    "PDEF Down [Pot: 15%] [Rng: Single Enemy] [Dur: 24s]",
                    "Standard synth Breach materia"));
                effects.Add(CreateAssumedMateriaEffect(
                    "patk_up",
                    "PATK Up [Pot: 15%] [Rng: Single Ally] [Dur: 24s]",
                    "Standard synth Bravery materia"));
            }
            else if (request.PreferredDamageType == DamageType.Magical)
            {
                effects.Add(CreateAssumedMateriaEffect(
                    "mdef_down",
                    "MDEF Down [Pot: 15%] [Rng: Single Enemy] [Dur: 24s]",
                    "Standard synth Mana Breach materia"));
                effects.Add(CreateAssumedMateriaEffect(
                    "matk_up",
                    "MATK Up [Pot: 15%] [Rng: Single Ally] [Dur: 24s]",
                    "Standard synth Faith materia"));
            }

            return effects;
        }

        private static DetectedActiveEffect CreateAssumedMateriaEffect(string key, string sourceText, string sourceName, string sourceElement = "None")
        {
            return new DetectedActiveEffect
            {
                Key = key,
                FamilyKey = GetActiveEffectFamilyKey(key),
                AxisKey = GetActiveEffectAxisKey(key),
                SourceName = sourceName,
                SourceType = "AssumedMateria",
                SourceText = sourceText,
                SourceAbilityType = string.Empty,
                SourceElement = sourceElement,
                PotencyPercent = TryParseMarker(PotencyMarkerRegex, sourceText),
                DurationSeconds = TryParseMarker(DurationMarkerRegex, sourceText),
                ExtensionSeconds = TryParseMarker(ExtensionMarkerRegex, sourceText),
                TargetScope = ParseTargetScope(sourceText, sourceText),
                IsAssumedMateria = true
            };
        }
    }
}
