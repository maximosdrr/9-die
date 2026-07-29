using Godot;
using System.Collections.Generic;

[GlobalClass]
public partial class LevelMultiplayerManager : Node
{
	private static readonly PackedScene PlayerScene = GD.Load<PackedScene>("uid://dq4mwkkjaq18o");

	[Export] public PlayersContainer PlayersContainer;
	[Export] public MultiplayerSpawner MultiplayerSpawner;
	[Export] public Godot.Collections.Array<NodePath> SpawnPointPaths = new();

	private readonly List<Marker3D> _spawnPoints = new();

	public override void _Ready()
	{
		foreach (var path in SpawnPointPaths)
			_spawnPoints.Add(GetNode<Marker3D>(path));

		MultiplayerSpawner.SpawnFunction = new Callable(this, MethodName.InitializePlayerNode);
		MultiplayerSpawner.SpawnPath = PlayersContainer.GetPath();

		NetworkManager.Instance.NetworkProvider.PlayerConnected += OnPlayerConnect;
		NetworkManager.Instance.NetworkProvider.PlayerDisconnected += OnPlayerDisconnect;
	}

	private void OnPlayerConnect(int peerId)
	{
		if (!IsMultiplayerAuthority())
			return;
		MultiplayerSpawner.Spawn(peerId);
	}

	private void OnPlayerDisconnect(int peerId)
	{
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
		playerInstance.Id = peerId;
		playerInstance.SetMultiplayerAuthority(peerId);

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
