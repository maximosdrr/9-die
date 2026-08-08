using System.Collections.Generic;
using Domino.Rules;
using Godot;

/// <summary>
/// Everything the removed panel used to say, said with objects on the table instead.
///
/// Whose turn it is, how many tiles each opponent is holding and what is left in the stock are all
/// derived from <see cref="DominoGame"/>'s PUBLIC state — the counts and the stock places, never a
/// tile id — so every peer draws the same table from what it already has and no message is added
/// to the protocol for any of it.
/// </summary>
[GlobalClass]
public partial class DominoSeatPresenter : Node3D
{
	[Export] public PackedScene TileScene;

	/// <summary>The cloth frame. Everything here is laid out in its local space, like the chain.</summary>
	[Export] public DominoChainPresenter ChainPresenter;

	[Export] public Node3D Seats;

	[ExportGroup("Stock")]
	[Export] public int StockColumns = 7;
	[Export] public float StockGap = 0.004f;
	[Export] public Vector2 StockOrigin = new(0.0f, 0.38f);

	[ExportGroup("Opponents")]
	/// <summary>How far from the middle of the cloth each player's tiles are laid.</summary>
	[Export] public float SeatFanRadius = 0.48f;

	/// <summary>Side by side rather than stacked: the row length is how the count is read.</summary>
	[Export] public float SeatFanSpacing = 0.039f;
	[Export] public float NameHeight = 0.16f;
	[Export] public Color TurnColor = new(1.0f, 0.478431f, 0.2f);
	[Export] public Color IdleColor = new(0.678431f, 0.752941f, 0.839216f);

	private DominoGame _game;
	private readonly List<DominoTile> _stock = new();
	private readonly Dictionary<string, List<DominoTile>> _seatTiles = new();
	private readonly Dictionary<string, Label3D> _seatNames = new();

	public override void _Ready()
	{
		_game = GetParent<DominoGame>();
		if (_game == null)
		{
			GD.PushError("DominoSeatPresenter precisa ser filho de um DominoGame.");
			return;
		}

		_game.StockSpec = BuildStockSpec();

		SignalUtil.ConnectGuarded(_game, DominoGame.SignalName.HudStateUpdated,
			new Callable(this, MethodName.Refresh));
	}

	private BoneyardSpec BuildStockSpec()
	{
		var spec = ChainPresenter?.Spec ?? LayoutSpec.Default;
		return new BoneyardSpec(spec.TileLength, spec.TileWidth, StockGap, StockColumns, StockOrigin);
	}

	/// <summary>Redraws the table's furniture whenever the public state moves.</summary>
	public void Refresh()
	{
		if (_game == null || ChainPresenter == null || TileScene == null)
			return;

		RefreshStock();
		RefreshSeats();
	}

	// ---------------------------------------------------------------- the stock

	private void RefreshStock()
	{
		var slots = _game.BoneyardSlots;
		var spec = _game.StockSpec;
		var tileSpec = ChainPresenter.Spec;

		Resize(_stock, slots.Length);

		for (var i = 0; i < slots.Length; i++)
		{
			var tile = _stock[i];

			// Face down, and configured with a placeholder id: the stock's contents are the
			// server's alone, and a peer cannot show what it was never told.
			if (!tile.IsFaceDown)
				tile.Configure(0, tileSpec, faceDown: true);

			var position = DominoBoneyardLayout.SlotPosition(slots[i], spec);
			tile.Position = new Vector3(position.X, tileSpec.TileThickness * 0.5f, position.Y);
			tile.Rotation = Vector3.Zero;
		}
	}

	// ---------------------------------------------------------------- the players

	private void RefreshSeats()
	{
		var tileSpec = ChainPresenter.Spec;
		var seen = new HashSet<string>();

		for (var index = 0; index < _game.TurnOrder.Count; index++)
		{
			var playerId = (string)_game.TurnOrder[index];
			seen.Add(playerId);

			var seat = _game.SeatFor(playerId);
			if (seat == null)
				continue;

			// The direction the player is sitting, flattened onto the cloth.
			var toSeat = ToLocal(seat.GlobalPosition);
			var facing = new Vector2(toSeat.X, toSeat.Z).Normalized();
			if (facing.LengthSquared() < 1e-6f)
				facing = Vector2.Down;

			var isSelf = _game.Player != null && (string)_game.Player.Name == playerId;
			var count = _game.HandCounts.TryGetValue(playerId, out var held) ? held : 0;

			// The player's own tiles are in their hand, not on the table, and they need no name
			// plate telling them who they are — their own hand being up is how they know it is
			// their turn.
			RefreshSeatFan(playerId, isSelf ? 0 : count, facing, tileSpec);

			if (!isSelf)
				RefreshSeatName(playerId, facing);
		}

		// Someone left: their tiles and their name plate go with them.
		foreach (var playerId in new List<string>(_seatTiles.Keys))
		{
			if (seen.Contains(playerId))
				continue;

			foreach (var tile in _seatTiles[playerId])
				tile.QueueFree();

			_seatTiles.Remove(playerId);
		}

		foreach (var playerId in new List<string>(_seatNames.Keys))
		{
			if (seen.Contains(playerId))
				continue;

			_seatNames[playerId].QueueFree();
			_seatNames.Remove(playerId);
		}
	}

	/// <summary>
	/// A row of face-down tiles in front of a player. The count IS the information — you read an
	/// opponent's hand size the way you would at a real table, by looking at it.
	/// </summary>
	private void RefreshSeatFan(string playerId, int count, Vector2 facing, LayoutSpec tileSpec)
	{
		if (!_seatTiles.TryGetValue(playerId, out var tiles))
		{
			tiles = new List<DominoTile>();
			_seatTiles[playerId] = tiles;
		}

		Resize(tiles, count);

		var across = new Vector2(-facing.Y, facing.X);
		var origin = facing * SeatFanRadius;
		var yaw = Mathf.Atan2(facing.X, facing.Y);

		for (var i = 0; i < count; i++)
		{
			var offset = (i - (count - 1) * 0.5f) * SeatFanSpacing;
			var position = origin + across * offset;

			var tile = tiles[i];
			if (!tile.IsFaceDown)
				tile.Configure(0, tileSpec, faceDown: true);

			tile.Position = new Vector3(position.X, tileSpec.TileThickness * 0.5f, position.Y);
			tile.Rotation = new Vector3(0.0f, yaw, 0.0f);
		}
	}

	private void RefreshSeatName(string playerId, Vector2 facing)
	{
		if (!_seatNames.TryGetValue(playerId, out var label))
		{
			label = new Label3D
			{
				Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
				NoDepthTest = false,
				PixelSize = 0.0006f,
				FontSize = 64,
				OutlineSize = 12,
			};

			AddChild(label);
			_seatNames[playerId] = label;
		}

		var isTurn = _game.TurnOwner != null && (string)_game.TurnOwner.Name == playerId;
		var position = facing * (SeatFanRadius + 0.06f);

		label.Text = NameOf(playerId);
		label.Modulate = isTurn ? TurnColor : IdleColor;
		label.Position = new Vector3(position.X, NameHeight, position.Y);
	}

	// ---------------------------------------------------------------- helpers

	private void Resize(List<DominoTile> tiles, int count)
	{
		while (tiles.Count > count)
		{
			var last = tiles[^1];
			tiles.RemoveAt(tiles.Count - 1);
			last.QueueFree();
		}

		while (tiles.Count < count)
		{
			var tile = TileScene.Instantiate<DominoTile>();
			AddChild(tile);
			tiles.Add(tile);
		}
	}

	private static string NameOf(string playerId)
	{
		var player = PlayerRegistry.Instance?.GetPlayerById(playerId);
		return player != null && !string.IsNullOrWhiteSpace(player.Nickname)
			? player.Nickname
			: $"Jogador {playerId}";
	}
}
