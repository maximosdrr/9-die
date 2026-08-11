using System.Collections.Generic;
using Godot;

public partial class MatchRanking : Node
{
    public static MatchRanking Instance { get; private set; }

    public Dictionary<string, int> WinsByPlayer = new();

    [Signal]
    public delegate void RankingUpdatedEventHandler();

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        NetworkManager.Instance.NetworkProvider.PlayerConnected += OnPlayerConnected;
    }

    public void RegisterWin(string playerId)
    {
        if (!Multiplayer.IsServer() || string.IsNullOrEmpty(playerId))
            return;

        WinsByPlayer.TryGetValue(playerId, out var current);
        WinsByPlayer[playerId] = current + 1;

        BroadcastRanking();
    }

    private void OnPlayerConnected(int peerId)
    {
        if (!Multiplayer.IsServer() || peerId == Multiplayer.GetUniqueId())
            return;

        RpcId(peerId, MethodName.RpcSyncRanking, BuildRankingDictionary());
    }

    private void BroadcastRanking()
    {
        Rpc(MethodName.RpcSyncRanking, BuildRankingDictionary());
    }

    private Godot.Collections.Dictionary BuildRankingDictionary()
    {
        var dict = new Godot.Collections.Dictionary();
        foreach (var kvp in WinsByPlayer)
            dict[kvp.Key] = kvp.Value;
        return dict;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RpcSyncRanking(Godot.Collections.Dictionary ranking)
    {
        WinsByPlayer.Clear();
        foreach (var key in ranking.Keys)
            WinsByPlayer[(string)key] = (int)ranking[key];

        EmitSignal(SignalName.RankingUpdated);
    }
}
