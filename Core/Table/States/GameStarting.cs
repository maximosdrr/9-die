using Godot;
using Godot.Collections;
using System.Collections.Generic;
using System.Linq;

[GlobalClass]
public partial class GameStarting : State
{
    [Export] public Timer StartGameTimer;
    [Export] public PoolStartGameUI StartGameUI;

    public Array PlayersIds = new();

    public GameStarting()
    {
        Type = StatesRef.GameStarting;
    }

    public override void Enter(Dictionary metadata)
    {
        PlayersIds = (Array)metadata["players_ids"];

        SignalUtil.ConnectGuarded(StartGameTimer, Timer.SignalName.Timeout, new Callable(this, MethodName.OnTimeEnds));

        // Visibility belongs to this state on every peer. Body-overlap notifications are local and
        // are not a reliable way for a client to learn that the authoritative countdown started.
        StartGameUI.Show();
        StartGameTimer.Start();
    }

    public override void Process(double delta)
    {
        var text = $"Game starts in {Mathf.Ceil(StartGameTimer.TimeLeft)}";
        StartGameUI.SetText(text);
    }

    public override void Exit(Dictionary metadata)
    {
        SignalUtil.DisconnectGuarded(StartGameTimer, Timer.SignalName.Timeout,
            new Callable(this, MethodName.OnTimeEnds));
        StartGameTimer.Stop();

        // The server used to hide this in OnTimeEnds before broadcasting GAME_STARTED. Clients do
        // not execute that branch, leaving their last "Game starts in 1" frame visible forever.
        StartGameUI.Hide();
    }

    private void OnTimeEnds()
    {
        StartGameTimer.Stop();

        // Only the server decides who survived the countdown. In particular, a Player node may
        // have disappeared while its id was still present in the original metadata.
        if (!Multiplayer.IsServer())
            return;

        var table = Parent as Table;
        var game = table?.CurrentTableGame;
        var presentIds = table?.TableInfluence?.GetOverlappingBodies()
            .OfType<Player>()
            .Where(IsInstanceValid)
            .Select(player => (string)player.Name)
            .ToList();
		var validatedPlayers = RetainPresentPlayers(PlayersIds, presentIds);

        if (game == null || !game.CanStartWith(validatedPlayers.Count))
        {
            StartGameUI.Hide();
            StateMachine.ChangeState(StatesRef.GameWaitingStart,
                new Dictionary { ["is_restart"] = true });
            return;
        }

        StartGameUI.Hide();
        var metadata = new Dictionary { ["players_ids"] = validatedPlayers };
        StateMachine.ChangeState(StatesRef.GameStarted, metadata);
    }

	internal static Array RetainPresentPlayers(Array requestedPlayers, IEnumerable<string> presentPlayers)
	{
		var present = new HashSet<string>(presentPlayers ?? System.Array.Empty<string>());
		var retained = new Array();
		foreach (var playerVariant in requestedPlayers)
		{
			var playerId = (string)playerVariant;
			if (present.Remove(playerId))
				retained.Add(playerId);
		}
		return retained;
	}
}
