using Godot;

/// <summary>
/// Protects the composition boundaries of the main level without starting a network session.
/// </summary>
public partial class PrototypeSceneLoadTest : Node
{
    private int _passed;
    private int _failed;

    public override async void _Ready()
    {
        GD.Print("=== Teste de composição do cenário principal ===");

        var scene = GD.Load<PackedScene>("res://World/Levels/PrototypeLevel/Prototype.tscn");
        var level = scene?.Instantiate<Node3D>();

        Check("a cena principal pode ser instanciada", level != null);
        if (level != null)
        {
            AddChild(level);

            var manager = level.GetNodeOrNull<LevelMultiplayerManager>(
                "Multiplayer/LevelMultiplayerManager");
            var spawnRoot = level.GetNodeOrNull<Node3D>("Multiplayer/SpawnPoints");
            var barVisual = level.GetNodeOrNull<Node3D>("Bar");
            var barCollision = level.GetNodeOrNull<Node3D>("BarCollision");
            var tv = level.GetNodeOrNull<TvScreenShare>("TV");

            Check("visual do bar é um componente instanciado", barVisual?.GetChildCount() > 0);
            Check("colisões do bar ficam em um componente separado",
                barCollision?.GetNodeOrNull<StaticBody3D>("RoomBoundary") != null
                && barCollision.GetNodeOrNull<StaticBody3D>("FurnitureCollision") != null);
            Check("a TV usa um root comportamental e um filho visual",
                tv != null && tv.GetNodeOrNull<MeshInstance3D>("Frame") != null);
            Check("o nível oferece quatro pontos de spawn explícitos",
                spawnRoot?.GetChildCount() == 4);
            Check("o gerenciador recebe suas dependências pelo Inspector",
                manager?.PlayerScene != null
                && manager.PlayersContainer != null
                && manager.MultiplayerSpawner != null
                && manager.SpawnPointsRoot == spawnRoot
                && manager.TvScreen == tv
                && manager.LocalPresentationRoot != null);
            Check("a apresentação local é irmã dos avatares replicados",
                manager != null
                && manager.LocalPresentationRoot?.GetParent()
                    == manager.PlayersContainer?.GetParent());

            level.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        GD.Print($"=== {_passed} passaram, {_failed} falharam ===");
        GetTree().Quit(_failed > 0 ? 1 : 0);
    }

    private void Check(string label, bool condition)
    {
        if (condition)
        {
            _passed++;
            GD.Print($"  OK   {label}");
        }
        else
        {
            _failed++;
            GD.Print($"  FALHA {label}");
        }
    }
}
