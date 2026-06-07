using System;
using System.Collections.Generic;
using System.Linq;
using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    // Item 3 — the estimated-damage scoring core (design: docs/item3-damage-model-design.md).
    // Damage is a PRODUCT of (1 + family%) terms (the real Shira_Damage_Calc formula): percentages ADD
    // within a family/term and MULTIPLY across families. This file holds the foundational pieces — the real
    // active tier→% tables and the multiplicative product — that the team estimator is built on. Built
    // alongside the existing weighted-sum scorer; not yet wired into team selection.
    public sealed partial class PlayerPowerAnalyzerV2Service
    {
        // Active buff/debuff tier → real % (as a fraction), from Shira_Damage_Calc.xlsx "Data Table".
        // Index = potency tier rank: 1=Low, 2=Mid, 3=High, 4=Extra High, 5=Extreme High, 6=+(amped), 7=++.
        // The +/++ rows are the Enliven/amplifier tiers (an amplifier moves a buff +N rows up its table).
        private static readonly double[] OffensiveBuffTierPercents = { 0, 0.10, 0.20, 0.30, 0.40, 0.50, 0.60, 0.70 };
        private static readonly double[] ElementalDamageUpTierPercents = { 0, 0.10, 0.25, 0.40, 0.60, 0.80, 1.00, 1.20 };
        private static readonly double[] DefenseDebuffTierPercents = { 0, 0.15, 0.25, 0.35, 0.45, 0.55 };
        private static readonly double[] ElementalResistDebuffTierPercents = { 0, 0.15, 0.30, 0.50, 0.75, 1.00 };

        // The real % (fraction) an active effect contributes to its family's (1+Σ%) term. Tier-based stat
        // buffs/debuffs use the tier tables above; everything else (damage bonus, weapon boost, exploit
        // weakness, amplification, damage-received-up, …) carries an explicit % in the ability text.
        private static double GetActiveEffectRealPercent(DetectedActiveEffect effect)
        {
            return ResolveActiveRealPercent(effect.Key, effect.PotencyTierRank ?? 0, effect.PotencyPercent);
        }

        // Split out from GetActiveEffectRealPercent so it can be exercised with primitive args in tests.
        private static double ResolveActiveRealPercent(string key, int tierRank, double? explicitPercent)
        {
            switch (key)
            {
                case "patk_up":
                case "matk_up":
                    return LookupTierPercent(OffensiveBuffTierPercents, tierRank, explicitPercent);
                case "elemental_damage_up":
                    return LookupTierPercent(ElementalDamageUpTierPercents, tierRank, explicitPercent);
                case "pdef_down":
                case "mdef_down":
                    return LookupTierPercent(DefenseDebuffTierPercents, tierRank, explicitPercent);
                case "elemental_resistance_down":
                    return LookupTierPercent(ElementalResistDebuffTierPercents, tierRank, explicitPercent);
                default:
                    // Effects that state an explicit % (e.g. "[Pot: 30%]", "[Damage +40%…]").
                    return explicitPercent.HasValue ? System.Math.Max(0, explicitPercent.Value) / 100.0 : 0.0;
            }
        }

        // Map a tier rank to its table %, clamping out-of-range ranks. If a tier-based effect happens to
        // also carry an explicit %, prefer the explicit value (some sources state both).
        private static double LookupTierPercent(IReadOnlyList<double> table, int tierRank, double? explicitPercent)
        {
            if (explicitPercent.HasValue && explicitPercent.Value > 0)
            {
                return explicitPercent.Value / 100.0;
            }

            if (tierRank <= 0 || table.Count <= 1)
            {
                return 0.0;
            }

            return tierRank < table.Count ? table[tierRank] : table[table.Count - 1];
        }

        // Amplifiable, tier-based families and the table each one moves up. Enliven / Applied Stats Buff
        // Tier Increase lift ALLY buffs (attack_buff, damage_up); Applied Stats Debuff Tier Increase lifts
        // ENEMY debuffs (defense_debuff, elemental_resistance_debuff). Weapon boost / damage bonus / amp are
        // NOT amplified by these. An amplifier raises the relevant family ~2 tiers up its table.
        private static readonly Dictionary<string, double[]> AmplifiableFamilyTables = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["attack_buff"] = OffensiveBuffTierPercents,
            ["damage_up"] = ElementalDamageUpTierPercents,
            ["defense_debuff"] = DefenseDebuffTierPercents,
            ["elemental_resistance_debuff"] = ElementalResistDebuffTierPercents,
        };

        private const int AmplifierTierBump = 2;

        // Given each amplifiable family's best BASE tier present on the team, return its effective % after
        // amplifiers. A family is bumped +2 tiers (capped at its table max) only when the matching amplifier
        // is on the team AND the family actually has a base tier — amplifiers are worth 0 with nothing to
        // amplify (Applied Stats Debuff Tier Increased does nothing with no enemy debuffs, etc.).
        private static Dictionary<string, double> ResolveAmplifiedFamilyPercents(
            IReadOnlyDictionary<string, int> familyBestTiers,
            bool hasBuffAmplifier,
            bool hasDebuffAmplifier)
        {
            var result = new Dictionary<string, double>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var pair in familyBestTiers)
            {
                var family = pair.Key;
                var baseTier = pair.Value;
                if (baseTier <= 0 || !AmplifiableFamilyTables.TryGetValue(family, out var table))
                {
                    continue;
                }

                var isAllyBuff = family.Equals("attack_buff", System.StringComparison.OrdinalIgnoreCase)
                    || family.Equals("damage_up", System.StringComparison.OrdinalIgnoreCase);
                var amplify = isAllyBuff ? hasBuffAmplifier : hasDebuffAmplifier;
                var tier = amplify ? baseTier + AmplifierTierBump : baseTier;
                if (tier > table.Length - 1)
                {
                    tier = table.Length - 1;
                }

                result[family] = table[tier];
            }

            return result;
        }

        // The offensive families that are damage-contributing (1+%) terms in the formula. Excludes tempo
        // (ATB), healing, defensive buffs (PDEF/MDEF Up), enemy-attack debuffs (PATK/MATK Down), and the
        // amplifier families (which are modifiers applied via ResolveAmplifiedFamilyPercents, not terms).
        private static readonly HashSet<string> DamageContributingFamilies = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "attack_buff", "damage_up", "defense_debuff", "elemental_resistance_debuff",
            "exploit_weakness", "damage_bonus", "weapon_boost", "ability_amplification",
            "damage_received_up", "torpor"
        };

        private static bool HasBuffAmplifier(IEnumerable<DetectedActiveEffect> effects)
            => effects.Any(e => e.Key is "enliven" or "stat_buff_tier_increase");

        private static bool HasDebuffAmplifier(IEnumerable<DetectedActiveEffect> effects)
            => effects.Any(e => e.Key is "enfeeble" or "stat_debuff_tier_increase");

        // Aggregate a pool of detected active effects into the damage formula's family→% terms.
        // Within a family, different effect KEYS add but duplicates of the SAME key share a cap (we take the
        // best % per key, then sum across keys — matching the formula's (1+WeaponCCBuff+ElementalWeaponCCBuff)
        // grouping). Amplifiable, tier-based families are resolved via their tier (so an amplifier can lift
        // them) rather than summed. Off-axis effects (MATK in a physical build, etc.) are dropped.
        private static Dictionary<string, double> BuildActiveFamilyState(
            IEnumerable<DetectedActiveEffect> effects,
            DamageType damageType)
        {
            var materialized = effects as IReadOnlyList<DetectedActiveEffect> ?? effects.ToList();
            var additiveByFamily = new Dictionary<string, Dictionary<string, double>>(System.StringComparer.OrdinalIgnoreCase);
            var amplifiableBestTiers = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var effect in materialized)
            {
                var family = string.IsNullOrWhiteSpace(effect.FamilyKey) ? effect.Key : effect.FamilyKey;
                if (!DamageContributingFamilies.Contains(family))
                {
                    continue;
                }

                // On-axis filter: a magical buff does nothing for a physical build and vice versa.
                if (damageType == DamageType.Physical && effect.AxisKey == "magical")
                {
                    continue;
                }

                if (damageType == DamageType.Magical && effect.AxisKey == "physical")
                {
                    continue;
                }

                if (AmplifiableFamilyTables.ContainsKey(family))
                {
                    var tier = effect.PotencyTierRank ?? 0;
                    if (tier > 0 && (!amplifiableBestTiers.TryGetValue(family, out var existingTier) || tier > existingTier))
                    {
                        amplifiableBestTiers[family] = tier;
                    }
                }
                else
                {
                    var pct = GetActiveEffectRealPercent(effect);
                    if (pct <= 0)
                    {
                        continue;
                    }

                    if (!additiveByFamily.TryGetValue(family, out var byKey))
                    {
                        byKey = new Dictionary<string, double>(System.StringComparer.OrdinalIgnoreCase);
                        additiveByFamily[family] = byKey;
                    }

                    if (!byKey.TryGetValue(effect.Key, out var existing) || pct > existing)
                    {
                        byKey[effect.Key] = pct; // best per key (same buff shares a cap)
                    }
                }
            }

            var state = new Dictionary<string, double>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var pair in additiveByFamily)
            {
                state[pair.Key] = pair.Value.Values.Sum(); // different keys in a family add
            }

            var amplified = ResolveAmplifiedFamilyPercents(amplifiableBestTiers, HasBuffAmplifier(materialized), HasDebuffAmplifier(materialized));
            foreach (var pair in amplified)
            {
                state[pair.Key] = pair.Value;
            }

            return state;
        }

        // The multiplicative core: damage multiplier = ∏ over families (1 + that family's summed %). This is
        // where cross-family coverage compounds and within-family stacking does not — e.g. two different
        // families at +30% → 1.69, versus one family at +60% → 1.60.
        private static double EstimateDamageMultiplierProduct(IReadOnlyDictionary<string, double> familyPercents)
        {
            var product = 1.0;
            foreach (var familyPercent in familyPercents.Values)
            {
                product *= 1.0 + familyPercent;
            }

            return product;
        }

        // Passive R-abilities feed two formula terms (distinct from the active families above):
        //   passive_ability_potency — Boost Ability Pot / Phys|Mag Ability Pot / element potency (these ADD
        //     into one (1+Σ) block); passive_stat_boost — Boost ATK/PATK/MATK (raises the ATK/MATK stat).
        private const string AbilityPotencyTerm = "passive_ability_potency";
        private const string StatBoostTerm = "passive_stat_boost";

        // Classify a single passive R-ability into its damage term, returning the real % as a fraction.
        // On-axis / on-element gated: off-type or off-element passives contribute nothing.
        private static bool TryGetPassiveDamageTerm(string skillName, int points, DamageType damageType, Element enemyWeakness, out string term, out double percentFraction)
        {
            term = string.Empty;
            percentFraction = 0;
            if (points <= 0 || !TryGetPassiveBonusValue(skillName, points, out var bonusPercent) || bonusPercent <= 0)
            {
                return false;
            }

            var n = skillName.ToLowerInvariant();
            var pct = bonusPercent / 100.0;

            // Generic element-pot arcanum (All Allies) adapts to whatever element you run — always on.
            if (IsGenericElementPotArcanumAllAlliesPassiveLabel(n))
            {
                term = AbilityPotencyTerm; percentFraction = pct; return true;
            }

            // Element-specific potency/ability — on-element only.
            if (IsElementOffensivePassiveLabel(n) || IsElementAbilityAllAlliesPassiveLabel(n))
            {
                var onElement = enemyWeakness != Element.None
                    && n.Contains(enemyWeakness.ToString().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
                if (!onElement) { return false; }
                term = AbilityPotencyTerm; percentFraction = pct; return true;
            }

            // Phys / Mag ability potency — on-axis only.
            if (IsPhysAbilityPassiveLabel(n))
            {
                if (damageType == DamageType.Magical) { return false; }
                term = AbilityPotencyTerm; percentFraction = pct; return true;
            }

            if (IsMagAbilityPassiveLabel(n))
            {
                if (damageType == DamageType.Physical) { return false; }
                term = AbilityPotencyTerm; percentFraction = pct; return true;
            }

            // Generic ability potency (Boost Ability Pot) — helps either damage type.
            if (n.Contains("boost ability pot", StringComparison.OrdinalIgnoreCase) || IsGenericAbilityPassiveLabel(n))
            {
                term = AbilityPotencyTerm; percentFraction = pct; return true;
            }

            // Stat boosts (raise ATK/MATK): Boost MATK (magical), Boost PATK (physical), Boost ATK (both).
            if (n.Contains("boost matk", StringComparison.OrdinalIgnoreCase))
            {
                if (damageType == DamageType.Physical) { return false; }
                term = StatBoostTerm; percentFraction = pct; return true;
            }

            if (n.Contains("boost patk", StringComparison.OrdinalIgnoreCase))
            {
                if (damageType == DamageType.Magical) { return false; }
                term = StatBoostTerm; percentFraction = pct; return true;
            }

            if (n.Contains("boost atk", StringComparison.OrdinalIgnoreCase))
            {
                term = StatBoostTerm; percentFraction = pct; return true;
            }

            return false;
        }

        // Sum a build/team's passive points into the two passive damage terms (real %s, on-axis/on-element).
        private static Dictionary<string, double> BuildPassiveFamilyTerms(IReadOnlyDictionary<string, int> passivePoints, DamageType damageType, Element enemyWeakness)
        {
            var terms = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in passivePoints)
            {
                if (TryGetPassiveDamageTerm(pair.Key, pair.Value, damageType, enemyWeakness, out var term, out var pct))
                {
                    terms[term] = terms.TryGetValue(term, out var existing) ? existing + pct : pct;
                }
            }

            return terms;
        }

        // [D1] Cast-share weighting: a real fight funnels casts to the carry. Attackers sorted by weapon %
        // descending get these shares (carry 60% / secondary 30% / support 10%); extra attackers add 0.
        private static readonly double[] CarryCastShares = { 0.6, 0.3, 0.1 };

        // Phase-1b estimated team damage: the team's shared active buff/debuff multiplier applied to the
        // cast-share-weighted sum of attackers' weapon percentages. (Passive ability-potency / stat terms and
        // uptime fidelity are layered on in later increments; this is the offensive core for ranking builds.)
        private static double EstimateTeamDamage(
            IReadOnlyList<double> attackerWeaponPercents,
            IEnumerable<DetectedActiveEffect> teamEffects,
            IReadOnlyDictionary<string, double> passiveTerms,
            DamageType damageType)
        {
            var state = BuildActiveFamilyState(teamEffects, damageType);
            if (passiveTerms != null)
            {
                foreach (var pair in passiveTerms)
                {
                    if (pair.Value > 0)
                    {
                        // Distinct term keys (passive_*) — never collide with the active families.
                        state[pair.Key] = state.TryGetValue(pair.Key, out var existing) ? existing + pair.Value : pair.Value;
                    }
                }
            }

            var multiplier = EstimateDamageMultiplierProduct(state);

            var ordered = attackerWeaponPercents.OrderByDescending(percent => percent).ToList();
            var carryWeightedWeapon = 0.0;
            for (var index = 0; index < ordered.Count; index++)
            {
                var share = index < CarryCastShares.Length ? CarryCastShares[index] : 0.0;
                carryWeightedWeapon += ordered[index] * share;
            }

            return carryWeightedWeapon * multiplier;
        }

        // A character's attacking weapon % (best of main / off-hand / ultimate damage abilities).
        private static double GetVariantWeaponDamagePercent(CharacterBuildCandidate variant)
        {
            var main = variant.MainWeapon?.DamagePercent ?? 0d;
            var off = variant.OffHandWeapon?.DamagePercent ?? 0d;
            var ultimate = variant.UltimateWeapon?.DamagePercent ?? 0d;
            return Math.Max(main, Math.Max(off, ultimate));
        }

        // Integration overload: estimate a team's damage straight from its candidate variants. Extracts each
        // attacker's weapon %, pools the team's detected active effects, and builds the carry's passive terms
        // = the carry's own R-abilities + every other member's team-wide (All-Allies) R-abilities (which
        // stack onto the carry). The carry is the highest-weapon-% character. (Uptime fidelity is layered in
        // next; this is the ranking signal that Phase 2 swaps into the offensive score.)
        private static double EstimateTeamDamage(IReadOnlyList<CharacterBuildCandidate> baseVariants, PlayerPowerAnalyzerV2Request request)
        {
            if (baseVariants.Count == 0)
            {
                return 0d;
            }

            var weaponPercents = baseVariants.Select(GetVariantWeaponDamagePercent).ToList();
            var teamEffects = baseVariants.SelectMany(variant => GetDetectedEffectsForVariant(variant, request)).ToList();

            var carry = baseVariants.OrderByDescending(GetVariantWeaponDamagePercent).First();
            var carryEffectivePassives = new Dictionary<string, int>(carry.PassivePoints, StringComparer.OrdinalIgnoreCase);
            foreach (var variant in baseVariants)
            {
                if (ReferenceEquals(variant, carry))
                {
                    continue;
                }

                foreach (var passive in variant.PassivePoints)
                {
                    if (IsTeamWidePassive(passive.Key))
                    {
                        carryEffectivePassives[passive.Key] = carryEffectivePassives.TryGetValue(passive.Key, out var existing)
                            ? existing + passive.Value
                            : passive.Value;
                    }
                }
            }

            var passiveTerms = BuildPassiveFamilyTerms(carryEffectivePassives, request.PreferredDamageType, request.EnemyWeakness);
            return EstimateTeamDamage(weaponPercents, teamEffects, passiveTerms, request.PreferredDamageType);
        }
    }
}
