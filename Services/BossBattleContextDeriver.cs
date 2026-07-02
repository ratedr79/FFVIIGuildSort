using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    // The battle context inferred from a selected enemy, for the Player Power Analyzer V2 "By Boss" mode.
    // All values are shown to the user and overridable.
    public sealed record DerivedBattleContext(
        Element EnemyWeakness,
        DamageType PreferredDamageType,
        EnemyTargetScenario TargetScenario,
        IReadOnlyList<string> RequiredSigils,   // break sigils → hard requirement
        IReadOnlyList<string> BonusSigils,      // damage sigils → soft score bonus
        IReadOnlyList<string> BossImmunityKeys, // effects the enemy resists → the analyzer's boss-immunity filter
        BossDefensiveAdvisory DefensiveAdvisory,// advisory-only heads-up on what the boss does TO you (no scoring impact)
        string DamageTypeReason);               // short explanation of the damage-type call (UI transparency)

    // Advisory-only: what the boss deals and inflicts, so the player can shore up defenses manually. Best-effort from
    // the enemy's description + attack stats; it does NOT influence the analyzer's (offense-only) ranking.
    public sealed record BossDefensiveAdvisory(
        string? AttackElement,                   // "Earth" / null
        string? AttackDamageType,                // "Physical" / "Magical" / null
        IReadOnlyList<string> InflictedAilments, // e.g. ["Incapacitate"]
        IReadOnlyList<string> Recommendations)   // e.g. ["Earth Resistance", "PDEF (or PDEF All Allies)"]
    {
        public bool HasContent => !string.IsNullOrEmpty(AttackElement)
            || !string.IsNullOrEmpty(AttackDamageType)
            || InflictedAilments.Count > 0
            || Recommendations.Count > 0;
    }

    // Derives battle context from an enemy's detail view. Pure/static so it's unit-testable.
    public static class BossBattleContextDeriver
    {
        private static readonly string[] SigilTypes = { "Circle", "Triangle", "Cross", "Diamond" };

        public static DerivedBattleContext Derive(EnemyDetailView detail)
        {
            var weakness = DeriveWeakness(detail.ElementResistances);
            var (damageType, reason) = DeriveDamageType(detail);
            // Boss fights default to single-target (overridable); enemy-count is not currently plumbed.
            return new DerivedBattleContext(
                weakness,
                damageType,
                EnemyTargetScenario.SingleEnemy,
                MapSigils(detail.BattleSigils),
                MapSigils(detail.BattleDamageSigils),
                MapBossImmunities(detail, PlayerPowerAnalyzerV2Service.AvailableBossImmunityOptions),
                DeriveDefensiveAdvisory(detail),
                reason);
        }

        private static readonly (string Stem, string Display)[] AilmentStems =
        {
            ("incapacitat", "Incapacitate"), ("poison", "Poison"), ("silence", "Silence"), ("darkness", "Darkness"),
            ("blind", "Blind"), ("sleep", "Sleep"), ("stun", "Stun"), ("paralyz", "Paralysis"), ("confus", "Confusion"),
            ("petrif", "Petrify"), ("berserk", "Berserk"), ("toad", "Toad"), ("zombie", "Zombie"), ("charm", "Charm")
        };

        // What the boss deals to YOU (element + physical/magical) and inflicts, → defensive recommendations. Best-effort
        // from the description prose (with the "Immunities:"/resisted-debuff header lines stripped so we don't misread
        // the boss's OWN resisted statuses as attacks) plus PATK/MATK as an attack-type fallback.
        private static BossDefensiveAdvisory DeriveDefensiveAdvisory(EnemyDetailView detail)
        {
            var prose = StripImmunityLines(detail.Description ?? string.Empty);
            // Attack element/type describe what the boss DOES — exclude "effective against" (that's YOUR offense).
            var attackText = RemoveEffectiveAgainstSentences(prose);
            var attackElement = FindAttackElement(attackText);
            var attackType = FindAttackDamageType(attackText, detail.PhysicalAttack, detail.MagicalAttack);
            var ailments = FindInflictedAilments(prose);

            var recommendations = new List<string>();
            if (attackElement != null) { recommendations.Add($"{attackElement} Resistance"); }
            if (attackType == "Physical") { recommendations.Add("PDEF (or PDEF All Allies)"); }
            else if (attackType == "Magical") { recommendations.Add("MDEF (or MDEF All Allies)"); }

            return new BossDefensiveAdvisory(attackElement, attackType, ailments, recommendations);
        }

        private static string StripImmunityLines(string description)
        {
            var kept = description
                .Split('\n')
                .Where(line => !line.TrimStart().StartsWith("Immunities:", System.StringComparison.OrdinalIgnoreCase)
                    && !line.Contains("Dmg. Rcvd. Up", System.StringComparison.OrdinalIgnoreCase));
            return string.Join(" ", kept);
        }

        private static string RemoveEffectiveAgainstSentences(string text)
        {
            var sentences = text.Split('.')
                .Where(sentence => !sentence.Contains("effective against", System.StringComparison.OrdinalIgnoreCase));
            return string.Join(". ", sentences);
        }

        private static string? FindAttackElement(string text)
        {
            foreach (var element in new[] { "Fire", "Ice", "Lightning", "Earth", "Water", "Wind" })
            {
                if (text.Contains(element + "-element", System.StringComparison.OrdinalIgnoreCase)
                    || text.Contains(element + " element", System.StringComparison.OrdinalIgnoreCase))
                {
                    return element;
                }
            }
            return null;
        }

        private static string? FindAttackDamageType(string text, int physicalAttack, int magicalAttack)
        {
            var lower = text.ToLowerInvariant();
            var mentionsPhysical = lower.Contains("physical");
            var mentionsMagical = lower.Contains("magic"); // covers "magic" and "magical"
            if (mentionsPhysical && !mentionsMagical) { return "Physical"; }
            if (mentionsMagical && !mentionsPhysical) { return "Magical"; }
            // Ambiguous / not stated → fall back to the enemy's stronger attack stat.
            if (physicalAttack > magicalAttack) { return "Physical"; }
            if (magicalAttack > physicalAttack) { return "Magical"; }
            return null;
        }

        private static IReadOnlyList<string> FindInflictedAilments(string prose)
        {
            var lower = prose.ToLowerInvariant();
            var found = new List<string>();
            foreach (var (stem, display) in AilmentStems)
            {
                if (lower.Contains(stem) && !found.Contains(display))
                {
                    found.Add(display);
                }
            }
            return found;
        }

        // Which analyzer boss-immunity options the enemy actually has, so selecting a boss stops the analyzer from
        // crediting teams that rely on an effect the boss resists (e.g. Titan EX 3 is immune to PATK/PDEF/MATK Down).
        // Data-driven: each option's label minus " Immunity" is the phrase to look for in the enemy's immunity lists.
        // Legacy "broad" options are skipped in favour of the specific ones.
        public static IReadOnlyList<string> MapBossImmunities(EnemyDetailView detail, IReadOnlyList<PlayerPowerAnalyzerV2EffectOption> options)
        {
            var enemyImmunities = detail.BuffDebuffImmunities.Concat(detail.StatusImmunities).ToList();
            if (enemyImmunities.Count == 0)
            {
                return System.Array.Empty<string>();
            }

            var keys = new List<string>();
            foreach (var option in options)
            {
                if (option.Group.Equals("Legacy Broad Immunities", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var phrase = option.Label.Replace(" Immunity", string.Empty, System.StringComparison.OrdinalIgnoreCase).Trim();
                if (phrase.Length > 0 && enemyImmunities.Any(immunity => immunity.Contains(phrase, System.StringComparison.OrdinalIgnoreCase)))
                {
                    keys.Add(option.Key);
                }
            }
            return keys;
        }

        // Weakness = the element the enemy resists the LEAST (most negative resistance). None if nothing is negative.
        private static Element DeriveWeakness(IReadOnlyList<ResistanceEntry> resistances)
        {
            var best = Element.None;
            var bestValue = 0; // only a negative resistance is a weakness
            foreach (var resist in resistances)
            {
                if (!Enum.TryParse<Element>(resist.Type, ignoreCase: true, out var element) || element == Element.None)
                {
                    continue; // skip non-elemental rows and Holy/Dark (not representable in the analyzer's Element enum)
                }
                if (TryParsePercent(resist.Value, out var value) && value < bestValue)
                {
                    bestValue = value;
                    best = element;
                }
            }
            return best;
        }

        // Damage type from (weakest → strongest signal): PDEF vs MDEF, then def-down-resist asymmetry, then an
        // explicit description hint. Each stronger signal overrides the weaker.
        private static (DamageType DamageType, string Reason) DeriveDamageType(EnemyDetailView detail)
        {
            var result = DamageType.Any;
            var reason = "PDEF/MDEF comparable";
            if (detail.PhysicalDefense < detail.MagicalDefense) { result = DamageType.Physical; reason = "Lower PDEF"; }
            else if (detail.MagicalDefense < detail.PhysicalDefense) { result = DamageType.Magical; reason = "Lower MDEF"; }

            var resistsPdefDown = ContainsImmunity(detail.BuffDebuffImmunities, "PDEF Down");
            var resistsMdefDown = ContainsImmunity(detail.BuffDebuffImmunities, "MDEF Down");
            if (resistsPdefDown && !resistsMdefDown) { result = DamageType.Magical; reason = "Resists PDEF Down (can debuff magic def, not physical)"; }
            else if (resistsMdefDown && !resistsPdefDown) { result = DamageType.Physical; reason = "Resists MDEF Down (can debuff physical def, not magic)"; }

            var description = detail.Description ?? string.Empty;
            if (MentionsEffective(description, magic: true)) { result = DamageType.Magical; reason = "Description: magic abilities effective"; }
            else if (MentionsEffective(description, magic: false)) { result = DamageType.Physical; reason = "Description: physical abilities effective"; }

            return (result, reason);
        }

        // e.g. "Wind-element magic abilities are effective against this enemy."
        private static bool MentionsEffective(string description, bool magic)
        {
            var lower = description.ToLowerInvariant();
            if (magic)
            {
                return lower.Contains("magic abilities are effective") || lower.Contains("magical abilities are effective");
            }
            return lower.Contains("physical abilities are effective");
        }

        private static bool ContainsImmunity(IReadOnlyList<string> immunities, string needle)
            => immunities.Any(i => i.Contains(needle, StringComparison.OrdinalIgnoreCase));

        // Display sigils ("◯ Circle") → canonical type list ("Circle"), deduped, in canonical order.
        private static IReadOnlyList<string> MapSigils(IReadOnlyList<string> displaySigils)
        {
            var result = new List<string>();
            foreach (var sigilType in SigilTypes)
            {
                if (displaySigils.Any(s => s.Contains(sigilType, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(sigilType);
                }
            }
            return result;
        }

        private static bool TryParsePercent(string value, out int parsed)
        {
            parsed = 0;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            return int.TryParse(value.Replace("%", string.Empty).Trim(), out parsed);
        }
    }
}
