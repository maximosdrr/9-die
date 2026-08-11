using System.Linq;
using System.Text;
using Godot;

[GlobalClass]
public partial class RankingBoard : Node3D
{
    [Export] public Label3D RankingLabel;

    public override void _Ready()
    {
        SignalUtil.ConnectGuarded(MatchRanking.Instance, MatchRanking.SignalName.RankingUpdated, new Callable(this, MethodName.Refresh));
        Refresh();
    }

    private void Refresh()
    {
        var entries = MatchRanking.Instance.WinsByPlayer
            .OrderByDescending(kvp => kvp.Value)
            .ToList();

        if (entries.Count == 0)
        {
            RankingLabel.Text = "Ninguém venceu ainda...";
            return;
        }

        var lines = new StringBuilder();
        for (var i = 0; i < entries.Count; i++)
        {
            var playerId = entries[i].Key;
            var wins = entries[i].Value;
            var label = GetPlayerLabel(playerId);
            var winsWord = wins == 1 ? "vitória" : "vitórias";
            lines.AppendLine($"{i + 1}º {label} — {wins} {winsWord}");
        }

        RankingLabel.Text = lines.ToString().TrimEnd();
    }

    private string GetPlayerLabel(string playerId)
    {
        var player = PlayerRegistry.Instance.GetPlayerById(playerId);
        return player != null && !string.IsNullOrWhiteSpace(player.Nickname) ? player.Nickname : $"Jogador {playerId}";
    }
}
