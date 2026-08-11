using System.Collections.Generic;
using System.Linq;
using Godot;
using Poker.Rules;

/// <summary>
/// The rules layer, with no scene, no node and no network: card identity, the hand evaluator, the
/// deal and how an amount becomes physical chips.
///
/// The evaluator gets the most attention because every other part of the game trusts it. Two of its
/// answers are load-bearing in a way that is easy to miss: the WHEEL has to rank below a six-high
/// straight, and two genuinely equal hands have to compare EXACTLY equal — a "close enough" ordering
/// would silently hand one player a pot that should have been split.
/// </summary>
public partial class PokerRulesTest : Node
{
    private int _passed;
    private int _failed;

    public override void _Ready()
    {
        GD.Print("=== Teste de regras do poker ===");

        TestCardIdentity();
        TestCategories();
        TestWheel();
        TestKickersAndTies();
        TestBestFiveOfSeven();
        TestDealing();
        TestChipStacks();
        TestWagerInteraction();

        GD.Print($"=== {_passed} passaram, {_failed} falharam ===");
        if (_failed > 0)
            GD.PushWarning($"{_failed} verificação(ões) de regra do poker falharam.");

        GetTree().Quit(_failed > 0 ? 1 : 0);
    }

    // ---------------------------------------------------------------- card identity

    private void TestCardIdentity()
    {
        var seen = new HashSet<int>();
        foreach (var card in CardId.FullDeck())
            seen.Add(card);

        Check($"o baralho tem 52 cartas distintas (achou {seen.Count})", seen.Count == CardId.Count);

        var roundTrips = true;
        for (var suit = 0; suit < CardId.Suits; suit++)
        {
            for (var rank = 0; rank < CardId.Ranks; rank++)
            {
                var id = CardId.From(rank, suit);
                if (CardId.RankOf(id) != rank || CardId.SuitOf(id) != suit)
                    roundTrips = false;
            }
        }

        Check("naipe e valor sobrevivem à ida e volta pelo id", roundTrips);

        Check("o ás é o valor mais alto", CardId.Ace > CardId.King && CardId.King > CardId.Queen);
        Check("o dois é o mais baixo", CardId.Two == 0);

        Check($"id inválido é rejeitado ({CardId.Label(52)})",
            !CardId.IsValid(-1) && !CardId.IsValid(52) && CardId.IsValid(0) && CardId.IsValid(51));

        Check($"rótulo legível ({CardId.Label(CardId.From(CardId.Ace, CardId.Spades))})",
            CardId.Label(CardId.From(CardId.Ace, CardId.Spades)) == "A♠");
    }

    // ---------------------------------------------------------------- category ordering

    private void TestCategories()
    {
        // One representative of each category, weakest to strongest.
        var ladder = new (string Name, string Cards, HandCategory Category)[]
        {
            ("carta alta", "Ah Kd 9c 7s 3h", HandCategory.HighCard),
            ("um par", "Ah Ad 9c 7s 3h", HandCategory.Pair),
            ("dois pares", "Ah Ad 9c 9s 3h", HandCategory.TwoPair),
            ("trinca", "Ah Ad Ac 9s 3h", HandCategory.ThreeOfAKind),
            ("sequência", "9h 8d 7c 6s 5h", HandCategory.Straight),
            ("flush", "Ah Jh 9h 7h 3h", HandCategory.Flush),
            ("full house", "Ah Ad Ac 9s 9h", HandCategory.FullHouse),
            ("quadra", "Ah Ad Ac As 9h", HandCategory.FourOfAKind),
            ("straight flush", "9h 8h 7h 6h 5h", HandCategory.StraightFlush),
        };

        foreach (var entry in ladder)
        {
            var rank = PokerHandEvaluator.Evaluate(Cards(entry.Cards));
            Check($"{entry.Name} é classificada como {entry.Category} (deu {rank.Category})",
                rank.Category == entry.Category);
        }

        var ordered = true;
        for (var i = 1; i < ladder.Length; i++)
        {
            var weaker = PokerHandEvaluator.Evaluate(Cards(ladder[i - 1].Cards));
            var stronger = PokerHandEvaluator.Evaluate(Cards(ladder[i].Cards));
            if (!(stronger > weaker))
                ordered = false;
        }

        Check("cada categoria bate a anterior, da carta alta ao straight flush", ordered);

        // A full house built from TWO trips, which the naive "find a pair" search misses entirely.
        var twoTrips = PokerHandEvaluator.Evaluate(Cards("8h 8d 8c 5s 5h 5d Kc"));
        Check($"duas trincas viram full house de oitos ({twoTrips.Category})",
            twoTrips.Category == HandCategory.FullHouse);
        Check("o full house de duas trincas usa a trinca mais alta em cima",
            twoTrips > PokerHandEvaluator.Evaluate(Cards("7h 7d 7c 5s 5h 5d Kc")));

        // Quads beat a full house made on the same board.
        Check("quadra bate full house",
            PokerHandEvaluator.Evaluate(Cards("9h 9d 9c 9s Kh"))
            > PokerHandEvaluator.Evaluate(Cards("Ah Ad Ac Ks Kh")));

        // Flush beats a straight even when the straight is higher in rank.
        Check("flush bate sequência",
            PokerHandEvaluator.Evaluate(Cards("2h 5h 7h 9h Jh"))
            > PokerHandEvaluator.Evaluate(Cards("Ah Kd Qc Js Th")));
    }

    // ---------------------------------------------------------------- the wheel

    private void TestWheel()
    {
        var wheel = PokerHandEvaluator.Evaluate(Cards("Ah 2d 3c 4s 5h"));
        Check($"A-2-3-4-5 é uma sequência ({wheel.Category})", wheel.Category == HandCategory.Straight);

        var sixHigh = PokerHandEvaluator.Evaluate(Cards("2h 3d 4c 5s 6h"));
        Check("a roda é a MENOR sequência, abaixo da de seis", sixHigh > wheel);

        var aceHigh = PokerHandEvaluator.Evaluate(Cards("Ah Kd Qc Js Th"));
        Check("a roda não é lida como sequência de ás", aceHigh > wheel);

        var steelWheel = PokerHandEvaluator.Evaluate(Cards("Ah 2h 3h 4h 5h"));
        Check($"A-2-3-4-5 do mesmo naipe é straight flush ({steelWheel.Category})",
            steelWheel.Category == HandCategory.StraightFlush);
        Check("o straight flush da roda perde para o de seis",
            PokerHandEvaluator.Evaluate(Cards("2h 3h 4h 5h 6h")) > steelWheel);

        // An ace at both ends is not a straight.
        var wrapAround = PokerHandEvaluator.Evaluate(Cards("Qh Kd Ac 2s 3h"));
        Check($"Q-K-A-2-3 não é sequência ({wrapAround.Category})",
            wrapAround.Category == HandCategory.HighCard);
    }

    // ---------------------------------------------------------------- kickers and exact ties

    private void TestKickersAndTies()
    {
        var betterKicker = PokerHandEvaluator.Evaluate(Cards("9h 9d Ac 7s 3h"));
        var worseKicker = PokerHandEvaluator.Evaluate(Cards("9c 9s Kc 7d 3d"));
        Check("o mesmo par é separado pelo kicker", betterKicker > worseKicker);

        // The same hand in different suits is a GENUINE tie: no suit beats another.
        var handA = PokerHandEvaluator.Evaluate(Cards("9h 9d Ac 7s 3h"));
        var handB = PokerHandEvaluator.Evaluate(Cards("9c 9s Ad 7h 3d"));
        Check("mãos idênticas em naipes diferentes empatam exatamente", handA == handB);

        var flushA = PokerHandEvaluator.Evaluate(Cards("Ah Jh 9h 7h 3h"));
        var flushB = PokerHandEvaluator.Evaluate(Cards("As Js 9s 7s 3s"));
        Check("flushes iguais em naipes diferentes empatam exatamente", flushA == flushB);

        var higherFlush = PokerHandEvaluator.Evaluate(Cards("Ah Qh 9h 7h 3h"));
        Check("o flush se decide na segunda carta quando a primeira empata", higherFlush > flushA);

        var twoPairKicker = PokerHandEvaluator.Evaluate(Cards("Kh Kd 9c 9s Ah"));
        var twoPairWorse = PokerHandEvaluator.Evaluate(Cards("Kc Ks 9h 9d Qh"));
        Check("dois pares iguais são separados pelo kicker", twoPairKicker > twoPairWorse);

        var highPair = PokerHandEvaluator.Evaluate(Cards("Kh Kd 9c 8s 4h"));
        var lowTwoPair = PokerHandEvaluator.Evaluate(Cards("3h 3d 2c 2s 4h"));
        Check("dois pares baixos batem um par alto", lowTwoPair > highPair);

        Check("mão vazia não vale nada",
            PokerHandEvaluator.Evaluate(new List<int>()) == PokerHandRank.None);
    }

    // ---------------------------------------------------------------- best five of seven

    private void TestBestFiveOfSeven()
    {
        // Five hearts on the board: the flush is already there and the hole cards add nothing.
        var board = Cards("Ah Kh 7h 3h 2h");
        var playing = PokerHandEvaluator.Evaluate(Cards("5d 4s"), board);
        Check($"a mesa joga sozinha quando as cartas da mão não ajudam ({playing.Category})",
            playing.Category == HandCategory.Flush);

        // One hole card of the same suit beats the board's own flush.
        var better = PokerHandEvaluator.Evaluate(Cards("Qh 4s"), board);
        Check("uma carta do mesmo naipe melhora o flush da mesa", better > playing);

        // Five suited cards in sequence: the straight flush must be found inside the seven.
        var straightFlush = PokerHandEvaluator.Evaluate(Cards("9h 8h 7h 6h 5h 4d Ac"));
        Check($"o straight flush é achado dentro das sete cartas ({straightFlush.Category})",
            straightFlush.Category == HandCategory.StraightFlush);

        // A flush and a straight that do not share cards: the flush wins, and it is NOT promoted.
        var both = PokerHandEvaluator.Evaluate(Cards("9h 8h 7h 6h 2h 5c 4d"));
        Check($"flush e sequência separados: vence o flush, sem virar straight flush ({both.Category})",
            both.Category == HandCategory.Flush);

        // Two players on the same board, decided only by their own cards.
        var shared = Cards("Th 9d 2c 5s Kh");
        var pairOfKings = PokerHandEvaluator.Evaluate(Cards("Kd 3c"), shared);
        var pairOfTens = PokerHandEvaluator.Evaluate(Cards("Ts 3d"), shared);
        Check("par de reis bate par de dez na mesma mesa", pairOfKings > pairOfTens);

        var sevenCardTie = PokerHandEvaluator.Evaluate(Cards("Kd 3c"), shared)
                           == PokerHandEvaluator.Evaluate(Cards("Kc 3d"), shared);
        Check("as mesmas cartas em naipes trocados continuam empatando com sete cartas", sevenCardTie);

        var displayedFlush = PokerHandEvaluator.BestFive(Cards("Qh 4s"), board);
        Check("a apresentação escolhe exatamente cinco cartas", displayedFlush.Length == 5);
        Check("a apresentação do flush exclui a carta fora do naipe",
            displayedFlush.All(card => CardId.SuitOf(card) == CardId.Hearts));

        var displayedFullHouse = PokerHandEvaluator.BestFive(Cards("8h 8d 8c 5s 5h 5d Kc"));
        Check("o full house apresentado começa pela trinca mais alta",
            displayedFullHouse.Take(3).All(card => CardId.RankOf(card) == 6)
            && displayedFullHouse.Skip(3).All(card => CardId.RankOf(card) == CardId.Five));

        var displayedWheel = PokerHandEvaluator.BestFive(Cards("Ah 2d 3c 4s 5h 9d Kc"));
        Check("a sequência baixa é mostrada de cinco até ás",
            CardId.RankOf(displayedWheel[0]) == CardId.Five
            && CardId.RankOf(displayedWheel[4]) == CardId.Ace);
    }

    // ---------------------------------------------------------------- dealing

    private void TestDealing()
    {
        var players = new List<string> { "1", "2", "3" };

        var deal = PokerDeal.Deal(players, 12345UL);
        Check($"cada jogador recebe duas cartas ({deal.HoleCards.Count} mãos)",
            deal.HoleCards.Count == 3
            && deal.HoleCards.Values.All(hand => hand.Count == PokerDeal.HoleCardCount));

        var dealt = deal.HoleCards.Values.SelectMany(hand => hand).ToList();
        Check($"nenhuma carta foi dada duas vezes ({dealt.Count} distribuídas)",
            dealt.Distinct().Count() == dealt.Count);

        Check($"o resto do baralho fica no monte ({deal.Stub.Count})",
            deal.Stub.Count == CardId.Count - dealt.Count
            && !deal.Stub.Intersect(dealt).Any());

        var again = PokerDeal.Deal(players, 12345UL);
        var sameDeal = players.All(id => deal.HoleCards[id].SequenceEqual(again.HoleCards[id]));
        Check("a mesma semente dá exatamente a mesma distribuição", sameDeal);

        var different = PokerDeal.Deal(players, 999UL);
        Check("sementes diferentes dão distribuições diferentes",
            !players.All(id => deal.HoleCards[id].SequenceEqual(different.HoleCards[id])));

        var board = PokerDeal.DealBoard(deal.Stub);
        Check($"a mesa tem cinco cartas comunitárias ({board.Count})", board.Count == PokerDeal.BoardCount);
        Check("nenhuma comunitária saiu de uma mão", !board.Intersect(dealt).Any());
        Check("as comunitárias são distintas entre si", board.Distinct().Count() == board.Count);

        // A card is burned before each street, exactly as at a table.
        Check("uma carta é queimada antes do flop, do turn e do river",
            board[0] == deal.Stub[1] && board[3] == deal.Stub[5] && board[4] == deal.Stub[7]);

        Check($"o board cresce 0/3/4/5 pelas ruas",
            PokerDeal.BoardSize(PokerStreet.Preflop) == 0
            && PokerDeal.BoardSize(PokerStreet.Flop) == 3
            && PokerDeal.BoardSize(PokerStreet.Turn) == 4
            && PokerDeal.BoardSize(PokerStreet.River) == 5);

        Check("as ruas avançam até o showdown e param lá",
            PokerDeal.NextStreet(PokerStreet.Preflop) == PokerStreet.Flop
            && PokerDeal.NextStreet(PokerStreet.River) == PokerStreet.Showdown
            && PokerDeal.NextStreet(PokerStreet.Showdown) == PokerStreet.Showdown);
    }

    // ---------------------------------------------------------------- chips

    private void TestChipStacks()
    {
        var exact = true;
        for (var amount = 1; amount <= 2000; amount++)
        {
            if (PokerChipStack.Total(PokerChipStack.Decompose(amount)) != amount)
                exact = false;
        }

        Check("toda quantia de 1 a 2000 é montada exatamente em fichas", exact);

        var stack = PokerChipStack.Decompose(225);
        Check($"225 vira 2x100 + 1x25 ({string.Join(" + ", stack.Select(r => $"{r.Count}x{r.Denomination}"))})",
            stack.Count == 2
            && stack[0].Denomination == 100 && stack[0].Count == 2
            && stack[1].Denomination == 25 && stack[1].Count == 1);

        Check("quantia zero não gera pilha", PokerChipStack.Decompose(0).Count == 0);
        Check("quantia negativa não gera pilha", PokerChipStack.Decompose(-50).Count == 0);

        // The cap must never make a stack worth LESS than the number it stands for.
        var capped = PokerChipStack.Decompose(1234, maxRuns: 2);
        Check($"a pilha limitada a 2 denominações nunca vale menos que o valor ({PokerChipStack.Total(capped)} >= 1234)",
            capped.Count <= 2 && PokerChipStack.Total(capped) >= 1234);

        Check("as denominações estão em ordem decrescente",
            PokerChipStack.Denominations
                .Zip(PokerChipStack.Denominations.Skip(1), (a, b) => a > b).All(ok => ok));

        Check($"a maior ficha que cabe em 60 é 50 ({PokerChipStack.LargestFitting(60)})",
            PokerChipStack.LargestFitting(60) == 50);

        var bankCoversEveryBet = true;
        for (var balance = 5; balance <= 500; balance += 5)
        {
            for (var amount = 5; amount <= balance; amount += 5)
            {
                var bank = PokerChipStack.CreatePlayableBank(balance);
                if (!PokerChipStack.TryTake(bank, amount, out var payment)
                    || PokerChipStack.Total(payment) != amount
                    || PokerChipStack.Total(bank) != balance - amount)
                    bankCoversEveryBet = false;
            }
        }

        Check("todo saldo ate 500 paga qualquer valor em passos de 5 sem fabricar troco",
            bankCoversEveryBet);

        var exactBank = PokerChipStack.CreatePlayableBank(500);
        var beforeExact = PokerChipStack.Total(exactBank);
        var exactSelection = new[] { 25, 10, 5 };
        var acceptedExact = PokerChipStack.TryTakeExact(
            exactBank, exactSelection, 40, out var exactPayment);
        Check("a autoridade preserva as denominações fisicamente selecionadas",
            acceptedExact && PokerChipStack.Expand(exactPayment).OrderBy(value => value)
                .SequenceEqual(exactSelection.OrderBy(value => value))
            && PokerChipStack.Total(exactBank) == beforeExact - 40);

        var beforeInvalid = PokerChipStack.Total(exactBank);
        var rejectedFabricated = !PokerChipStack.TryTakeExact(
            exactBank, new[] { 25, 25, 25, 25 }, 100, out _);
        Check("uma seleção impossível é recusada sem fabricar nem perder fichas",
            rejectedFabricated && PokerChipStack.Total(exactBank) == beforeInvalid);
    }

    private void TestWagerInteraction()
    {
        var state = new PlayerBetState
        {
            Stack = 95,
            CommittedThisRound = 5,
            CommittedThisHand = 5,
        };
        var options = PokerBetting.LegalActions(state, currentBet: 10, minRaiseIncrement: 10);

        Check("cinco fichas sobre o blind de cinco viram call para dez",
            PokerWagerInteraction.TryResolve(options, 5, 5,
                out var call, out var callTotal, out var callProblem, out _)
            && call == PokerActionKind.Call && callTotal == 10
            && callProblem == PokerWagerProblem.None);

        Check("uma quantia entre call e aumento minimo fica na mesa para correcao",
            !PokerWagerInteraction.TryResolve(options, 5, 10,
                out _, out _, out var shortProblem, out var minimum)
            && shortProblem == PokerWagerProblem.BelowMinimum && minimum == 15);

        Check("quinze fichas sobre o blind viram aumento para vinte",
            PokerWagerInteraction.TryResolve(options, 5, 15,
                out var raise, out var raiseTotal, out _, out _)
            && raise == PokerActionKind.Raise && raiseTotal == 20);

        Check("clicar no pote sem selecionar fichas nunca passa por acidente",
            !PokerWagerInteraction.TryResolve(options, 5, 0,
                out _, out _, out var emptyProblem, out _)
            && emptyProblem == PokerWagerProblem.NoChipsSelected);

        var freeOptions = PokerBetting.LegalActions(new PlayerBetState { Stack = 100 }, 0, 10);
        Check("uma aposta abaixo do minimo pos-flop informa dez fichas",
            !PokerWagerInteraction.TryResolve(freeOptions, 0, 5,
                out _, out _, out var postFlopProblem, out var postFlopMinimum)
            && postFlopProblem == PokerWagerProblem.BelowMinimum
            && postFlopMinimum == 10);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Cards written the way they are spoken: "Ah 2d Tc" — rank then suit, space separated. Tests
    /// that read like the hands they describe are tests whose failures are legible.
    /// </summary>
    private static int[] Cards(string spec)
    {
        const string ranks = "23456789TJQKA";
        const string suits = "cdhs";

        return spec.Split(' ', System.StringSplitOptions.RemoveEmptyEntries)
            .Select(token => CardId.From(ranks.IndexOf(token[0]), suits.IndexOf(token[1])))
            .ToArray();
    }

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
