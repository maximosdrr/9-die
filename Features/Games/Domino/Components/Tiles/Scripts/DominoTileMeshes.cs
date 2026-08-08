using Domino.Rules;
using Godot;

/// <summary>
/// Pulls the 28 tile faces out of the art pack once and hands them out by tile id.
///
/// The pack ships every tile as its own mesh inside a single .glb, all sharing one atlas material,
/// so each mesh already carries its own pips. Rather than baking 28 separate .tscn files the way
/// Ball.cs does for the pool balls, the pack is instantiated once, the 28 meshes are kept as shared
/// resources and the rest of the scene (a leather box and three dice) is dropped.
///
/// Which mesh shows which tile is not derivable from the node names, so it is recorded below.
/// </summary>
public static class DominoTileMeshes
{
	private const string PackPath = "res://Assets/Dominoes/dominoes.glb";

	/// <summary>
	/// Pack units to metres: a tile is 2 x 4 x 0.645 there and 36 x 72 x 11.6 mm here.
	///
	/// Half again as large as a real domino, on purpose. Legibility from the seat is a fight
	/// against scale invariance: a bigger tile needs a bigger playing area, which needs a bigger
	/// table, which sits the player further back and cancels the gain. This size is the largest
	/// that still fits the 28-tile chain and the stock on the SAME 0.60 m table, so the growth is
	/// all relative and none of it pushes the player away.
	/// </summary>
	public const float SourceScale = 0.018f;

	/// <summary>Tile thickness in metres once scaled — what the layout reserves in Y.</summary>
	public const float Thickness = 0.6452f * SourceScale;

	/// <summary>
	/// Orients a pack mesh the way the game expects a tile: face up, long axis on Z, and the half
	/// the artist drew on +Y ending up on +Z.
	///
	/// The pack lies flat in XY with its face normal on +Z; the game wants the face on +Y and the
	/// long axis on +Z. Written as explicit axis images rather than Euler angles, because Godot
	/// composes Euler rotations in YXZ order and getting that wrong silently mirrors the pips.
	/// </summary>
	private static readonly Basis FaceUp = new(
		new Vector3(-1.0f, 0.0f, 0.0f),
		new Vector3(0.0f, 0.0f, 1.0f),
		new Vector3(0.0f, 1.0f, 0.0f));

	/// <summary>Half turn about the tile's own long axis, to swap which half leads.</summary>
	private static readonly Basis HalfTurn = new(
		new Vector3(-1.0f, 0.0f, 0.0f),
		new Vector3(0.0f, 1.0f, 0.0f),
		new Vector3(0.0f, 0.0f, -1.0f));

	/// <summary>
	/// For each mesh named Dominoes_001..Dominoes_028, the pips on its +Y half and its -Y half —
	/// read off a render of the pack, since the numbering carries no meaning.
	/// </summary>
	private static readonly int[,] PackFaces =
	{
		{ 4, 3 }, { 6, 6 }, { 6, 5 }, { 6, 4 }, { 6, 3 }, { 6, 2 }, { 6, 1 },
		{ 6, 0 }, { 5, 5 }, { 5, 4 }, { 5, 3 }, { 5, 2 }, { 5, 1 }, { 5, 0 },
		{ 4, 4 }, { 0, 0 }, { 1, 0 }, { 1, 1 }, { 2, 0 }, { 2, 1 }, { 2, 2 },
		{ 3, 0 }, { 3, 1 }, { 3, 2 }, { 3, 3 }, { 4, 0 }, { 4, 1 }, { 4, 2 },
	};

	private static Mesh[] _meshes;
	private static Transform3D[] _transforms;

	/// <summary>The face for a tile, or null when the pack could not be read.</summary>
	public static Mesh For(int tileId)
	{
		EnsureLoaded();
		return DominoTileId.IsValid(tileId) ? _meshes[tileId] : null;
	}

	/// <summary>
	/// Where to put that mesh inside a tile node: rotated face up, scaled to metres, and shifted
	/// so its centre — the pack's meshes are offset on the thickness axis by a back bevel — lands
	/// on the node's origin.
	/// </summary>
	public static Transform3D TransformFor(int tileId)
	{
		EnsureLoaded();
		return DominoTileId.IsValid(tileId) ? _transforms[tileId] : Transform3D.Identity;
	}

	public static bool IsAvailable
	{
		get
		{
			EnsureLoaded();
			return _meshes[0] != null;
		}
	}

	private static void EnsureLoaded()
	{
		if (_meshes != null)
			return;

		_meshes = new Mesh[DominoTileId.Count];
		_transforms = new Transform3D[DominoTileId.Count];
		for (var i = 0; i < DominoTileId.Count; i++)
			_transforms[i] = Transform3D.Identity;

		var pack = GD.Load<PackedScene>(PackPath);
		if (pack == null)
		{
			GD.PushError($"Pacote de arte do dominó não encontrado em {PackPath}; as peças ficarão sem malha.");
			return;
		}

		// Never added to the tree: only the mesh resources are kept, and they outlive the nodes.
		var instance = pack.Instantiate();
		var root = instance.FindChild("RootNode", true, false) ?? instance;

		var node = 0;
		foreach (var child in root.GetChildren())
		{
			if (!child.Name.ToString().StartsWith("Dominoes_"))
				continue;

			if (node >= PackFaces.GetLength(0))
				break;

			if (child.GetChildCount() > 0 && child.GetChild(0) is MeshInstance3D source && source.Mesh != null)
				Register(node, source.Mesh);

			node++;
		}

		instance.QueueFree();

		for (var tileId = 0; tileId < DominoTileId.Count; tileId++)
		{
			if (_meshes[tileId] == null)
				GD.PushError($"Peça {DominoTileId.Label(tileId)} sem malha no pacote de arte.");
		}
	}

	private static void Register(int node, Mesh mesh)
	{
		var plusY = PackFaces[node, 0];
		var minusY = PackFaces[node, 1];
		var tileId = DominoTileId.From(plusY, minusY);

		ImproveReadability(mesh);
		_meshes[tileId] = mesh;

		// FaceUp lands the +Y half on +Z. The game's convention is that +Z carries the HIGH half,
		// so a mesh drawn the other way round gets an extra half turn. Derived rather than baked
		// into the table, so replacing the pack cannot silently reverse a tile.
		var basis = plusY == DominoTileId.High(tileId) ? FaceUp : HalfTurn * FaceUp;
		basis = basis.Scaled(new Vector3(SourceScale, SourceScale, SourceScale));

		_transforms[tileId] = new Transform3D(basis, -(basis * mesh.GetAabb().GetCenter()));
	}

	/// <summary>
	/// The player sees most tiles at a steep angle. Anisotropic filtering keeps the atlas pips and
	/// centre divider sharper in that situation, while extra roughness prevents the pub lights from
	/// washing the ivory face out. The material belongs only to this domino pack and is shared by
	/// all 28 meshes, so applying this repeatedly is harmless and avoids per-tile material copies.
	/// </summary>
	private static void ImproveReadability(Mesh mesh)
	{
		for (var surface = 0; surface < mesh.GetSurfaceCount(); surface++)
		{
			if (mesh.SurfaceGetMaterial(surface) is not BaseMaterial3D material)
				continue;

			material.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
			material.Roughness = Mathf.Max(material.Roughness, 0.62f);
		}
	}
}
