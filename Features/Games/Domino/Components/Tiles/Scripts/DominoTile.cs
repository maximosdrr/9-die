using Domino.Rules;
using Godot;

/// <summary>
/// One tile on the table. Identity is a tile id and nothing else; how that id becomes a face is
/// entirely this class's business, so the layout, the rules, the network and the hand views never
/// learn which art pack arrived.
///
/// Falls back to a labelled box whenever the pack cannot be read, so the game stays playable — and
/// debuggable — even with the art missing.
/// </summary>
[GlobalClass]
public partial class DominoTile : Node3D
{
	[Export] public MeshInstance3D Body;
	[Export] public Label3D FaceLabel;

	/// <summary>Draws the box and the written value instead of the model. Useful for debugging.</summary>
	[Export] public bool ForcePlaceholder;

	public int TileId { get; private set; } = DominoTileId.NoEnd;

	/// <summary>
	/// Gives the tile its identity. The placeholder is sized from the layout spec rather than from
	/// the scene, so the box always matches the footprint the chain reserved for it — a mismatch
	/// there would make tiles look overlapped while the layout test still passes.
	/// </summary>
	public void Configure(int tileId, LayoutSpec spec)
	{
		TileId = tileId;

		if (Body == null)
			return;

		if (!ForcePlaceholder && DominoTileMeshes.For(tileId) is { } face)
		{
			Body.Mesh = face;
			Body.Transform = DominoTileMeshes.TransformFor(tileId);
			// Cleared so the pack's own atlas material shows the pips; an override here would
			// repaint all 28 tiles blank.
			Body.MaterialOverride = null;
			FaceLabel?.Hide();
			return;
		}

		ApplyPlaceholder(spec);

		if (FaceLabel == null)
			return;

		FaceLabel.Show();
		FaceLabel.Text = DominoTileId.Label(tileId);
		FaceLabel.Position = new Vector3(0.0f, spec.TileThickness * 0.5f + 0.001f, 0.0f);
	}

	private static readonly StandardMaterial3D PlaceholderMaterial = new()
	{
		AlbedoColor = new Color(0.925f, 0.925f, 0.898f),
		Roughness = 0.45f,
	};

	private void ApplyPlaceholder(LayoutSpec spec)
	{
		Body.Transform = Transform3D.Identity;
		Body.MaterialOverride = PlaceholderMaterial;

		// Duplicated per instance: sizing a shared BoxMesh would resize every tile at once.
		var box = (BoxMesh)(Body.Mesh as BoxMesh ?? new BoxMesh()).Duplicate();
		box.Size = new Vector3(spec.TileWidth, spec.TileThickness, spec.TileLength);
		Body.Mesh = box;
	}
}
