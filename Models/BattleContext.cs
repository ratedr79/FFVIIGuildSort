namespace FFVIIEverCrisisAnalyzer.Models
{
    public enum Element
    {
        None,
        Earth,
        Fire,
        Water,
        Ice,
        Lightning,
        Wind
    }

    public enum DamageType
    {
        Any,
        Physical,
        Magical
    }

    public enum EnemyTargetScenario
    {
        Unknown,
        SingleEnemy,
        MultipleEnemies
    }

    public sealed class BattleContext
    {
        public Element EnemyWeakness { get; set; } = Element.None;
        public DamageType PreferredDamageType { get; set; } = DamageType.Any;
        public EnemyTargetScenario TargetScenario { get; set; } = EnemyTargetScenario.Unknown;

        // Optional: additional multiplier for specific synergy effects.
        // Value is a percent bonus (0, 10, 20, 30, 40, 50). When null or missing keys, behaves as 0%.
        public Dictionary<string, int>? SynergyEffectBonusPercents { get; set; }
    }
}
