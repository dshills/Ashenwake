namespace Ashenwake.Client;

/// <summary>Shared wardrobe colors for equipped meshes and their inventory silhouettes.</summary>
internal sealed record ArmorPalette(string Shell, string Cloth, string Trim, bool Metallic = false)
{
    public const string Leather = "513e36", Ivory = "dac7a2", Ember = "d68450";

    public static ArmorPalette For(string discipline) => discipline.ToLowerInvariant() switch
    {
        "veilwalker" => new("56506e", "302d43", "b6a0d8"),
        "arcanist" => new("526e91", "293950", "d3b875"),
        "gravecaller" => new("b7ae98", "353f42", Ivory),
        "warden" => new("776846", "344c3d", "b8c78b"),
        _ => new("899ba4", "304c58", "cbb58b", Metallic: true)
    };
}
