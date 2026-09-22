using Godot;

namespace Ashenwake.Client;

/// <summary>Small, room-owned pools of light at authored flame positions. The floor remains
/// readable under the main lighting; these add local warmth without shadow maps or simulation state.</summary>
public partial class OpeningLighting : Node3D
{
    private readonly List<(OmniLight3D Light, float Energy)> _lights = [];
    private double _time;

    public string Style { get; private set; } = "";
    public int Capacity => _lights.Count;
    public int ActiveCount => _lights.Count(entry => entry.Light.Visible);
    public double MotionTime => _time;

    public static bool Supports(string style) => style is "greyhaven" or "road" or "crypt" or "monastery" or "sanctum";

    public static OpeningLighting Create(string style, float halfWidth, float halfDepth)
    {
        if (!Supports(style)) throw new ArgumentException("Unsupported opening lighting style.", nameof(style));
        if (!float.IsFinite(halfWidth) || halfWidth <= 0) throw new ArgumentOutOfRangeException(nameof(halfWidth));
        if (!float.IsFinite(halfDepth) || halfDepth <= 0) throw new ArgumentOutOfRangeException(nameof(halfDepth));
        var lighting = new OpeningLighting { Name = "OpeningLighting", Style = style };
        lighting.Build(halfWidth, halfDepth);
        lighting.Animate(0, true, false, "High");
        return lighting;
    }

    public void Animate(double delta, bool paused, bool reducedEffects, string quality)
    {
        // Reduced effects retains the authored light pools but removes all fluctuation,
        // including when the preference changes while the game is paused.
        if (reducedEffects) _time = 0;
        else if (!paused && double.IsFinite(delta) && delta > 0)
            _time = (_time + Math.Min(delta, .1)) % 24;
        int limit = GraphicsProfile.Normalize(quality) == "Performance" ? 3 : Capacity;
        for (int i = 0; i < _lights.Count; i++)
        {
            var (light, energy) = _lights[i];
            light.Visible = i < limit;
            float flutter = reducedEffects ? 0 : .035f * Mathf.Sin((float)(_time * Math.Tau / 8)) * Mathf.Cos(i * 1.7f) +
                .020f * Mathf.Sin((float)(_time * Math.Tau / 12)) * Mathf.Sin(i * 2.3f);
            light.LightEnergy = energy * (1 + flutter);
        }
    }

    private void Build(float x, float z)
    {
        // Keep the most useful three first for the Performance preset. Anchors match the
        // existing lanterns/candles in GreyhavenArt, GreyMarchArt and GreyMarchExplorationArt.
        if (Style == "greyhaven")
        {
            float forge = Mathf.Min(x * .22f, 3), lodge = -Mathf.Min(x * .60f, 7.2f);
            float healer = Mathf.Min(x * .71f, 8.5f);
            Add("Forge", new(forge + 1.2f, 1.25f, -z - .7f), "ffbd78", 2.1f, 6.5f);
            Add("WestLantern", new(-x - .72f, 2.22f, z * .37f + .39f), "ffd59a", 1.45f, 5.2f);
            Add("EastLantern", new(x + .72f, 2.22f, -z * .65f + .39f), "ffd59a", 1.45f, 5.2f);
            Add("LodgeLantern", new(lodge - 1.02f, 2.03f, -z - 1.37f), "ffce89", 1.6f, 5.5f);
            Add("HealerLantern", new(healer - 1.37f, 2.03f, -z - .995f), "ffe0ae", 1.2f, 4.5f);
            Add("WorkshopLantern", new(forge - 1.57f, 2.03f, -z - 1.32f), "ffce89", .95f, 4);
        }
        else if (Style == "road")
        {
            for (int i = 0; i < 6; i += 2)
                Add("GraveCandle" + i, new(-x * .28f + i * x * .23f + .67f, .625f,
                    -z - 2.05f - i % 3 * 1.15f - i * .13f), "f4c28a", 1.25f, 4.5f);
        }
        else if (Style == "monastery")
        {
            Add("WestArchCandle", new(-x - .9f, .625f, -z * .48f + 1.65f), "ffd099", 1.7f, 5);
            Add("SouthArchCandle", new(-x - .9f, .625f, z * .22f + 1.65f), "ffd099", 1.7f, 5);
        }
        else if (Style == "sanctum")
        {
            foreach (float side in new[] { -1f, 1f })
                Add(side < 0 ? "WestRitualCandle" : "EastRitualCandle",
                    new(side * (x - 1.2f), .975f, -z - 1.35f), "f6c582", 1.9f, 6);
        }
        else if (Style == "crypt")
        {
            Add("WestBurialCandle", new(-x - 1, 1.8f, -z * .66f + .56f), "ffca92", 1.55f, 5);
            foreach (int bay in new[] { 0, 4, 2 })
                Add("NicheCandle" + bay, new((bay - 2) * x * .36f + x * .13f, .45f, -z - .5f), "f3c18b", 1.55f, 5);
            Add("SouthBurialCandle", new(-x - 1, 1.8f, z * .20f + .56f), "ffca92", 1.55f, 5);
        }
    }

    private void Add(string name, Vector3 position, string color, float energy, float range)
    {
        var light = new OmniLight3D
        {
            Name = name,
            Position = position,
            LightColor = new Color(color),
            LightEnergy = energy,
            LightSpecular = .7f,
            OmniRange = range,
            OmniAttenuation = 1.35f,
            ShadowEnabled = false
        };
        AddChild(light);
        _lights.Add((light, energy));
    }
}
