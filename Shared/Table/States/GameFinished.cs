using Godot;
using Godot.Collections;

[GlobalClass]
public partial class GameFinished : State
{
    [Export] public PoolStartGameUI StartGameUI;
    [Export] public Timer ReturnTimer;

    public GameFinished()
    {
        Type = StatesRef.GameFinished;
    }

    public override void Enter(Dictionary metadata)
    {
        StartGameUI.SetText(BuildResultText(metadata));
        StartGameUI.Show();

        SignalUtil.ConnectGuarded(ReturnTimer, Timer.SignalName.Timeout, new Callable(this, MethodName.OnReturnTimeout));
        ReturnTimer.Start();
    }

    public override void Exit(Dictionary metadata)
    {
        SignalUtil.DisconnectGuarded(ReturnTimer, Timer.SignalName.Timeout, new Callable(this, MethodName.OnReturnTimeout));
        ReturnTimer.Stop();
        StartGameUI.Hide();
    }

    private void OnReturnTimeout()
    {
        StateMachine.ChangeState(StatesRef.GameWaitingStart, new Dictionary { ["is_restart"] = true });
    }

    private static string BuildResultText(Dictionary metadata)
    {
        var winnerId = metadata.TryGetValue("winner", out var winnerVariant) ? (string)winnerVariant : null;
        var reason = metadata.TryGetValue("reason", out var reasonVariant) ? (string)reasonVariant : "win";

        if (winnerId == null)
            return "Partida encerrada";

        return reason switch
        {
            "fatal_foul" => $"Fim de jogo!\nJogador {winnerId} venceu (falta fatal do oponente)",
            "opponent_disconnected" => $"Fim de jogo!\nJogador {winnerId} venceu (oponente desconectou)",
            "opponent_left" => $"Fim de jogo!\nJogador {winnerId} venceu (oponente desistiu)",
            _ => $"Fim de jogo!\nJogador {winnerId} venceu!",
        };
    }
}
