using System.Collections.Generic;
using System.Linq;
using Godot;
using Poker.Rules;

/// <summary>
/// What a player may do, when the street closes, and who speaks when.
///
/// The heads-up cases get their own checks because every blind and order rule reverses with two
/// players — the button posts the SMALL blind, acts first before the flop and last after it. A
/// three-handed table passing does not prove any of that.
/// </summary>
public partial class PokerBettingTest : Node
{
	private int _passed;
	private int _failed;

	public override void _Ready()
	{
		GD.Print("=== Teste de apostas do poker ===");

		TestLegalActions();
		TestRaiseLimits();
		TestRoundCompletion();
		TestSeatingThreeHanded();
		TestSeatingHeadsUp();
		TestActionOrderSkips();
		TestRaisePresets();

		GD.Print($"=== {_passed} passaram, {_failed} falharam ===");
		if (_failed > 0)
			GD.PushWarning($"{_failed} verificação(ões) de aposta do poker falharam.");

		GetTree().Quit(_failed > 0 ? 1 : 0);
	}

	// ---------------------------------------------------------------- what is on offer

	private void TestLegalActions()
	{
		var nothingToCall = Player("1", stack: 500, committed: 20);
		var options = PokerBetting.LegalActions(nothingToCall, currentBet: 20, minRaiseIncrement: 20);

		Check("sem nada a pagar, passar está disponível",
			options.Any(o => o.Kind == PokerActionKind.Check));
		Check("sem nada a pagar, desistir NÃO é oferecido",
			options.All(o => o.Kind != PokerActionKind.Fold));
		Check("sem nada a pagar, aumentar continua disponível",
			options.Any(o => o.Kind == PokerActionKind.Raise));

		var facingBet = Player("2", stack: 500, committed: 20);
		var facing = PokerBetting.LegalActions(facingBet, currentBet: 60, minRaiseIncrement: 40);

		Check("diante de uma aposta, desistir e pagar aparecem",
			facing.Any(o => o.Kind == PokerActionKind.Fold)
			&& facing.Any(o => o.Kind == PokerActionKind.Call));
		Check("diante de uma aposta, passar some",
			facing.All(o => o.Kind != PokerActionKind.Check));

		var call = facing.First(o => o.Kind == PokerActionKind.Call);
		Check($"pagar leva ao total da aposta corrente ({call.MinTotal})", call.MinTotal == 60);

		var folded = Player("3", stack: 500, committed: 0);
		folded.HasFolded = true;
		Check("quem desistiu não tem ação nenhuma",
			PokerBetting.LegalActions(folded, 60, 40).Count == 0);

		var allIn = Player("4", stack: 0, committed: 200);
		Check("quem está all-in não tem ação nenhuma",
			PokerBetting.LegalActions(allIn, 200, 40).Count == 0 && allIn.IsAllIn);
	}

	private void TestRaiseLimits()
	{
		var deep = Player("1", stack: 500, committed: 20);
		var raise = PokerBetting.LegalActions(deep, currentBet: 60, minRaiseIncrement: 40)
			.First(o => o.Kind == PokerActionKind.Raise);

		Check($"o aumento mínimo é a aposta mais o último aumento ({raise.MinTotal})",
			raise.MinTotal == 100);
		Check($"o aumento máximo é todo o stack ({raise.MaxTotal})", raise.MaxTotal == 520);

		// A stack too short for a full raise can still shove.
		var shortStack = Player("2", stack: 50, committed: 20);
		var shove = PokerBetting.LegalActions(shortStack, currentBet: 60, minRaiseIncrement: 40)
			.First(o => o.Kind == PokerActionKind.Raise);

		Check($"stack curto ainda pode ir de all-in abaixo do mínimo ({shove.MinTotal} a {shove.MaxTotal})",
			shove.MinTotal == 70 && shove.MaxTotal == 70);
		Check("um all-in curto não conta como aumento cheio",
			!PokerBetting.IsFullRaise(70, currentBet: 60, minRaiseIncrement: 40));
		Check("um aumento que alcança o incremento conta como cheio",
			PokerBetting.IsFullRaise(100, currentBet: 60, minRaiseIncrement: 40));

		// Calling with less than the bet is a call all-in, not a fold.
		var tiny = Player("3", stack: 15, committed: 0);
		Check($"pagar é limitado pelo stack ({PokerBetting.AmountToCall(tiny, 60)})",
			PokerBetting.AmountToCall(tiny, 60) == 15);

		Check("um valor fora da faixa é recusado",
			!PokerBetting.IsLegal(deep, PokerActionKind.Raise, 90, 60, 40, out var tooLow)
			&& tooLow == "amount_out_of_range");
		Check("acima do stack também é recusado",
			!PokerBetting.IsLegal(deep, PokerActionKind.Raise, 999, 60, 40, out _));
		Check("o mínimo exato é aceito",
			PokerBetting.IsLegal(deep, PokerActionKind.Raise, 100, 60, 40, out _));
		Check("passar diante de uma aposta é recusado",
			!PokerBetting.IsLegal(deep, PokerActionKind.Check, 20, 60, 40, out var noCheck)
			&& noCheck == "action_not_available");
	}

	// ---------------------------------------------------------------- closing the street

	private void TestRoundCompletion()
	{
		var players = new List<PlayerBetState>
		{
			Player("1", stack: 400, committed: 100, acted: true),
			Player("2", stack: 400, committed: 100, acted: true),
			Player("3", stack: 400, committed: 100, acted: true),
		};

		Check("todos pagaram e agiram: a rodada fecha",
			PokerBetting.RoundIsComplete(players, currentBet: 100));

		players[2].HasActedThisRound = false;
		Check("alguém ainda não agiu: a rodada não fecha",
			!PokerBetting.RoundIsComplete(players, currentBet: 100));

		players[2].HasActedThisRound = true;
		players[2].CommittedThisRound = 40;
		Check("alguém não igualou a aposta: a rodada não fecha",
			!PokerBetting.RoundIsComplete(players, currentBet: 100));

		// All-in players have no decision left and must not hold the street open.
		players[2].CommittedThisRound = 40;
		players[2].Stack = 0;
		Check("um all-in por menos não segura a rodada",
			PokerBetting.RoundIsComplete(players, currentBet: 100));

		// Everyone but one folded: the hand is over whatever the bets look like.
		var folded = new List<PlayerBetState>
		{
			Player("1", stack: 400, committed: 100, acted: true),
			Player("2", stack: 400, committed: 0),
			Player("3", stack: 400, committed: 0),
		};
		folded[1].HasFolded = true;
		folded[2].HasFolded = true;

		Check("sobrando um jogador, a rodada fecha na hora",
			PokerBetting.RoundIsComplete(folded, currentBet: 100));
		Check($"a contagem de vivos ignora quem desistiu ({PokerBetting.CountLive(folded)})",
			PokerBetting.CountLive(folded) == 1);
		Check($"a contagem de quem pode agir ignora all-in e desistentes "
			  + $"({PokerBetting.CountAbleToAct(players)})",
			PokerBetting.CountAbleToAct(players) == 2);
	}

	// ---------------------------------------------------------------- who speaks when

	private void TestSeatingThreeHanded()
	{
		const int button = 0;
		const int seats = 3;

		Check($"o small blind fica à esquerda do botão ({PokerSeating.SmallBlindSeat(button, seats)})",
			PokerSeating.SmallBlindSeat(button, seats) == 1);
		Check($"o big blind vem depois ({PokerSeating.BigBlindSeat(button, seats)})",
			PokerSeating.BigBlindSeat(button, seats) == 2);
		Check($"antes do flop fala primeiro quem está depois do big blind "
			  + $"({PokerSeating.FirstToActPreflop(button, seats)})",
			PokerSeating.FirstToActPreflop(button, seats) == 0);
		Check($"depois do flop fala primeiro o small blind "
			  + $"({PokerSeating.FirstToActPostflop(button, seats)})",
			PokerSeating.FirstToActPostflop(button, seats) == 1);

		Check("o botão gira", PokerSeating.Next(2, seats) == 0);

		var order = PokerSeating.OddChipOrder(new List<string> { "A", "B", "C" }, buttonSeat: 0);
		Check($"a ordem da ficha ímpar começa à esquerda do botão ({string.Join(",", order)})",
			order.SequenceEqual(new[] { "B", "C", "A" }));
	}

	private void TestSeatingHeadsUp()
	{
		const int button = 0;
		const int seats = 2;

		Check($"mão a mão, o botão PAGA o small blind ({PokerSeating.SmallBlindSeat(button, seats)})",
			PokerSeating.SmallBlindSeat(button, seats) == 0);
		Check($"e o outro paga o big blind ({PokerSeating.BigBlindSeat(button, seats)})",
			PokerSeating.BigBlindSeat(button, seats) == 1);
		Check($"mão a mão, o botão fala PRIMEIRO antes do flop "
			  + $"({PokerSeating.FirstToActPreflop(button, seats)})",
			PokerSeating.FirstToActPreflop(button, seats) == 0);
		Check($"e fala POR ÚLTIMO depois do flop "
			  + $"({PokerSeating.FirstToActPostflop(button, seats)})",
			PokerSeating.FirstToActPostflop(button, seats) == 1);

		// The same rules from the other button, so nothing is accidentally hard-coded to seat zero.
		Check("as regras mão a mão valem com o botão no outro assento",
			PokerSeating.SmallBlindSeat(1, seats) == 1
			&& PokerSeating.BigBlindSeat(1, seats) == 0
			&& PokerSeating.FirstToActPreflop(1, seats) == 1
			&& PokerSeating.FirstToActPostflop(1, seats) == 0);
	}

	private void TestActionOrderSkips()
	{
		var players = new List<PlayerBetState>
		{
			Player("1", stack: 400, committed: 0),
			Player("2", stack: 400, committed: 0),
			Player("3", stack: 400, committed: 0),
			Player("4", stack: 400, committed: 0),
		};

		players[1].HasFolded = true;
		players[2].Stack = 0;

		Check($"a vez pula quem desistiu e quem está all-in "
			  + $"({PokerSeating.NextAbleToAct(players, 0)})",
			PokerSeating.NextAbleToAct(players, 0) == 3);
		Check("a vez dá a volta na mesa", PokerSeating.NextAbleToAct(players, 3) == 0);
		Check("começar uma rua inclui o próprio assento se ele puder agir",
			PokerSeating.FirstAbleToActFrom(players, 0) == 0);
		Check("começar numa cadeira sem ação anda até a próxima que tenha",
			PokerSeating.FirstAbleToActFrom(players, 1) == 3);

		foreach (var player in players)
			player.HasFolded = true;

		Check("sem ninguém para agir, não há próxima vez",
			PokerSeating.NextAbleToAct(players, 0) == -1
			&& PokerSeating.FirstAbleToActFrom(players, 0) == -1);
	}

	private void TestRaisePresets()
	{
		var player = Player("1", stack: 1000, committed: 0);
		var presets = PokerBetting.RaisePresets(player, currentBet: 40, minRaiseIncrement: 40, potSize: 100);

		Check($"os atalhos de aumento existem ({string.Join(", ", presets)})", presets.Count > 0);
		Check("os atalhos estão em ordem crescente",
			presets.Zip(presets.Skip(1), (a, b) => a < b).All(ok => ok));
		Check("nenhum atalho se repete", presets.Distinct().Count() == presets.Count);

		var min = PokerBetting.MinRaiseTotal(player, 40, 40);
		var max = PokerBetting.MaxTotal(player);
		Check($"todo atalho é legal ({min} a {max})",
			presets.All(total => PokerBetting.IsLegal(player, PokerActionKind.Raise, total, 40, 40, out _)));
		Check("o primeiro atalho é o mínimo e o último é o all-in",
			presets[0] == min && presets[^1] == max);

		// A stack barely above the bet has only one stop, and it is the shove.
		var shortStack = Player("2", stack: 45, committed: 0);
		var few = PokerBetting.RaisePresets(shortStack, currentBet: 40, minRaiseIncrement: 40, potSize: 100);
		Check($"um stack curto oferece só o all-in ({string.Join(", ", few)})",
			few.Count == 1 && few[0] == 45);

		var covered = Player("3", stack: 10, committed: 0);
		Check("quem não consegue passar da aposta não recebe atalho nenhum",
			PokerBetting.RaisePresets(covered, currentBet: 40, minRaiseIncrement: 40, potSize: 100).Count == 0);
	}

	// ---------------------------------------------------------------- helpers

	private static PlayerBetState Player(string id, int stack, int committed, bool acted = false) =>
		new()
		{
			PlayerId = id,
			Stack = stack,
			CommittedThisRound = committed,
			CommittedThisHand = committed,
			HasActedThisRound = acted,
		};

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
