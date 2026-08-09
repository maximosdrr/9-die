using System.Collections.Generic;
using Godot;
using Godot.Collections;
using Poker.Rules;

/// <summary>
/// The per-player surface of a poker session.
///
/// The chair, the first-person and overhead cameras and the hold-to-leave are all
/// <see cref="SeatedTableController"/>'s and are shared with the dominoes. What is left here is only
/// what is poker: working out what this player may legally do right now, and relaying the one intent
/// the hand view emits to the server.
/// </summary>
[GlobalClass]
public partial class PokerController : SeatedTableController
{
	public PokerGame Game => Table as PokerGame;

	private PokerHandView _handView;

	private static readonly List<ActionOption> NoOptions = new();

	public override bool AllowsControlSwitch => true;

	// ---------------------------------------------------------------- what the seat needs to know

	protected override SeatedHandView HandView => _handView;

	protected override Node3D TableSurface => Game?.BoardPresenter;

	protected override Marker3D SeatFor(string playerId) => Game?.SeatFor(playerId);

	protected override Node3D SeatsRoot => Game?.Seats;

	protected override bool OnSetup()
	{
		if (Game == null)
		{
			GD.PushError("PokerController equipado num jogo que não é poker.");
			return false;
		}

		SignalUtil.ConnectGuarded(Game, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnChanged));
		SignalUtil.ConnectGuarded(Game, TableGame.SignalName.TurnExtended, new Callable(this, MethodName.OnTurnExtended));
		SignalUtil.ConnectGuarded(Game, PokerGame.SignalName.HudStateUpdated, new Callable(this, MethodName.OnStateUpdated));
		SignalUtil.ConnectGuarded(Game, PokerGame.SignalName.LocalHandChanged, new Callable(this, MethodName.OnStateUpdated));

		if (Game.Resolver != null)
		{
			SignalUtil.ConnectGuarded(Game.Resolver, SecretHandTurnResolver.SignalName.ActionRejected,
				new Callable(this, MethodName.OnActionRejected));
		}

		return true;
	}

	protected override void OnHandViewSpawned(SeatedHandView view)
	{
		_handView = view as PokerHandView;
		if (_handView == null)
		{
			GD.PushError("A cena de mão do poker não é uma PokerHandView.");
			return;
		}

		_handView.Setup(Game, Player);
		_handView.ActionRequested += OnActionRequested;
	}

	public override void _ExitTree()
	{
		base._ExitTree();

		if (Game == null)
			return;

		SignalUtil.DisconnectGuarded(Game, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnChanged));
		SignalUtil.DisconnectGuarded(Game, TableGame.SignalName.TurnExtended, new Callable(this, MethodName.OnTurnExtended));
		SignalUtil.DisconnectGuarded(Game, PokerGame.SignalName.HudStateUpdated, new Callable(this, MethodName.OnStateUpdated));
		SignalUtil.DisconnectGuarded(Game, PokerGame.SignalName.LocalHandChanged, new Callable(this, MethodName.OnStateUpdated));

		if (Game.Resolver != null)
		{
			SignalUtil.DisconnectGuarded(Game.Resolver, SecretHandTurnResolver.SignalName.ActionRejected,
				new Callable(this, MethodName.OnActionRejected));
		}
	}

	// ---------------------------------------------------------------- the poker part

	private void OnTurnChanged(string nextPlayerId, Dictionary context) => RefreshView();

	private void OnTurnExtended(Dictionary context) => RefreshView();

	/// <summary>
	/// Signals are dispatched by name through Godot, so the target has to be a method declared on
	/// this script rather than an override of one further up — hence the wrapper.
	/// </summary>
	private void OnStateUpdated() => RefreshView();

	protected override void RefreshView()
	{
		if (_handView == null || Game == null || Player == null || !IsMultiplayerAuthority())
			return;

		if (!Seated)
		{
			_handView.SetInteractive(false);
			return;
		}

		var playerId = (string)Player.Name;

		// A settled hand still names a turn owner — whoever acted last — so the settled check is
		// what stops the panel offering a call on a pot that has already been paid out.
		var isYourTurn = Game.IsMatchActive
			&& !Game.HandSettled
			&& Game.IsTurnOwner(playerId)
			&& (Game.SeatPresenter?.PresentationReadyForAction ?? true);

		// The same pure function the server re-runs on whatever comes back, so the interface can
		// never offer an action the server would reject.
		var options = isYourTurn
			? PokerBetting.LegalActions(
				Game.BetStateOf(playerId), Game.CurrentBet, Game.MinRaiseIncrement)
			: NoOptions;

		_handView.Refresh(Game.LocalHoleCards, options, isYourTurn);
	}

	private void OnActionRequested(int actionKind, int total) =>
		Game?.Resolver?.RequestAction(Game.TurnToken, actionKind, total);

	private void OnActionRejected(string reason)
	{
		if (!IsMultiplayerAuthority())
			return;

		_handView?.ShowRejection(reason);
		// The rejection may have been "the turn already moved", so repaint from real state rather
		// than leaving the interface showing what the player thought was true.
		RefreshView();
	}
}
