using System.Collections.Generic;
using Godot;

/// <summary>
/// Shared meshes from the optimized numbered-chip pack.
///
/// The GLB is instantiated once only to discover its resources. Every visible chip then shares the
/// matching Mesh and overrides only its body surface, so changing denominations does not allocate
/// another imported scene or disturb the movement node owned by <see cref="PokerChipPile"/>.
/// </summary>
public static class PokerChipAssetMeshes
{
    private const string PackPath = "res://Assets/Poker/poker_chip_set_optimized.glb";

    public const float Diameter = 0.040f;
    public const float SourceThickness = 0.0035f;
    // Slightly stylised 4.5 mm profile: still credible at 40 mm wide, but the rim remains readable
    // from a seated camera instead of collapsing into a paper-thin line.
    public const float Thickness = 0.0045f;
    public const float HeightScale = Thickness / SourceThickness;

    private static readonly Dictionary<int, Mesh> Meshes = new();
    private static readonly Dictionary<int, int> BodySurfaces = new();
    private static bool _loaded;

    public static bool IsAvailable
    {
        get
        {
            EnsureLoaded();
            return Meshes.Count > 0;
        }
    }

    public static bool TryGet(int denomination, out Mesh mesh, out int bodySurface)
    {
        EnsureLoaded();
        if (!Meshes.TryGetValue(denomination, out mesh))
        {
            mesh = null;
            bodySurface = -1;
            return false;
        }

        bodySurface = BodySurfaces.GetValueOrDefault(denomination, -1);
        return true;
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;

        _loaded = true;
        var pack = GD.Load<PackedScene>(PackPath);
        if (pack == null)
        {
            GD.PushWarning($"Pacote otimizado de fichas não encontrado em {PackPath}.");
            return;
        }

        var instance = pack.Instantiate();
        Discover(instance);
        instance.Free();

        if (Meshes.Count == 0)
            GD.PushWarning("O pacote otimizado não contém nós Chip_<valor> reconhecíveis.");
    }

    private static void Discover(Node node)
    {
        if (node is MeshInstance3D meshInstance
            && meshInstance.Mesh != null
            && TryReadDenomination(meshInstance.Name, out var denomination))
        {
            Meshes[denomination] = meshInstance.Mesh;
            BodySurfaces[denomination] = FindBodySurface(meshInstance.Mesh);
        }

        foreach (var child in node.GetChildren())
            Discover(child);
    }

    private static bool TryReadDenomination(StringName name, out int denomination)
    {
        const string prefix = "Chip_";
        var text = name.ToString();
        denomination = 0;
        return text.StartsWith(prefix)
            && int.TryParse(text[prefix.Length..], out denomination);
    }

    private static int FindBodySurface(Mesh mesh)
    {
        for (var surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            var materialName = mesh.SurfaceGetMaterial(surface)?.ResourceName ?? string.Empty;
            if (materialName.StartsWith("ChipBody"))
                return surface;
        }

        return -1;
    }
}
