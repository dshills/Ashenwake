using Ashenwake.Core.Progression;

namespace Ashenwake.Core.Combat;

/// <summary>Static passive additions; final hit mitigation and resource caps still apply in combat.</summary>
public sealed record PassiveEffectView(int DamageIncreaseBasisPoints, int AddedArmor, int ResourceInvestment, int GenerationBonus);

public static class PassiveEffects
{
    public static int OffenseBasisPoints(int rank) => rank * 200;
    public static int DefenseArmor(int rank) => rank * 100;
    public static int GenerationBonus(int resourceBonus) => resourceBonus / 10;

    /// <summary>Uses the same equipped-affix and passive investment as the production combat projection.</summary>
    public static PassiveEffectView Inspect(ProgressionView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        int investment = Math.Min(1000, view.Stats.GetValueOrDefault("affix.resource") + view.Stats.GetValueOrDefault("passive.Resource"));
        return new(OffenseBasisPoints(view.Stats.GetValueOrDefault("passive.Offense")),
            DefenseArmor(view.Stats.GetValueOrDefault("passive.Defense")), investment, GenerationBonus(investment));
    }
}
