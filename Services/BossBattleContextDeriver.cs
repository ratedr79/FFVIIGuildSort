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
        string DamageTypeReason);               // short explanation of the damage-type call (UI transparency)

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
                reason);
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
