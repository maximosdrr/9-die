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

        foreach (var playerIdVariant in playersIds)
        {
            var playerId = (string)playerIdVariant;
            var player = PlayerRegistry.Instance.GetPlayerById(playerId);

            Table.PlayersOnMatch.Add((string)player.Name);

            if (int.Parse((string)player.Name) == Multiplayer.GetUniqueId())
                Table.CurrentTableGame.Player = player;
        }

        Table.CurrentTableGame.TurnOrder = playersIds;
        var firstTurnOwnerId = (string)playersIds[0];

        var firstPlayer = PlayerRegistry.Instance.GetPlayerById(firstTurnOwnerId);
        Table.CurrentTableGame.TurnOwner = firstPlayer;

        if (Multiplayer.IsServer())
            Table.CurrentTableGame.SetupMatch(playersIds, firstTurnOwnerId);
    }
}
