using System.Collections.Generic;
using Domino.Rules;
using Godot;

/// <summary>
/// Draws the played chain. Takes the ordered plays, asks <see cref="DominoChainLayout"/> where
/// every tile goes and puts a node there.
///
/// Tiles are spawned locally on every peer rather than replicated: the layout is a pure function
/// of the play list, so each peer derives identical transforms from state it already has. That is
/// why a play costs two integers on the wire instead of a node spawn plus a transform.
/// </summary>
[GlobalClass]
public partial class DominoChainPresenter : Node3D
{
	[Export] public PackedScene TileScene;

	[ExportGroup("Layout")]
	[Export] public float TileLength = 0.048f;
	[Export] public float TileWidth = 0.024f;
	[Export] public float TileThickness = 0.0077f;
	[Export] public float Gap = 0.002f;

	/// <summary>Half extents of the usable cloth, X and Z. The chain turns corners inside this.</summary>
	[Export] public Vector2 PlayHalfExtents = new(0.32f, 0.22f);

	private readonly List<DominoTile> _tiles = new();
	private bool _warnedOverflow;

	public LayoutSpec Spec => new(TileLength, TileWidth, TileThickness, Gap, PlayHalfExtents);

	/// <summary>
	/// Brings the table in line with <paramref name="plays"/>. Tiles already down are left exactly
	/// where they are — the layout guarantees appending never disturbs them — so this doubles as
	/// both the incremental update after one play and the full rebuild a late joiner needs.
	/// </summary>
	public void Sync(IReadOnlyList<PlayRecord> plays)
	{
		var layout = DominoChainLayout.Rebuild(plays, Spec);

		if (layout.OverflowedTable && !_warnedOverflow)
		{
			_warnedOverflow = true;
			GD.PushWarning($"Corrente de dominó não coube na área de jogo ({PlayHalfExtents}); "
						   + "peças foram empilhadas para não dessincronizar a mesa.");
		}

		while (_tiles.Count > layout.Placements.Count)
		{
			var last = _tiles[^1];
			_tiles.RemoveAt(_tiles.Count - 1);
			last.QueueFree();
		}

		for (var i = 0; i < layout.Placements.Count; i++)
		{
			var placement = layout.Placements[i];

			if (i == _tiles.Count)
			{
				var tile = TileScene.Instantiate<DominoTile>();
				AddChild(tile);
				_tiles.Add(tile);
			}

			var node = _tiles[i];
			if (node.TileId != placement.TileId)
				node.Configure(placement.TileId, Spec);

			node.Position = new Vector3(placement.Center.X, TileThickness * 0.5f, placement.Center.Y);
			node.Rotation = new Vector3(0.0f, placement.Yaw, 0.0f);
		}
	}

	public void Clear()
	{
		foreach (var tile in _tiles)
			tile.QueueFree();

		_tiles.Clear();
		_warnedOverflow = false;
	}
}
