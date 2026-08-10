using Godot;
using Godot.Collections;

[GlobalClass]
public partial class PlayerHud : CanvasLayer
{
    public Player Player;
    [Export] public Label TurnLabel;
    [Export] public Label TargetBallLabel;
    [Export] public Label TimerLabel;
    [Export] public VBoxContainer ScoreList;
    [Export] public Button PushOutButton;
    [Export] public Button AcceptPushOutButton;
    [Export] public Button PassBackButton;
    [Export] public Button SurrenderButton;

    private static readonly Color YourTurnColor = new(1.0f, 0.478431f, 0.2f);
    private static readonly Color NormalTextColor = new(0.933333f, 0.956863f, 0.984314f);
    private static readonly Color DimTextColor = new(0.678431f, 0.752941f, 0.839216f);

    private PoolGame _poolGame;
    private float _turnSeconds;

    public override void _Ready()
    {
        Visible = false;
        SurrenderButton.Pressed += OnSurrenderPressed;
        PushOutButton.Pressed += OnPushOutPressed;
        AcceptPushOutButton.Pressed += OnAcceptPushOutPressed;
        PassBackButton.Pressed += OnPassBackPressed;
    }

    private void OnSurrenderPressed()
    {
        if (_poolGame == null || Player == null)
            return;

        _poolGame.RequestSurrender((string)Player.Name);
    }

    private PoolTurnResolver PoolResolver =>
        _poolGame?.GameModeHandler?.CurrentGameMode?.TurnResolver as PoolTurnResolver;

    private void OnPushOutPressed() => PoolResolver?.RequestDeclarePushOut();

    private void OnAcceptPushOutPressed() => PoolResolver?.RequestPushOutChoice(passBack: false);

    private void OnPassBackPressed() => PoolResolver?.RequestPushOutChoice(passBack: true);

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible || _poolGame == null || @event.IsEcho())
            return;

        if (@event.IsActionPressed("declare_push_out") && PushOutButton.Visible)
        {
            OnPushOutPressed();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("accept_push_out") && AcceptPushOutButton.Visible)
        {
            OnAcceptPushOutPressed();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("pass_push_out") && PassBackButton.Visible)
        {
            OnPassBackPressed();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Initialize(Player player)
    {
        Player = player;

        if (!Player.IsMultiplayerAuthority())
        {
            SetProcess(false);
            return;
        }

        SignalUtil.ConnectGuarded(Player.GameHandler, PlayerGameHandler.SignalName.ControllerEquipped, new Callable(this, MethodName.OnControllerEquipped));
        SignalUtil.ConnectGuarded(Player.GameHandler, PlayerGameHandler.SignalName.ControllerUnequipped, new Callable(this, MethodName.OnControllerUnequipped));
    }

    public override void _Process(double delta)
    {
        if (_poolGame == null)
            return;

        _turnSeconds += (float)delta;
        TimerLabel.Text = FormatTime(_turnSeconds);
    }

    private void OnControllerEquipped(TableGame tableGame)
    {
        if (tableGame is not PoolGame poolGame)
            return;

        _poolGame = poolGame;
        Visible = true;
        _turnSeconds = 0;

        SignalUtil.ConnectGuarded(_poolGame, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnChanged));
        SignalUtil.ConnectGuarded(_poolGame, TableGame.SignalName.TurnExtended, new Callable(this, MethodName.OnTurnExtended));
        SignalUtil.ConnectGuarded(_poolGame, PoolGame.SignalName.HudStateUpdated, new Callable(this, MethodName.OnHudStateUpdated));

        Refresh();
    }

    private void OnControllerUnequipped()
    {
        if (_poolGame != null)
        {
            SignalUtil.DisconnectGuarded(_poolGame, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnChanged));
            SignalUtil.DisconnectGuarded(_poolGame, TableGame.SignalName.TurnExtended, new Callable(this, MethodName.OnTurnExtended));
            SignalUtil.DisconnectGuarded(_poolGame, PoolGame.SignalName.HudStateUpdated, new Callable(this, MethodName.OnHudStateUpdated));
        }

        _poolGame = null;
        Visible = false;
    }

    private void OnTurnChanged(string nextPlayerId, Dictionary context)
    {
        _turnSeconds = 0;
        Refresh();
    }

    private void OnTurnExtended(Dictionary context)
    {
        _turnSeconds = 0;
        Refresh();
    }

    private void OnHudStateUpdated()
    {
        Refresh();
    }

    private void Refresh()
    {
        if (_poolGame == null || !IsInstanceValid(_poolGame)
            || string.IsNullOrEmpty(_poolGame.TurnOwnerId))
            return;

        var isYourTurn = _poolGame.IsTurnOwner((string)Player.Name);

        TurnLabel.Text = isYourTurn ? "Sua vez!" : $"Vez de {GetPlayerLabel(_poolGame.TurnOwnerId)}";
        if (_poolGame.PushOutDeclared)
            TurnLabel.Text += " • push-out";
        else if (_poolGame.PushOutChoicePending && isYourTurn)
            TurnLabel.Text = "Escolha após o push-out";
        TurnLabel.AddThemeColorOverride("font_color", isYourTurn ? YourTurnColor : NormalTextColor);

        TargetBallLabel.Text = _poolGame.CurrentTargetBallIndex > 0
            ? $"Bola-alvo: {_poolGame.CurrentTargetBallIndex}"
            : "";

        PushOutButton.Visible = isYourTurn && _poolGame.PushOutAvailable;
        AcceptPushOutButton.Visible = isYourTurn && _poolGame.PushOutChoicePending;
        PassBackButton.Visible = isYourTurn && _poolGame.PushOutChoicePending;

        RefreshScoreList();
    }

    private void RefreshScoreList()
    {
        foreach (var child in ScoreList.GetChildren())
            child.QueueFree();

        foreach (var playerIdVariant in _poolGame.TurnOrder)
        {
            var playerId = (string)playerIdVariant;
            var pocketed = _poolGame.BallsPocketedByPlayer.TryGetValue(playerId, out var balls) ? balls.Count : 0;
            var fouls = _poolGame.ConsecutiveFoulsByPlayer.TryGetValue(playerId, out var foulCount) ? foulCount : 0;

            var row = new Label();
            row.Text = fouls > 0
                ? $"{GetPlayerLabel(playerId)}: {pocketed} bola(s) • "
                  + $"{fouls}/{PoolTurnResolver.ConsecutiveFoulLossThreshold} faltas"
                : $"{GetPlayerLabel(playerId)}: {pocketed} bola(s)";
            row.AddThemeFontSizeOverride("font_size", 15);
            row.AddThemeColorOverride("font_color", DimTextColor);
            ScoreList.AddChild(row);
        }
    }

    private string GetPlayerLabel(string playerId)
    {
        var player = PlayerRegistry.Instance.GetPlayerById(playerId);
        return player != null && !string.IsNullOrWhiteSpace(player.Nickname) ? player.Nickname : $"Jogador {playerId}";
    }

    private static string FormatTime(float seconds)
    {
        var totalSeconds = Mathf.FloorToInt(seconds);
        var minutes = totalSeconds / 60;
        var secs = totalSeconds % 60;
        return $"{minutes:00}:{secs:00}";
    }
}
