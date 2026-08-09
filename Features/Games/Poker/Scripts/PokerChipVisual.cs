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

	public void Configure(Color colour)
	{
		Prepare();
		if (TintTarget == null || (_hasColour && _lastColour.IsEqualApprox(colour)))
			return;

		if (_runtimeMaterial == null)
		{
			_runtimeMaterial = TintTarget.MaterialOverride?.Duplicate() as Material
				?? new StandardMaterial3D { Roughness = 0.55f };
			TintTarget.MaterialOverride = _runtimeMaterial;
		}

		if (_runtimeMaterial is StandardMaterial3D standard)
			standard.AlbedoColor = colour;
		else if (_runtimeMaterial is ShaderMaterial shader)
			shader.SetShaderParameter(ShaderTintParameter, colour);

		_lastColour = colour;
		_hasColour = true;
	}

	private void Prepare()
	{
		if (_prepared)
			return;

		_prepared = true;
		if (!GenerateCurrentPlaceholderWhenEmpty || GetChildCount() > 0)
			return;

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
	}
}
