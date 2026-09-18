using Ashenwake.Core.Combat;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Exact Core memory proximity and hostile Storm geometry; all effects remain visible with reduced effects enabled.</summary>
public partial class ExperimentPresentation : Node3D
{
    private Node3D offered = null!, storm = null!;
    private MeshInstance3D fill = null!, rim = null!;
    private Label3D offerLabel = null!, stormLabel = null!;
    public override void _Ready()
    {
        offered = new Node3D(); AddChild(offered);
        Mesh(new TorusMesh { InnerRadius = .56f, OuterRadius = .65f }, new(0, .12f, 0), new("96d5ff"), offered);
        Mesh(new SphereMesh { Radius = .18f, Height = .36f }, new(0, .7f, 0), new("b9e5ff"), offered);
        offerLabel = Label(new(0, 1.25f, 0)); offered.AddChild(offerLabel);
        storm = new Node3D(); AddChild(storm);
        fill = Mesh(new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = .025f }, new(0, .14f, 0), new Color(1, .7f, .2f, .25f), storm);
        rim = Mesh(new TorusMesh { InnerRadius = .95f, OuterRadius = 1 }, new(0, .17f, 0), new("ffe2a1"), storm);
        stormLabel = Label(new(0, .75f, 0)); storm.AddChild(stormLabel); offered.Visible = storm.Visible = false;
    }
    public void Show(BorrowedMemoryView? view)
    {
        offered.Visible = view?.Status == "Offered"; storm.Visible = view is { HazardStage: "Warning" or "Active" };
        if (view is null) return;
        if (offered.Visible) { offered.Position = Point(view.MemoryPosition); offerLabel.Text = "ELITE MEMORY\n" + (view.CanBind ? "[H] BIND" : "APPROACH TO BIND"); }
        if (!storm.Visible) return;
        storm.Position = Point(view.HazardPosition); float radius = view.HazardRadius * .001f;
        fill.Scale = new(radius, 1, radius); rim.Scale = new(radius, 1, radius);
        bool active = view.HazardStage == "Active";
        ((StandardMaterial3D)fill.MaterialOverride).AlbedoColor = active ? new Color(1, .18f, .2f, .42f) : new Color(1, .72f, .2f, .24f);
        ((StandardMaterial3D)rim.MaterialOverride).AlbedoColor = active ? new("ff715f") : new("ffe2a1");
        stormLabel.Text = $"{(active ? "STORM · DANGER" : "STORM · LEAVE CIRCLE")}\n{view.HazardRemainingTicks / 30d:F1}s";
    }
    private static Vector3 Point(CorePosition p) => new(p.X * .001f, 0, p.Z * .001f);
    private static Label3D Label(Vector3 at) => new() { Position = at, FontSize = 37, PixelSize = .012f, OutlineSize = 5, NoDepthTest = true, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled };
    private static MeshInstance3D Mesh(Mesh shape, Vector3 at, Color color, Node parent)
    { var mesh = new MeshInstance3D { Mesh = shape, Position = at, MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 1, Transparency = color.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled } }; parent.AddChild(mesh); return mesh; }
}
