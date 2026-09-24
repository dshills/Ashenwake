using System.Globalization;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private static CylinderMesh? _briarThornMesh;
    private static StandardMaterial3D? _briarThornMaterial;
    private string CombinedEquipmentReadiness() => string.Join("\n", new[]
    {
        _view.Legendary is { } legendary ? LegendaryReadiness(legendary, _view.Discipline) : "",
        EquipmentSetReadiness(_view.EquipmentSets)
    }.Where(line => line.Length > 0));

    internal static string EquipmentSetReadiness(EquipmentSetCombatView? sets)
    {
        if (sets is null) return "";
        static string Seconds(int ticks) => (Math.Ceiling(ticks / 3d) / 10).ToString("F1", CultureInfo.InvariantCulture) + "s";
        var lines = new List<string>();
        if (sets.LastVigilActive) lines.Add(sets.VigilReadyTicks > 0 ? "VIGIL · COUNTER READY " + Seconds(sets.VigilReadyTicks)
            : sets.VigilCooldownTicks > 0 ? "VIGIL · " + Seconds(sets.VigilCooldownTicks) : "VIGIL · ABSORB DAMAGE");
        if (sets.BriarboundActive) lines.Add(sets.BriarCooldownTicks > 0 ? "BRIAR · " + Seconds(sets.BriarCooldownTicks) : "BRIAR · POISON KILL");
        if (sets.AshrunnerActive) lines.Add(sets.AshrunnerReadyTicks > 0 ? "ASHRUNNER · TRAIL READY " + Seconds(sets.AshrunnerReadyTicks)
            : sets.AshrunnerCooldownTicks > 0 ? "ASHRUNNER · " + Seconds(sets.AshrunnerCooldownTicks) : "ASHRUNNER · EVADE A HIT");
        // Two entries per line keeps the combined readiness readable at the minimum window size.
        return string.Join("\n", lines.Chunk(2).Select(pair => string.Join("   ·   ", pair)));
    }

    private void PresentEquipmentSetEvent(CombatEvent e, ActorPresentation? actor, ActorPresentation? target, Vector3 direction)
    {
        if (actor is not { Health: > 0, AuthoredVisible: true }) return;
        bool ready = e.Kind == "EquipmentSetReadied";
        string cue = ready ? "legendary_ready" : e.ContentId == EquipmentSets.Briarbound ? "legendary_rotwake"
            : e.ContentId == EquipmentSets.Ashrunner ? "legendary_cinder" : "legendary_oath";
        Color color = e.ContentId == EquipmentSets.Briarbound ? new("a5d87b") : e.ContentId == EquipmentSets.Ashrunner ? new("ffc06c") : new("a9ddeb");
        var origin = !ready && e.ContentId == EquipmentSets.Briarbound ? target : actor;
        if (origin?.AuthoredVisible == true) _combatEffects.Emit(cue, origin.Current, direction, color, _reduceEffects);
        _legendaryTriggerText = e.ContentId switch
        {
            EquipmentSets.LastVigil => ready ? "LAST VIGIL · spectral counter readied" : "LAST VIGIL · spectral counter",
            EquipmentSets.Briarbound => $"BRIARBOUND · thorns grow · {e.Amount} companion healing",
            EquipmentSets.Ashrunner => ready ? "ASHRUNNER · ember trail readied" : $"ASHRUNNER · {e.Amount} ember patches",
            _ => ""
        };
        _legendaryTriggerPower = e.ContentId; _legendaryTriggerUntil = e.Tick + 45;
        if (!ready) { PlayTone(cue); LegendaryTriggerCueCount++; LastLegendaryTriggerCue = cue; }
    }

    private static void PresentBriarPatch(MeshInstance3D mesh, CombatAreaView area)
    {
        mesh.Name = "BriarPatch_" + area.Id;
        if (mesh.GetNodeOrNull<Node3D>("Thorns") is not null) return;
        var thorns = new Node3D { Name = "Thorns" }; mesh.AddChild(thorns);
        var material = _briarThornMaterial ??= Material(new Color("9ac570"), true);
        var geometry = _briarThornMesh ??= new CylinderMesh { TopRadius = 0, BottomRadius = .055f, Height = .24f, RadialSegments = 12 };
        for (int i = 0; i < 6; i++)
        {
            float angle = i * Mathf.Tau / 6;
            thorns.AddChild(new MeshInstance3D
            {
                Mesh = geometry,
                Position = new(Mathf.Cos(angle) * .6f, .12f, Mathf.Sin(angle) * .6f),
                Rotation = new(.3f * Mathf.Sin(angle), 0, .3f * Mathf.Cos(angle)),
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            });
        }
    }
}
