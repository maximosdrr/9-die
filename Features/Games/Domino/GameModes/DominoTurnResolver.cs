using System.Collections.Generic;
using Domino.Rules;
using Godot;
using Godot.Collections;

/// <summary>
/// Runs a domino match server side and is the only place a hand ever exists.
///
/// The secrecy contract, stated once: <see cref="_hands"/>, <see cref="_boneyard"/> and
/// <see cref="_dealSeed"/> never leave this node. A hand reaches its owner and nobody else, through
/// the single targeted <see cref="ReceiveHand"/>. The seed in particular would reconstruct every
/// hand at once, so it is never put in a context, a broadcast or a log.
///
/// Everything public — the chain, the ends, the counts — rides the turn context that
/// TableTurnNetworkBridge already replicates. Reusing that channel rather than adding a second one
/// means the board can never arrive out of step with the turn it belongs to.
///
/// This node belongs to the server, unlike CueNetworkBridge which belongs to a player, so
/// RpcMode.Authority is correct for every server-to-client message here.
/// </summary>
[GlobalClass]
public partial class DominoTurnResolver : TurnResolver
{
	public DominoGame Game;

	private readonly System.Collections.Generic.Dictionary<string, List<int>> _hands = new();
	private readonly List<int> _boneyard = new();
	private readonly DominoBoardState _board = new();

	private ulong _dealSeed;
	private int _turnToken;
	private int _consecutivePasses;
	private string _lastPlayerId = "";
	private bool _matchRunning;

	[Signal]
	public delegate void ActionRejectedEventHandler(string reason);

	public override void Setup(TableGame tableGame)
	{
		Game = (DominoGame)tableGame;

		_hands.Clear();
		_boneyard.Clear();
		_board.Reset();
		_turnToken = 0;
		_consecutivePasses = 0;
		_lastPlayerId = "";
		_matchRunning = false;

		if (!Multiplayer.IsServer())
			return;

		// A peer that connects mid-match never runs SetupMatch — GameStarted only calls it on the
		// server — so without this it would sit in front of an empty table until the next turn.
		SignalUtil.ConnectGuarded(NetworkManager.Instance.NetworkProvider,
			NetworkProvider.SignalName.PlayerConnected,
			new Callable(this, MethodName.OnPeerConnected));
	}

	public override void HandleNewTurnContext(Dictionary context) => Game?.ApplyPublicSnapshot(context);

	public override void HandleTurnExtensionContext(Dictionary context) => Game?.ApplyPublicSnapshot(context);

	/// <summary>
	/// Context handed to the next player when the current one leaves. TableGame has already put
	/// the leaver's tiles back in the boneyard by the time this runs, so the counts are correct.
	/// </summary>
	public override Dictionary BuildHandoffContext(string outgoingPlayerId) =>
		BuildContext("left", outgoingPlayerId, DominoTileId.NoEnd);

	// ---------------------------------------------------------------- deal

	/// <summary>Shuffles, deals privately and puts the opening tile down. Server only.</summary>
	public void BeginDeal(Array players)
	{
		if (!Multiplayer.IsServer() || Game == null)
			return;

		var playerIds = new List<string>();
		foreach (var playerVariant in players)
			playerIds.Add((string)playerVariant);

		if (playerIds.Count == 0)
			return;

		_dealSeed = ((ulong)GD.Randi() << 32) | GD.Randi();
		var deal = DominoDeal.Deal(playerIds, _dealSeed);

		_hands.Clear();
		foreach (var entry in deal.Hands)
			_hands[entry.Key] = entry.Value;

		_boneyard.Clear();
		_boneyard.AddRange(deal.Boneyard);

		_board.Reset();
		_consecutivePasses = 0;
		_matchRunning = true;

		foreach (var playerId in playerIds)
			SendHand(playerId);

		// Highest double leads, which is rarely whoever GameStarted happened to seat first.
		var opener = DominoDeal.PickOpener(_hands, playerIds, out var openingTile);
		if (opener == null || openingTile == DominoTileId.NoEnd)
			return;

		_hands[opener].Remove(openingTile);
		_board.TryPlay(opener, openingTile, ChainEnd.Right);
		_lastPlayerId = opener;
		SendHand(opener);

		// The opening tile is forced, so it is played for the opener and the turn moves straight
		// on. Routing it through ApplyNewTurn rather than editing GameStarted keeps the opener
		// override on a path that is already replicated.
		var nextIndex = (playerIds.IndexOf(opener) + 1) % playerIds.Count;
		Game.ApplyNewTurn(playerIds[nextIndex], BuildContext("open", opener, openingTile));
	}

	// ---------------------------------------------------------------- player requests

	public void RequestPlayTile(int turnToken, int tileId, int end)
	{
		if (Multiplayer.IsServer())
			TryPlayTile(Multiplayer.GetUniqueId(), turnToken, tileId, end);
		else
			RpcId(1, MethodName.PlayTileOnServer, turnToken, tileId, end);
	}

	public void RequestDrawTile(int turnToken)
	{
		if (Multiplayer.IsServer())
			TryDrawTile(Multiplayer.GetUniqueId(), turnToken);
		else
			RpcId(1, MethodName.DrawTileOnServer, turnToken);
	}

	public void RequestPass(int turnToken)
	{
		if (Multiplayer.IsServer())
			TryPass(Multiplayer.GetUniqueId(), turnToken);
		else
			RpcId(1, MethodName.PassOnServer, turnToken);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void PlayTileOnServer(int turnToken, int tileId, int end)
	{
		if (!Multiplayer.IsServer())
			return;

		TryPlayTile(Multiplayer.GetRemoteSenderId(), turnToken, tileId, end);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void DrawTileOnServer(int turnToken)
	{
		if (!Multiplayer.IsServer())
			return;

		TryDrawTile(Multiplayer.GetRemoteSenderId(), turnToken);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void PassOnServer(int turnToken)
	{
		if (!Multiplayer.IsServer())
			return;

		TryPass(Multiplayer.GetRemoteSenderId(), turnToken);
	}

	// ---------------------------------------------------------------- server rulings

	private void TryPlayTile(int requesterId, int turnToken, int tileId, int end)
	{
		var playerId = requesterId.ToString();

		if (!TurnIsOpenFor(playerId, turnToken, out var hand, out var reason))
		{
			Reject(requesterId, turnToken, reason);
			return;
		}

		if (!DominoTileId.IsValid(tileId))
		{
			Reject(requesterId, turnToken, "invalid_tile");
			return;
		}

		if (!hand.Contains(tileId))
		{
			Reject(requesterId, turnToken, "tile_not_in_hand");
			return;
		}

		if (end != (int)ChainEnd.Left && end != (int)ChainEnd.Right)
		{
			Reject(requesterId, turnToken, "invalid_end");
			return;
		}

		var chainEnd = (ChainEnd)end;
		if (!DominoRules.CanPlace(tileId, chainEnd, _board.LeftEnd, _board.RightEnd))
		{
			Reject(requesterId, turnToken, "tile_does_not_match");
			return;
		}

		if (!_board.TryPlay(playerId, tileId, chainEnd))
		{
			Reject(requesterId, turnToken, "tile_does_not_match");
			return;
		}

		hand.Remove(tileId);
		_consecutivePasses = 0;
		_lastPlayerId = playerId;
		SendHand(playerId);

		if (hand.Count == 0)
		{
			_matchRunning = false;
			Game.ApplyMatchOver(playerId, BuildContext("domino", playerId, tileId));
			return;
		}

		Game.CallNextTurn(BuildContext("play", playerId, tileId));
	}

	private void TryDrawTile(int requesterId, int turnToken)
	{
		var playerId = requesterId.ToString();

		if (!TurnIsOpenFor(playerId, turnToken, out var hand, out var reason))
		{
			Reject(requesterId, turnToken, reason);
			return;
		}

		if (_boneyard.Count == 0)
		{
			Reject(requesterId, turnToken, "boneyard_empty");
			return;
		}

		// Drawing with a playable tile in hand would be a way to stall forever, and to fish the
		// boneyard for a better tile.
		if (DominoRules.HasLegalMove(hand, _board.LeftEnd, _board.RightEnd))
		{
			Reject(requesterId, turnToken, "has_legal_move");
			return;
		}

		hand.Add(_boneyard[^1]);
		_boneyard.RemoveAt(_boneyard.Count - 1);
		SendHand(playerId);

		// A draw keeps the turn where it is, which is exactly what TurnExtended already means.
		Game.CallExtendCurrentTurn(BuildContext("draw", playerId, DominoTileId.NoEnd));
	}

	private void TryPass(int requesterId, int turnToken)
	{
		var playerId = requesterId.ToString();

		if (!TurnIsOpenFor(playerId, turnToken, out var hand, out var reason))
		{
			Reject(requesterId, turnToken, reason);
			return;
		}

		if (_boneyard.Count > 0)
		{
			Reject(requesterId, turnToken, "must_draw");
			return;
		}

		if (DominoRules.HasLegalMove(hand, _board.LeftEnd, _board.RightEnd))
		{
			Reject(requesterId, turnToken, "has_legal_move");
			return;
		}

		_consecutivePasses++;

		if (_consecutivePasses >= Game.TurnOrder.Count)
		{
			ResolveLockedGame();
			return;
		}

		Game.CallNextTurn(BuildContext("pass", playerId, DominoTileId.NoEnd));
	}

	private void ResolveLockedGame()
	{
		_matchRunning = false;

		var turnOrder = new List<string>();
		foreach (var playerVariant in Game.TurnOrder)
			turnOrder.Add((string)playerVariant);

		var pipTotals = new System.Collections.Generic.Dictionary<string, int>();
		foreach (var playerId in turnOrder)
		{
			if (_hands.TryGetValue(playerId, out var hand))
				pipTotals[playerId] = DominoRules.PipTotal(hand);
		}

		var winner = DominoRules.ResolveLock(pipTotals, turnOrder, _lastPlayerId);
		var context = BuildContext("locked", _lastPlayerId, DominoTileId.NoEnd);

		// Hands only become public once the match is decided by them.
		var pipPlayers = new string[turnOrder.Count];
		var pips = new int[turnOrder.Count];
		for (var i = 0; i < turnOrder.Count; i++)
		{
			pipPlayers[i] = turnOrder[i];
			pips[i] = pipTotals.TryGetValue(turnOrder[i], out var total) ? total : 0;
		}

		context["final_pip_players"] = Variant.From(pipPlayers);
		context["final_pips"] = Variant.From(pips);
		context["reason"] = "locked";

		Game.ApplyMatchOver(winner, context);
	}

	/// <summary>
	/// The three checks every action shares: the match is live, it is this player's turn, and the
	/// request quotes the turn stamp the server is currently on. The stamp is what kills a double
	/// click, a click that lands after the turn already moved, and a replayed packet.
	/// </summary>
	private bool TurnIsOpenFor(string playerId, int turnToken, out List<int> hand, out string reason)
	{
		hand = null;

		if (!_matchRunning || Game == null)
		{
			reason = "match_not_running";
			return false;
		}

		if (Game.TurnOwner == null || (string)Game.TurnOwner.Name != playerId)
		{
			reason = "not_your_turn";
			return false;
		}

		if (turnToken != _turnToken)
		{
			reason = "stale_turn";
			return false;
		}

		if (!_hands.TryGetValue(playerId, out hand))
		{
			reason = "not_in_match";
			return false;
		}

		reason = null;
		return true;
	}

	// ---------------------------------------------------------------- private hand channel

	private void SendHand(string playerId)
	{
		if (!Multiplayer.IsServer() || !_hands.TryGetValue(playerId, out var hand))
			return;

		var tiles = hand.ToArray();

		if (!int.TryParse(playerId, out var peerId))
			return;

		if (peerId == Multiplayer.GetUniqueId())
			Game.ApplyLocalHand(tiles);
		else
			RpcId(peerId, MethodName.ReceiveHand, tiles);
	}

	/// <summary>
	/// The one and only route a hand takes. Covers the deal, every draw and a reclaimed slot, so
	/// there is a single method to audit for the secrecy rule.
	/// </summary>
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReceiveHand(int[] tileIds)
	{
		Game?.ApplyLocalHand(tileIds);
	}

	private void Reject(int requesterId, int turnToken, string reason)
	{
		if (requesterId == Multiplayer.GetUniqueId())
			EmitSignal(SignalName.ActionRejected, reason);
		else
			RpcId(requesterId, MethodName.ReceiveActionRejected, turnToken, reason);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReceiveActionRejected(int turnToken, string reason)
	{
		EmitSignal(SignalName.ActionRejected, reason);
	}

	// ---------------------------------------------------------------- late joiners and reconnects

	private void OnPeerConnected(int peerId)
	{
		if (!Multiplayer.IsServer() || !_matchRunning)
			return;

		RpcId(peerId, MethodName.ReceiveFullState, BuildSnapshot());
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReceiveFullState(Dictionary context)
	{
		Game?.ApplyPublicSnapshot(context);
	}

	/// <summary>Hands a reclaimed slot's tiles and the whole board to the peer that came back.</summary>
	public void ReissueStateTo(string oldPlayerId, string newPlayerId)
	{
		if (!Multiplayer.IsServer())
			return;

		if (_hands.Remove(oldPlayerId, out var hand))
			_hands[newPlayerId] = hand;

		if (_lastPlayerId == oldPlayerId)
			_lastPlayerId = newPlayerId;

		SendHand(newPlayerId);

		if (int.TryParse(newPlayerId, out var peerId) && peerId != Multiplayer.GetUniqueId())
			RpcId(peerId, MethodName.ReceiveFullState, BuildSnapshot());
	}

	/// <summary>The public state as it stands, without moving the turn on.</summary>
	private Dictionary BuildSnapshot() =>
		BuildContext(Game.LastAction, Game.LastPlayer, Game.LastTile, advanceTurn: false);

	/// <summary>Returns a player's tiles to the bottom of the boneyard when they leave for good.</summary>
	public void ReturnTilesToBoneyard(string playerId)
	{
		if (!Multiplayer.IsServer())
			return;

		if (!_hands.Remove(playerId, out var hand))
			return;

		// Kept rather than discarded so "the boneyard is empty" and the locked-game count stay
		// honest for everyone still playing.
		_boneyard.InsertRange(0, hand);
	}

	// ---------------------------------------------------------------- context packing

	/// <summary>
	/// Packs the entire public state into the turn context. Called on every state change, which is
	/// what makes the turn context the single sync channel. It carries no hand contents and no
	/// seed — only what every peer is allowed to know.
	/// </summary>
	private Dictionary BuildContext(string lastAction, string lastPlayer, int lastTile,
		bool advanceTurn = true)
	{
		// Only a real turn boundary burns the stamp. A catch-up snapshot for a late joiner must
		// not, or it would invalidate the action the current player is in the middle of sending.
		if (advanceTurn)
			_turnToken++;

		var plays = _board.Plays;
		var tiles = new int[plays.Count];
		var ends = new int[plays.Count];
		var players = new string[plays.Count];

		for (var i = 0; i < plays.Count; i++)
		{
			tiles[i] = plays[i].TileId;
			ends[i] = (int)plays[i].End;
			players[i] = plays[i].PlayerId;
		}

		var handPlayers = new List<string>();
		var handCounts = new List<int>();
		foreach (var playerVariant in Game.TurnOrder)
		{
			var playerId = (string)playerVariant;
			handPlayers.Add(playerId);
			handCounts.Add(_hands.TryGetValue(playerId, out var hand) ? hand.Count : 0);
		}

		return new Dictionary
		{
			["turn_token"] = _turnToken,
			["play_tiles"] = Variant.From(tiles),
			["play_ends"] = Variant.From(ends),
			["play_players"] = Variant.From(players),
			["left_end"] = _board.LeftEnd,
			["right_end"] = _board.RightEnd,
			["boneyard_count"] = _boneyard.Count,
			["hand_players"] = Variant.From(handPlayers.ToArray()),
			["hand_counts"] = Variant.From(handCounts.ToArray()),
			["last_action"] = lastAction ?? "",
			["last_player"] = lastPlayer ?? "",
			["last_tile"] = lastTile,
			["consecutive_passes"] = _consecutivePasses,
		};
	}
}
