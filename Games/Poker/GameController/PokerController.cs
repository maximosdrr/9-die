using System.Collections.Generic;
using System.Linq;
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
	private int _latestPreparedWagerTurn = -1;
	private int _latestPreparedWagerRevision = -1;

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
			SignalUtil.ConnectGuarded(Game.Resolver,
				SecretHandTurnResolver.SignalName.StampedActionRejected,
				new Callable(this, MethodName.OnStampedActionRejected));
			SignalUtil.ConnectGuarded(Game.Resolver,
				PokerTurnResolver.SignalName.PreparedWagerRejected,
				new Callable(this, MethodName.OnPreparedWagerRejected));
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
		_handView.PreparedWagerChanged += OnPreparedWagerChanged;
	}

	public override void _ExitTree()
	{
		base._ExitTree();

		if (_handView != null)
		{
			_handView.ActionRequested -= OnActionRequested;
			_handView.PreparedWagerChanged -= OnPreparedWagerChanged;
		}

		if (Game == null)
			return;

		SignalUtil.DisconnectGuarded(Game, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnChanged));
		SignalUtil.DisconnectGuarded(Game, TableGame.SignalName.TurnExtended, new Callable(this, MethodName.OnTurnExtended));
		SignalUtil.DisconnectGuarded(Game, PokerGame.SignalName.HudStateUpdated, new Callable(this, MethodName.OnStateUpdated));
		SignalUtil.DisconnectGuarded(Game, PokerGame.SignalName.LocalHandChanged, new Callable(this, MethodName.OnStateUpdated));

		if (Game.Resolver != null)
		{
			SignalUtil.DisconnectGuarded(Game.Resolver,
				SecretHandTurnResolver.SignalName.StampedActionRejected,
				new Callable(this, MethodName.OnStampedActionRejected));
			SignalUtil.DisconnectGuarded(Game.Resolver,
				PokerTurnResolver.SignalName.PreparedWagerRejected,
				new Callable(this, MethodName.OnPreparedWagerRejected));
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
			&& !Game.ShowdownWaiting
			&& Game.IsTurnOwner(playerId)
			&& (Game.SeatPresenter?.PresentationReadyForAction ?? true);

		// The same pure function the server re-runs on whatever comes back, so the interface can
		// never offer an action the server would reject.
		var options = isYourTurn
			? PokerBetting.LegalActions(
				Game.BetStateOf(playerId),
				Game.CurrentBet,
				Game.MinRaiseIncrement,
				Game.HasOpponentWhoCanAct(playerId))
			: NoOptions;

		_handView.Refresh(Game.LocalHoleCards, options, isYourTurn);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (Seated && IsMultiplayerAuthority() && Game?.LocalMustReveal == true
			&& @event.IsActionPressed(PokerInput.ShowdownReveal))
		{
			Game.Resolver?.RequestShowdownReveal();
			GetViewport().SetInputAsHandled();
			return;
		}

		base._UnhandledInput(@event);
	}

	private void OnActionRequested(int actionKind, int total)
	{
		if (Game?.Resolver == null)
			return;
		var presenter = Game.SeatPresenter;
		var denominations = presenter?.PreparedWagerDenominations
			?? System.Array.Empty<int>();
		var playerId = Game.Player == null ? "" : (string)Game.Player.Name;
		var added = string.IsNullOrEmpty(playerId) ? 0
			: Mathf.Max(0, total - Game.BetOf(playerId));
		if (presenter?.PreparedWagerSubmitted != true || denominations.Sum() != added)
		{
			if (presenter?.PreparedWagerAmount > 0)
				// Publish the empty snapshot before an action that does not consume the preview.
				// Both messages use the same reliable peer channel, so the server observes the
				// cancellation before validating an all-in/check/fold request.
				_handView?.CancelPreparedWager(immediate: true);
			denominations = System.Array.Empty<int>();
		}
		Game.Resolver.RequestAction(Game.TurnToken, actionKind, total, denominations);
	}

	private void OnPreparedWagerChanged(
		int turnToken, int revision, int[] denominations)
	{
		if (!IsMultiplayerAuthority() || Game?.Resolver == null)
			return;
		_latestPreparedWagerTurn = turnToken;
		_latestPreparedWagerRevision = revision;
		Game.Resolver.RequestPreparedWager(
			turnToken, revision, denominations ?? System.Array.Empty<int>());
	}

	private void OnStampedActionRejected(int turnToken, string reason)
	{
		if (!IsMultiplayerAuthority() || Game == null || turnToken != Game.TurnToken)
			return;

		// A stale or refused request never owns the tentative chips. Put them back before repainting
		// the legal state so the local table and the authoritative stack cannot disagree.
		_handView?.CancelPreparedWager();
		_handView?.ShowRejection(reason);
		// The rejection may have been "the turn already moved", so repaint from real state rather
		// than leaving the interface showing what the player thought was true.
		RefreshView();
	}

	private void OnPreparedWagerRejected(int turnToken, int revision, string reason)
	{
		if (!IsMultiplayerAuthority() || Game == null
			|| turnToken != Game.TurnToken
			|| turnToken != _latestPreparedWagerTurn
			|| revision != _latestPreparedWagerRevision)
			return;

		// Only the exact rejected revision owns the local actors. An older refusal can arrive after
		// a corrected selection and must never pull that newer wager back into the bank.
		_handView?.CancelPreparedWager();
		_handView?.ShowRejection(reason);
		RefreshView();
	}
}
