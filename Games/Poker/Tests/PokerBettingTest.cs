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
        TestShortAllInReopening();
        TestShortOpeningAllIn();
        TestShortBigBlindBringIn();
        TestRoundCompletion();
        TestSeatingThreeHanded();
        TestSeatingHeadsUp();
        TestThreeToTwoButtonTransition();
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

        Check("passar com fichas provisórias devolve as fichas sem bloquear o turno",
            PokerWagerInteraction.TryPrepareCheck(options, 5, out var returnSelected)
            && returnSelected);
        Check("quando ainda há call, passar mantém as fichas para completar o valor",
            !PokerWagerInteraction.TryPrepareCheck(facing, 5, out var keepSelected)
            && !keepSelected);

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

    /// <summary>
    /// A short all-in changes the amount to call but does not automatically give a previous actor a
    /// second raise. Multiple short all-ins can add up to a full raise and reopen it. This is the
    /// distinction that a single "has acted" flag cannot express without the remembered bet level.
    /// </summary>
    private void TestShortAllInReopening()
    {
        var previousActor = Player(
            "1", stack: 400, committed: 100, acted: true, betLevelWhenLastActed: 100);
        var afterOneShortAllIn = PokerBetting.LegalActions(
            previousActor, currentBet: 150, minRaiseIncrement: 100);

        Check("um all-in curto ainda permite pagar ou desistir",
            afterOneShortAllIn.Any(option => option.Kind == PokerActionKind.Call)
            && afterOneShortAllIn.Any(option => option.Kind == PokerActionKind.Fold));
        Check("um all-in curto isolado nao reabre o raise para quem ja agiu",
            afterOneShortAllIn.All(option => option.Kind != PokerActionKind.Raise)
            && !PokerBetting.IsLegal(
                previousActor,
                PokerActionKind.Raise,
                total: 250,
                currentBet: 150,
                minRaiseIncrement: 100,
                out var closedReason)
            && closedReason == "action_not_available");
        Check("atalhos de raise tambem somem quando a acao nao foi reaberta",
            PokerBetting.RaisePresets(
                previousActor, 150, 100, potSize: 300).Count == 0);

        var notYetActed = Player("2", stack: 400, committed: 100);
        Check("quem ainda nao agiu pode aumentar depois do mesmo all-in curto",
            PokerBetting.LegalActions(notYetActed, 150, 100)
                .Any(option => option.Kind == PokerActionKind.Raise));

        var afterCumulativeShortAllIns = PokerBetting.LegalActions(
            previousActor, currentBet: 200, minRaiseIncrement: 100);
        Check("all-ins curtos cumulativos que somam um raise cheio reabrem a acao",
            afterCumulativeShortAllIns.Any(option =>
                option.Kind == PokerActionKind.Raise && option.MinTotal == 300));

        previousActor.BetLevelWhenLastActed = 150;
        Check("pagar um all-in curto inicia uma nova base para a proxima reabertura",
            PokerBetting.LegalActions(previousActor, 200, 100)
                .All(option => option.Kind != PokerActionKind.Raise));

        var lastPlayerWithChips = Player("3", stack: 400, committed: 100);
        var headsUpAgainstAllIn = PokerBetting.LegalActions(
            lastPlayerWithChips,
            currentBet: 150,
            minRaiseIncrement: 100,
            anotherPlayerCanAct: false);
        Check("contra oponentes todos all-in, a ultima decisao e apenas pagar ou desistir",
            headsUpAgainstAllIn.Any(option => option.Kind == PokerActionKind.Call)
            && headsUpAgainstAllIn.Any(option => option.Kind == PokerActionKind.Fold)
            && headsUpAgainstAllIn.All(option => option.Kind != PokerActionKind.Raise));
    }

    private void TestShortOpeningAllIn()
    {
        var unactedPlayer = Player("1", stack: 100, committed: 0);
        var optionsAfterShortOpening = PokerBetting.LegalActions(
            unactedPlayer, currentBet: 3, minRaiseIncrement: 10);
        var fullRaise = optionsAfterShortOpening.FirstOrDefault(option =>
            option.Kind == PokerActionKind.Raise);

        Check("depois de uma abertura all-in de 3, o raise NL minimo soma 10 e vai a 13",
            fullRaise.Kind == PokerActionKind.Raise
            && fullRaise.MinTotal == 13
            && fullRaise.MaxTotal == 100
            && PokerBetting.IsFullRaise(13, 3, 10));

        var previousCaller = Player(
            "2", stack: 97, committed: 3, acted: true, betLevelWhenLastActed: 3);
        Check("um segundo all-in curto ate 10 nao reabre para quem ja pagou 3",
            PokerBetting.LegalActions(previousCaller, currentBet: 10, minRaiseIncrement: 10)
                .All(option => option.Kind != PokerActionKind.Raise));

        var previousChecker = Player(
            "3", stack: 100, committed: 0, acted: true, betLevelWhenLastActed: 0);
        Check("um check anterior nao e reaberto por uma aposta all-in curta de 3",
            PokerBetting.LegalActions(previousChecker, currentBet: 3, minRaiseIncrement: 10)
                .All(option => option.Kind != PokerActionKind.Raise));
        Check("o raise cheio posterior a 13 reabre a acao para quem havia dado check",
            PokerBetting.LegalActions(previousChecker, currentBet: 13, minRaiseIncrement: 10)
                .Any(option => option.Kind == PokerActionKind.Raise
                               && option.MinTotal == 23));
    }

    private void TestShortBigBlindBringIn()
    {
        var headsUp = new List<PlayerBetState>
        {
            Player("button", stack: 99, committed: 1),
            Player("big-blind", stack: 0, committed: 3),
        };
        Check("contra um big blind all-in de 3, o unico oponente paga somente ate 3",
            PokerBetting.BetToMatchAfterBlinds(headsUp, configuredBigBlind: 10) == 3);

        var multiway = new List<PlayerBetState>(headsUp)
        {
            Player("third", stack: 100, committed: 0),
        };
        Check("com dois jogadores ainda financiados, o bring-in continua no big blind de 10",
            PokerBetting.BetToMatchAfterBlinds(multiway, configuredBigBlind: 10) == 10);
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

        var dealOrder = PokerSeating.DealOrder(
            new List<string> { "A", "B", "C" }, buttonSeat: 0);
        Check("a primeira carta sai à esquerda do botão em uma mesa de três",
            dealOrder.SequenceEqual(new[] { "B", "C", "A" }));
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

        var dealOrder = PokerSeating.DealOrder(new List<string> { "A", "B" }, button);
        Check("mão a mão, o big blind recebe primeiro e o botão/small blind recebe por último",
            dealOrder.SequenceEqual(new[] { "B", "A" }));
    }

    private void TestThreeToTwoButtonTransition()
    {
        var expectedButtons = new[] { "C", "C", "B" };
        var expectedBigBlinds = new[] { "B", "A", "A" };

        for (var bustedSeat = 0; bustedSeat < 3; bustedSeat++)
        {
            var oldSeats = new List<PlayerBetState>
            {
                Player("A", stack: 100, committed: 0),
                Player("B", stack: 100, committed: 0),
                Player("C", stack: 100, committed: 0),
            };
            oldSeats[bustedSeat].Stack = 0;

            var nextButtonInOldOrder = PokerSeating.NextButtonSeat(oldSeats, currentButtonSeat: 0);
            var nextButtonId = oldSeats[nextButtonInOldOrder].PlayerId;
            var survivors = oldSeats.Where(player => player.Stack > 0).ToList();
            var nextButton = survivors.FindIndex(player => player.PlayerId == nextButtonId);
            var nextBigBlind = survivors[
                PokerSeating.BigBlindSeat(nextButton, survivors.Count)].PlayerId;

            Check($"transição 3→2 com {oldSeats[bustedSeat].PlayerId} eliminado não repete o BB",
                nextButtonId == expectedButtons[bustedSeat]
                && nextBigBlind == expectedBigBlinds[bustedSeat]
                && (nextBigBlind != "C" || bustedSeat == 2));
        }

        var fourSeatIds = new[] { "A", "B", "C", "D" };
        for (var firstSurvivor = 0; firstSurvivor < fourSeatIds.Length - 1; firstSurvivor++)
        {
            for (var secondSurvivor = firstSurvivor + 1;
                 secondSurvivor < fourSeatIds.Length;
                 secondSurvivor++)
            {
                var fourSeats = fourSeatIds
                    .Select((id, seat) => Player(
                        id,
                        stack: seat == firstSurvivor || seat == secondSurvivor ? 100 : 0,
                        committed: 0))
                    .ToList();

                const int oldBigBlind = 2; // Button A: small blind B, big blind C.
                var expectedBigBlindSeat = Enumerable.Range(1, fourSeats.Count)
                    .Select(step => (oldBigBlind + step) % fourSeats.Count)
                    .First(seat => fourSeats[seat].Stack > 0);
                var expectedBigBlindId = fourSeats[expectedBigBlindSeat].PlayerId;

                var nextButtonInOldOrder = PokerSeating.NextButtonSeat(
                    fourSeats, currentButtonSeat: 0);
                var nextButtonId = fourSeats[nextButtonInOldOrder].PlayerId;
                var survivors = fourSeats.Where(player => player.Stack > 0).ToList();
                var compactButton = survivors.FindIndex(player => player.PlayerId == nextButtonId);
                var actualBigBlindId = survivors[
                    PokerSeating.BigBlindSeat(compactButton, survivors.Count)].PlayerId;

                Check($"transição 4→2 com {string.Join('/', survivors.Select(player => player.PlayerId))} preserva o próximo BB",
                    actualBigBlindId == expectedBigBlindId
                    && nextButtonId != expectedBigBlindId);
            }
        }
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

    private static PlayerBetState Player(
        string id,
        int stack,
        int committed,
        bool acted = false,
        int betLevelWhenLastActed = 0) =>
        new()
        {
            PlayerId = id,
            Stack = stack,
            CommittedThisRound = committed,
            CommittedThisHand = committed,
            HasActedThisRound = acted,
            BetLevelWhenLastActed = betLevelWhenLastActed,
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
