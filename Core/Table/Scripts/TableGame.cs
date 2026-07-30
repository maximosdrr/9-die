using Godot;
using Godot.Collections;

[GlobalClass]
public partial class TableGame : Node3D
{
    public Dictionary<ulong, Player> PlayersOnArea = new();
    public Area3D TableInfluenceArea;
    public PlayersContainer PlayersContainer;
    public Table Table;
    public GameModeHandler GameModeHandler;
    public Array TurnOrder = new();
    public Player TurnOwner;
    public Player Player;
    public TableTurnNetworkBridge NetworkTurnSyncronization;

    [Signal]
    public delegate void TurnChangedEventHandler(string nextPlayerName, Dictionary context);

    [Signal]
    public delegate void MatchStartedEventHandler(Array playersIds, string firstTurnPlayer);

    [Signal]
    public delegate void TurnExtendedEventHandler(Dictionary context);

    [Signal]
    public delegate void MatchOverEventHandler(string winner, Dictionary context);

    public virtual void Setup(Table table)
    {
        Table = table;
        TableInfluenceArea = table.TableInfluence;
        SetupNetworkTurnSyncronization(this);
        ConnectSignals();

        NetworkManager.Instance.NetworkProvider.PlayerDisconnected += OnPlayerDisconnected;
    }

    private void OnPlayerDisconnected(int peerId)
    {
        if (!Multiplayer.IsServer())
            return;

        if (TurnOwner == null || TurnOrder.Count == 0)
            return;

        var disconnectedId = peerId.ToString();

        if (!TurnOrder.Contains(disconnectedId))
            return;

        string remainingId = null;
        foreach (var idVariant in TurnOrder)
        {
            var id = (string)idVariant;
            if (id != disconnectedId)
            {
                remainingId = id;
                break;
            }
        }

        if (remainingId == null)
            return;

        ApplyMatchOver(remainingId, new Dictionary { ["reason"] = "opponent_disconnected" });
    }

    private void SetupNetworkTurnSyncronization(TableGame tableGame)
    {
        if (!Table.EnableNetworkTurnSyncronization)
            return;

        NetworkTurnSyncronization = new TableTurnNetworkBridge();
        NetworkTurnSyncronization.Name = "TableTurnNetworkBridge";

        AddChild(NetworkTurnSyncronization);

        NetworkTurnSyncronization.Setup(tableGame);
    }

    private void ConnectSignals()
    {
        SignalUtil.ConnectGuarded(TableInfluenceArea, Area3D.SignalName.BodyEntered, new Callable(this, MethodName.OnTableInfluenceBodyEntered));
        SignalUtil.ConnectGuarded(TableInfluenceArea, Area3D.SignalName.BodyExited, new Callable(this, MethodName.OnTableInfluenceBodyExited));
    }

    private void OnTableInfluenceBodyEntered(Node3D body)
    {
        if (body is not Player player)
            return;

        var id = player.GetInstanceId();
        if (PlayersOnArea.ContainsKey(id))
            return;

        PlayersOnArea[id] = player;
    }

    private void OnTableInfluenceBodyExited(Node3D body)
    {
        if (body is not Player player)
            return;

        PlayersOnArea.Remove(player.GetInstanceId());
    }

    public virtual void SetupMatch(Array players, string firstTurnOwnerId) { }

    public void CallNextTurn(Dictionary context)
    {
        var currentId = (string)TurnOwner.Name;
        var currentIndex = TurnOrder.IndexOf(currentId);

        if (currentIndex == -1)
            return;

        var nextIndex = (currentIndex + 1) % TurnOrder.Count;
        var nextPlayerId = (string)TurnOrder[nextIndex];

        ApplyNewTurn(nextPlayerId, context);

        GD.Print("Turn passed to: ", nextPlayerId);
    }

    public void ApplyNewTurn(string playerId, Dictionary context)
    {
        var nextPlayer = PlayerRegistry.Instance.GetPlayerById(playerId);

        if (nextPlayer == null)
        {
            GD.PushError("Tentativa de mudar turno para jogador inexistente: " + playerId);
            return;
        }

        TurnOwner = nextPlayer;

        if (GameModeHandler != null)
            GameModeHandler.CurrentGameMode.TurnResolver.HandleNewTurnContext(context);
        else
            GD.PushWarning("Game mode handler is not configured on table: ", Name);

        EmitSignal(SignalName.TurnChanged, playerId, context);
    }

    public void CallExtendCurrentTurn(Dictionary context)
    {
        ApplyTurnExtension(context);
        GD.Print("Turn extended for: ", TurnOwner.Name);
    }

    public void ApplyTurnExtension(Dictionary context)
    {
        EmitSignal(SignalName.TurnExtended, context);

        if (GameModeHandler != null)
            GameModeHandler.CurrentGameMode.TurnResolver.HandleTurnExtensionContext(context);
        else
            GD.PushWarning("Game mode handler is not configured on table: ", Name);
    }

    public void ApplyMatchOver(string winner, Dictionary context)
    {
        context["winner"] = winner;
        Table.StateMachine.ChangeState(StatesRef.GameFinished, context);
        EmitSignal(SignalName.MatchOver, winner, context);
    }
}
