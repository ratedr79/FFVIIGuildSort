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
    }
}
