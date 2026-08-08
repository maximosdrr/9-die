using System.Collections.Generic;
using Domino.Rules;
using Godot;
using Godot.Collections;

/// <summary>
/// Double-six dominoes for two to four players, each for themselves, one round per match.
///
/// Holds only the PUBLIC state — the chain, the open ends, how many tiles everyone is holding and
/// how many are left in the boneyard — plus this peer's own hand. Nobody else's tiles are ever
/// stored here, because everything here reaches every peer. The secret half lives on
/// <see cref="DominoTurnResolver"/>, server side, and never leaves it.
/// </summary>
[GlobalClass]
public partial class DominoGame : TableGame
{
	[Export] public PackedScene GameControllerScene;
	[Export] public DominoChainPresenter ChainPresenter;
	[Export] public Node3D Seats;

	/// <summary>
	/// Lets a lone host start a match to try the game out. Solo matches are excluded from the
	/// ranking, the same way a solo pool frame is.
	/// </summary>
	[Export] public bool AllowSoloDebug;

	public GlobalCamera Camera;
	public bool IsSoloMatch { get; private set; }

	public readonly List<PlayRecord> Plays = new();

	/// <summary>How many tiles each player is holding. Counts only — never contents.</summary>
	public readonly System.Collections.Generic.Dictionary<string, int> HandCounts = new();

	public int LeftEnd = DominoTileId.NoEnd;
	public int RightEnd = DominoTileId.NoEnd;
	public int BoneyardCount;

	/// <summary>Server-issued stamp for the current turn; a request carrying a stale one is dropped.</summary>
	public int TurnToken;

	public string LastAction = "";
	public string LastPlayer = "";
	public int LastTile = DominoTileId.NoEnd;

	/// <summary>This peer's own tiles. Empty on every peer that is not their owner.</summary>
	public int[] LocalHand = System.Array.Empty<int>();

	private GameModeHandler _gameModeHandler;

	public override int MaxPlayers => 4;

	public override bool CanStartWith(int playerCount) =>
		playerCount <= MaxPlayers && playerCount >= (AllowSoloDebug ? 1 : 2);

	public override bool CountsWinsForRanking => !IsSoloMatch;

	[Signal]
	public delegate void HudStateUpdatedEventHandler();

	[Signal]
	public delegate void LocalHandChangedEventHandler();

	public DominoTurnResolver Resolver =>
		_gameModeHandler?.CurrentGameMode?.TurnResolver as DominoTurnResolver;

	public override void _Ready()
	{
		_gameModeHandler = GetNode<GameModeHandler>("GameModeHandler");
		GameModeHandler = _gameModeHandler;

		MatchStarted += OnMatchStarts;
		MatchOver += OnMatchIsOver;
		PlayerRemovedFromMatch += OnPlayerRemovedFromMatch;
		PlayerReclaimed += OnPlayerReclaimed;
	}

	public override void SetCamera(GlobalCamera camera)
	{
		Camera = camera;
	}

	public Marker3D SeatFor(string playerId)
	{
		var index = TurnOrder.IndexOf(playerId);
		if (index < 0 || Seats == null || index >= Seats.GetChildCount())
			return null;

		return Seats.GetChild(index) as Marker3D;
	}

	public override void SetupMatch(Array players, string firstTurnOwner)
	{
		IsSoloMatch = players.Count == 1;

		ResetPublicState();
		ChainPresenter?.Clear();

		_gameModeHandler.Setup(this);

		// Clients equip their controllers and take their seats off this signal, so it has to reach
		// them before the deal does. The turn bridge mirrors it on the same reliable channel the
		// deal uses, which keeps that order.
		EmitSignal(SignalName.MatchStarted, players, firstTurnOwner);

		if (Multiplayer.IsServer())
			Resolver?.BeginDeal(players);
	}

	private void ResetPublicState()
	{
		Plays.Clear();
		HandCounts.Clear();
		LeftEnd = DominoTileId.NoEnd;
		RightEnd = DominoTileId.NoEnd;
		BoneyardCount = 0;
		TurnToken = 0;
		LastAction = "";
		LastPlayer = "";
		LastTile = DominoTileId.NoEnd;
		LocalHand = System.Array.Empty<int>();
	}

	/// <summary>
	/// Takes the public snapshot the server packed into a turn context. Every state change already
	/// travels as a turn context, so this is the single place the table catches up — there is no
	/// second sync channel that could arrive out of order with the turn itself.
	/// </summary>
	public void ApplyPublicSnapshot(Dictionary context)
	{
		if (context == null || !context.ContainsKey("play_tiles"))
			return;

		var tiles = context["play_tiles"].AsInt32Array();
		var ends = context["play_ends"].AsInt32Array();
		var players = context["play_players"].AsStringArray();

		Plays.Clear();
		for (var i = 0; i < tiles.Length; i++)
		{
			var playerId = i < players.Length ? players[i] : "";
			var end = i < ends.Length && ends[i] == (int)ChainEnd.Left ? ChainEnd.Left : ChainEnd.Right;
			Plays.Add(new PlayRecord(playerId, tiles[i], end));
		}

		LeftEnd = (int)context["left_end"];
		RightEnd = (int)context["right_end"];
		BoneyardCount = (int)context["boneyard_count"];
		TurnToken = (int)context["turn_token"];

		HandCounts.Clear();
		var handPlayers = context["hand_players"].AsStringArray();
		var handSizes = context["hand_counts"].AsInt32Array();
		for (var i = 0; i < handPlayers.Length && i < handSizes.Length; i++)
			HandCounts[handPlayers[i]] = handSizes[i];

		LastAction = context.TryGetValue("last_action", out var action) ? (string)action : "";
		LastPlayer = context.TryGetValue("last_player", out var player) ? (string)player : "";
		LastTile = context.TryGetValue("last_tile", out var tile) ? (int)tile : DominoTileId.NoEnd;

		ChainPresenter?.Sync(Plays);
		EmitSignal(SignalName.HudStateUpdated);
	}

	/// <summary>Called only on the peer the hand belongs to, from a targeted RPC.</summary>
	public void ApplyLocalHand(int[] tiles)
	{
		LocalHand = tiles ?? System.Array.Empty<int>();
		EmitSignal(SignalName.LocalHandChanged);
		EmitSignal(SignalName.HudStateUpdated);
	}

	private void OnMatchStarts(Array playersIds, string firstTurnOwner)
	{
		foreach (var playerIdVariant in playersIds)
		{
			var player = PlayerRegistry.Instance.GetPlayerById((string)playerIdVariant);
			player?.GameHandler.EquipGameController(GameControllerScene, this, Camera);
		}
	}

	private void OnMatchIsOver(string winner, Dictionary context)
	{
		// The winning tile reaches the table through this context rather than a turn change,
		// because a match-ending play never hands the turn on.
		ApplyPublicSnapshot(context);

		if (Player != null && Player.GameHandler.CurrentController != null)
		{
			Player.GameHandler.CurrentController.GiveControl();
			Player.GameHandler.UnequipCurrentController();
			Player.TakeControl();
		}
	}

	private void OnPlayerRemovedFromMatch(string playerId, Array turnOrder)
	{
		// Their tiles go back to the boneyard. Every peer can work the new count out for itself
		// from the hand size it already knew, so this needs no message of its own.
		if (HandCounts.TryGetValue(playerId, out var heldTiles))
		{
			BoneyardCount += heldTiles;
			HandCounts.Remove(playerId);
		}

		// TableGame fires this signal before it builds the handoff context, so moving the real
		// tiles now is what keeps that context's counts honest.
		if (Multiplayer.IsServer())
			Resolver?.ReturnTilesToBoneyard(playerId);

		EmitSignal(SignalName.HudStateUpdated);

		var leavingPlayer = PlayerRegistry.Instance.GetPlayerById(playerId);
		if (leavingPlayer?.GameHandler.CurrentController == null)
			return;

		leavingPlayer.GameHandler.CurrentController.GiveControl();
		leavingPlayer.GameHandler.UnequipCurrentController();
		leavingPlayer.TakeControl();
	}

	private void OnPlayerReclaimed(string oldPlayerId, string newPlayerId, Array turnOrder)
	{
		var newPlayer = PlayerRegistry.Instance.GetPlayerById(newPlayerId);
		if (newPlayer == null)
			return;

		newPlayer.GameHandler.EquipGameController(GameControllerScene, this, Camera);

		if (HandCounts.TryGetValue(oldPlayerId, out var heldTiles))
		{
			HandCounts.Remove(oldPlayerId);
			HandCounts[newPlayerId] = heldTiles;
		}

		// The old Player node is already gone, so TurnOwner may be a stale reference; this only
		// reads the name it carries, the same way RemovePlayerFromMatch does.
		if (TurnOwner != null && (string)TurnOwner.Name == oldPlayerId)
			TurnOwner = newPlayer;

		EmitSignal(SignalName.HudStateUpdated);

		if (Multiplayer.IsServer())
			Resolver?.ReissueStateTo(oldPlayerId, newPlayerId);
	}
}
