using Ashenwake.Core.Content;

namespace Ashenwake.Core.Experiments;

public sealed record ExperimentRules(int SchemaVersion, string Version, string Id, string Name, string EchoSkillId,
    int BindRadius, int EchoLifetimeTicks, int WarningTicks, int HazardTicks, int HazardRadius, int HazardDamage, string CosmeticId);
public sealed record ExperimentCatalog(int SchemaVersion, string Admission, ExperimentRules Rules);

/// <summary>Retirement changes entry policy, never the immutable rules used by an existing run or replay.</summary>
public sealed class ExperimentContent
{
    private readonly ExperimentRules rules;
    public string Hash => JsonData.Hash(rules);
    public string Admission { get; }
    public bool AcceptingEntries => Admission == "Available";
    public ExperimentRules Capture() => JsonData.Copy(rules);
    public ExperimentContent WithAdmission(string admission) => Create(new(1, admission, rules));
    private ExperimentContent(ExperimentCatalog catalog) { rules = JsonData.Copy(catalog.Rules); Admission = catalog.Admission; }
    public static ExperimentContent Parse(string json) => Create(JsonData.Read<ExperimentCatalog>(json));
    public static ExperimentContent Default() => Create(new(1, "Available", new(1, "echoes.1", "experiment.borrowed_memory", "Echoes: Borrowed Memory", "skill.echo_storm", 1400, 450, 45, 60, 1600, 18, "cosmetic.borrowed_memory")));
    public static ExperimentContent Create(ExperimentCatalog catalog)
    {
        if (catalog is null || catalog.SchemaVersion != 1 || catalog.Admission is not ("Available" or "Retired")) throw new InvalidDataException("Unsupported experiment catalog or admission policy.");
        ValidateRules(catalog.Rules); return new(catalog);
    }
    public static void ValidateRules(ExperimentRules rules)
    {
        if (rules is null || rules.SchemaVersion != 1 || rules.Version != "echoes.1" || rules.Id != "experiment.borrowed_memory" || rules.EchoSkillId != "skill.echo_storm" || rules.CosmeticId != "cosmetic.borrowed_memory" || string.IsNullOrWhiteSpace(rules.Name) || rules.Name.Length > 80 ||
            rules.BindRadius is < 800 or > 2000 || rules.EchoLifetimeTicks is < 90 or > 900 || rules.WarningTicks is < 30 or > 90 || rules.HazardTicks is < 20 or > 120 || rules.HazardRadius is < 1000 or > 2500 || rules.HazardDamage is < 1 or > 60)
            throw new InvalidDataException("Invalid or unsupported Borrowed Memory rules.");
    }
}
