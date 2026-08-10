using System.Collections.Generic;
using System.Linq;
using Godot;
using Poker.Rules;

/// <summary>
/// Side pots and how they pay out.
///
/// This is the part of poker that quietly steals chips when it is wrong, because nothing looks
/// broken — the hand ends, somebody is paid, and the stacks are simply off. So every case here
/// asserts the two conservation laws as well as the outcome: the pots sum to exactly what was put
/// in, and the payouts sum to exactly the pots.
/// </summary>
public partial class PokerPotTest : Node
{
    private int _passed;
    private int _failed;

    public override void _Ready()
    {
        GD.Print("=== Teste de potes do poker ===");

        TestSinglePot();
        TestSplitPot();
        TestOddChip();
        TestSidePot();
        TestChainedAllIns();
        TestFoldedContributions();

        GD.Print($"=== {_passed} passaram, {_failed} falharam ===");
        if (_failed > 0)
            GD.PushWarning($"{_failed} verificação(ões) de pote do poker falharam.");

        GetTree().Quit(_failed > 0 ? 1 : 0);
    }

    private void TestSinglePot()
    {
        var contributions = new Dictionary<string, int> { ["1"] = 100, ["2"] = 100, ["3"] = 100 };
        var pots = PokerPot.Build(contributions, new[] { "1", "2", "3" });

        Check($"três apostas iguais formam um pote só ({pots.Count})", pots.Count == 1);
        Check($"o pote vale a soma das apostas ({PokerPot.Total(pots)})", PokerPot.Total(pots) == 300);
        CheckConserved("pote único", contributions, pots);

        var ranks = new Dictionary<string, PokerHandRank>
        {
            ["1"] = Rank("Ah Ad Ac As Kh"),
            ["2"] = Rank("Kh Kd Kc Ks Qh"),
            ["3"] = Rank("2h 3d 5c 7s 9h"),
        };

        var awards = PokerPot.Award(pots, ranks, Order("1", "2", "3"));
        Check($"a melhor mão leva o pote inteiro ({awards.GetValueOrDefault("1")})",
            awards.GetValueOrDefault("1") == 300 && awards.Count == 1);
        CheckPaidOut("pote único", pots, awards);
    }

    private void TestSplitPot()
    {
        var contributions = new Dictionary<string, int> { ["1"] = 100, ["2"] = 100 };
        var pots = PokerPot.Build(contributions, new[] { "1", "2" });

        // The same hand in different suits: a real tie, not an ordering accident.
        var ranks = new Dictionary<string, PokerHandRank>
        {
            ["1"] = Rank("Ah Kh Qh Jh 9h"),
            ["2"] = Rank("As Ks Qs Js 9s"),
        };

        var awards = PokerPot.Award(pots, ranks, Order("1", "2"));

        Check($"empate exato divide o pote ao meio ({awards.GetValueOrDefault("1")} / {awards.GetValueOrDefault("2")})",
            awards.GetValueOrDefault("1") == 100 && awards.GetValueOrDefault("2") == 100);
        CheckPaidOut("pote dividido", pots, awards);
    }

    private void TestOddChip()
    {
        var tie = new Dictionary<string, PokerHandRank>
        {
            ["1"] = Rank("Ah Kh Qh Jh 9h"),
            ["2"] = Rank("As Ks Qs Js 9s"),
        };

        // A pot that will not divide: three equal contributions, two players splitting it.
        var contributions = new Dictionary<string, int> { ["1"] = 25, ["2"] = 25, ["3"] = 25 };
        var pots = PokerPot.Build(contributions, new[] { "1", "2" });

        Check($"o pote de 75 não divide em dois ({PokerPot.Total(pots)})", PokerPot.Total(pots) == 75);

        // The order is what decides the odd chip, so running it both ways must move it.
        var awardsA = PokerPot.Award(pots, tie, Order("1", "2"));
        var awardsB = PokerPot.Award(pots, tie, Order("2", "1"));

        Check($"a ficha ímpar vai para o primeiro da ordem ({awardsA.GetValueOrDefault("1")} / {awardsA.GetValueOrDefault("2")})",
            awardsA.GetValueOrDefault("1") == 38 && awardsA.GetValueOrDefault("2") == 37);
        Check($"invertendo a ordem, a ficha ímpar troca de dono ({awardsB.GetValueOrDefault("1")} / {awardsB.GetValueOrDefault("2")})",
            awardsB.GetValueOrDefault("2") == 38 && awardsB.GetValueOrDefault("1") == 37);

        var stable = Enumerable.Range(0, 20)
            .All(_ => PokerPot.Award(pots, tie, Order("1", "2")).GetValueOrDefault("1") == 38);
        Check("a ficha ímpar cai sempre no mesmo lugar", stable);

        CheckPaidOut("ficha ímpar", pots, awardsA);

        // Not an odd chip at all: a bet nobody covered comes BACK to whoever made it, even on a
        // tie. Betting one more than your opponent can afford must not buy you half of it.
        var uncalled = new Dictionary<string, int> { ["1"] = 51, ["2"] = 50 };
        var uncalledPots = PokerPot.Build(uncalled, new[] { "1", "2" });
        var uncalledAwards = PokerPot.Award(uncalledPots, tie, Order("2", "1"));

        Check($"a ficha não coberta volta para quem apostou "
              + $"({uncalledAwards.GetValueOrDefault("1")} / {uncalledAwards.GetValueOrDefault("2")})",
            uncalledAwards.GetValueOrDefault("1") == 51 && uncalledAwards.GetValueOrDefault("2") == 50);
        CheckConserved("aposta não coberta", uncalled, uncalledPots);
        CheckPaidOut("aposta não coberta", uncalledPots, uncalledAwards);
    }

    private void TestSidePot()
    {
        // One player all-in for less; the other two keep betting past them.
        var contributions = new Dictionary<string, int> { ["1"] = 50, ["2"] = 200, ["3"] = 200 };
        var pots = PokerPot.Build(contributions, new[] { "1", "2", "3" });

        Check($"um all-in curto abre um pote lateral ({pots.Count} potes)", pots.Count == 2);
        Check($"o pote principal é o all-in vezes três ({pots[0].Amount})", pots[0].Amount == 150);
        Check($"o pote lateral é o excedente dos outros dois ({pots[1].Amount})", pots[1].Amount == 300);
        Check("todos disputam o principal", pots[0].EligiblePlayers.Count == 3);
        Check("só quem cobriu disputa o lateral",
            pots[1].EligiblePlayers.Count == 2 && !pots[1].EligiblePlayers.Contains("1"));
        CheckConserved("pote lateral", contributions, pots);

        // The short stack has the best hand: they win the main pot and nothing more.
        var ranks = new Dictionary<string, PokerHandRank>
        {
            ["1"] = Rank("Ah Ad Ac As Kh"),
            ["2"] = Rank("Kh Kd Kc Ks Qh"),
            ["3"] = Rank("2h 3d 5c 7s 9h"),
        };

        var awards = PokerPot.Award(pots, ranks, Order("1", "2", "3"));

        Check($"o all-in curto com a melhor mão leva só o principal ({awards.GetValueOrDefault("1")})",
            awards.GetValueOrDefault("1") == 150);
        Check($"o lateral fica com a melhor mão entre quem o disputava ({awards.GetValueOrDefault("2")})",
            awards.GetValueOrDefault("2") == 300);
        Check("quem tinha a pior mão não recebe nada", awards.GetValueOrDefault("3") == 0);
        CheckPaidOut("pote lateral", pots, awards);
    }

    private void TestChainedAllIns()
    {
        // Three different all-in levels: 50, 120, 200.
        var contributions = new Dictionary<string, int> { ["1"] = 50, ["2"] = 120, ["3"] = 200 };
        var pots = PokerPot.Build(contributions, new[] { "1", "2", "3" });

        Check($"três níveis de all-in dão três potes ({pots.Count})", pots.Count == 3);
        Check($"os potes valem 150 / 140 / 80 ({string.Join(" / ", pots.Select(p => p.Amount))})",
            pots[0].Amount == 150 && pots[1].Amount == 140 && pots[2].Amount == 80);
        Check("o pote mais alto só tem um dono possível",
            pots[2].EligiblePlayers.Count == 1 && pots[2].EligiblePlayers[0] == "3");
        CheckConserved("all-ins encadeados", contributions, pots);

        // The shortest stack wins outright, so the pots above them go to the next best.
        var ranks = new Dictionary<string, PokerHandRank>
        {
            ["1"] = Rank("Ah Ad Ac As Kh"),
            ["2"] = Rank("2h 3d 5c 7s 9h"),
            ["3"] = Rank("Kh Kd Kc Ks Qh"),
        };

        var awards = PokerPot.Award(pots, ranks, Order("1", "2", "3"));
        Check($"cada pote vai para a melhor mão que podia disputá-lo "
              + $"({awards.GetValueOrDefault("1")} / {awards.GetValueOrDefault("2")} / {awards.GetValueOrDefault("3")})",
            awards.GetValueOrDefault("1") == 150
            && awards.GetValueOrDefault("2") == 0
            && awards.GetValueOrDefault("3") == 220);
        CheckPaidOut("all-ins encadeados", pots, awards);
    }

    private void TestFoldedContributions()
    {
        // A folded player's chips stay in the pot, but they cannot win any of it.
        var contributions = new Dictionary<string, int> { ["1"] = 100, ["2"] = 100, ["3"] = 40 };
        var pots = PokerPot.Build(contributions, new[] { "1", "2" });

        Check($"as fichas de quem desistiu continuam no pote ({PokerPot.Total(pots)})",
            PokerPot.Total(pots) == 240);
        Check("quem desistiu não aparece como disputante",
            pots.All(pot => !pot.EligiblePlayers.Contains("3")));
        CheckConserved("fichas de quem desistiu", contributions, pots);

        // And the awkward case: the folded player put in MORE than anyone still in the hand.
        var overfolded = new Dictionary<string, int> { ["1"] = 50, ["2"] = 50, ["3"] = 200 };
        var overpots = PokerPot.Build(overfolded, new[] { "1", "2" });

        Check($"dinheiro morto acima do all-in de todos vira um pote só ({overpots.Count})",
            overpots.Count == 1);
        Check($"e ele vale tudo que entrou ({PokerPot.Total(overpots)})",
            PokerPot.Total(overpots) == 300);
        CheckConserved("dinheiro morto", overfolded, overpots);

        var ranks = new Dictionary<string, PokerHandRank>
        {
            ["1"] = Rank("Ah Ad Ac As Kh"),
            ["2"] = Rank("2h 3d 5c 7s 9h"),
        };

        var awards = PokerPot.Award(overpots, ranks, Order("1", "2"));
        Check($"o dinheiro morto vai para quem ganhou a mão ({awards.GetValueOrDefault("1")})",
            awards.GetValueOrDefault("1") == 300);
        CheckPaidOut("dinheiro morto", overpots, awards);
    }

    // ---------------------------------------------------------------- helpers

    private void CheckConserved(
        string label, IReadOnlyDictionary<string, int> contributions, IReadOnlyList<Pot> pots)
    {
        var contributed = contributions.Values.Sum();
        var pooled = PokerPot.Total(pots);

        Check($"{label}: os potes somam o que foi apostado ({pooled} = {contributed})",
            pooled == contributed);
    }

    private void CheckPaidOut(string label, IReadOnlyList<Pot> pots, IReadOnlyDictionary<string, int> awards)
    {
        var paid = awards.Values.Sum();
        var pooled = PokerPot.Total(pots);

        Check($"{label}: os pagamentos somam os potes ({paid} = {pooled})", paid == pooled);
    }

    private static PokerHandRank Rank(string spec)
    {
        const string ranks = "23456789TJQKA";
        const string suits = "cdhs";

        var cards = spec.Split(' ', System.StringSplitOptions.RemoveEmptyEntries)
            .Select(token => CardId.From(ranks.IndexOf(token[0]), suits.IndexOf(token[1])))
            .ToArray();

        return PokerHandEvaluator.Evaluate(cards);
    }

    private static List<string> Order(params string[] players) => players.ToList();

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
