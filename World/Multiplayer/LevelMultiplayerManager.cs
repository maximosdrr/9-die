using System.Collections.Generic;
using Godot;

[GlobalClass]
public partial class LevelMultiplayerManager : Node
{
    [Export] public PackedScene PlayerScene;
    [Export] public PlayersContainer PlayersContainer;
    [Export] public MultiplayerSpawner MultiplayerSpawner;
    [Export] public Node3D SpawnPointsRoot;
    [Export] public TvScreenShare TvScreen;
    [Export] public Node LocalPresentationRoot;

    public GlobalCamera Camera;

    private readonly List<Marker3D> _spawnPoints = new();

    public override void _Ready()
    {
        _spawnPoints.Clear();
        if (SpawnPointsRoot != null)
        {
            foreach (var child in SpawnPointsRoot.GetChildren())
            {
                if (child is Marker3D marker)
                    _spawnPoints.Add(marker);
            }
        }

        MultiplayerSpawner.SpawnFunction = new Callable(this, MethodName.InitializePlayerNode);
        MultiplayerSpawner.SpawnPath = PlayersContainer.GetPath();

        NetworkManager.Instance.NetworkProvider.PlayerConnected += OnPlayerConnect;
        NetworkManager.Instance.NetworkProvider.PlayerDisconnected += OnPlayerDisconnect;
    }

    public override void _ExitTree()
    {
        var provider = NetworkManager.Instance?.NetworkProvider;
        if (provider == null)
            return;

        provider.PlayerConnected -= OnPlayerConnect;
        provider.PlayerDisconnected -= OnPlayerDisconnect;
    }

    private void OnPlayerConnect(int peerId)
    {
        if (!IsMultiplayerAuthority())
            return;
        MultiplayerSpawner.Spawn(peerId);
    }

    private void OnPlayerDisconnect(int peerId)
    {
        if (!IsMultiplayerAuthority())
            return;

        GD.Print($"Server - Player disconnected Peer ID: {peerId}");

        if (PlayersContainer.HasNode(peerId.ToString()))
        {
            var player = PlayersContainer.GetNode(peerId.ToString());
            player.QueueFree();
        }
    }

    private Node InitializePlayerNode(int peerId)
    {
        var playerInstance = (Player)PlayerScene.Instantiate();

        playerInstance.Name = peerId.ToString();
        playerInstance.ConfigureNetworkMovement(peerId);
        playerInstance.ConfigureNetworkProfile(peerId);
        // Peer authority owns input, camera and per-player game views. Player movement itself is
        // simulated by the server and arrives on clients only through authoritative snapshots.
        playerInstance.SetMultiplayerAuthority(peerId);
        playerInstance.ConfigureServerAuthoritativeReplication();
        playerInstance.Camera = Camera;
        playerInstance.TvScreen = TvScreen;
        playerInstance.LocalPresentationRoot = LocalPresentationRoot;

        if (_spawnPoints.Count == 0)
        {
            GD.PushError("There's no spawn points yet");
            return null;
        }

        var playersCount = PlayersContainer.GetChildCount();
        var spawnIndex = playersCount % _spawnPoints.Count;

        var spawnPoint = _spawnPoints[spawnIndex];
        playerInstance.GlobalTransform = spawnPoint.GlobalTransform;

        return playerInstance;
    }
}
