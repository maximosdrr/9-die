using System.Collections.Generic;
using Godot;

[Tool]
[GlobalClass]
public partial class Table : Node3D
{
    public Godot.Collections.Array<string> PlayersOnMatch = new();

    [ExportGroup("Scene References")]
    [Export] public Node3D TableGameHandler;
    [Export] public Area3D TableInfluence;
    [Export] public Node StateMachineNode;
    [Export] public Timer StartGameTimer;
    [Export] public Timer EndMatchTimer;
    [Export] public Label3D DebugLabel;
    public StateMachine StateMachine;

    [ExportCategory("NetworkConfiguration")]
    [Export] public bool EnableNetworkTurnSynchronization = true;

    private PackedScene _tableGameScene;
    [Export]
    public PackedScene TableGameScene
    {
        get => _tableGameScene;
        set
        {
            _tableGameScene = value;
            if (Engine.IsEditorHint())
                RebuildEditorPreview();
        }
    }

    private bool _editorBuilding = false;
    public TableGame CurrentTableGame;

    public override string[] _GetConfigurationWarnings()
    {
        var warnings = new List<string>();
        if (TableGameScene == null)
            warnings.Add("TableGameScene precisa apontar para uma cena de jogo de mesa.");
        if (TableInfluence == null)
            warnings.Add("TableInfluence (Area3D) é obrigatório para detectar jogadores próximos.");
        if (StateMachineNode == null)
            warnings.Add("StateMachine é obrigatória para o ciclo de vida da mesa.");
        if (StartGameTimer == null || EndMatchTimer == null)
            warnings.Add("Os timers de início e encerramento da partida são obrigatórios.");
        return warnings.ToArray();
    }

    public void SetCamera(GlobalCamera camera)
    {
        CurrentTableGame?.SetCamera(camera);
    }

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
        {
            RebuildEditorPreview();
            return;
        }

        StateMachine = StateMachineNode as StateMachine;
        SetProcess(DebugLabel?.Visible == true);

        SpawnRuntimeGame();
    }

    public override void _Process(double delta)
    {
        if (!IsInstanceValid(CurrentTableGame))
        {
            CurrentTableGame = null;
            return;
        }

        var turnOwner = CurrentTableGame.TurnOwner;
        if (!IsInstanceValid(turnOwner) || !IsInstanceValid(DebugLabel))
            return;

        DebugLabel.Text = $"Current player turn: {turnOwner.Name}";
    }

    private void SpawnRuntimeGame()
    {
        if (TableGameScene == null)
        {
            GD.PushError("Table scene is null!");
            return;
        }

        var tableGameInstance = TableGameScene.Instantiate();
        var tableGame = (TableGame)tableGameInstance;
        tableGame.Setup(this);

        TableGameHandler.AddChild(tableGameInstance);
        CurrentTableGame = tableGame;
    }

    private void RebuildEditorPreview()
    {
        if (!IsInsideTree() || TableGameHandler == null)
            return;

        if (_editorBuilding)
            return;
        _editorBuilding = true;

        foreach (var c in TableGameHandler.GetChildren())
            c.QueueFree();

        if (TableGameScene != null)
        {
            var preview = TableGameScene.Instantiate();
            preview.Name = "TableGamePreview";
            TableGameHandler.AddChild(preview);

            if (GetTree() != null && GetTree().EditedSceneRoot != null)
                preview.Owner = GetTree().EditedSceneRoot;
        }

        _editorBuilding = false;
    }
}
