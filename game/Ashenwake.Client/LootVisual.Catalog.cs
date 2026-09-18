using Godot;

namespace Ashenwake.Client;

public partial class LootVisual
{
    // Four immutable primitives and at most nineteen merged templates, independent of item instance count.
    private static readonly BoxMesh CubeMesh = new() { Size = Vector3.One };
    private static readonly SphereMesh OrbMesh = new() { Radius = .5f, Height = 1, RadialSegments = 10, Rings = 5 };
    private static readonly CylinderMesh ConeMesh = new() { BottomRadius = .5f, TopRadius = 0, Height = 1, RadialSegments = 8, Rings = 1 };
    private static readonly TorusMesh RingMesh = new() { InnerRadius = .46f, OuterRadius = .5f, Rings = 24, RingSegments = 6 };
    private static readonly Mesh?[] RarityMarkerMeshes = new Mesh?[6];
    private static Mesh? SelectionMesh;
    private sealed record Piece(Mesh Mesh, Transform3D Transform, Surface Surface);

    private static Mesh BuildMarkerMesh(int rarityTier)
    {
        using var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        if (rarityTier > 0)
            surface.AppendFrom(RingMesh, 0, new Transform3D(Basis.Identity.Scaled(new(.86f, .30f, .86f)), new(0, .025f, 0)));
        int count = rarityTier > 0 ? rarityTier : 4;
        for (int i = 0; i < count; i++)
        {
            float angle = rarityTier > 0 ? (i - (rarityTier - 1) * .5f) * .24f : i * Mathf.Pi / 2;
            float radius = rarityTier > 0 ? .43f : .50f;
            var size = rarityTier > 0 ? new Vector3(.043f, .025f, .043f) : new Vector3(.10f, .025f, .025f);
            var basis = Basis.FromEuler(new(0, rarityTier > 0 ? Mathf.Pi / 4 : angle, 0)).ScaledLocal(size);
            surface.AppendFrom(CubeMesh, 0, new Transform3D(basis, new(MathF.Sin(angle) * radius, .04f, MathF.Cos(angle) * radius)));
        }
        return surface.Commit();
    }

    private static ModelPart[] BuildModel(ItemStyle style)
    {
        var pieces = new List<Piece>();
        void Part(Mesh mesh, Vector3 at, Vector3 size, Surface surface, Vector3 rotation = default)
            => pieces.Add(new(mesh, new Transform3D(Basis.FromEuler(rotation).ScaledLocal(size), at), surface));
        void Box(Vector3 at, Vector3 size, Surface surface, float yaw = 0)
            => Part(CubeMesh, at, size, surface, new(0, yaw, 0));
        void Orb(Vector3 at, Vector3 size, Surface surface) => Part(OrbMesh, at, size, surface);
        void Ring(Vector3 at, Vector3 size, Surface surface) => Part(RingMesh, at, size, surface);
        void Point(Vector3 at, Vector3 size, Surface surface, float pitch = Mathf.Pi / 2)
            => Part(ConeMesh, at, size, surface, new(pitch, 0, 0));
        void Handle(float length = .67f)
        {
            Box(new(0, 0, .025f), new(.06f, .065f, length), Surface.Leather);
            Box(new(0, .007f, length * .40f), new(.085f, .08f, .065f), Surface.Metal);
            Box(new(0, .01f, length * .22f), new(.075f, .075f, .035f), Surface.Dark);
        }

        switch (style)
        {
            case ItemStyle.Axe:
            case ItemStyle.Ashcleaver:
                Handle();
                Box(new(.10f, .005f, -.19f), new(.30f, .075f, .15f), Surface.Dark, -.28f);
                Box(new(.22f, .005f, -.18f), new(.085f, .09f, .28f), Surface.Metal, -.28f);
                Box(new(-.09f, .005f, -.23f), new(.18f, .07f, .09f), Surface.Metal, -.2f);
                if (style == ItemStyle.Ashcleaver)
                {
                    Box(new(.22f, .055f, -.18f), new(.024f, .017f, .23f), Surface.Ember, -.28f);
                    Box(new(.10f, .055f, -.2f), new(.17f, .017f, .018f), Surface.Ember, .35f);
                    Point(new(.15f, .02f, -.35f), new(.10f, .18f, .07f), Surface.Dark, -Mathf.Pi / 2);
                    Orb(new(0, .035f, -.19f), new(.10f, .055f, .10f), Surface.Ember);
                }
                break;
            case ItemStyle.Saber:
                Box(new(0, 0, .27f), new(.065f, .07f, .22f), Surface.Leather);
                Box(new(0, .005f, .16f), new(.23f, .075f, .05f), Surface.Metal);
                Box(new(0, .005f, -.10f), new(.10f, .045f, .47f), Surface.Metal, -.08f);
                Point(new(.02f, .005f, -.36f), new(.10f, .14f, .045f), Surface.Metal, -Mathf.Pi / 2);
                Box(new(-.025f, .032f, -.08f), new(.015f, .012f, .40f), Surface.Rarity, -.08f);
                break;
            case ItemStyle.Hammer:
                Handle(.60f);
                Box(new(0, .02f, -.19f), new(.40f, .16f, .20f), Surface.Dark);
                foreach (int side in new[] { -1, 1 }) Box(new(side * .17f, .02f, -.19f), new(.09f, .18f, .22f), Surface.Metal);
                Box(new(0, .105f, -.19f), new(.045f, .012f, .17f), Surface.Rarity);
                break;
            case ItemStyle.Pike:
                Handle(.70f);
                Point(new(0, .015f, -.36f), new(.15f, .22f, .06f), Surface.Metal, -Mathf.Pi / 2);
                Box(new(0, .02f, -.26f), new(.09f, .08f, .045f), Surface.Rarity);
                break;
            case ItemStyle.Greatstaff:
                Handle(.72f);
                Ring(new(0, .035f, -.29f), new(.26f, .70f, .26f), Surface.Bone);
                Orb(new(0, .035f, -.29f), new(.10f, .10f, .10f), Surface.Rarity);
                Box(new(0, .015f, -.14f), new(.12f, .10f, .045f), Surface.Metal);
                break;
            case ItemStyle.Shield:
                Orb(new(0, .015f, 0), new(.54f, .10f, .66f), Surface.Dark);
                Orb(new(0, .055f, 0), new(.45f, .055f, .56f), Surface.Metal);
                Box(new(0, .09f, 0), new(.045f, .018f, .44f), Surface.Rarity);
                Box(new(0, .09f, -.06f), new(.31f, .018f, .04f), Surface.Rarity);
                Orb(new(0, .105f, -.045f), new(.10f, .06f, .10f), Surface.Bone);
                break;
            case ItemStyle.Focus:
                Box(Vector3.Zero, new(.38f, .09f, .48f), Surface.Dark);
                Box(new(.01f, .052f, 0), new(.31f, .02f, .41f), Surface.Bone);
                Box(new(0, .07f, 0), new(.36f, .025f, .46f), Surface.Cloth);
                Ring(new(0, .09f, 0), new(.19f, .6f, .19f), Surface.Metal);
                Orb(new(0, .10f, 0), new(.085f, .06f, .085f), Surface.Rarity);
                break;
            case ItemStyle.Robe:
                Box(new(0, 0, .055f), new(.44f, .08f, .45f), Surface.Cloth);
                Box(new(0, .04f, .03f), new(.36f, .055f, .35f), Surface.Cloth);
                foreach (int side in new[] { -1, 1 }) Box(new(side * .18f, .055f, -.08f), new(.12f, .07f, .25f), Surface.Cloth, side * .30f);
                Ring(new(0, .04f, -.15f), new(.16f, .40f, .15f), Surface.Bone);
                Box(new(0, .075f, .13f), new(.40f, .025f, .045f), Surface.Rarity);
                break;
            case ItemStyle.Plate:
                Box(new(0, 0, .03f), new(.43f, .11f, .47f), Surface.Dark);
                Orb(new(0, .065f, -.045f), new(.44f, .14f, .31f), Surface.Metal);
                Box(new(0, .095f, .11f), new(.37f, .045f, .10f), Surface.Metal);
                foreach (int side in new[] { -1, 1 }) Box(new(side * .175f, .04f, -.16f), new(.085f, .065f, .22f), Surface.Metal, side * .2f);
                Box(new(0, .14f, -.05f), new(.065f, .012f, .15f), Surface.Rarity);
                break;
            case ItemStyle.Helmet:
                Orb(new(0, .025f, -.04f), new(.43f, .28f, .39f), Surface.Dark);
                Orb(new(0, .07f, -.065f), new(.40f, .22f, .35f), Surface.Metal);
                Box(new(0, .05f, .125f), new(.32f, .04f, .035f), Surface.Dark);
                Box(new(0, .09f, .13f), new(.055f, .19f, .045f), Surface.Metal);
                Box(new(0, .19f, -.075f), new(.035f, .06f, .25f), Surface.Rarity);
                break;
            case ItemStyle.Shoulders:
                foreach (int side in new[] { -1, 1 })
                {
                    Orb(new(side * .16f, .015f, 0), new(.26f, .14f, .38f), Surface.Dark);
                    Orb(new(side * .16f, .05f, -.025f), new(.27f, .095f, .27f), Surface.Metal);
                    Box(new(side * .16f, .10f, -.025f), new(.15f, .018f, .025f), Surface.Rarity);
                }
                break;
            case ItemStyle.Gloves:
                foreach (int side in new[] { -1, 1 })
                {
                    Box(new(side * .12f, 0, .04f), new(.16f, .075f, .25f), Surface.Leather, side * -.12f);
                    Box(new(side * .12f, .048f, .10f), new(.17f, .035f, .07f), Surface.Metal);
                    Box(new(side * .20f, 0, -.015f), new(.065f, .065f, .105f), Surface.Leather, side * -.35f);
                    Box(new(side * .12f, .05f, -.035f), new(.115f, .024f, .07f), Surface.Rarity);
                }
                break;
            case ItemStyle.Belt:
                Ring(Vector3.Zero, new(.60f, .85f, .39f), Surface.Leather);
                Box(new(0, .035f, .19f), new(.18f, .05f, .10f), Surface.Metal);
                Box(new(0, .065f, .19f), new(.08f, .012f, .065f), Surface.Rarity);
                break;
            case ItemStyle.Legs:
                Box(new(0, 0, -.14f), new(.36f, .08f, .15f), Surface.Leather);
                foreach (int side in new[] { -1, 1 })
                {
                    Box(new(side * .10f, 0, .085f), new(.155f, .075f, .36f), Surface.Cloth, side * .045f);
                    Box(new(side * .10f, .05f, .035f), new(.14f, .04f, .13f), Surface.Metal);
                }
                Box(new(0, .05f, -.14f), new(.055f, .018f, .07f), Surface.Rarity);
                break;
            case ItemStyle.Boots:
                foreach (int side in new[] { -1, 1 })
                {
                    Box(new(side * .13f, 0, .045f), new(.19f, .085f, .35f), Surface.Dark);
                    Box(new(side * .13f, .06f, -.03f), new(.17f, .13f, .18f), Surface.Leather);
                    Box(new(side * .13f, .05f, .14f), new(.195f, .055f, .14f), Surface.Metal);
                    Box(new(side * .13f, .135f, -.03f), new(.18f, .025f, .18f), Surface.Rarity);
                }
                break;
            case ItemStyle.Ring:
                Ring(Vector3.Zero, new(.38f, .90f, .38f), Surface.Metal);
                Box(new(0, .02f, -.18f), new(.13f, .055f, .12f), Surface.Dark);
                Orb(new(0, .065f, -.18f), new(.10f, .075f, .095f), Surface.Rarity);
                break;
            case ItemStyle.Amulet:
                Ring(new(0, 0, -.045f), new(.38f, .45f, .44f), Surface.Metal);
                Box(new(0, .015f, .20f), new(.19f, .045f, .19f), Surface.Dark, Mathf.Pi / 4);
                Box(new(0, .045f, .20f), new(.125f, .025f, .125f), Surface.Metal, Mathf.Pi / 4);
                Orb(new(0, .07f, .20f), new(.09f, .045f, .09f), Surface.Rarity);
                break;
            default:
                Box(Vector3.Zero, new(.40f, .16f, .34f), Surface.Cloth);
                Box(new(0, .085f, 0), new(.035f, .018f, .35f), Surface.Leather);
                Box(new(0, .085f, 0), new(.41f, .018f, .035f), Surface.Leather);
                Orb(new(0, .11f, 0), new(.09f, .06f, .09f), Surface.Rarity);
                break;
        }
        var result = new List<ModelPart>();
        foreach (var group in pieces.GroupBy(p => p.Surface))
        {
            using var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
            foreach (var piece in group) surface.AppendFrom(piece.Mesh, 0, piece.Transform);
            result.Add(new(surface.Commit(), group.Key));
        }
        return result.ToArray();
    }
}
