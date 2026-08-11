using System.Collections.Generic;
using Godot;

public partial class PlayerRegistry : Node
{
    public static PlayerRegistry Instance { get; private set; }

    private readonly Dictionary<string, Player> _playersById = new();
    private Node3D _playersContainer;

    /// <summary>
    /// The active level's player container. Assigning it rebuilds the index and subscribes to
    /// spawn/despawn events. Kept as a property so existing setup code remains source-compatible.
    /// </summary>
    public Node3D PlayersContainer
    {
        get => IsInstanceValid(_playersContainer) ? _playersContainer : null;
        set => SetContainer(value);
    }

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _ExitTree()
    {
        SetContainer(null);
        if (Instance == this)
            Instance = null;
    }

    public bool HasContainer() => IsInstanceValid(_playersContainer);

    public void RegisterContainer(Node3D container)
    {
        SetContainer(container);
    }

    public void UnregisterContainer(Node3D container)
    {
        if (_playersContainer == container)
            SetContainer(null);
    }

    /// <summary>Looks up a live player without logging when spawn order makes a miss expected.</summary>
    public bool TryGetPlayerById(string id, out Player player)
    {
        player = null;
        if (string.IsNullOrEmpty(id) || !HasContainer())
            return false;

        if (!_playersById.TryGetValue(id, out var indexedPlayer))
            return false;

        if (!IsInstanceValid(indexedPlayer))
        {
            _playersById.Remove(id);
            return false;
        }

        player = indexedPlayer;
        return true;
    }

    public Player GetPlayerById(string id)
    {
        if (!HasContainer())
        {
            GD.PushError("PlayersContainer not set yet");
            return null;
        }

        if (TryGetPlayerById(id, out var player))
            return player;

        GD.PushError($"Player '{id}' not found");
        return null;
    }

    private void SetContainer(Node3D container)
    {
        if (_playersContainer == container)
        {
            RebuildIndex();
            return;
        }

        DisconnectContainerSignals();
        _playersById.Clear();
        _playersContainer = IsInstanceValid(container) ? container : null;

        if (!HasContainer())
            return;

        _playersContainer.ChildEnteredTree += OnContainerChildEnteredTree;
        _playersContainer.ChildExitingTree += OnContainerChildExitingTree;
        RebuildIndex();
    }

    private void DisconnectContainerSignals()
    {
        if (!IsInstanceValid(_playersContainer))
            return;

        _playersContainer.ChildEnteredTree -= OnContainerChildEnteredTree;
        _playersContainer.ChildExitingTree -= OnContainerChildExitingTree;
    }

    private void RebuildIndex()
    {
        _playersById.Clear();
        if (!HasContainer())
            return;

        foreach (var child in _playersContainer.GetChildren())
            IndexPlayer(child);
    }

    private void OnContainerChildEnteredTree(Node child)
    {
        IndexPlayer(child);
    }

    private void OnContainerChildExitingTree(Node child)
    {
        if (child is not Player player)
            return;

        var id = (string)player.Name;
        if (_playersById.TryGetValue(id, out var indexedPlayer) && indexedPlayer == player)
            _playersById.Remove(id);
    }

    private void IndexPlayer(Node child)
    {
        if (child is not Player player || !IsInstanceValid(player))
            return;

        _playersById[(string)player.Name] = player;
    }
}
