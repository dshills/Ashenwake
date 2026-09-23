using Godot;

namespace Ashenwake.Client;

public static partial class AppearanceVisualChecks
{
    private static void CheckSurfaces(Action<bool, string> require)
    {
        var identities = new HashSet<ulong>();
        foreach (var kind in Enum.GetValues<SurfaceKind>())
        {
            using var first = SurfaceMaterials.Create("87918a", kind);
            using var second = SurfaceMaterials.Create("55473b", kind, worldScale: true);
            var textures = new[] { first.AlbedoTexture, first.NormalTexture, first.RoughnessTexture };
            var reused = new[] { second.AlbedoTexture, second.NormalTexture, second.RoughnessTexture };
            require(first.GetInstanceId() != second.GetInstanceId() &&
                textures.Zip(reused).All(pair => pair.First.GetInstanceId() == pair.Second.GetInstanceId()),
                "surface_" + kind + "_reuses_textures_across_independent_materials");
            bool bounded = true;
            foreach (var texture in textures)
            {
                identities.Add(texture.GetInstanceId());
                using var image = texture.GetImage();
                bounded &= image.GetWidth() == 512 && image.GetHeight() == 512 && image.GetFormat() == Image.Format.Rgba8 &&
                    image.HasMipmaps() && image.GetMipmapCount() == 9 && image.GetData().Length < 1_400_000;
            }
            require(bounded, "surface_" + kind + "_textures_have_bounded_complete_mipmaps");
            require(first.NormalEnabled && first.NormalScale is > 0 and <= 1 &&
                ValidSurfaceNormals(first.NormalTexture) && OpaqueSurfaceTexture(first.AlbedoTexture) && OpaqueSurfaceTexture(first.RoughnessTexture),
                "surface_" + kind + "_normal_vectors_are_unit_length_and_maps_are_opaque");
            require(first.AlbedoColor == new Color("87918a") && second.AlbedoColor == new Color("55473b") &&
                first.RoughnessTextureChannel == BaseMaterial3D.TextureChannel.Red && first.TextureRepeat &&
                first.TextureFilter == BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                "surface_" + kind + "_retains_palette_and_filters_distant_detail");
            require(!first.Uv1Triplanar && second.Uv1Triplanar && !second.Uv1WorldTriplanar &&
                second.Uv1Scale.IsFinite() && second.Uv1Scale.X > 0 && !second.HeightmapEnabled,
                "surface_" + kind + "_uses_uvs_on_characters_and_stable_triplanar_on_environment");
            using var glow = SurfaceMaterials.Create("f5be65", kind, emissive: true, worldScale: true);
            require(glow.EmissionEnabled && glow.Emission == new Color("f5be65") && !glow.NormalEnabled &&
                glow.AlbedoTexture is null && glow.NormalTexture is null && glow.RoughnessTexture is null && !glow.Uv1Triplanar,
                "surface_" + kind + "_emissive_art_remains_smooth_and_untextured");
        }
        require(identities.Count == 21 && SurfaceMaterials.CachedTextureCount == 21, "surface_texture_cache_has_exactly_seven_shared_sets");
        for (int variation = 0; variation < 64; variation++)
        {
            using var temporary = SurfaceMaterials.Create(new Color(.3f + variation * .002f, .4f, .5f).ToHtml(), SurfaceKind.Stone);
        }
        require(SurfaceMaterials.CachedTextureCount == 21, "surface_palette_variants_do_not_expand_texture_cache");

        using var metal = SurfaceMaterials.Create("727477", SurfaceKind.Metal);
        using var cloth = SurfaceMaterials.Create("657879", SurfaceKind.Cloth);
        require(metal.Metallic > .5f && cloth.Metallic == 0 &&
            metal.RoughnessTexture.GetInstanceId() != cloth.RoughnessTexture.GetInstanceId() &&
            MeanSurfaceRed(metal.RoughnessTexture) + .3f < MeanSurfaceRed(cloth.RoughnessTexture),
            "surface_metal_reflects_more_cleanly_than_cloth");
        CheckSurfaceIsolationAndBatching(require);
        CheckSurfaceTangents(require);
    }

    private static bool ValidSurfaceNormals(Texture2D texture)
    {
        using var image = texture.GetImage();
        byte[] pixels = image.GetData();
        // Include the generated mip levels: distant materials must retain valid normals too.
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            var normal = new Vector3(pixels[offset] / 127.5f - 1, pixels[offset + 1] / 127.5f - 1, pixels[offset + 2] / 127.5f - 1);
            if (!normal.IsFinite() || Math.Abs(normal.LengthSquared() - 1) > .025f || normal.Z <= 0 || pixels[offset + 3] != 255) return false;
        }
        return true;
    }

    private static bool OpaqueSurfaceTexture(Texture2D texture)
    {
        using var image = texture.GetImage();
        byte[] pixels = image.GetData();
        for (int offset = 3; offset < pixels.Length; offset += 4) if (pixels[offset] != 255) return false;
        return true;
    }

    private static float MeanSurfaceRed(Texture2D texture)
    {
        using var image = texture.GetImage();
        byte[] pixels = image.GetData(); int count = image.GetWidth() * image.GetHeight();
        long sum = 0;
        for (int pixel = 0; pixel < count; pixel++) sum += pixels[pixel * 4];
        return sum / (255f * count);
    }

    private static void CheckSurfaceIsolationAndBatching(Action<bool, string> require)
    {
        var root = new Node3D();
        try
        {
            var builder = new EnvironmentBuilder(root, "SurfaceCheck");
            builder.Box(new(2, 1, 1), Vector3.Zero, "64706d");
            builder.Box(new(1, 2, 1), Vector3.Right * 3, "64706d");
            builder.Cylinder(.4f, .3f, 1, Vector3.Left * 3, "64706d");
            builder.Torus(.3f, .4f, Vector3.Up * 2, "ad8b57");
            builder.Flush();
            var meshes = Descendants(root).OfType<MeshInstance3D>().ToArray();
            require(meshes.Length == 2 && meshes.All(mesh => mesh.Mesh.GetSurfaceCount() == 1) && CosmeticOnly(root),
                "surface_environment_preserves_palette_batching_without_gameplay_bodies");
            var stoneMesh = meshes.Single(mesh => ((StandardMaterial3D)mesh.Mesh.SurfaceGetMaterial(0)).AlbedoColor == new Color("64706d"));
            var original = (StandardMaterial3D)stoneMesh.Mesh.SurfaceGetMaterial(0);
            using var independent = SurfaceMaterials.Create("64706d", SurfaceKind.Stone, worldScale: true);
            using var faded = (StandardMaterial3D)original.Duplicate();
            faded.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            faded.AlbedoColor = new Color(faded.AlbedoColor, .25f);
            require(original.AlbedoColor.A == 1 && independent.AlbedoColor.A == 1 && faded.AlbedoColor.A == .25f &&
                original.Transparency == BaseMaterial3D.TransparencyEnum.Disabled &&
                faded.AlbedoTexture.GetInstanceId() == original.AlbedoTexture.GetInstanceId() &&
                faded.NormalTexture.GetInstanceId() == independent.NormalTexture.GetInstanceId(),
                "surface_occluder_fade_mutates_only_its_owned_material_and_reuses_textures");
            require(meshes.All(mesh =>
            {
                var arrays = mesh.Mesh.SurfaceGetArrays(0);
                var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                return vertices.Length == normals.Length && vertices.All(v => v.IsFinite()) &&
                    normals.All(n => n.IsFinite() && n.LengthSquared() > .001f) &&
                    mesh.Mesh.SurfaceGetMaterial(0) is StandardMaterial3D { NormalEnabled: true, Uv1Triplanar: true };
            }), "surface_merged_environment_keeps_finite_normals_for_triplanar_mapping");
        }
        finally { root.Free(); }
    }

    private static void CheckSurfaceTangents(Action<bool, string> require)
    {
        using var sphere = new SphereMesh { Radius = .4f, Height = .8f };
        using var cylinder = new CylinderMesh { BottomRadius = .4f, TopRadius = .25f, Height = 1 };
        var box = CharacterVisual.PolishedBoxMesh(new(.42f, .46f, .08f));
        foreach (var (name, mesh) in new (string, Mesh)[] { ("beveled_box", box), ("sphere", sphere), ("cylinder", cylinder) })
        {
            var arrays = mesh.SurfaceGetArrays(0);
            int count = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length;
            var uv = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            var tangents = arrays[(int)Mesh.ArrayType.Tangent].AsFloat32Array();
            bool valid = uv.Length == count && uv.All(p => p.IsFinite()) && tangents.Length == count * 4;
            for (int offset = 0; offset < tangents.Length && valid; offset += 4)
            {
                var tangent = new Vector3(tangents[offset], tangents[offset + 1], tangents[offset + 2]);
                valid &= tangent.IsFinite() && Math.Abs(tangent.LengthSquared() - 1) < .025f && Math.Abs(Math.Abs(tangents[offset + 3]) - 1) < .001f;
            }
            require(valid, "surface_" + name + "_has_valid_uv_tangents_for_character_normal_maps");
        }
    }
}
