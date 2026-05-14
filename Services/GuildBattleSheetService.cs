using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using FFVIIEverCrisisAnalyzer.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class GuildBattleSheetService
    {
        private const int SupportedSchemaVersion = 1;

        private readonly ILogger<GuildBattleSheetService> _logger;
        private readonly IWebHostEnvironment _env;
        private readonly WeaponSearchDataService _weaponData;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private GuildBattleSheetConfiguration _config = new();

        public GuildBattleSheetService(
            ILogger<GuildBattleSheetService> logger,
            IWebHostEnvironment env,
            WeaponSearchDataService weaponData,
            GuildBattleSheetConfiguration? overrideConfig = null)
        {
            _logger = logger;
            _env = env;
            _weaponData = weaponData;
            _config = overrideConfig ?? LoadConfiguration();
        }

        public IReadOnlyList<GuildBattleSheetBattleSummary> GetBattleSummaries()
        {
            return GetOrderedBattleSummaries();
        }

        public GuildBattleSheetViewModel? BuildSheet(
            string? battleId,
            bool debugMode,
            GuildBattleSheetRecommendationMode recommendationMode = GuildBattleSheetRecommendationMode.Traditional)
        {
            var summaries = GetOrderedBattleSummaries();
            if (summaries.Count == 0)
            {
                return null;
            }

            var selectedSummary = summaries.FirstOrDefault(s => string.Equals(s.Id, battleId, StringComparison.OrdinalIgnoreCase))
                ?? summaries[0];
            var selectedBattle = _config.Battles.First(b => string.Equals(b.Id, selectedSummary.Id, StringComparison.OrdinalIgnoreCase));

            var hiddenWeapons = selectedBattle.HiddenWeapons
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(NormalizeName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var topPickNames = selectedBattle.TopPicks
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(NormalizeName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var activeConditionalMechanics = debugMode
                ? selectedBattle.ConditionalMechanics
                    .Where(mechanic => !string.IsNullOrWhiteSpace(mechanic))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<string>();

            var visibleWeapons = _weaponData.GetWeapons()
                .Where(w => !hiddenWeapons.Contains(NormalizeName(w.Name)))
                .ToList();

            var featuredWeapons = visibleWeapons
                .Where(w => string.Equals(w.EquipmentType, "Featured", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var mainRecommendations = recommendationMode == GuildBattleSheetRecommendationMode.Character
                ? BuildCharacterModeMainRecommendations(featuredWeapons, selectedBattle, topPickNames, activeConditionalMechanics)
                : BuildTraditionalMainRecommendations(featuredWeapons, selectedBattle, topPickNames, activeConditionalMechanics);

            var potencySection = BuildSection(
                title: $"{selectedBattle.Element} Potency",
                emptyMessage: $"No featured weapons with Boost {selectedBattle.Element} Potency R abilities were found.",
                candidates: featuredWeapons,
                selector: w => BuildPotencyCandidate(w, selectedBattle),
                take: 8);

            var subWeaponSection = BuildSection(
                title: selectedBattle.AbilityType.Equals("Physical", StringComparison.OrdinalIgnoreCase)
                    ? "Featured Physical Sub Weapons"
                    : "Featured Magical Sub Weapons",
                emptyMessage: $"No featured {selectedBattle.AbilityType.ToLowerInvariant()}-leaning sub weapons were found.",
                candidates: featuredWeapons,
                selector: w => BuildSubWeaponCandidate(w, selectedBattle),
                take: 9);

            var resistanceDownSection = BuildSection(
                title: $"{selectedBattle.Element} Resistance Down",
                emptyMessage: $"No weapons with {selectedBattle.Element} Resistance Down were found.",
                candidates: visibleWeapons,
                selector: w => BuildEffectTagCandidate(w, $"{selectedBattle.Element} Resistance Down"),
                take: 8);

            var damageUpSection = BuildSection(
                title: $"{selectedBattle.Element} Damage Up",
                emptyMessage: $"No weapons with {selectedBattle.Element} Damage Up were found.",
                candidates: visibleWeapons,
                selector: w => BuildEffectTagCandidate(w, $"{selectedBattle.Element} Damage Up"),
                take: 8);

            var damageBonusSection = BuildSection(
                title: $"{selectedBattle.Element} Damage Bonus",
                emptyMessage: $"No weapons with {selectedBattle.Element} Damage Bonus or {selectedBattle.Element} Weapon Boost were found.",
                candidates: visibleWeapons,
                selector: w => BuildAnyDisplayTagCandidate(
                    w,
                    $"{selectedBattle.Element} Damage Bonus",
                    $"{selectedBattle.Element} Weapon Boost"),
                take: 8);

            var usesManualWriteup = !string.IsNullOrWhiteSpace(selectedBattle.Writeup);
            var writeupLines = usesManualWriteup
                ? SplitWriteup(selectedBattle.Writeup)
                : BuildAutoWriteup(selectedBattle, mainRecommendations, potencySection, subWeaponSection, resistanceDownSection, damageUpSection, damageBonusSection);

            return new GuildBattleSheetViewModel
            {
                SelectedBattle = selectedBattle,
                SelectedBattleSummary = selectedSummary,
                AvailableBattles = summaries,
                RecommendationMode = recommendationMode,
                MainRecommendations = mainRecommendations,
                PotencySection = potencySection,
                SubWeaponSection = subWeaponSection,
                ResistanceDownSection = resistanceDownSection,
                DamageUpSection = damageUpSection,
                DamageBonusSection = damageBonusSection,
                WriteupLines = writeupLines,
                UsesManualWriteup = usesManualWriteup,
                DebugMode = debugMode,
                GeneratedAtUtc = DateTimeOffset.UtcNow
            };
        }

        private GuildBattleSheetConfiguration LoadConfiguration()
        {
            var path = Path.Combine(_env.ContentRootPath, "data", "guildBattleSheets.json");
            if (!File.Exists(path))
            {
                _logger.LogWarning("Guild battle sheet config file not found at {Path}", path);
                return new GuildBattleSheetConfiguration();
            }

            try
            {
                var json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<GuildBattleSheetConfiguration>(json, _jsonOptions) ?? new GuildBattleSheetConfiguration();
                if (config.SchemaVersion != SupportedSchemaVersion)
                {
                    _logger.LogWarning("Guild battle sheet config schema version {SchemaVersion} is not supported", config.SchemaVersion);
                    return new GuildBattleSheetConfiguration();
                }

                return new GuildBattleSheetConfiguration
                {
                    SchemaVersion = config.SchemaVersion,
                    Battles = config.Battles
                        .Where(IsValidBattle)
                        .ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load guild battle sheet config");
                return new GuildBattleSheetConfiguration();
            }
        }

        private List<GuildBattleSheetBattleSummary> GetOrderedBattleSummaries()
        {
            return _config.Battles
                .Where(IsValidBattle)
                .Select(b => new GuildBattleSheetBattleSummary
                {
                    Id = b.Id,
                    DisplayLabel = $"{b.Month} {b.Year} — {b.AbilityType} {b.Element}",
                    Element = b.Element,
                    AbilityType = b.AbilityType,
                    Rank = b.Rank,
                    SortKey = BuildSortKey(b)
                })
                .OrderByDescending(b => b.SortKey)
                .ThenByDescending(b => b.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private GuildBattleSheetSection BuildSection(
            string title,
            string emptyMessage,
            IEnumerable<WeaponSearchItem> candidates,
            Func<WeaponSearchItem, RecommendationCandidate> selector,
            int take)
        {
            var entries = candidates
                .Select(selector)
                .Where(c => c.ShouldInclude)
                .OrderByDescending(c => c.Score)
                .ThenByDescending(c => c.Weapon.DamagePercent)
                .ThenBy(c => c.Weapon.Name, StringComparer.OrdinalIgnoreCase)
                .Take(take)
                .Select(c => BuildEntry(c.Weapon, c.Score, c.Reasons, c.Highlights, false))
                .ToList();

            return new GuildBattleSheetSection
            {
                Title = title,
                EmptyMessage = emptyMessage,
                Entries = entries
            };
        }

        private List<GuildBattleSheetEntry> BuildTraditionalMainRecommendations(
            IReadOnlyList<WeaponSearchItem> featuredWeapons,
            GuildBattleSheetBattleDefinition battle,
            HashSet<string> topPickNames,
            IReadOnlyCollection<string> activeConditionalMechanics)
        {
            var mainCandidates = featuredWeapons
                .Select(w => BuildMainRecommendationCandidate(w, battle, topPickNames, activeConditionalMechanics))
                .Where(c => c.ShouldInclude)
                .ToList();

            var orderedMainCandidates = OrderMainRecommendationCandidates(mainCandidates, battle)
                .ToList();
            var reservedSupportCandidates = orderedMainCandidates
                .Where(c => IsReservedSupportMainRecommendationCandidate(c.Weapon, battle, activeConditionalMechanics))
                .Take(2)
                .ToList();
            var reservedSupportNames = reservedSupportCandidates
                .Select(c => NormalizeName(c.Weapon.Name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return reservedSupportCandidates
                .Concat(orderedMainCandidates
                    .Where(c => !reservedSupportNames.Contains(NormalizeName(c.Weapon.Name)))
                    .Take(Math.Max(0, 12 - reservedSupportCandidates.Count)))
                .OrderByDescending(c => c.Score)
                .ThenByDescending(c => c.Weapon.DamagePercent)
                .ThenByDescending(c => GetPrimaryStat(c.Weapon, battle.AbilityType))
                .ThenBy(c => c.Weapon.Name, StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .Select(c => BuildEntry(c.Weapon, c.Score, c.Reasons, c.Highlights, topPickNames.Contains(NormalizeName(c.Weapon.Name))))
                .ToList();
        }

        private List<GuildBattleSheetEntry> BuildCharacterModeMainRecommendations(
            IReadOnlyList<WeaponSearchItem> featuredWeapons,
            GuildBattleSheetBattleDefinition battle,
            HashSet<string> topPickNames,
            IReadOnlyCollection<string> activeConditionalMechanics)
        {
            return featuredWeapons
                .GroupBy(w => w.Character, StringComparer.OrdinalIgnoreCase)
                .Select(group => BuildCharacterModeRecommendationCandidate(group.ToList(), battle, topPickNames, activeConditionalMechanics))
                .Where(candidate => candidate != null)
                .Select(candidate => candidate!)
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.Weapon.DamagePercent)
                .ThenByDescending(candidate => GetPrimaryStat(candidate.Weapon, battle.AbilityType))
                .ThenBy(candidate => candidate.Weapon.Name, StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .Select(candidate => BuildEntry(candidate.Weapon, candidate.Score, candidate.Reasons, candidate.Highlights, candidate.IsTopPick))
                .ToList();
        }

        private CharacterRecommendationCandidate? BuildCharacterModeRecommendationCandidate(
            IReadOnlyList<WeaponSearchItem> characterWeapons,
            GuildBattleSheetBattleDefinition battle,
            HashSet<string> topPickNames,
            IReadOnlyCollection<string> activeConditionalMechanics)
        {
            if (characterWeapons.Count == 0)
            {
                return null;
            }

            var characterRole = CharacterRoleRegistry.GetRoleOrDefault(characterWeapons[0].Character);
            var scoredCandidates = characterWeapons
                .Select(w => BuildMainRecommendationCandidate(w, battle, topPickNames, activeConditionalMechanics))
                .ToList();
            var battleFitMainHands = OrderMainRecommendationCandidates(
                    scoredCandidates.Where(c => IsBattleFitMainWeapon(c.Weapon, battle)),
                    battle)
                .ToList();

            if (characterRole == CharacterRole.DPS && battleFitMainHands.Count > 0)
            {
                var mainHand = battleFitMainHands[0];
                var fightFacingUtilityCompanion = OrderMainRecommendationCandidates(
                        scoredCandidates
                            .Where(c => !string.Equals(c.Weapon.Id, mainHand.Weapon.Id, StringComparison.OrdinalIgnoreCase))
                            .Where(c => HasCharacterModeSupportValue(c.Weapon, battle, activeConditionalMechanics, allowSelfOnlySupportEffects: false)),
                        battle)
                    .FirstOrDefault();
                var utilityCompanion = fightFacingUtilityCompanion
                    ?? OrderMainRecommendationCandidates(
                        scoredCandidates
                            .Where(c => !string.Equals(c.Weapon.Id, mainHand.Weapon.Id, StringComparison.OrdinalIgnoreCase))
                            .Where(c => HasCharacterModeDpsCompanionValue(c.Weapon, battle, activeConditionalMechanics)),
                        battle)
                    .FirstOrDefault();

                var reasons = new List<string>(mainHand.Reasons)
                {
                    "Character mode: anchor this character with a battle-fit DPS main hand"
                };
                var highlights = new List<string>(mainHand.Highlights)
                {
                    "DPS Package"
                };
                var score = mainHand.Score + 90;

                if (utilityCompanion != null)
                {
                    if (ReferenceEquals(utilityCompanion, fightFacingUtilityCompanion))
                    {
                        score += 80 + utilityCompanion.Score * 0.35;
                        reasons.Add($"Party or boss-facing companion utility available from {utilityCompanion.Weapon.Name}");
                    }
                    else
                    {
                        score += 45 + utilityCompanion.Score * 0.2;
                        reasons.Add($"Self-target DPS companion utility available from {utilityCompanion.Weapon.Name}");
                        reasons.Add("Utility companion is a self-target effect that still supports this DPS package");
                    }

                    var companionHighlight = utilityCompanion.Highlights
                        .FirstOrDefault(highlight => !string.IsNullOrWhiteSpace(highlight)
                            && !highlights.Contains(highlight, StringComparer.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(companionHighlight))
                    {
                        highlights.Add(companionHighlight);
                    }
                }

                return new CharacterRecommendationCandidate(
                    mainHand.Weapon,
                    score,
                    reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    highlights.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    topPickNames.Contains(NormalizeName(mainHand.Weapon.Name)));
            }

            var supportAnchor = OrderMainRecommendationCandidates(
                    scoredCandidates.Where(c => HasCharacterModeSupportValue(c.Weapon, battle, activeConditionalMechanics, allowSelfOnlySupportEffects: false)),
                    battle)
                .FirstOrDefault();
            if (supportAnchor == null)
            {
                var pinnedFallback = OrderMainRecommendationCandidates(
                        scoredCandidates.Where(c => topPickNames.Contains(NormalizeName(c.Weapon.Name))),
                        battle)
                    .FirstOrDefault();
                if (pinnedFallback == null)
                {
                    return null;
                }

                var fallbackReasons = new List<string>(pinnedFallback.Reasons)
                {
                    "Character mode: pinned top pick fallback"
                };
                var fallbackHighlights = new List<string>(pinnedFallback.Highlights)
                {
                    "Top Pick"
                };

                return new CharacterRecommendationCandidate(
                    pinnedFallback.Weapon,
                    pinnedFallback.Score + 25,
                    fallbackReasons.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    fallbackHighlights.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    true);
            }

            var supportReasons = new List<string>(supportAnchor.Reasons);
            var supportHighlights = new List<string>(supportAnchor.Highlights);
            var supportScore = supportAnchor.Score + 55;
            if (characterRole == CharacterRole.DPS)
            {
                supportReasons.Add("Character mode: no battle-fit DPS main hand was found, so this character is evaluated as support");
                supportHighlights.Add("Support Pivot");
            }
            else
            {
                supportReasons.Add("Character mode: evaluate this character as a support-style utility pick");
                supportHighlights.Add("Support Package");
            }

            return new CharacterRecommendationCandidate(
                supportAnchor.Weapon,
                supportScore,
                supportReasons.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                supportHighlights.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                topPickNames.Contains(NormalizeName(supportAnchor.Weapon.Name)));
        }

        private RecommendationCandidate BuildMainRecommendationCandidate(
            WeaponSearchItem weapon,
            GuildBattleSheetBattleDefinition battle,
            HashSet<string> topPickNames,
            IReadOnlyCollection<string> activeConditionalMechanics)
        {
            var reasons = new List<string>();
            var highlights = new List<string>();
            var score = 0d;
            var normalizedName = NormalizeName(weapon.Name);
            var isTopPick = topPickNames.Contains(normalizedName);
            var characterRole = CharacterRoleRegistry.GetRoleOrDefault(weapon.Character);
            var isDpsCharacter = characterRole == CharacterRole.DPS;
            var matchesElement = string.Equals(weapon.Element, battle.Element, StringComparison.OrdinalIgnoreCase);
            var matchesAbilityType = MatchesBattleAbilityType(weapon.AbilityType, battle.AbilityType);
            var isPhysicalBattle = battle.AbilityType.Equals("Physical", StringComparison.OrdinalIgnoreCase);
            var hasSelfOnlySupportEffect = IsSelfOnlySupportEffect(weapon);
            var battleBuffTag = battle.AbilityType.Equals("Physical", StringComparison.OrdinalIgnoreCase) ? "PATK Up" : "MATK Up";
            var battleBreakTag = battle.AbilityType.Equals("Physical", StringComparison.OrdinalIgnoreCase) ? "PDEF Down" : "MDEF Down";
            var battleDamageBonusTag = battle.AbilityType.Equals("Physical", StringComparison.OrdinalIgnoreCase) ? "Phys. Damage Bonus" : "Mag. Damage Bonus";
            var battleDamageReceivedUpTag = battle.AbilityType.Equals("Physical", StringComparison.OrdinalIgnoreCase) ? "Phys. Dmg. Rcvd. Up" : "Mag. Dmg. Rcvd. Up";
            var battleAllTargetDamageReceivedUpTag = battle.AbilityType.Equals("Physical", StringComparison.OrdinalIgnoreCase) ? "All-Tgt. Phys. Dmg. Rcvd. Up" : "All-Tgt. Mag. Dmg. Rcvd. Up";
            var battleStatusDamageReceivedUpTag = battle.AbilityType.Equals("Physical", StringComparison.OrdinalIgnoreCase) ? "Status Ailment: All-Tgt. Phys. Dmg. Rcvd. Up" : "Status Ailment: All-Tgt. Mag. Dmg. Rcvd. Up";
            var battleAtbConservationTag = battle.AbilityType.Equals("Physical", StringComparison.OrdinalIgnoreCase) ? "Phys. ATB Conservation Effect" : "Mag. ATB Conservation Effect";
            var hasElementResistanceDown = HasEffectTag(weapon, $"{battle.Element} Resistance Down");
            var hasElementDamageUp = HasEffectTag(weapon, $"{battle.Element} Damage Up");
            var hasElementDamageBonus = HasEffectTag(weapon, $"{battle.Element} Damage Bonus");
            var hasElementWeaponBoost = HasEffectTag(weapon, $"{battle.Element} Weapon Boost");
            var hasExploitWeakness = HasEffectTag(weapon, "Exploit Weakness");
            var hasBattleBuff = HasEffectTag(weapon, battleBuffTag);
            var hasBattleBreak = HasEffectTag(weapon, battleBreakTag);
            var hasBattleDamageBonus = HasEffectTag(weapon, battleDamageBonusTag);
            var hasBattleDamageReceivedUp = HasAnyEffectTag(weapon, battleDamageReceivedUpTag, battleAllTargetDamageReceivedUpTag, battleStatusDamageReceivedUpTag);
            var hasEnfeeble = HasAnyEffectTag(weapon, "Enfeeble", "Status Ailment: Enfeeble");
            var hasHaste = HasEffectTag(weapon, "Haste");
            var hasAppliedStatsDebuffTierIncreased = HasEffectTag(weapon, "Applied Stats Debuff Tier Increased");
            var hasAppliedStatsBuffTierIncreased = HasEffectTag(weapon, "Applied Stats Buff Tier Increased");
            var hasEnliven = HasEffectTag(weapon, "Enliven");
            var hasBattleAtbConservation = HasEffectTag(weapon, battleAtbConservationTag);
            var hasCommandGaugeHelp = ContainsText(weapon.AbilityText, "command gauge");
            var hasRelevantRAbility = matchesElement && HasRelevantBattleRAbility(weapon, battle);
            var matchedConditionalMechanics = GetMatchedConditionalMechanics(weapon, activeConditionalMechanics);
            var hasConditionalMechanicUtility = matchedConditionalMechanics.Count > 0;
            var hasFightFacingBattleBuff = hasBattleBuff && !hasSelfOnlySupportEffect;
            var hasFightFacingHaste = hasHaste && !hasSelfOnlySupportEffect;
            var hasFightFacingAppliedStatsBuffTierIncreased = hasAppliedStatsBuffTierIncreased && !hasSelfOnlySupportEffect;
            var hasFightFacingEnliven = hasEnliven && !hasSelfOnlySupportEffect;
            var hasSelfOnlyBattleBuff = hasBattleBuff && hasSelfOnlySupportEffect;
            var hasSelfOnlyHaste = hasHaste && hasSelfOnlySupportEffect;
            var hasSelfOnlyAppliedStatsBuffTierIncreased = hasAppliedStatsBuffTierIncreased && hasSelfOnlySupportEffect;
            var hasSelfOnlyEnliven = hasEnliven && hasSelfOnlySupportEffect;
            var hasFightFacingElementDamageUp = hasElementDamageUp && !hasSelfOnlySupportEffect;
            var hasFightFacingElementDamageBonus = hasElementDamageBonus && !hasSelfOnlySupportEffect;
            var hasFightFacingElementWeaponBoost = hasElementWeaponBoost && !hasSelfOnlySupportEffect;
            var hasFightFacingExploitWeakness = hasExploitWeakness && !hasSelfOnlySupportEffect;
            var hasElementUtility = hasElementResistanceDown
                || hasFightFacingElementDamageUp
                || hasFightFacingElementDamageBonus
                || hasFightFacingElementWeaponBoost;
            var hasGeneralBattleSupportWeaponEffect = hasFightFacingExploitWeakness
                || hasFightFacingBattleBuff
                || hasBattleBreak
                || hasBattleDamageBonus
                || hasBattleDamageReceivedUp
                || hasEnfeeble
                || hasFightFacingHaste
                || hasAppliedStatsDebuffTierIncreased
                || hasFightFacingAppliedStatsBuffTierIncreased
                || hasFightFacingEnliven
                || hasBattleAtbConservation
                || hasConditionalMechanicUtility;
            var hasSupportiveWeaponEffect = hasElementUtility || hasGeneralBattleSupportWeaponEffect;
            var hasPartyFacingSupport = hasFightFacingBattleBuff
                || hasFightFacingHaste
                || hasFightFacingAppliedStatsBuffTierIncreased
                || hasFightFacingEnliven
                || hasFightFacingElementDamageUp
                || hasFightFacingElementDamageBonus
                || hasFightFacingElementWeaponBoost
                || hasBattleDamageBonus
                || hasBattleAtbConservation
                || hasCommandGaugeHelp;
            var hasBossDebuffSetup = hasElementResistanceDown
                || hasFightFacingExploitWeakness
                || hasBattleBreak
                || hasBattleDamageReceivedUp
                || hasEnfeeble
                || hasAppliedStatsDebuffTierIncreased
                || hasConditionalMechanicUtility;
            var hasSelfOnlyDpsPrep = matchesAbilityType
                && (hasSelfOnlyBattleBuff
                    || hasSelfOnlyHaste
                    || hasSelfOnlyAppliedStatsBuffTierIncreased
                    || hasSelfOnlyEnliven);
            var partySupportTargetBonus = hasPartyFacingSupport ? GetPartySupportTargetBonus(weapon) : 0;
            var bossSetupTargetBonus = hasBossDebuffSetup ? GetBossSetupTargetBonus(weapon) : 0;
            var hasDirectFightUtility = hasPartyFacingSupport || hasBossDebuffSetup;
            var qualifiesAsUtilityFeaturedPick = !isDpsCharacter
                ? hasSupportiveWeaponEffect || hasCommandGaugeHelp
                : hasDirectFightUtility && matchesAbilityType;

            if (isTopPick)
            {
                score += 10000;
                reasons.Add("Pinned Top Pick");
                highlights.Add("Top Pick");
            }

            if (isDpsCharacter && matchesElement && matchesAbilityType)
            {
                score += 420;
                reasons.Add($"DPS weapon matches {battle.Element} element and {battle.AbilityType.ToLowerInvariant()} damage type");
                highlights.Add(battle.Element);
                highlights.Add(battle.AbilityType);
            }
            else if (!isDpsCharacter && matchesElement && matchesAbilityType)
            {
                score += 300;
                reasons.Add($"{characterRole} sub-DPS option matches {battle.Element} element and {battle.AbilityType.ToLowerInvariant()} damage type");
                highlights.Add(battle.Element);
                highlights.Add(battle.AbilityType);
            }

            if (qualifiesAsUtilityFeaturedPick && (!matchesElement || !matchesAbilityType))
            {
                score += 175;
                if (hasPartyFacingSupport && hasBossDebuffSetup)
                {
                    reasons.Add("Featured weapon brings party support and boss debuff setup");
                }
                else if (hasPartyFacingSupport)
                {
                    reasons.Add($"Featured weapon brings party-facing {(isPhysicalBattle ? "physical" : "magical")} fight utility");
                }
                else if (hasBossDebuffSetup)
                {
                    reasons.Add("Featured weapon brings boss-facing fight utility");
                }
                else
                {
                    reasons.Add($"Featured weapon brings direct {(isPhysicalBattle ? "physical" : "magical")} fight utility");
                }

                highlights.Add("Fight Support");
            }

            if (hasPartyFacingSupport)
            {
                score += 70 + partySupportTargetBonus;
                reasons.Add("Provides party-facing support");
                if (partySupportTargetBonus >= 50)
                {
                    reasons.Add("Party support reaches all allies");
                    highlights.Add("All Allies");
                }
                else if (partySupportTargetBonus >= 35)
                {
                    reasons.Add("Party support reaches other allies");
                    highlights.Add("Other Allies");
                }
                else if (partySupportTargetBonus >= 20)
                {
                    reasons.Add("Party support can target a single ally");
                    highlights.Add("Single Ally");
                }

                highlights.Add("Party Support");
            }

            if (hasBossDebuffSetup)
            {
                score += 65 + bossSetupTargetBonus;
                reasons.Add("Provides boss debuff setup");
                if (bossSetupTargetBonus >= 35)
                {
                    reasons.Add("Boss setup can hit all enemies");
                    highlights.Add("All Enemies");
                }
                else if (bossSetupTargetBonus >= 20)
                {
                    reasons.Add("Boss setup focuses a single boss target");
                    highlights.Add("Boss Focus");
                }

                highlights.Add("Boss Setup");
            }

            if (isDpsCharacter && hasSelfOnlyDpsPrep)
            {
                score += 25;
                reasons.Add("Provides self DPS setup");
                highlights.Add("Self Prep");
            }

            if (!isDpsCharacter && matchesElement && hasSupportiveWeaponEffect)
            {
                score += 240;
                reasons.Add($"{characterRole} weapon brings element-matching fight utility");
                highlights.Add($"{characterRole} Utility");
            }
            else if (matchesElement)
            {
                score += isDpsCharacter ? 40 : 80;
                reasons.Add($"Matches {battle.Element} element");
                highlights.Add(battle.Element);
            }

            if (matchesAbilityType)
            {
                score += isDpsCharacter ? 120 : 70;
                if (!reasons.Any(r => r.Contains("damage type", StringComparison.OrdinalIgnoreCase)))
                {
                    reasons.Add($"Matches {battle.AbilityType} damage type");
                }
                if (!highlights.Contains(battle.AbilityType, StringComparer.OrdinalIgnoreCase))
                {
                    highlights.Add(battle.AbilityType);
                }
            }

            if (hasElementResistanceDown)
            {
                score += 180;
                reasons.Add($"Applies {battle.Element} Resistance Down");
                highlights.Add($"{battle.Element} Resist Down");
            }

            if (hasFightFacingElementDamageUp)
            {
                score += 140;
                reasons.Add($"Provides {battle.Element} Damage Up");
                highlights.Add($"{battle.Element} Damage Up");
            }

            if (hasFightFacingElementDamageBonus)
            {
                score += 140;
                reasons.Add($"Provides {battle.Element} Damage Bonus");
                highlights.Add($"{battle.Element} Damage Bonus");
            }

            if (hasFightFacingElementWeaponBoost)
            {
                score += 140;
                reasons.Add($"Provides {battle.Element} Weapon Boost");
                highlights.Add($"{battle.Element} Weapon Boost");
            }

            if (hasFightFacingExploitWeakness)
            {
                score += 130;
                reasons.Add("Provides Exploit Weakness support");
                highlights.Add("Exploit Weakness");
            }

            if (hasFightFacingBattleBuff)
            {
                score += 110;
                reasons.Add($"Provides {battleBuffTag}");
                highlights.Add(battleBuffTag);
            }

            if (hasBattleBreak)
            {
                score += 100;
                reasons.Add($"Applies {battleBreakTag}");
                highlights.Add(battleBreakTag);
            }

            if (hasBattleDamageBonus)
            {
                score += 90;
                reasons.Add($"Provides {battleDamageBonusTag}");
                highlights.Add(battleDamageBonusTag);
            }

            if (hasBattleDamageReceivedUp)
            {
                score += 125;
                reasons.Add($"Amplifies {(isPhysicalBattle ? "physical" : "magical")} damage taken");
                highlights.Add(isPhysicalBattle ? "Phys. Dmg Taken Up" : "Mag. Dmg Taken Up");
            }

            if (hasRelevantRAbility)
            {
                score += 85;
                reasons.Add("Provides battle-relevant R ability support");
                highlights.Add("R Ability Value");
            }

            if (hasEnfeeble)
            {
                score += 135;
                reasons.Add("Applies Enfeeble");
                highlights.Add("Enfeeble");
            }

            if (hasFightFacingHaste)
            {
                score += 125;
                reasons.Add("Provides Haste support");
                highlights.Add("Haste");
            }

            if (hasAppliedStatsDebuffTierIncreased)
            {
                score += 120;
                reasons.Add("Amplifies applied debuffs");
                highlights.Add("Debuff Amp");
            }

            if (hasFightFacingAppliedStatsBuffTierIncreased)
            {
                score += 115;
                reasons.Add("Amplifies applied buffs");
                highlights.Add("Buff Amp");
            }

            if (hasFightFacingEnliven)
            {
                score += 95;
                reasons.Add("Raises damage buffs with Enliven");
                highlights.Add("Enliven");
            }

            if (hasBattleAtbConservation)
            {
                score += 105;
                reasons.Add($"Provides {battleAtbConservationTag}");
                highlights.Add(isPhysicalBattle ? "Phys. ATB Save" : "Mag. ATB Save");
            }

            if (hasConditionalMechanicUtility)
            {
                score += 150;
                foreach (var mechanic in matchedConditionalMechanics)
                {
                    reasons.Add($"Conditional mechanic: {mechanic}");
                    highlights.Add(mechanic);
                }
            }

            if (hasCommandGaugeHelp)
            {
                score += 75;
                reasons.Add("Helps fill Command Gauge");
                highlights.Add("Command Gauge");
            }

            var damageContribution = GetMainRecommendationDamageContribution(
                weapon,
                matchesElement,
                matchesAbilityType,
                hasSupportiveWeaponEffect,
                hasPartyFacingSupport,
                hasBossDebuffSetup,
                hasSelfOnlyDpsPrep);
            if (damageContribution > 0)
            {
                score += damageContribution;
                if (matchesElement && matchesAbilityType)
                {
                    reasons.Add($"Damage potency {weapon.DamagePercent:0.#}%");
                }
            }

            var shouldInclude = isTopPick
                || (isDpsCharacter && matchesElement && matchesAbilityType)
                || (!isDpsCharacter && matchesElement && (matchesAbilityType || hasSupportiveWeaponEffect))
                || qualifiesAsUtilityFeaturedPick;

            return new RecommendationCandidate(weapon, score, shouldInclude, reasons, highlights);
        }

        private RecommendationCandidate BuildPotencyCandidate(WeaponSearchItem weapon, GuildBattleSheetBattleDefinition battle)
        {
            var allRAbilities = EnumerateRAbilityNames(weapon)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var names = allRAbilities
                .Where(n => ContainsText(n, $"Boost {battle.Element} Pot"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var isPhysical = battle.AbilityType.Equals("Physical", StringComparison.OrdinalIgnoreCase);
            var preferredCompanion = isPhysical ? "Boost PATK" : "Boost MATK";
            var preferredMatches = allRAbilities
                .Where(n => ContainsText(n, preferredCompanion))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var hpMatches = allRAbilities
                .Where(n => ContainsText(n, "Boost HP"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var offTypeOffensiveMatches = allRAbilities
                .Where(n => isPhysical
                    ? ContainsText(n, "Boost MATK") || ContainsText(n, "Boost Mag. Ability Pot")
                    : ContainsText(n, "Boost PATK") || ContainsText(n, "Boost Phys. Ability Pot"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var otherMatches = allRAbilities
                .Where(n => !names.Contains(n, StringComparer.OrdinalIgnoreCase))
                .Where(n => !preferredMatches.Contains(n, StringComparer.OrdinalIgnoreCase))
                .Where(n => !hpMatches.Contains(n, StringComparer.OrdinalIgnoreCase))
                .Where(n => !offTypeOffensiveMatches.Contains(n, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var score = names.Count * 100;
            if (names.Count > 0 && preferredMatches.Count > 0)
            {
                score += 160;
            }
            else if (names.Count > 0 && hpMatches.Count > 0)
            {
                score += 90;
            }
            else if (names.Count > 0 && otherMatches.Count > 0)
            {
                score += 60;
            }

            if (ContainsText(weapon.AbilityText, battle.Element))
            {
                score += 25;
            }

            var highlights = names
                .Concat(preferredMatches)
                .Concat(hpMatches)
                .Concat(otherMatches)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList();

            var reasons = names.Select(n => $"R Ability: {n}").ToList();
            reasons.AddRange(preferredMatches.Select(n => $"R Ability: {n}"));
            reasons.AddRange(hpMatches.Select(n => $"R Ability: {n}"));
            reasons.AddRange(otherMatches.Select(n => $"R Ability: {n}"));

            return new RecommendationCandidate(
                weapon,
                score,
                names.Count > 0 && offTypeOffensiveMatches.Count == 0,
                reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                highlights);
        }

        private RecommendationCandidate BuildSubWeaponCandidate(WeaponSearchItem weapon, GuildBattleSheetBattleDefinition battle)
        {
            var matched = new List<string>();
            var score = 0d;
            var isPhysical = battle.AbilityType.Equals("Physical", StringComparison.OrdinalIgnoreCase);

            foreach (var name in EnumerateRAbilityNames(weapon))
            {
                if (isPhysical)
                {
                    if (ContainsText(name, "Boost PATK"))
                    {
                        matched.Add(name);
                        score += 120;
                    }
                    else if (ContainsText(name, "Boost Phys. Ability Pot") || ContainsText(name, "Boost Ability Pot"))
                    {
                        matched.Add(name);
                        score += 110;
                    }
                }
                else
                {
                    if (ContainsText(name, "Boost MATK"))
                    {
                        matched.Add(name);
                        score += 120;
                    }
                    else if (ContainsText(name, "Boost Mag. Ability Pot") || ContainsText(name, "Boost Ability Pot"))
                    {
                        matched.Add(name);
                        score += 110;
                    }
                }
            }

            score += isPhysical ? Math.Min(weapon.PatkAtMaxLevel / 10.0, 80) : Math.Min(weapon.MatkAtMaxLevel / 10.0, 80);

            return new RecommendationCandidate(
                weapon,
                score,
                matched.Count > 0,
                matched.Select(m => $"R Ability: {m}").ToList(),
                matched.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }

        private RecommendationCandidate BuildEffectTagCandidate(WeaponSearchItem weapon, string tag)
        {
            var matched = EnumerateWeaponSearchTexts(weapon)
                .Where(t => ContainsText(t, tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var displayLabels = EnumerateWeaponDisplayLabels(weapon)
                .Where(t => ContainsText(t, tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var score = matched.Count * 100 + weapon.DamagePercent;
            return new RecommendationCandidate(
                weapon,
                score,
                matched.Count > 0,
                matched.Select(m => $"Effect: {m}").ToList(),
                displayLabels);
        }

        private RecommendationCandidate BuildAnyEffectTagCandidate(WeaponSearchItem weapon, params string[] tags)
        {
            var matched = EnumerateWeaponSearchTexts(weapon)
                .Where(t => tags.Any(tag => ContainsText(t, tag)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var displayLabels = EnumerateWeaponDisplayLabels(weapon)
                .Where(t => tags.Any(tag => ContainsText(t, tag)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var score = matched.Count * 100 + weapon.DamagePercent;
            return new RecommendationCandidate(
                weapon,
                score,
                matched.Count > 0,
                matched.Select(m => $"Effect: {m}").ToList(),
                displayLabels);
        }

        private RecommendationCandidate BuildAnyDisplayTagCandidate(WeaponSearchItem weapon, params string[] tags)
        {
            var matched = EnumerateWeaponDisplayLabels(weapon)
                .Where(t => tags.Any(tag => ContainsText(t, tag)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var score = matched.Count * 100 + weapon.DamagePercent;
            return new RecommendationCandidate(
                weapon,
                score,
                matched.Count > 0,
                matched.Select(m => $"Effect: {m}").ToList(),
                matched);
        }

        private GuildBattleSheetEntry BuildEntry(
            WeaponSearchItem weapon,
            double score,
            List<string> reasons,
            List<string> highlights,
            bool isTopPick)
        {
            var condensedReasons = reasons
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var condensedHighlights = highlights
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList();
            if (condensedHighlights.Count == 0 && weapon.EffectTags.Count > 0)
            {
                condensedHighlights = weapon.EffectTags.Take(3).ToList();
            }

            var relevantCustomizationDetails = GetRelevantCustomizationDetails(weapon, condensedHighlights, condensedReasons);
            var inclusionSummary = BuildInclusionSummary(condensedReasons, relevantCustomizationDetails, score);

            return new GuildBattleSheetEntry
            {
                WeaponId = weapon.Id,
                Name = weapon.Name,
                Character = weapon.Character,
                ImageUrl = weapon.ImageUrl,
                PreviewImageUrl = weapon.PreviewImageUrl,
                Element = weapon.Element,
                AbilityType = weapon.AbilityType,
                AbilityText = weapon.AbilityText,
                EquipmentType = weapon.EquipmentType,
                Highlights = condensedHighlights,
                MatchReasons = condensedReasons,
                InclusionSummary = inclusionSummary,
                RelevantCustomizationDetails = relevantCustomizationDetails,
                EffectTags = weapon.EffectTags.Take(8).ToList(),
                RAbilityNames = EnumerateRAbilityNames(weapon).Take(8).ToList(),
                RAbilityDetails = EnumerateRAbilityDetails(weapon).Take(8).ToList(),
                IsTopPick = isTopPick,
                HasCustomizations = weapon.Customizations.Count > 0,
                DamagePercent = weapon.DamagePercent,
                Score = score,
                PatkAtMaxLevel = weapon.PatkAtMaxLevel,
                MatkAtMaxLevel = weapon.MatkAtMaxLevel,
                HealAtMaxLevel = weapon.HealAtMaxLevel
            };
        }

        private static string BuildInclusionSummary(
            IReadOnlyList<string> reasons,
            IReadOnlyList<string> relevantCustomizationDetails,
            double score)
        {
            var summaryParts = reasons
                .Where(reason => !string.IsNullOrWhiteSpace(reason))
                .Take(2)
                .ToList();

            if (relevantCustomizationDetails.Count > 0)
            {
                summaryParts.Add($"Relevant customization: {relevantCustomizationDetails[0]}");
            }

            if (summaryParts.Count == 0)
            {
                return "Included because it cleared the current featured recommendation scoring.";
            }

            return $"Included because: {string.Join(" • ", summaryParts)}";
        }

        private static List<string> GetRelevantCustomizationDetails(
            WeaponSearchItem weapon,
            IReadOnlyCollection<string> highlights,
            IReadOnlyCollection<string> reasons)
        {
            if (weapon.Customizations.Count == 0)
            {
                return new List<string>();
            }

            var signals = highlights
                .Concat(reasons)
                .Concat(weapon.EffectTags)
                .Where(signal => !string.IsNullOrWhiteSpace(signal))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var relevant = weapon.Customizations
                .Where(customization => !string.IsNullOrWhiteSpace(customization.Description))
                .Where(customization =>
                    signals.Any(signal => ContainsText(customization.Description, signal))
                    || customization.PassiveEffects.Any(effect => signals.Any(signal => ContainsText(effect.Label, signal) || ContainsText(effect.Description, signal))))
                .Select(customization => customization.Description.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();

            return relevant;
        }

        private static List<string> SplitWriteup(string writeup)
        {
            return writeup
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim().TrimStart('•', '-', '*').Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
        }

        private static List<string> BuildAutoWriteup(
            GuildBattleSheetBattleDefinition battle,
            IReadOnlyList<GuildBattleSheetEntry> mainRecommendations,
            GuildBattleSheetSection potencySection,
            GuildBattleSheetSection subWeaponSection,
            GuildBattleSheetSection resistanceDownSection,
            GuildBattleSheetSection damageUpSection,
            GuildBattleSheetSection damageBonusSection)
        {
            var lines = new List<string>();
            var bestDps = mainRecommendations.FirstOrDefault(e => string.Equals(e.Element, battle.Element, StringComparison.OrdinalIgnoreCase));
            if (bestDps != null)
            {
                lines.Add($"Featured headline pick: {bestDps.Name} ({bestDps.Character}) is one of the strongest {battle.AbilityType.ToLowerInvariant()} {battle.Element.ToLowerInvariant()} options in the current featured pool.");
            }

            var supportNames = new[]
            {
                resistanceDownSection.Entries.FirstOrDefault()?.Name,
                damageUpSection.Entries.FirstOrDefault()?.Name,
                damageBonusSection.Entries.FirstOrDefault()?.Name
            }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
            if (supportNames.Count > 0)
            {
                lines.Add($"Featured support standouts include {string.Join(", ", supportNames)} for elemental setup and damage amplification.");
            }

            var subNames = subWeaponSection.Entries.Take(3).Select(e => $"{e.Name} ({e.Character})").ToList();
            if (subNames.Count > 0)
            {
                lines.Add($"Sub-weapon priorities for this month lean toward {string.Join(", ", subNames)}.");
            }

            var potencyNames = potencySection.Entries.Take(3).Select(e => e.Name).ToList();
            if (potencyNames.Count > 0)
            {
                lines.Add($"For extra {battle.Element.ToLowerInvariant()} scaling, look at featured potency weapons like {string.Join(", ", potencyNames)}.");
            }

            return lines.Take(4).ToList();
        }

        private static IEnumerable<string> EnumerateRAbilityNames(WeaponSearchItem weapon)
        {
            return weapon.MaxPassiveSkills
                .Select(p => p.SkillName)
                .Concat(weapon.Customizations
                    .Where(c => c.Kind == "R Ability")
                    .Select(c => !string.IsNullOrWhiteSpace(c.PassiveSkillName) ? c.PassiveSkillName! : c.Description))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> EnumerateRAbilityDetails(WeaponSearchItem weapon)
        {
            return weapon.MaxPassiveSkills
                .Where(passive => passive.TotalPoints > 0 && !string.IsNullOrWhiteSpace(passive.SkillName))
                .Select(passive => $"{passive.SkillName} +{passive.TotalPoints} pts")
                .Concat(weapon.Customizations
                    .Where(c => c.Kind == "R Ability")
                    .Where(c => c.PassiveSkillPoints > 0)
                    .Select(c => $"{(!string.IsNullOrWhiteSpace(c.PassiveSkillName) ? c.PassiveSkillName! : c.Description)} +{c.PassiveSkillPoints} pts"))
                .Where(detail => !string.IsNullOrWhiteSpace(detail))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> EnumerateWeaponSearchTexts(WeaponSearchItem weapon)
        {
            return EnumerateWeaponDisplayLabels(weapon)
                .Concat(weapon.MaxPassiveSkills.SelectMany(p => p.Effects.Select(e => e.Description)))
                .Concat(weapon.Customizations.Select(c => c.Description))
                .Concat(weapon.Customizations.SelectMany(c => c.PassiveEffects.Select(e => e.Description)))
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> EnumerateWeaponDisplayLabels(WeaponSearchItem weapon)
        {
            return weapon.EffectTags
                .Concat(weapon.MaxPassiveSkills.Select(p => p.SkillName))
                .Concat(weapon.MaxPassiveSkills.SelectMany(p => p.Effects.Select(e => e.Label)))
                .Concat(weapon.Customizations
                    .Where(c => !string.IsNullOrWhiteSpace(c.PassiveSkillName))
                    .Select(c => c.PassiveSkillName!))
                .Concat(weapon.Customizations.SelectMany(c => c.PassiveEffects.Select(e => e.Label)))
                .Concat(weapon.SubRAbilityTags.Select(t => t.Label))
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static bool HasRelevantBattleRAbility(WeaponSearchItem weapon, GuildBattleSheetBattleDefinition battle)
        {
            var isPhysical = battle.AbilityType.Equals("Physical", StringComparison.OrdinalIgnoreCase);

            return EnumerateRAbilityNames(weapon).Any(name =>
                ContainsText(name, $"Boost {battle.Element} Pot")
                || ContainsText(name, "Boost Ability Pot")
                || (isPhysical && (ContainsText(name, "Boost PATK") || ContainsText(name, "Boost Phys. Ability Pot")))
                || (!isPhysical && (ContainsText(name, "Boost MATK") || ContainsText(name, "Boost Mag. Ability Pot"))));
        }

        private static string GetElementArcanumLabel(string element)
        {
            return element.Trim().ToLowerInvariant() switch
            {
                "fire" => "Flameblade Arcanum",
                "ice" => "Frostblade Arcanum",
                "lightning" => "Levinblade Arcanum",
                "earth" => "Earthblade Arcanum",
                "water" => "Waterblade Arcanum",
                "wind" => "Windstrike Arcanum",
                _ => $"{element} Arcanum"
            };
        }

        private static string GetElementMasteryLabel(string element)
        {
            return element.Trim().ToLowerInvariant() switch
            {
                "fire" => "Fire Mastery",
                "ice" => "Ice Mastery",
                "lightning" => "Lightning Mastery",
                "earth" => "Earth Mastery",
                "water" => "Water Mastery",
                "wind" => "Wind Mastery",
                _ => $"{element} Mastery"
            };
        }

        private static double GetMainRecommendationDamageContribution(
            WeaponSearchItem weapon,
            bool matchesElement,
            bool matchesAbilityType,
            bool hasSupportiveWeaponEffect,
            bool hasPartyFacingSupport,
            bool hasBossDebuffSetup,
            bool hasSelfOnlyDpsPrep)
        {
            if (weapon.DamagePercent <= 0)
            {
                return 0;
            }

            if (matchesElement && matchesAbilityType)
            {
                return weapon.DamagePercent;
            }

            if (hasPartyFacingSupport || hasBossDebuffSetup || hasSupportiveWeaponEffect)
            {
                return Math.Min(weapon.DamagePercent, 500) * 0.35;
            }

            if (matchesAbilityType && hasSelfOnlyDpsPrep)
            {
                return Math.Min(weapon.DamagePercent, 450) * 0.2;
            }

            if (matchesAbilityType)
            {
                return Math.Min(weapon.DamagePercent, 350) * 0.15;
            }

            return Math.Min(weapon.DamagePercent, 250) * 0.1;
        }

        private static int GetPartySupportTargetBonus(WeaponSearchItem weapon)
        {
            if (HasAnyTargetMarker(weapon, "All Allies"))
            {
                return 50;
            }

            if (HasAnyTargetMarker(weapon, "All Enemies + Allies"))
            {
                return 45;
            }

            if (HasAnyTargetMarker(weapon, "Other Allies"))
            {
                return 35;
            }

            if (HasAnyTargetMarker(weapon, "Single Ally"))
            {
                return 20;
            }

            return 0;
        }

        private static int GetBossSetupTargetBonus(WeaponSearchItem weapon)
        {
            if (HasAnyTargetMarker(weapon, "All Enemies") || HasAnyTargetMarker(weapon, "All Enemies + Allies"))
            {
                return 35;
            }

            if (HasAnyTargetMarker(weapon, "Single Enemy"))
            {
                return 20;
            }

            return 0;
        }

        private static bool HasAnyTargetMarker(WeaponSearchItem weapon, string targetLabel)
        {
            return ContainsText(weapon.AbilityText, $"[Rng.: {targetLabel}]")
                || ContainsText(weapon.MaxAbilityDescription, $"[Rng.: {targetLabel}]")
                || string.Equals(weapon.Range?.Trim(), targetLabel, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidBattle(GuildBattleSheetBattleDefinition battle)
        {
            return !string.IsNullOrWhiteSpace(battle.Id)
                && !string.IsNullOrWhiteSpace(battle.Month)
                && battle.Year > 0
                && !string.IsNullOrWhiteSpace(battle.Element)
                && !string.IsNullOrWhiteSpace(battle.AbilityType);
        }

        private static int BuildSortKey(GuildBattleSheetBattleDefinition battle)
        {
            return battle.Year * 100 + ResolveMonthNumber(battle.Month);
        }

        private static int ResolveMonthNumber(string month)
        {
            if (DateTime.TryParseExact(month.Trim(), new[] { "MMMM", "MMM" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed.Month;
            }

            return 0;
        }

        private static bool MatchesBattleAbilityType(string weaponAbilityType, string battleAbilityType)
        {
            if (battleAbilityType.Equals("Physical", StringComparison.OrdinalIgnoreCase))
            {
                return ContainsText(weaponAbilityType, "Phys");
            }

            if (battleAbilityType.Equals("Magical", StringComparison.OrdinalIgnoreCase))
            {
                return ContainsText(weaponAbilityType, "Mag");
            }

            return false;
        }

        private static bool HasEffectTag(WeaponSearchItem weapon, string tag)
        {
            return weapon.EffectTags.Any(t => ContainsText(t, tag));
        }

        private static bool HasAnyEffectTag(WeaponSearchItem weapon, params string[] tags)
        {
            return tags.Any(tag => HasEffectTag(weapon, tag));
        }

        private static bool ContainsText(string? input, string value)
        {
            return !string.IsNullOrWhiteSpace(input)
                && input.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizeName(string value)
        {
            return string.Join(" ", value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        }

        private static int GetPrimaryStat(WeaponSearchItem weapon, string battleAbilityType)
        {
            return battleAbilityType.Equals("Physical", StringComparison.OrdinalIgnoreCase)
                ? weapon.PatkAtMaxLevel
                : weapon.MatkAtMaxLevel;
        }

        private static IOrderedEnumerable<RecommendationCandidate> OrderMainRecommendationCandidates(
            IEnumerable<RecommendationCandidate> candidates,
            GuildBattleSheetBattleDefinition battle)
        {
            return candidates
                .OrderByDescending(c => c.Score)
                .ThenByDescending(c => c.Weapon.DamagePercent)
                .ThenByDescending(c => GetPrimaryStat(c.Weapon, battle.AbilityType))
                .ThenBy(c => c.Weapon.Name, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsReservedSupportMainRecommendationCandidate(
            WeaponSearchItem weapon,
            GuildBattleSheetBattleDefinition battle,
            IReadOnlyCollection<string> activeConditionalMechanics)
        {
            var characterRole = CharacterRoleRegistry.GetRoleOrDefault(weapon.Character);
            if (characterRole == CharacterRole.DPS)
            {
                return false;
            }

            var matchesElement = string.Equals(weapon.Element, battle.Element, StringComparison.OrdinalIgnoreCase);
            var matchesAbilityType = MatchesBattleAbilityType(weapon.AbilityType, battle.AbilityType);
            var hasSelfOnlySupportEffect = IsSelfOnlySupportEffect(weapon);
            var hasConditionalMechanicUtility = GetMatchedConditionalMechanics(weapon, activeConditionalMechanics).Count > 0;
            if (matchesElement && matchesAbilityType)
            {
                return false;
            }

            return HasCharacterModeSupportValue(weapon, battle, activeConditionalMechanics, allowSelfOnlySupportEffects: false)
                || hasConditionalMechanicUtility
                || (hasSelfOnlySupportEffect && false);
        }

        private static bool IsBattleFitMainWeapon(WeaponSearchItem weapon, GuildBattleSheetBattleDefinition battle)
        {
            return string.Equals(weapon.Element, battle.Element, StringComparison.OrdinalIgnoreCase)
                && MatchesBattleAbilityType(weapon.AbilityType, battle.AbilityType);
        }

        private static bool HasCharacterModeDpsCompanionValue(
            WeaponSearchItem weapon,
            GuildBattleSheetBattleDefinition battle,
            IReadOnlyCollection<string> activeConditionalMechanics)
        {
            if (!MatchesBattleAbilityType(weapon.AbilityType, battle.AbilityType))
            {
                return false;
            }

            return HasCharacterModeSupportValue(weapon, battle, activeConditionalMechanics, allowSelfOnlySupportEffects: true);
        }

        private static bool HasCharacterModeSupportValue(
            WeaponSearchItem weapon,
            GuildBattleSheetBattleDefinition battle,
            IReadOnlyCollection<string> activeConditionalMechanics,
            bool allowSelfOnlySupportEffects)
        {
            var hasSelfOnlySupportEffect = IsSelfOnlySupportEffect(weapon);
            var canUseSelfOnlySupportEffects = allowSelfOnlySupportEffects || !hasSelfOnlySupportEffect;
            var isPhysicalBattle = battle.AbilityType.Equals("Physical", StringComparison.OrdinalIgnoreCase);
            var battleBuffTag = isPhysicalBattle ? "PATK Up" : "MATK Up";
            var battleBreakTag = isPhysicalBattle ? "PDEF Down" : "MDEF Down";
            var battleDamageBonusTag = isPhysicalBattle ? "Phys. Damage Bonus" : "Mag. Damage Bonus";
            var battleDamageReceivedUpTag = isPhysicalBattle ? "Phys. Dmg. Rcvd. Up" : "Mag. Dmg. Rcvd. Up";
            var battleAllTargetDamageReceivedUpTag = isPhysicalBattle ? "All-Tgt. Phys. Dmg. Rcvd. Up" : "All-Tgt. Mag. Dmg. Rcvd. Up";
            var battleStatusDamageReceivedUpTag = isPhysicalBattle ? "Status Ailment: All-Tgt. Phys. Dmg. Rcvd. Up" : "Status Ailment: All-Tgt. Mag. Dmg. Rcvd. Up";
            var battleAtbConservationTag = isPhysicalBattle ? "Phys. ATB Conservation Effect" : "Mag. ATB Conservation Effect";
            var hasConditionalMechanicUtility = GetMatchedConditionalMechanics(weapon, activeConditionalMechanics).Count > 0;
            var hasFightFacingExploitWeakness = HasEffectTag(weapon, "Exploit Weakness") && canUseSelfOnlySupportEffects;
            var hasFightFacingElementDamageUp = HasEffectTag(weapon, $"{battle.Element} Damage Up") && canUseSelfOnlySupportEffects;
            var hasFightFacingElementDamageBonus = HasEffectTag(weapon, $"{battle.Element} Damage Bonus") && canUseSelfOnlySupportEffects;
            var hasFightFacingElementWeaponBoost = HasEffectTag(weapon, $"{battle.Element} Weapon Boost") && canUseSelfOnlySupportEffects;

            var hasElementUtility = HasEffectTag(weapon, $"{battle.Element} Resistance Down")
                || hasFightFacingElementDamageUp
                || hasFightFacingElementDamageBonus
                || hasFightFacingElementWeaponBoost;
            var hasGeneralBattleSupportWeaponEffect = hasFightFacingExploitWeakness
                || (HasEffectTag(weapon, battleBuffTag) && canUseSelfOnlySupportEffects)
                || HasEffectTag(weapon, battleBreakTag)
                || HasEffectTag(weapon, battleDamageBonusTag)
                || HasAnyEffectTag(weapon, battleDamageReceivedUpTag, battleAllTargetDamageReceivedUpTag, battleStatusDamageReceivedUpTag)
                || HasAnyEffectTag(weapon, "Enfeeble", "Status Ailment: Enfeeble")
                || (HasEffectTag(weapon, "Haste") && canUseSelfOnlySupportEffects)
                || HasEffectTag(weapon, "Applied Stats Debuff Tier Increased")
                || (HasEffectTag(weapon, "Applied Stats Buff Tier Increased") && canUseSelfOnlySupportEffects)
                || (HasEffectTag(weapon, "Enliven") && canUseSelfOnlySupportEffects)
                || HasEffectTag(weapon, battleAtbConservationTag)
                || ContainsText(weapon.AbilityText, "command gauge")
                || hasConditionalMechanicUtility;

            return hasElementUtility || hasGeneralBattleSupportWeaponEffect;
        }

        private static List<string> GetMatchedConditionalMechanics(
            WeaponSearchItem weapon,
            IReadOnlyCollection<string> activeConditionalMechanics)
        {
            if (activeConditionalMechanics.Count == 0)
            {
                return new List<string>();
            }

            var searchTexts = EnumerateWeaponSearchTexts(weapon)
                .Concat(new[]
                {
                    weapon.AbilityText,
                    weapon.MaxAbilityDescription,
                    weapon.Range
                })
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return activeConditionalMechanics
                .Where(mechanic => searchTexts.Any(text => ContainsText(text, mechanic)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
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

        private sealed record CharacterRecommendationCandidate(
            WeaponSearchItem Weapon,
            double Score,
            List<string> Reasons,
            List<string> Highlights,
            bool IsTopPick);

        private sealed record RecommendationCandidate(
            WeaponSearchItem Weapon,
            double Score,
            bool ShouldInclude,
            List<string> Reasons,
            List<string> Highlights);
    }
}
