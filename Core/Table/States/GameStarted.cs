using Godot;
using Godot.Collections;

[GlobalClass]
public partial class GameStarted : State
{
    [Export] public Table Table;

    public GameStarted()
    {
        Type = StatesRef.GameStarted;
    }

    public override void Enter(Dictionary metadata)
    {
        Table.PlayersOnMatch = new Array<string>();
        var playersIds = (Array)metadata["players_ids"];

        if (playersIds.Count == 0)
        {
            GD.PushWarning("GameStarted recebeu uma partida sem jogadores.");
            return;
        }

        foreach (var playerIdVariant in playersIds)
        {
            var playerId = (string)playerIdVariant;
            var player = PlayerRegistry.Instance.GetPlayerById(playerId);

            // State synchronization can reach a reconnecting client before its replacement Player
            // node. Keep the authoritative id without dereferencing a disposed/missing object.
            Table.PlayersOnMatch.Add(playerId);

            if (player != null && int.TryParse(playerId, out var peerId)
                && peerId == Multiplayer.GetUniqueId())
                Table.CurrentTableGame.Player = player;
        }

        var firstTurnOwnerId = (string)playersIds[0];
        Table.CurrentTableGame.PrepareMatch(playersIds, firstTurnOwnerId);

        if (Multiplayer.IsServer())
            Table.CurrentTableGame.SetupMatch(playersIds, firstTurnOwnerId);
    }
}
