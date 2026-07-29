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
    public delegate void TurnExtendedEventHandler();

    [Signal]
    public delegate void MatchOverEventHandler(string winner, Dictionary context);

    public virtual void Setup(Table table)
    {
        Table = table;
        TableInfluenceArea = table.TableInfluence;
        SetupNetworkTurnSyncronization(this);
        ConnectSignals();
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
            GameModeHandler.CurrentGameMode.TurnResolver.HandleNewTurnContext();
        else
            GD.PushWarning("Game mode handler is not configured on table: ", Name);

        EmitSignal(SignalName.TurnChanged, playerId, context);
    }

    public void CallExtendCurrentTurn()
    {
        ApplyTurnExtension();
        GD.Print("Turn extended for: ", TurnOwner.Name);
    }

    public void ApplyTurnExtension()
    {
        EmitSignal(SignalName.TurnExtended);

        if (GameModeHandler != null)
            GameModeHandler.CurrentGameMode.TurnResolver.HandleTurnExtensionContext();
        else
            GD.PushWarning("Game mode handler is not configured on table: ", Name);
    }

    public void CallMatchOver(string winner, Dictionary context)
    {
        ApplyMatchOver(winner, context);
    }

    public void ApplyMatchOver(string winner, Dictionary context)
    {
        Table.StateMachine.ChangeState(StatesRef.GameWaitingStart, new Dictionary { ["is_restart"] = true });
        EmitSignal(SignalName.MatchOver, winner, context);
    }
}
