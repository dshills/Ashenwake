using Godot;

namespace Ashenwake.Client;

public enum SurfaceKind { Stone, Wood, Metal, Cloth, Skin, Bone, Earth }

/// <summary>Scene-thread material factory. Seven reusable texture sets are the entire cache;
/// callers own materials so obstacle fading and character accents cannot affect another model.</summary>
public static class SurfaceMaterials
{
    private const int TextureSize = 128;
    private sealed record TextureSet(ImageTexture Albedo, ImageTexture Normal, ImageTexture Roughness);
    private static readonly Dictionary<SurfaceKind, TextureSet> Textures = [];
    internal static int CachedTextureCount => Textures.Count * 3;

    public static StandardMaterial3D Create(string color, SurfaceKind kind, bool emissive = false, bool worldScale = false)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var material = new StandardMaterial3D
        {
            ResourceName = "Surface_" + kind + "_" + color,
            AlbedoColor = new Color(color),
            Roughness = emissive ? .8f : 1,
            Metallic = kind == SurfaceKind.Metal && !emissive ? .62f : 0,
            MetallicSpecular = kind is SurfaceKind.Metal or SurfaceKind.Skin or SurfaceKind.Bone ? .5f : .32f,
            EmissionEnabled = emissive,
            Emission = new Color(color),
            EmissionEnergyMultiplier = 1.2f
        };
        // Eyes, runes and announced mechanics keep their clean authored silhouettes.
        if (emissive) return material;
        if (!Textures.TryGetValue(kind, out var set)) Textures.Add(kind, set = CreateTextures(kind));
        material.AlbedoTexture = set.Albedo;
        material.RoughnessTexture = set.Roughness;
        material.RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Red;
        material.NormalEnabled = true;
        material.NormalTexture = set.Normal;
        material.NormalScale = kind is SurfaceKind.Skin or SurfaceKind.Bone ? .3f : kind == SurfaceKind.Cloth ? .4f : .55f;
        material.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
        material.TextureRepeat = true;
        if (worldScale)
        {
            // Merged environment vertices already use scene units. Local triplanar mapping
            // gives stone a consistent grain without texture sliding on animated boss props.
            material.Uv1Triplanar = true;
            material.Uv1WorldTriplanar = false;
            material.Uv1TriplanarSharpness = 8;
            material.Uv1Scale = Vector3.One * (kind == SurfaceKind.Wood ? .65f : kind == SurfaceKind.Earth ? .55f : .8f);
        }
        return material;
    }

    /// <summary>Existing authored palette identities distinguish surfaces without recoloring them.
    /// An explicit builder override handles reused colors; unclassified scenery remains stone.</summary>
    internal static SurfaceKind EnvironmentKind(string color) => color switch
    {
        "55473b" or "8c7457" or "4e4a45" or "414b36" or "64704a" or "73664b" or "655944" or "9a9470" => SurfaceKind.Wood,
        "303b40" or "ad8a57" or "535650" or "ad8b57" or "dec895" or "41454b" or "727477" or "785440" or "a07753" or "9c9070" or "6d6c5d" => SurfaceKind.Metal,
        "397e7a" or "629a8c" or "657879" or "698d80" or "74625c" or "566878" or "b6a57a" => SurfaceKind.Cloth,
        "a69d77" or "7d8161" or "9eabaf" or "c2c8bc" or "7f908f" or "c8bd9b" or "e5d7b5" => SurfaceKind.Bone,
        "526844" or "466653" or "6b8058" or "304d43" => SurfaceKind.Earth,
        _ => SurfaceKind.Stone
    };

    private static TextureSet CreateTextures(SurfaceKind kind)
    {
        var albedo = new byte[TextureSize * TextureSize * 4];
        var roughness = new byte[albedo.Length];
        var normals = new byte[albedo.Length];
        var heights = new float[TextureSize * TextureSize];
        for (int y = 0; y < TextureSize; y++)
            for (int x = 0; x < TextureSize; x++)
            {
                var sample = Sample(kind, x / (float)TextureSize, y / (float)TextureSize);
                int index = y * TextureSize + x;
                heights[index] = sample.Height;
                Gray(albedo, index * 4, sample.Albedo);
                Gray(roughness, index * 4, sample.Roughness);
            }
        float At(int x, int y) => heights[((y + TextureSize) % TextureSize) * TextureSize + (x + TextureSize) % TextureSize];
        for (int y = 0; y < TextureSize; y++)
            for (int x = 0; x < TextureSize; x++)
            {
                var normal = new Vector3((At(x - 1, y) - At(x + 1, y)) * 7,
                    (At(x, y + 1) - At(x, y - 1)) * 7, 1).Normalized();
                int index = (y * TextureSize + x) * 4;
                normals[index] = Byte(normal.X * .5f + .5f);
                normals[index + 1] = Byte(normal.Y * .5f + .5f);
                normals[index + 2] = Byte(normal.Z * .5f + .5f);
                normals[index + 3] = 255;
            }
        return new(Texture(albedo, kind + "_Grain"), Texture(normals, kind + "_Normal", true), Texture(roughness, kind + "_Roughness"));
    }

    private static (float Albedo, float Height, float Roughness) Sample(SurfaceKind kind, float u, float v)
    {
        float broad = Noise(u, v, 4, 4, 17), grain = Noise(u, v, 19, 19, 73), fine = Noise(u, v, 47, 47, 139);
        switch (kind)
        {
            case SurfaceKind.Wood:
                float warp = u * Mathf.Tau * 11 + MathF.Sin(v * Mathf.Tau * 2) * .65f + Noise(u, v, 3, 4, 29) * 2;
                float rings = MathF.Pow(MathF.Abs(MathF.Sin(warp)), 8);
                return (.94f + broad * .045f - rings * .08f, broad * .18f + grain * .06f - rings * .085f, .76f + grain * .17f);
            case SurfaceKind.Metal:
                float brush = Noise(u, v, 3, 61, 47);
                return (.94f + brush * .045f - grain * .015f, brush * .06f + grain * .025f, .38f + grain * .18f);
            case SurfaceKind.Cloth:
                float weave = MathF.Sin(u * Mathf.Tau * 32) * MathF.Sin(v * Mathf.Tau * 32);
                return (.97f + weave * .018f - broad * .025f, weave * .025f + broad * .045f, .9f + grain * .07f);
            case SurfaceKind.Skin:
                return (.965f + broad * .03f, broad * .055f + fine * .022f, .64f + grain * .11f);
            case SurfaceKind.Bone:
                return (.94f + broad * .055f - fine * .018f, broad * .11f + grain * .035f, .65f + grain * .14f);
            case SurfaceKind.Earth:
                return (.85f + broad * .10f + grain * .045f, broad * .25f + grain * .12f + fine * .035f, .92f + grain * .065f);
            default:
                // Broad mineral wear and shallow pits remain quiet underneath combat warnings.
                return (.865f + broad * .10f + fine * .03f, broad * .29f + grain * .115f + fine * .035f, .84f + grain * .13f);
        }
    }

    private static float Noise(float u, float v, int columns, int rows, uint seed)
    {
        float px = u * columns, py = v * rows;
        int x = (int)MathF.Floor(px), y = (int)MathF.Floor(py);
        float fx = px - x, fy = py - y;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        float a = Hash(x % columns, y % rows, seed), b = Hash((x + 1) % columns, y % rows, seed);
        float c = Hash(x % columns, (y + 1) % rows, seed), d = Hash((x + 1) % columns, (y + 1) % rows, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    private static float Hash(int x, int y, uint seed)
    {
        uint value = unchecked((uint)x * 374761393u + (uint)y * 668265263u + seed * 1442695041u);
        value = unchecked((value ^ value >> 13) * 1274126177u);
        return ((value ^ value >> 16) & 65535) / 65535f;
    }

    private static byte Byte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
    private static void Gray(byte[] pixels, int at, float value)
    { pixels[at] = pixels[at + 1] = pixels[at + 2] = Byte(value); pixels[at + 3] = 255; }

    private static ImageTexture Texture(byte[] pixels, string name, bool normal = false)
    {
        using var image = Image.CreateFromData(TextureSize, TextureSize, false, Image.Format.Rgba8, pixels);
        image.GenerateMipmaps(normal);
        var texture = ImageTexture.CreateFromImage(image); texture.ResourceName = name;
        return texture;
    }
}
