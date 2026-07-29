using Godot;

[Tool]
[GlobalClass]
public partial class Table : Node3D
{
    public Godot.Collections.Array<string> PlayersOnMatch = new();

    public Node3D TableGameHandler;
    public Area3D TableInfluence;
    public StateMachine StateMachine;
    public Label3D DebugLabel;

    [ExportCategory("NetworkConfiguration")]
    [Export] public bool EnableNetworkTurnSyncronization = true;

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

    public override void _Ready()
    {
        TableGameHandler = GetNode<Node3D>("TableGameHandler");

        if (Engine.IsEditorHint())
        {
            RebuildEditorPreview();
            return;
        }

        TableInfluence = GetNode<Area3D>("TableInfluence");
        StateMachine = GetNode<StateMachine>("StateMachine");
        DebugLabel = GetNode<Label3D>("DebugLabel");

        SpawnRuntimeGame();
    }

    public override void _Process(double delta)
    {
        if (CurrentTableGame == null || CurrentTableGame.TurnOwner == null)
            return;

        DebugLabel.Text = $"Current player turn: {CurrentTableGame.TurnOwner.Name}";
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
        if (TableGameHandler == null)
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
