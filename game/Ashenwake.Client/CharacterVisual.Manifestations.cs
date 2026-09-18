using Godot;

namespace Ashenwake.Client;

public partial class CharacterVisual
{
    private bool _reducedVisualEffects;
    private Node3D? _shadowForm;
    private StandardMaterial3D? _burningFissures, _shadowSurface;

    /// <summary>Retains the mutation's silhouette while suppressing cosmetic pulsing and echo motion.</summary>
    public void SetReducedEffects(bool reduced)
    {
        if (_reducedVisualEffects == reduced) return;
        _reducedVisualEffects = reduced;
        if (!reduced) return;
        if (_burningFissures is not null) _burningFissures.EmissionEnergyMultiplier = 1.3f;
        if (_shadowSurface is not null) _shadowSurface.AlbedoColor = new Color(.42f, .31f, .72f, .40f);
        if (_shadowForm is not null) _shadowForm.Position = Vector3.Zero;
    }

    private void BuildManifestations(int mask)
    {
        if ((mask & 1) != 0)
        {
            _burningFissures = new StandardMaterial3D
            {
                AlbedoColor = new("ff9952"),
                EmissionEnabled = true,
                Emission = new("ff7137"),
                EmissionEnergyMultiplier = 1.3f,
                Roughness = .7f
            };
            BuildModule(BodyRoot, "ManifestationBurning", "manifestation/burning", n =>
            {
                Material charred = SharedMaterial("3b292b");
                for (int side = -1; side <= 1; side += 2)
                {
                    Box(n, new(side * .18f, 1.42f, -.382f), new(.20f, .32f, .05f), charred, new(0, 0, side * -14));
                    Rod(n, new(side * .13f, 1.64f, -.424f), new(side * .23f, 1.46f, -.425f), .018f, _burningFissures);
                    Rod(n, new(side * .23f, 1.46f, -.425f), new(side * .12f, 1.29f, -.425f), .024f, _burningFissures);
                    Rod(n, new(side * .12f, 1.29f, -.425f), new(side * .22f, 1.12f, -.394f), .014f, _burningFissures);
                    Rod(n, new(side * .20f, 1.42f, -.426f), new(side * .31f, 1.37f, -.38f), .013f, _burningFissures);
                    Cone(n, new(side * .47f, 1.77f, .03f), .075f, 0, .27f, _burningFissures, new(0, 0, side * -22));
                }
            });
            BuildModule(_equipmentRight, "ManifestationBurningHand", "manifestation/burning-hand", n =>
            {
                Rod(n, new(.08f, -.27f, -.167f), new(-.005f, -.45f, -.18f), .017f, _burningFissures);
                Rod(n, new(-.005f, -.45f, -.18f), new(.09f, -.59f, -.16f), .02f, _burningFissures);
            });
        }
        if ((mask & 2) != 0)
        {
            // Each hero owns this translucent material; pulsing never changes another actor or preview.
            _shadowSurface = new StandardMaterial3D
            {
                AlbedoColor = new(.42f, .31f, .72f, .40f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                Roughness = 1,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
            };
            _shadowForm = BuildModule(BodyRoot, "ManifestationShadow", "manifestation/shadow", n =>
            {
                Material eye = SharedMaterial("c9afff", emissive: true);
                for (int side = -1; side <= 1; side += 2)
                {
                    float x = side * .48f;
                    Orb(n, new(x, 1.96f, .34f), new(.33f, .43f, .31f), _shadowSurface);
                    var echo = Cone(n, new(x, 1.20f, .34f), .07f, .23f, 1.2f, _shadowSurface);
                    echo.Scale = new(1, 1, .6f);
                    Rod(n, new(x + side * .15f, 1.66f, .34f), new(x + side * .27f, .98f, .31f), .055f, _shadowSurface);
                    Orb(n, new(x - .052f, 1.99f, .177f), new(.036f, .027f, .022f), eye);
                    Orb(n, new(x + .052f, 1.99f, .177f), new(.036f, .027f, .022f), eye);
                }
            });
        }
        if ((mask & 4) != 0)
        {
            BuildModule(BodyRoot, "ManifestationStone", "manifestation/stone", n =>
            {
                Material stone = SharedMaterial("758184"), seam = SharedMaterial("b7d6c9", emissive: true);
                for (int side = -1; side <= 1; side += 2)
                {
                    Box(n, new(side * .45f, 1.70f, -.04f), new(.33f, .30f, .40f), stone, new(7, side * 13, side * 19));
                    Box(n, new(side * .28f, 1.28f, -.34f), new(.23f, .31f, .10f), stone, new(0, side * 15, side * 17));
                    Box(n, new(side * .38f, 1.62f, -.27f), new(.11f, .16f, .075f), stone, new(0, 0, side * -16));
                    Rod(n, new(side * .35f, 1.31f, -.402f), new(side * .26f, 1.24f, -.407f), .013f, seam);
                    Cone(n, new(side * .47f, 1.93f, .02f), .095f, 0, .30f, stone, new(0, 0, side * -16));
                }
            });
        }
        if ((mask & 8) != 0)
        {
            BuildModule(BodyRoot, "ManifestationRenewal", "manifestation/renewal", n =>
            {
                Material vine = SharedMaterial("406649"), leaf = SharedMaterial("94b975"), bud = SharedMaterial("e6b57f", emissive: true);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 previous = new(side * .27f, 1.0f, -.30f);
                    for (int segment = 0; segment < 5; segment++)
                    {
                        float y = 1.13f + segment * .13f;
                        float x = side * (.23f + (segment % 2 == 0 ? -.055f : .055f));
                        Vector3 next = new(x, y, -.442f);
                        Rod(n, previous, next, .025f, vine); previous = next;
                        var foliage = Orb(n, next + new Vector3(side * .07f, .025f, -.01f), new(.19f, .065f, .06f), leaf);
                        foliage.RotationDegrees = new(0, 0, side * -32);
                    }
                    Rod(n, previous, new(side * .46f, 1.77f, -.19f), .028f, vine);
                    Rod(n, new(side * .46f, 1.77f, -.19f), new(side * .56f, 1.98f, -.07f), .021f, vine);
                    for (int petal = 0; petal < 3; petal++)
                    {
                        var foliage = Orb(n, new(side * (.46f + petal * .045f), 1.80f + petal * .075f, -.19f + petal * .05f), new(.19f, .085f, .08f), leaf);
                        foliage.RotationDegrees = new(0, 0, side * (-25 + petal * 20));
                    }
                    Orb(n, new(side * .56f, 2.015f, -.07f), new(.12f, .16f, .12f), bud);
                }
            });
        }
    }

    private void AnimateManifestations()
    {
        if (_reducedVisualEffects) return;
        if (_burningFissures is not null)
            _burningFissures.EmissionEnergyMultiplier = 1.3f + MathF.Sin((float)_time * 3.1f) * .18f;
        if (_shadowForm is not null)
            _shadowForm.Position = new(MathF.Sin((float)_time * 1.8f) * .025f, MathF.Sin((float)_time * 2.1f) * .022f, .025f * MathF.Sin((float)_time * 1.4f));
        if (_shadowSurface is not null)
            _shadowSurface.AlbedoColor = new(.42f, .31f, .72f, .40f + MathF.Sin((float)_time * 1.7f) * .035f);
    }
}
