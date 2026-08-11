using Godot;

/// <summary>
/// The chip geometry, taken out of the art pack once.
///
/// Same recipe as <see cref="DominoTileMeshes"/>: the pack is instantiated once, the meshes are kept
/// as shared resources and the rest of the scene is thrown away. It ships 49 chips, but they are 49
/// scene props posed on a table — every one is the SAME mesh with a different flat colour — so only
/// one is worth keeping. The colour comes from the game's own denomination table instead.
///
/// A chip is two surfaces, a white rim and a coloured body, and both are kept: the rim is the whole
/// reason the pack chip reads better than the flat cylinder it replaces.
/// </summary>
public static class PokerChipMeshes
{
    public const string AssetPath = "res://Assets/Poker/poker_assets.glb";

    /// <summary>
    /// Pack units to metres, and NOT uniform.
    ///
    /// The pack chip is 100 across by 17.08 thick — a ratio of 5.9, where a real casino chip is
    /// about 11.4. Scaling it uniformly gives either a chip of the right width and twice the height,
    /// or a correct-looking chip half the diameter it should be. Squashing Y is the honest fix: the
    /// stack has to read as chips at a glance, and a chip is a thin disc.
    /// </summary>
    public static readonly Vector3 SourceScale = new(4.0e-4f, 2.05e-4f, 4.0e-4f);

    /// <summary>Raw pack dimensions, from the mesh bounds.</summary>
    private const float PackDiameter = 100.0f;
    private const float PackThickness = 17.0822f;

    public static float Diameter => PackDiameter * SourceScale.X;

    public static float Thickness => PackThickness * SourceScale.Y;

    private static bool _loaded;
    private static Mesh _rim;
    private static Mesh _body;

    /// <summary>The white outer ring.</summary>
    public static Mesh Rim
    {
        get
        {
            EnsureLoaded();
            return _rim;
        }
    }

    /// <summary>The inner disc, which takes the denomination's colour.</summary>
    public static Mesh Body
    {
        get
        {
            EnsureLoaded();
            return _body;
        }
    }

    public static bool IsAvailable
    {
        get
        {
            EnsureLoaded();
            return _rim != null && _body != null;
        }
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;

        _loaded = true;

        var pack = GD.Load<PackedScene>(AssetPath);
        if (pack == null)
        {
            GD.PushWarning($"Pacote de fichas não encontrado em {AssetPath}; usando cilindros.");
            return;
        }

        // Never added to the tree: only the mesh resources are kept, and they outlive the nodes.
        var instance = pack.Instantiate();
        var root = instance.FindChild("RootNode", true, false) ?? instance;

        foreach (var child in root.GetChildren())
        {
            if (!child.Name.ToString().StartsWith("Cylinder"))
                continue;

            TakeSurfaces(child);
            break;
        }

        instance.QueueFree();

        if (_rim == null || _body == null)
            GD.PushWarning("O pacote de poker não trouxe uma ficha reconhecível; usando cilindros.");
    }

    /// <summary>
    /// Splits the chip's two surfaces by SIZE rather than by child order: the rim is the wider of
    /// the two. Derived rather than assumed, so re-exporting the pack in a different order cannot
    /// silently turn a chip inside out.
    /// </summary>
    private static void TakeSurfaces(Node chip)
    {
        var widest = 0.0f;

        foreach (var part in chip.GetChildren())
        {
            if (part is not MeshInstance3D mesh || mesh.Mesh == null)
                continue;

            var width = mesh.Mesh.GetAabb().Size.X;

            if (width > widest)
            {
                // Whatever was thought to be the rim is narrower than this one, so it was the body.
                if (_rim != null)
                    _body = _rim;

                widest = width;
                _rim = mesh.Mesh;
                continue;
            }

            _body = mesh.Mesh;
        }

        ImproveReadability(_rim);
        ImproveReadability(_body);
    }

    /// <summary>
    /// The pack's own materials are flat colours with no texture, and the body's is replaced per
    /// denomination anyway. Only the rim keeps its material, so it is the only one worth tuning.
    /// </summary>
    private static void ImproveReadability(Mesh mesh)
    {
        if (mesh == null)
            return;

        for (var surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            if (mesh.SurfaceGetMaterial(surface) is not BaseMaterial3D material)
                continue;

            material.Roughness = Mathf.Max(material.Roughness, 0.55f);
        }
    }
}
