using Godot;

/// <summary>
/// Replaceable visual for one chip. Keep this script on the root of a custom chip scene and assign
/// TintTarget to the material that carries the denomination colour. The pile remains responsible
/// only for stacking and motion.
/// </summary>
[GlobalClass]
public partial class PokerChipVisual : Node3D
{
    [Export] public GeometryInstance3D TintTarget;
    [Export] public StringName ShaderTintParameter = "albedo_color";
    [Export] public bool GenerateCurrentPlaceholderWhenEmpty = true;

    private bool _prepared;
    private Material _runtimeMaterial;
    private Color _lastColour;
    private bool _hasColour;
    private MeshInstance3D _generatedMesh;
    private int _bodySurface = -1;
    private int _denomination = int.MinValue;

    public void Configure(Color colour)
        => Configure(0, colour);

    public void Configure(int denomination, Color colour)
    {
        Prepare(denomination);
        if (TintTarget == null || (_hasColour && _lastColour.IsEqualApprox(colour)))
            return;

        if (_runtimeMaterial == null)
        {
            var sourceMaterial = _bodySurface >= 0 && TintTarget is MeshInstance3D mesh
                ? mesh.Mesh?.SurfaceGetMaterial(_bodySurface)
                : TintTarget.MaterialOverride;
            _runtimeMaterial = sourceMaterial?.Duplicate() as Material
                ?? new StandardMaterial3D { Roughness = 0.55f };

            if (_bodySurface >= 0 && TintTarget is MeshInstance3D surfaceTarget)
                surfaceTarget.SetSurfaceOverrideMaterial(_bodySurface, _runtimeMaterial);
            else
                TintTarget.MaterialOverride = _runtimeMaterial;
        }

        if (_runtimeMaterial is StandardMaterial3D standard)
            standard.AlbedoColor = colour;
        else if (_runtimeMaterial is ShaderMaterial shader)
            shader.SetShaderParameter(ShaderTintParameter, colour);

        _lastColour = colour;
        _hasColour = true;
    }

    private void Prepare(int denomination)
    {
        if (_prepared && (_generatedMesh == null || _denomination == denomination))
            return;

        if (!_prepared)
        {
            _prepared = true;
            if (!GenerateCurrentPlaceholderWhenEmpty || GetChildCount() > 0)
                return;
        }

        if (_generatedMesh != null
            && PokerChipAssetMeshes.TryGet(denomination, out var numbered, out var bodySurface))
        {
            ClearSurfaceOverrides(_generatedMesh);
            _generatedMesh.Mesh = numbered;
            _bodySurface = bodySurface;
            _denomination = denomination;
            _runtimeMaterial = null;
            _hasColour = false;
            return;
        }

        if (_generatedMesh == null
            && PokerChipAssetMeshes.TryGet(denomination, out numbered, out bodySurface))
        {
            _generatedMesh = new MeshInstance3D
            {
                Name = "NumberedChip",
                Mesh = numbered,
            };
            AddChild(_generatedMesh);
            TintTarget = _generatedMesh;
            _bodySurface = bodySurface;
            _denomination = denomination;
            return;
        }

        if (PokerChipMeshes.IsAvailable)
        {
            var rim = new MeshInstance3D
            {
                Name = "Rim",
                Mesh = PokerChipMeshes.Rim,
                Scale = PokerChipMeshes.SourceScale,
            };
            var body = new MeshInstance3D
            {
                Name = "Body",
                Mesh = PokerChipMeshes.Body,
                Scale = PokerChipMeshes.SourceScale,
            };

            AddChild(rim);
            AddChild(body);
            TintTarget = body;
            _denomination = denomination;
            return;
        }

        var fallback = new MeshInstance3D
        {
            Name = "Body",
            Mesh = new CylinderMesh
            {
                TopRadius = 0.020f,
                BottomRadius = 0.020f,
                Height = 0.0035f,
                RadialSegments = 20,
                Rings = 1,
            },
        };

        AddChild(fallback);
        TintTarget = fallback;
        _denomination = denomination;
    }

    private static void ClearSurfaceOverrides(MeshInstance3D mesh)
    {
        var surfaceCount = mesh.Mesh?.GetSurfaceCount() ?? 0;
        for (var surface = 0; surface < surfaceCount; surface++)
            mesh.SetSurfaceOverrideMaterial(surface, null);
    }
}
