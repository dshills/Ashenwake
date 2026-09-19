using System.Globalization;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public partial class SkillsPanel
{
    private void ShowSkill()
    {
        var skill = _combat.Skills.Single(s => s.Id == SelectedSkillId);
        var current = Inspect(skill.Id, _state!.Character.SelectedMutations.GetValueOrDefault(skill.Id, ""));
        var candidate = Inspect(skill.Id, SelectedMutationId);
        int mastery = _state.Character.Mastery.GetValueOrDefault(skill.Id);
        _title.Text = skill.Name;
        _description.Text = (candidate is null ? skill.Shape + " ability." : Describe(candidate)) +
            $"\nMastery {mastery} · mutations unlock at 100." + (!skill.Available ? "\nUltimate locked · reach level 10." : "");
        Variant("", "Base ability", "The original ability, without a mutation.", 0);
        int index = 1;
        foreach (var mutation in _combat.Mutations.Where(m => m.SkillId == skill.Id)) Variant(mutation.Id, mutation.Name, mutation.Description, index++);
        if (index == 1) _variants.AddChild(Caption("This ability has no mutation choices.", 12));
        Line("ACTIVE → SELECTED", "81d8ce");
        string active = _state.Character.SelectedMutations.GetValueOrDefault(skill.Id, "");
        Line(FormName(active) + " → " + FormName(SelectedMutationId));
        if (current is not null && candidate is not null)
        {
            Line($"Delivery: {current.Shape} → {candidate.Shape}");
            Line($"Damage type: {DamageName(candidate.Family)}");
            Line($"{(candidate.ResourceMode == "Heat" ? "Heat added" : "Resource cost")}: {current.Cost} → {candidate.Cost} {_view.Resource}");
            if (candidate.Generate > 0) Line($"Base resource generation: {candidate.Generate} {_view.Resource}");
            Line($"Cooldown: {Seconds(current.CooldownTicks)} → {Seconds(candidate.CooldownTicks)}");
            if (candidate.Shape is not ("Summon" or "Command"))
            {
                Line($"Base {(candidate.Shape == "Guard" ? "barrier" : "power")}: {candidate.BaseDamage}");
                Line($"Mutation power: {current.DamagePercent}% → {candidate.DamagePercent}%");
            }
            Line($"Range: {Distance(candidate.Range)} · radius: {Distance(current.Radius)} → {Distance(candidate.Radius)}");
            Line("These are ability parameters. Equipment, passives, targets and combat conditions determine the final result.", "b7c5ca");
        }
        if (SelectedMutationId.Length > 0) Line(_combat.Mutations.Single(m => m.Id == SelectedMutationId).Description);
        Line(Preview!.Success ? "No materials or passive points are spent by changing a mutation." : Preview.Reason, Preview.Success ? "b7c5ca" : "eeb196");
    }
    private CombatSkillInspection? Inspect(string id, string mutation) => _sandbox?.Session.InspectSkill(id, mutation);
    private string FormName(string id) => id.Length == 0 ? "Base ability" : _combat.Mutations.FirstOrDefault(m => m.Id == id)?.Name ?? id;
    private void Variant(string id, string name, string description, int index)
    {
        bool selected = id == SelectedMutationId;
        bool active = _state!.Character.SelectedMutations.GetValueOrDefault(SelectedSkillId, "") == id;
        bool locked = id.Length > 0 && _state.Character.Mastery.GetValueOrDefault(SelectedSkillId) < 100;
        var button = new Button { Name = "SkillMutation" + index, Text = (selected ? "◆ " : "") + name + (active ? " · ACTIVE" : locked ? " · MASTERY 100" : ""), TooltipText = description, CustomMinimumSize = new(0, 34), AutowrapMode = TextServer.AutowrapMode.WordSmart, ToggleMode = true, ButtonPressed = selected };
        button.AddThemeFontSizeOverride("font_size", 12); button.Pressed += () => SelectMutation(id); _variants.AddChild(button);
    }
    private void ShowPassives()
    {
        bool refund = _action == ProgressionBuildAction.Respec;
        _title.Text = refund ? "Refund passive points" : _passive + " investment";
        _description.Text = refund ? "Return every invested passive point. Inspect the effects you will lose and the material cost before confirming." : _passive switch
        {
            "Offense" => "Increase the passive damage bonus applied to your attacks.",
            "Defense" => "Add armor against physical hits. Final mitigation also depends on your other defenses and the incoming attack.",
            _ => "Invest in resource generation. The combined equipment and passive investment grants one extra resource per ten points, when an ability generates resource."
        };
        var preview = Preview!;
        Line(preview.Success ? "CURRENT → AFTER APPLY" : "CURRENT BUILD · CHANGE BLOCKED", "81d8ce");
        foreach (string passive in new[] { "Offense", "Defense", "Resource" })
        {
            int before = preview.Before.Character.Passives.GetValueOrDefault(passive), after = preview.After.Character.Passives.GetValueOrDefault(passive);
            Line(passive + " rank: " + (preview.Success ? $"{before} → {after}" : before.ToString(CultureInfo.InvariantCulture)), before == after ? "c2d0d4" : after > before ? "9eddb4" : "eeb196");
        }
        var old = PassiveEffects.Inspect(preview.BeforeView); var next = PassiveEffects.Inspect(preview.AfterView);
        Effect("Passive damage increase", Percent(old.DamageIncreaseBasisPoints), Percent(next.DamageIncreaseBasisPoints), preview.Success);
        Effect("Passive armor", old.AddedArmor.ToString(), next.AddedArmor.ToString(), preview.Success);
        Effect("Resource investment", old.ResourceInvestment.ToString(), next.ResourceInvestment.ToString(), preview.Success);
        Effect("Generation bonus before the 100 cap", old.GenerationBonus.ToString(), next.GenerationBonus.ToString(), preview.Success);
        if (!refund && _passive == "Resource" && next.GenerationBonus == old.GenerationBonus)
            Line("The next full ten-point threshold raises generation; this point does not increase it yet.", "e8cc9c");
        Effect("Unspent passive points", preview.BeforeView.AvailablePassivePoints.ToString(), preview.AfterView.AvailablePassivePoints.ToString(), preview.Success);
        Effect("Materials", preview.BeforeView.Materials.ToString(), preview.AfterView.Materials.ToString(), preview.Success);
        if (refund) Line($"Refund fee: {_definition.RespecCost} materials. Returned points may be invested again.");
        Line(preview.Success ? "Preview only. Your current build is unchanged." : preview.Reason, preview.Success ? "b7c5ca" : "eeb196");
    }
    private void Effect(string name, string before, string after, bool projected) => Line(name + ": " + before + (projected ? " → " + after : ""));
    private void Line(string text, string color = "c2d0d4")
    { var label = Caption(text, 12); label.Modulate = new(color); _comparison.AddChild(label); PreviewText += (PreviewText.Length == 0 ? "" : "\n") + text; }
    private static string Percent(int points) => (points / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";
    private static string Seconds(int ticks) => (ticks / 30m).ToString("0.##", CultureInfo.InvariantCulture) + "s";
    private static string Distance(int units) => (units / 1000m).ToString("0.##", CultureInfo.InvariantCulture) + "m";
    private static string DamageName(DamageFamily family) => family switch
    {
        DamageFamily.PhysicalSlash => "Physical · Slash",
        DamageFamily.PhysicalPierce => "Physical · Pierce",
        DamageFamily.PhysicalCrush => "Physical · Crush",
        DamageFamily.Fire => "Fire",
        DamageFamily.Frost => "Frost",
        DamageFamily.Storm => "Storm",
        DamageFamily.Decay => "Decay",
        DamageFamily.Venom => "Venom",
        DamageFamily.Void => "Void",
        _ => "Unknown"
    };
    private string ResultSummary(ProgressionBuildPreview preview)
    {
        var changes = new List<string>();
        foreach (string passive in new[] { "Offense", "Defense", "Resource" })
        {
            int before = preview.Before.Character.Passives.GetValueOrDefault(passive), after = preview.After.Character.Passives.GetValueOrDefault(passive);
            if (before != after) changes.Add($"{passive} rank {before} → {after}");
        }
        foreach (string id in preview.Before.Character.SelectedMutations.Keys.Concat(preview.After.Character.SelectedMutations.Keys).Distinct())
        {
            string before = preview.Before.Character.SelectedMutations.GetValueOrDefault(id, ""), after = preview.After.Character.SelectedMutations.GetValueOrDefault(id, "");
            if (before != after) changes.Add(FormName(before) + " → " + FormName(after));
        }
        int spent = preview.BeforeView.Materials - preview.AfterView.Materials;
        return "Build updated · " + spent + " materials spent.\n" + string.Join(" · ", changes) + $"\n{preview.AfterView.AvailablePassivePoints} unspent passive points.";
    }
    private static string Describe(CombatSkillInspection skill)
    {
        string description = skill.Behavior switch
        {
            "Vanish" => "Conceal yourself and raise a protective barrier.",
            "Vent" => "Release Instability and raise a protective barrier.",
            "Regenerate" => "Raise a protective barrier and regenerate health.",
            "Companion" => "Summon a feral companion to fight alongside you.",
            "Procession" => "Call an ancestral procession to fight alongside you.",
            "Leech" => "Strike a nearby enemy and siphon life.",
            "ConsumeMarked" => "Strike a nearby enemy and consume Marked for an execution bonus.",
            "DetonatePoison" => "Unleash an area attack that detonates poison.",
            _ => skill.Shape switch { "Melee" => "Strike a nearby enemy.", "Projectile" => "Send an attack toward your target.", "Area" => "Affect enemies in an area around you.", "Dash" => "Rush toward your target and strike.", "Guard" => "Raise a protective barrier.", "Summon" => "Raise an ally to fight alongside you.", "Command" => "Direct your summons against a marked target.", _ => "Use this discipline ability." }
        };
        return description + (skill.Status.Length > 0 ? " Applies " + skill.Status + "." : "");
    }
}
