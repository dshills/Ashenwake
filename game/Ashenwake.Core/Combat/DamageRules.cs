namespace Ashenwake.Core.Combat;

public sealed record DamageInput(int BaseDamage, int FlatBonus = 0, int IncreasedBasisPoints = 0, int MoreBasisPoints = 10000,
    bool Critical = false, int CriticalMultiplierBasisPoints = 15000, DamageFamily Family = DamageFamily.PhysicalSlash,
    DamageFamily? Conversion = null, int DefenseBasisPoints = 0, int PenetrationBasisPoints = 0,
    int VulnerabilityBasisPoints = 0, int Barrier = 0, bool Immune = false, bool DamageOverTime = false);
public sealed record DamageResult(DamageFamily Family, int BeforeBarrier, int Absorbed, int HealthDamage);
public static class DamageRules
{
    // Each integer stage floors. Increased modifiers add; independent More modifiers must be composed before this call.
    public static DamageResult Resolve(DamageInput input)
    {
        if (input.BaseDamage is < 0 or > 1000000 || input.FlatBonus is < 0 or > 1000000 || input.IncreasedBasisPoints is < -10000 or > 100000 || input.MoreBasisPoints is < 0 or > 100000 || input.CriticalMultiplierBasisPoints is < 10000 or > 100000 || input.Barrier < 0 || !Enum.IsDefined(input.Family) || (input.Conversion is { } conversion && !Enum.IsDefined(conversion)))
            throw new ArgumentOutOfRangeException(nameof(input));
        long amount = input.BaseDamage + input.FlatBonus;
        amount = amount * (10000 + input.IncreasedBasisPoints) / 10000;
        amount = amount * input.MoreBasisPoints / 10000;
        if (input.Critical && !input.DamageOverTime) amount = amount * input.CriticalMultiplierBasisPoints / 10000;
        var family = input.Conversion ?? input.Family;
        amount = amount * (10000 - Math.Clamp(input.DefenseBasisPoints - input.PenetrationBasisPoints, 0, 7500)) / 10000;
        amount = amount * (10000 + Math.Clamp(input.VulnerabilityBasisPoints, 0, 10000)) / 10000;
        int final = input.Immune ? 0 : (int)Math.Min(amount, 1000000);
        var absorbed = Math.Min(final, input.Barrier);
        return new(family, final, absorbed, final - absorbed);
    }
}
