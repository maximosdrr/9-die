using System.Collections.Generic;
using Domino.Rules;
using Godot;
using Godot.Collections;

/// <summary>
/// The per-player surface of a domino match.
///
/// The chair, the first-person and overhead cameras and the hold-to-leave are all
/// <see cref="SeatedTableController"/>'s and are shared with every other seated game. What is left
/// here is only what is dominoes: which tiles are legal right now, and relaying the four intents the
/// hand view emits to the server.
///
/// Nothing moves when the turn changes. Players normally stay seated, but may use the shared
/// control switch to get up, walk around and return to their own chair.
/// </summary>
[GlobalClass]
public partial class DominoController : SeatedTableController
{
    public DominoGame Game => Table as DominoGame;

    private DominoHandView _handView;

    private static readonly List<MoveOption> NoMoves = new();

    public override bool AllowsControlSwitch => true;

    // ---------------------------------------------------------------- what the seat needs to know

    protected override SeatedHandView HandView => _handView;

    protected override Node3D TableSurface => Game?.ChainPresenter;

    protected override Marker3D SeatFor(string playerId) => Game?.SeatFor(playerId);

    protected override Node3D SeatsRoot => Game?.Seats;

    protected override bool OnSetup()
    {
        if (Game == null)
        {
            GD.PushError("DominoController equipado num jogo que não é dominó.");
            return false;
        }

        SignalUtil.ConnectGuarded(Game, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnChanged));
        SignalUtil.ConnectGuarded(Game, TableGame.SignalName.TurnExtended, new Callable(this, MethodName.OnTurnExtended));
        SignalUtil.ConnectGuarded(Game, DominoGame.SignalName.HudStateUpdated, new Callable(this, MethodName.OnStateUpdated));
        SignalUtil.ConnectGuarded(Game, DominoGame.SignalName.LocalHandChanged, new Callable(this, MethodName.OnStateUpdated));

        if (Game.Resolver != null)
        {
            SignalUtil.ConnectGuarded(Game.Resolver, DominoTurnResolver.SignalName.ActionRejected,
                new Callable(this, MethodName.OnActionRejected));
        }

        return true;
    }

    protected override void OnHandViewSpawned(SeatedHandView view)
    {
        _handView = view as DominoHandView;
        if (_handView == null)
        {
            GD.PushError("A cena de mão do dominó não é uma DominoHandView.");
            return;
        }

        _handView.Setup(Game, Player);
        _handView.TilePlayRequested += OnTilePlayRequested;
        _handView.DrawRequested += OnDrawRequested;
        _handView.PassRequested += OnPassRequested;
    }

    public override void _ExitTree()
    {
        base._ExitTree();

        if (Game == null)
            return;

        SignalUtil.DisconnectGuarded(Game, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnChanged));
        SignalUtil.DisconnectGuarded(Game, TableGame.SignalName.TurnExtended, new Callable(this, MethodName.OnTurnExtended));
        SignalUtil.DisconnectGuarded(Game, DominoGame.SignalName.HudStateUpdated, new Callable(this, MethodName.OnStateUpdated));
        SignalUtil.DisconnectGuarded(Game, DominoGame.SignalName.LocalHandChanged, new Callable(this, MethodName.OnStateUpdated));

        if (Game.Resolver != null)
        {
            SignalUtil.DisconnectGuarded(Game.Resolver, DominoTurnResolver.SignalName.ActionRejected,
                new Callable(this, MethodName.OnActionRejected));
        }
    }

    // ---------------------------------------------------------------- the domino part

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

        var isYourTurn = Game.IsMatchActive && Game.IsTurnOwner((string)Player.Name);

        // The same pure functions the server re-runs on whatever comes back, so the interface can
        // never offer a move the server would reject.
        var moves = isYourTurn
            ? DominoRules.LegalMoves(Game.LocalHand, Game.LeftEnd, Game.RightEnd)
            : NoMoves;
        var canDraw = isYourTurn
                      && DominoRules.CanDraw(Game.LocalHand, Game.LeftEnd, Game.RightEnd, Game.BoneyardCount);
        var mustPass = isYourTurn
                       && DominoRules.MustPass(Game.LocalHand, Game.LeftEnd, Game.RightEnd, Game.BoneyardCount);

        _handView.Refresh(Game.LocalHand, moves, isYourTurn, canDraw, mustPass);
    }

    private void OnTilePlayRequested(int tileId, int end) =>
        Game?.Resolver?.RequestPlayTile(Game.TurnToken, tileId, end);

    private void OnDrawRequested(int slot) => Game?.Resolver?.RequestDrawTile(Game.TurnToken, slot);

    private void OnPassRequested() => Game?.Resolver?.RequestPass(Game.TurnToken);

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
