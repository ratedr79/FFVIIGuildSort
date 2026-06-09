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

        // True if an effect KEY contributes to DAMAGE on the requested axis. Used to filter the multi-team
        // "adds/drops vs best" rationale down to differences that actually matter for the requested damage type
        // — drops pure defensive/sustain effects (heal, PDEF/MDEF Up) and off-axis buffs (MATK Up in a physical
        // fight) so the explanation reads as real damage trade-offs, not noise. With DamageType.Any, axis is not
        // filtered (no requested axis).
        private static bool IsDamageRelevantEffectKey(string key, DamageType damageType)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (!DamageContributingFamilies.Contains(GetActiveEffectFamilyKey(key)))
            {
                return false;
            }

            var axis = GetActiveEffectAxisKey(key);
            if (damageType == DamageType.Physical && string.Equals(axis, "magical", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (damageType == DamageType.Magical && string.Equals(axis, "physical", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        // Off-axis buffs (MATK Up in a physical fight) add no on-axis damage but give a universal weapon a
        // small flexibility edge over a single-axis one (user decision). 10% of the buff's on-axis value.
        private const double OffAxisBuffFlexibilityFactor = 0.1;

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
            var offAxisByKey = new Dictionary<string, double>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var effect in materialized)
            {
                var family = string.IsNullOrWhiteSpace(effect.FamilyKey) ? effect.Key : effect.FamilyKey;
                if (!DamageContributingFamilies.Contains(family))
                {
                    continue;
                }

                // On-axis filter: a magical buff does nothing on the requested axis for a physical build and
                // vice versa, BUT it carries a small flexibility value (a universal PATK+MATK weapon edges out
                // a single-axis one). Off-axis effects accumulate into one small `off_axis_flexibility` term.
                if ((damageType == DamageType.Physical && effect.AxisKey == "magical")
                    || (damageType == DamageType.Magical && effect.AxisKey == "physical"))
                {
                    var offPct = GetActiveEffectRealPercent(effect);
                    if (offPct > 0 && (!offAxisByKey.TryGetValue(effect.Key, out var existingOff) || offPct > existingOff))
                    {
                        offAxisByKey[effect.Key] = offPct;
                    }

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

            // Small flexibility term: off-axis buffs give a universal weapon a slight edge over a single-axis one.
            var offAxisFlexibility = offAxisByKey.Values.Sum() * OffAxisBuffFlexibilityFactor;
            if (offAxisFlexibility > 0)
            {
                state["off_axis_flexibility"] = offAxisFlexibility;
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

        // Effective-damage % above which a weapon is treated as a real attacking weapon (so its holder's
        // main slot orients by damage, not R-abilities). Pure buff/heal weapons fall below this.
        private const double AttackingWeaponDamageThreshold = 500d;

        // [D2] Simple bucketed uptime: an active buff/debuff on an attacker's always-cast MAIN weapon is
        // auto-maintained every turn (full uptime); everything else is assumed maintainer-covered at this
        // fraction. (Start-only / non-reapplied fidelity is a later refinement.) Passives are flat (no uptime).
        private const double MaintainerUptime = 0.85;

        // Scale each ACTIVE family's % by its uptime: 1.0 if the family is carried on an always-cast main
        // weapon (auto-maintained), else maintainerUptime. With maintainerUptime = 1.0 this is a no-op.
        private static Dictionary<string, double> ApplyUptimeBuckets(
            IReadOnlyDictionary<string, double> activeFamilyState,
            ISet<string> fullUptimeFamilies,
            double maintainerUptime)
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in activeFamilyState)
            {
                var uptime = fullUptimeFamilies != null && fullUptimeFamilies.Contains(pair.Key) ? 1.0 : maintainerUptime;
                result[pair.Key] = pair.Value * uptime;
            }

            return result;
        }

        // True if a weapon's ability range hits all enemies (AOE), e.g. "All Enemies". An empty/unknown range
        // is treated as single-target (the common default) — we only DROP a single-enemy debuff for a known AOE
        // carry, so unknown ranges never lose coverage.
        private static bool IsAllEnemiesRange(string? range)
            => !string.IsNullOrWhiteSpace(range)
                && range.Contains("All", StringComparison.OrdinalIgnoreCase)
                && range.Contains("Enem", StringComparison.OrdinalIgnoreCase);

        // The active+passive damage MULTIPLIER for ONE effect set (no weapon weighting): family state → uptime →
        // defense-debuff denominator → merge passive terms → product. Reused PER-ATTACKER by the scope-aware
        // confinement (each attacker's own Self/Single-Ally buffs multiply only their OWN share).
        private static double EstimateDamageMultiplier(
            IEnumerable<DetectedActiveEffect> effects,
            IReadOnlyDictionary<string, double> passiveTerms,
            DamageType damageType,
            ISet<string> fullUptimeFamilies,
            double maintainerUptime)
        {
            var state = BuildActiveFamilyState(effects, damageType);

            // Uptime applies to ACTIVE families only; passives are flat and merged at full value afterward.
            state = ApplyUptimeBuckets(state, fullUptimeFamilies, maintainerUptime);

            // PDEF/MDEF Down reduce the enemy-defence DENOMINATOR (the formula's /EnemyDEF), so their true
            // damage effect is 1/(1-d), not 1+d — e.g. 35% → ×1.54, not ×1.35. Convert the defence-debuff term
            // to its equivalent additive value so the generic ∏(1+x) product matches the real formula. Without
            // this the model understates debuff coverage and lets a higher-raw-weapon team out-rank a broader
            // (debuff-heavy) one (the Kaiser/Cait-Trumpet coverage-breadth benchmarks).
            if (state.TryGetValue("defense_debuff", out var defenseDown) && defenseDown > 0 && defenseDown < 1)
            {
                state["defense_debuff"] = defenseDown / (1 - defenseDown);
            }

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

            return EstimateDamageMultiplierProduct(state);
        }

        // Estimated team damage (UNIFORM-multiplier form — used by the unit tests and the "Any"/no-variant path):
        // ONE multiplier applied to the cast-share-weighted sum of attackers' weapon %s. The SCOPE-AWARE per-
        // attacker form (which confines Self/Single-Ally buffs to their target) is in the baseVariants overload.
        private static double EstimateTeamDamage(
            IReadOnlyList<double> attackerWeaponPercents,
            IEnumerable<DetectedActiveEffect> teamEffects,
            IReadOnlyDictionary<string, double> passiveTerms,
            DamageType damageType,
            ISet<string> fullUptimeFamilies,
            double maintainerUptime)
        {
            var multiplier = EstimateDamageMultiplier(teamEffects, passiveTerms, damageType, fullUptimeFamilies, maintainerUptime);

            var ordered = attackerWeaponPercents.OrderByDescending(percent => percent).ToList();
            var carryWeightedWeapon = 0.0;
            for (var index = 0; index < ordered.Count; index++)
            {
                var share = index < CarryCastShares.Length ? CarryCastShares[index] : 0.0;
                carryWeightedWeapon += ordered[index] * share;
            }

            return carryWeightedWeapon * multiplier;
        }

        // Element/axis-gated EFFECTIVE damage % of a weapon for this request. An off-axis weapon (wrong
        // damage type) or off-element weapon (doesn't hit the enemy's weakness) is heavily discounted — it
        // gains none of the weakness-exploit / element terms. E.g. Bird of Prey 940% Water in a Lightning
        // fight is effectively ~470, far below Winged Chakram 1340% Lightning. Non-elemental weapons keep
        // most of their value (no weakness bonus, but not resisted either).
        private static double GetWeaponEffectiveDamagePercent(PlayerPowerAnalyzerV2ItemSlot weapon, PlayerPowerAnalyzerV2Request request)
        {
            if (weapon == null || weapon.DamagePercent <= 0)
            {
                return 0d;
            }

            var axisFactor = 1.0;
            if (request.PreferredDamageType != DamageType.Any && !MatchesRequestedDamageType(weapon.AbilityType, request.PreferredDamageType))
            {
                axisFactor = 0.3;
            }

            var elementFactor = 1.0;
            if (request.EnemyWeakness != Element.None)
            {
                if (string.IsNullOrWhiteSpace(weapon.Element) || weapon.Element.Equals("None", StringComparison.OrdinalIgnoreCase))
                {
                    elementFactor = 0.85; // non-elemental: no weakness bonus, but not resisted
                }
                else if (!MatchesRequestedElement(weapon.Element, request.EnemyWeakness))
                {
                    // 2.7 — off-element: no weakness-exploit, and resisted by a per-fight amount. Use the
                    // request's enemy off-element factor (moderate 0.5 default when unknown), not a hard constant.
                    elementFactor = request.OffElementDamageFactor > 0 ? request.OffElementDamageFactor : 0.5;
                }
            }

            return weapon.DamagePercent * axisFactor * elementFactor;
        }

        // A character's attacking weapon's effective % (best of main / off-hand / ultimate, element/axis-gated).
        private static double GetVariantWeaponDamagePercent(CharacterBuildCandidate variant, PlayerPowerAnalyzerV2Request request)
        {
            var main = variant.MainWeapon != null ? GetWeaponEffectiveDamagePercent(variant.MainWeapon, request) : 0d;
            var off = variant.OffHandWeapon != null ? GetWeaponEffectiveDamagePercent(variant.OffHandWeapon, request) : 0d;
            var ultimate = variant.UltimateWeapon != null ? GetWeaponEffectiveDamagePercent(variant.UltimateWeapon, request) : 0d;
            return Math.Max(main, Math.Max(off, ultimate));
        }

        // True if this attacker can actually hit the enemy's weakness — i.e. any of its main / off-hand / ultimate
        // weapons deals the weakness element. Only then do weakness/element-dependent effects apply to its damage.
        // A wholly off-element/non-elemental attacker (e.g. Cloud on Fusion Sword in a Lightning fight) gains
        // nothing from them. (Materia can technically deal ~300% on-element, but no real build relies on that.)
        private static bool AttackerCanHitWeakness(CharacterBuildCandidate variant, PlayerPowerAnalyzerV2Request request)
        {
            if (request.EnemyWeakness == Element.None)
            {
                return true;
            }

            return (variant.MainWeapon != null && MatchesRequestedElement(variant.MainWeapon.Element, request.EnemyWeakness))
                || (variant.OffHandWeapon != null && MatchesRequestedElement(variant.OffHandWeapon.Element, request.EnemyWeakness))
                || (variant.UltimateWeapon != null && MatchesRequestedElement(variant.UltimateWeapon.Element, request.EnemyWeakness));
        }

        // Effects whose value requires the attacker to deal the enemy's weakness element: Exploit Weakness (a
        // weakness-hit multiplier) and every "elemental" axis family (Elemental Resistance Down / Damage Up / …).
        // They contribute nothing to an attacker that can't hit the weakness, so they are stripped from ITS
        // per-attacker multiplier (the weapon-% element discount is the separate BASE penalty and stays).
        private static bool IsElementDependentEffect(DetectedActiveEffect effect)
        {
            return string.Equals(effect.Key, "exploit_weakness", StringComparison.OrdinalIgnoreCase)
                || string.Equals(effect.AxisKey, "elemental", StringComparison.OrdinalIgnoreCase);
        }

        // Integration overload: estimate a team's damage from its candidate variants, with SCOPE-AWARE per-
        // attacker buff application (the Self-confinement fix). Attackers are ranked by weapon % → cast shares
        // (carry 0.6 / 0.3 / 0.1). Each attacker's damage = weapon% × share × multiplier(SHARED effects +
        // their OWN Self buffs + Single-Ally buffs they were chosen for + teammates' Other-Allies buffs), so a
        // Self/Single-Ally buff multiplies only its real target instead of inflating the whole team.
        private static double EstimateTeamDamage(IReadOnlyList<CharacterBuildCandidate> baseVariants, PlayerPowerAnalyzerV2Request request, bool scopeAware = true)
        {
            if (baseVariants.Count == 0)
            {
                return 0d;
            }

            var damageType = request.PreferredDamageType;
            var ranked = baseVariants
                .Select(variant => (Variant: variant, Weapon: GetVariantWeaponDamagePercent(variant, request)))
                .OrderByDescending(x => x.Weapon)
                .ToList();
            var carry = ranked[0].Variant;

            // Partition each attacker's ACTIVE effects by TargetScope: Self → owner only; Single Ally → the carry
            // (player picks the best target for damage); Other Allies → teammates; everything else (All-Allies
            // buffs + enemy debuffs + Unknown) → SHARED/team-wide.
            var shared = new List<DetectedActiveEffect>();
            var ownByRank = new List<List<DetectedActiveEffect>>();
            var otherAlliesByRank = new List<List<DetectedActiveEffect>>();
            var singleAlly = new List<DetectedActiveEffect>();
            var allEffects = new List<DetectedActiveEffect>();
            for (var i = 0; i < ranked.Count; i++)
            {
                var own = new List<DetectedActiveEffect>();
                var other = new List<DetectedActiveEffect>();
                foreach (var effect in GetDetectedEffectsForVariant(ranked[i].Variant, request))
                {
                    allEffects.Add(effect);
                    switch (effect.TargetScope)
                    {
                        case ActiveEffectTargetScope.Self: own.Add(effect); break;
                        case ActiveEffectTargetScope.SingleAlly: singleAlly.Add(effect); break;
                        case ActiveEffectTargetScope.OtherAllies: other.Add(effect); break;
                        default: shared.Add(effect); break;
                    }
                }

                ownByRank.Add(own);
                otherAlliesByRank.Add(other);
            }

            // Single-Ally buffs land on the carry (rank 0) — the optimal damage target.
            if (singleAlly.Count > 0 && ownByRank.Count > 0)
            {
                ownByRank[0].AddRange(singleAlly);
            }

            // 2.6 attack-type-matched Damage-Received-Up (carry-range approximation): a "Single-Tgt. Dmg. Rcvd. Up"
            // debuff only raises damage from SINGLE-TARGET attacks, so if the carry swings an all-enemies (AOE)
            // weapon its hits don't benefit — drop it. ("All-Tgt."/bare dmg-rcvd-up apply to every attack → kept.)
            if (IsAllEnemiesRange(carry.MainWeapon?.Range))
            {
                shared = shared
                    .Where(effect => !effect.AppliesOnlyToSingleTargetAttacks)
                    .ToList();
            }

            // Passives (separate flat layer, applied to all attackers): the carry's own R-abilities PLUS every
            // OTHER member's team-wide (All-Allies) R-abilities. CRITICAL: same-name All-Allies R-abilities from
            // DIFFERENT providers STACK as separate buffs (Cloud at 46pt PATK-All-Allies = +25% AND Cid at 46pt =
            // +25% → +50% to the team) — they do NOT pool to a single breakpoint. So resolve EACH provider's
            // team-wide passives to a % at THAT provider's OWN breakpoint and SUM the %s; never sum the POINTS
            // across providers (one breakpoint on the pooled total badly under-credits spreading a buff across
            // bodies, since the breakpoint curve flattens hard — 45pt→25% but 92pt still caps at 28%).
            var passiveTerms = BuildPassiveFamilyTerms(carry.PassivePoints, request.PreferredDamageType, request.EnemyWeakness);
            foreach (var (variant, _) in ranked)
            {
                if (ReferenceEquals(variant, carry))
                {
                    continue;
                }

                var teamWidePassivePoints = variant.PassivePoints
                    .Where(passive => IsTeamWidePassive(passive.Key))
                    .ToDictionary(passive => passive.Key, passive => passive.Value, StringComparer.OrdinalIgnoreCase);
                if (teamWidePassivePoints.Count == 0)
                {
                    continue;
                }

                foreach (var providerTerm in BuildPassiveFamilyTerms(teamWidePassivePoints, request.PreferredDamageType, request.EnemyWeakness))
                {
                    passiveTerms[providerTerm.Key] = passiveTerms.TryGetValue(providerTerm.Key, out var existing)
                        ? existing + providerTerm.Value
                        : providerTerm.Value;
                }
            }

            // Uptime: families on an always-cast MAIN weapon are auto-maintained (full uptime); the rest fall to
            // the maintainer bucket. (Only main weapons count as always-cast.)
            var alwaysCastWeaponNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (variant, weapon) in ranked)
            {
                if (variant.MainWeapon != null && weapon > AttackingWeaponDamageThreshold)
                {
                    alwaysCastWeaponNames.Add(variant.MainWeapon.Name);
                }
            }

            var fullUptimeFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var effect in allEffects)
            {
                if (!string.IsNullOrEmpty(effect.SourceName) && alwaysCastWeaponNames.Contains(effect.SourceName))
                {
                    fullUptimeFamilies.Add(string.IsNullOrWhiteSpace(effect.FamilyKey) ? effect.Key : effect.FamilyKey);
                }
            }

            // Cheap-gate / precise-final split: the high-volume skeleton GATE passes scopeAware=false and uses
            // ONE uniform multiplier (pools all effects, incl. Self team-wide — same as before the confinement
            // fix). It only OVER-credits self-buffs, never under-ranks, so it can't exclude a team the precise
            // (final) scorer would want. Bounds the per-attacker cost to the final candidates.
            if (!scopeAware)
            {
                var uniformEffects = (IReadOnlyList<DetectedActiveEffect>)allEffects;
                if (IsAllEnemiesRange(carry.MainWeapon?.Range))
                {
                    uniformEffects = allEffects
                        .Where(effect => !effect.AppliesOnlyToSingleTargetAttacks)
                        .ToList();
                }

                var uniformMultiplier = EstimateDamageMultiplier(uniformEffects, passiveTerms, damageType, fullUptimeFamilies, MaintainerUptime);
                var weighted = 0.0;
                for (var i = 0; i < ranked.Count; i++)
                {
                    var s = i < CarryCastShares.Length ? CarryCastShares[i] : 0.0;
                    weighted += ranked[i].Weapon * s;
                }

                return weighted * uniformMultiplier;
            }

            // Per-attacker damage = weapon% × cast-share × scope-confined multiplier. Reuse one shared multiplier
            // for attackers that have no Self/Other-Allies effects (the common case → no extra cost). Off-element
            // attackers (can't hit the weakness) get a SEPARATE shared multiplier with the element-dependent
            // effects (Exploit Weakness, elemental buffs/debuffs) stripped — they gain nothing from them.
            var anyOtherAllies = otherAlliesByRank.Any(list => list.Count > 0);
            double? sharedOnElementCache = null;
            double? sharedOffElementCache = null;
            var totalDamage = 0.0;
            for (var i = 0; i < ranked.Count; i++)
            {
                var share = i < CarryCastShares.Length ? CarryCastShares[i] : 0.0;
                if (share <= 0 || ranked[i].Weapon <= 0)
                {
                    continue;
                }

                var canHitWeakness = AttackerCanHitWeakness(ranked[i].Variant, request);

                double multiplier;
                if (ownByRank[i].Count == 0 && !anyOtherAllies)
                {
                    if (canHitWeakness)
                    {
                        multiplier = sharedOnElementCache ??= EstimateDamageMultiplier(shared, passiveTerms, damageType, fullUptimeFamilies, MaintainerUptime);
                    }
                    else
                    {
                        multiplier = sharedOffElementCache ??= EstimateDamageMultiplier(
                            shared.Where(effect => !IsElementDependentEffect(effect)).ToList(),
                            passiveTerms, damageType, fullUptimeFamilies, MaintainerUptime);
                    }
                }
                else
                {
                    var effectsForAttacker = new List<DetectedActiveEffect>(shared);
                    effectsForAttacker.AddRange(ownByRank[i]);
                    for (var j = 0; j < otherAlliesByRank.Count; j++)
                    {
                        if (j != i)
                        {
                            effectsForAttacker.AddRange(otherAlliesByRank[j]);
                        }
                    }

                    // Off-element attacker: strip the weakness/elemental effects it can't use from ITS multiplier.
                    if (!canHitWeakness)
                    {
                        effectsForAttacker = effectsForAttacker.Where(effect => !IsElementDependentEffect(effect)).ToList();
                    }

                    multiplier = EstimateDamageMultiplier(effectsForAttacker, passiveTerms, damageType, fullUptimeFamilies, MaintainerUptime);
                }

                totalDamage += ranked[i].Weapon * share * multiplier;
            }

            return totalDamage;
        }

        // NOTE: a Phase 2.2 / target-#3 per-character support-valuation proxy (GetVariantTeamBuffContribution +
        // IsTeamWideOffensiveEffect + ReferenceCarryWeaponPercent) was tried twice and reverted both times. It
        // tried to value a support's team-wide buffs against a reference carry so a per-character score wouldn't
        // undervalue pure supports. It does NOT fix the support-exclusion, because the problem is structural: a
        // PER-CHARACTER score (any form) cannot rank a pure support (buffs only) vs an attacker-support (damage +
        // buffs) — that is a 3-character TEAM decision — and multiple GATES (shortlist, support pool, skeleton
        // cut) cut on the per-character score. The correct fix is gate de-pollution + letting the team-context
        // EstimateTeamDamage decide; see docs/item3-damage-model-design.md "Candidate-generation rework".
    }
}
