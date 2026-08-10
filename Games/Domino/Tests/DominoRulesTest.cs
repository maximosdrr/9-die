using System.Collections.Generic;
using Domino.Rules;
using Godot;

/// <summary>
/// Exercises the pure domino rules with no scene, no network and no nodes. Everything the server
/// validates and everything the hand UI offers comes from these functions, so pinning them here is
/// what keeps client and server from ever disagreeing about what a legal move is.
/// </summary>
public partial class DominoRulesTest : Node
{
    private int _passed;
    private int _failed;

    public override void _Ready()
    {
        GD.Print("=== Teste de regras do dominó ===");

        TestTileIdentity();
        TestDealing();
        TestOpenerSelection();
        TestPlacement();
        TestBoardState();
        TestStuckAndLock();
        TestScriptedGameTerminates();

        GD.Print($"=== {_passed} passaram, {_failed} falharam ===");
        if (_failed > 0)
            GD.PushWarning($"{_failed} verificação(ões) de regra falharam.");

        GetTree().Quit(_failed > 0 ? 1 : 0);
    }

    private void TestTileIdentity()
    {
        var seen = new HashSet<int>();
        var roundTrips = true;
        var doubles = 0;
        var pipsTotal = 0;

        for (var low = 0; low <= DominoTileId.MaxPips; low++)
        {
            for (var high = low; high <= DominoTileId.MaxPips; high++)
            {
                var id = DominoTileId.From(low, high);
                seen.Add(id);

                DominoTileId.Split(id, out var backLow, out var backHigh);
                if (backLow != low || backHigh != high)
                    roundTrips = false;

                // The pair is unordered: which way the caller names it must not matter.
                if (DominoTileId.From(high, low) != id)
                    roundTrips = false;

                if (DominoTileId.IsDouble(id))
                    doubles++;

                pipsTotal += DominoTileId.Pips(id);
            }
        }

        Check($"conjunto duplo-seis tem 28 peças distintas (achou {seen.Count})",
            seen.Count == DominoTileId.Count);
        Check("id vai e volta para as duas metades em qualquer ordem", roundTrips);
        Check($"conjunto tem 7 carroças (achou {doubles})", doubles == 7);
        Check($"soma de pinos do conjunto é 168 (achou {pipsTotal})", pipsTotal == 168);

        Check("ids fora da faixa são recusados",
            !DominoTileId.IsValid(-1) && !DominoTileId.IsValid(DominoTileId.Count));

        var threeFive = DominoTileId.From(3, 5);
        Check("a metade sobrando é a que vira ponta",
            DominoTileId.OtherHalf(threeFive, 3) == 5 && DominoTileId.OtherHalf(threeFive, 5) == 3);
        Check("peça que não casa não tem metade sobrando",
            DominoTileId.OtherHalf(threeFive, 4) == DominoTileId.NoEnd);

        var fiveFive = DominoTileId.From(5, 5);
        Check("carroça mantém o mesmo valor na ponta",
            DominoTileId.OtherHalf(fiveFive, 5) == 5);
    }

    private void TestDealing()
    {
        var four = new List<string> { "1", "2", "3", "4" };
        var three = new List<string> { "1", "2", "3" };
        var two = new List<string> { "1", "2" };

        var dealFour = DominoDeal.Deal(four, 12345UL);
        var dealThree = DominoDeal.Deal(three, 12345UL);
        var dealTwo = DominoDeal.Deal(two, 12345UL);

        Check("quatro jogadores recebem 6 peças e sobram 4 no monte",
            dealFour.Hands["1"].Count == 6 && dealFour.Boneyard.Count == 4);
        Check("três jogadores recebem 7 peças e sobram 7 no monte",
            dealThree.Hands["1"].Count == 7 && dealThree.Boneyard.Count == 7);
        Check("dois jogadores recebem 7 peças e sobram 14 no monte",
            dealTwo.Hands["1"].Count == 7 && dealTwo.Boneyard.Count == 14);

        var debugTen = DominoDeal.Deal(two, 12345UL, handSizeOverride: 10);
        Check("override temporario da 10 pecas a dois jogadores e preserva o monte",
            debugTen.Hands["1"].Count == 10 && debugTen.Hands["2"].Count == 10
            && debugTen.Boneyard.Count == 8);

        var unsafeDebugTen = DominoDeal.Deal(three, 12345UL, handSizeOverride: 10);
        Check("override que nao cabe no conjunto volta automaticamente ao padrao",
            unsafeDebugTen.Hands["1"].Count == 7 && unsafeDebugTen.Boneyard.Count == 7);

        Check("a distribuição não perde nem duplica peça", ConservesTheSet(dealFour, four));

        var again = DominoDeal.Deal(four, 12345UL);
        Check("mesma semente distribui exatamente igual", SameDeal(dealFour, again, four));

        var other = DominoDeal.Deal(four, 999UL);
        Check("sementes diferentes distribuem diferente", !SameDeal(dealFour, other, four));
    }

    private void TestOpenerSelection()
    {
        var turnOrder = new List<string> { "1", "2", "3" };

        // The 6|6 outranks a 5|5 even though the 5|5 sits earlier in the turn order.
        var withDoubles = new Dictionary<string, List<int>>
        {
            ["1"] = new() { DominoTileId.From(5, 5), DominoTileId.From(6, 4) },
            ["2"] = new() { DominoTileId.From(6, 6), DominoTileId.From(0, 1) },
            ["3"] = new() { DominoTileId.From(2, 2) },
        };
        var opener = DominoDeal.PickOpener(withDoubles, turnOrder, out var openingTile);
        Check("a maior carroça abre o jogo",
            opener == "2" && openingTile == DominoTileId.From(6, 6));

        // With no double anywhere, the heaviest tile leads.
        var noDoubles = new Dictionary<string, List<int>>
        {
            ["1"] = new() { DominoTileId.From(1, 2), DominoTileId.From(0, 3) },
            ["2"] = new() { DominoTileId.From(6, 5) },
            ["3"] = new() { DominoTileId.From(4, 3) },
        };
        opener = DominoDeal.PickOpener(noDoubles, turnOrder, out openingTile);
        Check("sem carroça, a peça mais pesada abre",
            opener == "2" && openingTile == DominoTileId.From(6, 5));

        // Equal weight is broken by the larger half, then by seat.
        var tied = new Dictionary<string, List<int>>
        {
            ["1"] = new() { DominoTileId.From(4, 2) },
            ["2"] = new() { DominoTileId.From(5, 1) },
            ["3"] = new() { DominoTileId.From(3, 3) },
        };
        opener = DominoDeal.PickOpener(tied, turnOrder, out openingTile);
        Check("carroça vence peças de mesmo peso",
            opener == "3" && openingTile == DominoTileId.From(3, 3));

        var tiedNoDouble = new Dictionary<string, List<int>>
        {
            ["1"] = new() { DominoTileId.From(4, 2) },
            ["2"] = new() { DominoTileId.From(5, 1) },
        };
        opener = DominoDeal.PickOpener(tiedNoDouble, new List<string> { "1", "2" }, out openingTile);
        Check("com peso empatado vence a peça de maior metade",
            opener == "2" && openingTile == DominoTileId.From(5, 1));

        // Identical tiles cannot exist in one deal, so the remaining tie is the seat order.
        var sameTile = new Dictionary<string, List<int>>
        {
            ["1"] = new() { DominoTileId.From(3, 1) },
            ["2"] = new() { DominoTileId.From(3, 1) },
        };
        opener = DominoDeal.PickOpener(sameTile, new List<string> { "1", "2" }, out _);
        Check("empate absoluto cai para o assento mais cedo", opener == "1");
    }

    private void TestPlacement()
    {
        const int empty = DominoTileId.NoEnd;
        var threeFive = DominoTileId.From(3, 5);

        Check("mesa vazia aceita qualquer peça em qualquer ponta",
            DominoRules.CanPlace(threeFive, ChainEnd.Left, empty, empty)
            && DominoRules.CanPlace(threeFive, ChainEnd.Right, empty, empty));

        Check("peça encaixa na ponta que casa",
            DominoRules.CanPlace(threeFive, ChainEnd.Left, 3, 6)
            && DominoRules.CanPlace(threeFive, ChainEnd.Right, 6, 5));
        Check("peça não encaixa na ponta que não casa",
            !DominoRules.CanPlace(threeFive, ChainEnd.Left, 6, 3)
            && !DominoRules.CanPlace(threeFive, ChainEnd.Right, 3, 6));

        // A tile matching both ends is two different placements on the table, so it must be
        // offered twice — this is exactly what makes the HUD ask which end.
        var both = DominoRules.LegalMoves(new[] { threeFive }, 3, 5);
        Check($"peça que casa nas duas pontas gera duas opções (achou {both.Count})",
            both.Count == 2
            && both.Exists(m => m.End == ChainEnd.Left)
            && both.Exists(m => m.End == ChainEnd.Right));

        var oneEnd = DominoRules.LegalMoves(new[] { threeFive }, 3, 6);
        Check("peça que casa numa ponta só gera uma opção",
            oneEnd.Count == 1 && oneEnd[0].End == ChainEnd.Left);

        var opening = DominoRules.LegalMoves(new[] { threeFive, DominoTileId.From(0, 0) }, empty, empty);
        Check($"abertura oferece cada peça uma única vez (achou {opening.Count})", opening.Count == 2);

        Check("mão sem peça compatível não tem jogada",
            !DominoRules.HasLegalMove(new[] { DominoTileId.From(0, 1) }, 3, 5));
        Check("mão com peça compatível tem jogada",
            DominoRules.HasLegalMove(new[] { DominoTileId.From(0, 1), threeFive }, 3, 5));

        Check("soma de pinos da mão",
            DominoRules.PipTotal(new[] { threeFive, DominoTileId.From(6, 6) }) == 20);
    }

    private void TestBoardState()
    {
        var board = new DominoBoardState();
        Check("mesa começa vazia e sem pontas",
            board.IsEmpty
            && board.LeftEnd == DominoTileId.NoEnd
            && board.RightEnd == DominoTileId.NoEnd);

        var opening = DominoTileId.From(3, 5);
        Check("abertura abre as pontas com as duas metades",
            board.TryPlay("1", opening, ChainEnd.Right)
            && board.LeftEnd == 3 && board.RightEnd == 5);

        Check("jogar na direita move só a ponta direita",
            board.TryPlay("2", DominoTileId.From(5, 2), ChainEnd.Right)
            && board.LeftEnd == 3 && board.RightEnd == 2);

        Check("jogar na esquerda move só a ponta esquerda",
            board.TryPlay("3", DominoTileId.From(3, 6), ChainEnd.Left)
            && board.LeftEnd == 6 && board.RightEnd == 2);

        var beforeCount = board.Count;
        var rejected = board.TryPlay("1", DominoTileId.From(0, 1), ChainEnd.Left);
        Check("jogada ilegal é recusada sem alterar a mesa",
            !rejected && board.Count == beforeCount && board.LeftEnd == 6 && board.RightEnd == 2);

        // A carroça leaves the end where it found it.
        Check("carroça mantém o valor da ponta",
            board.TryPlay("1", DominoTileId.From(6, 6), ChainEnd.Left) && board.LeftEnd == 6);

        var rebuilt = DominoBoardState.FromPlays(board.Plays);
        Check("mesa reconstruída das jogadas reproduz as pontas",
            rebuilt.Count == board.Count
            && rebuilt.LeftEnd == board.LeftEnd
            && rebuilt.RightEnd == board.RightEnd);
    }

    private void TestStuckAndLock()
    {
        var stuck = new[] { DominoTileId.From(0, 1), DominoTileId.From(0, 2) };

        Check("com monte cheio, quem trava compra", DominoRules.CanDraw(stuck, 4, 5, 3));
        Check("quem tem jogada não pode comprar",
            !DominoRules.CanDraw(new[] { DominoTileId.From(4, 6) }, 4, 5, 3));
        Check("com monte vazio e sem jogada, passa",
            DominoRules.MustPass(stuck, 4, 5, 0));
        Check("com monte vazio mas com jogada, não passa",
            !DominoRules.MustPass(new[] { DominoTileId.From(4, 6) }, 4, 5, 0));
        Check("com monte cheio não se passa a vez", !DominoRules.MustPass(stuck, 4, 5, 1));

        var turnOrder = new List<string> { "1", "2", "3" };
        var hands = new Dictionary<string, List<int>>
        {
            ["1"] = new() { DominoTileId.From(0, 1) },
            ["2"] = new() { DominoTileId.From(0, 2) },
            ["3"] = new() { DominoTileId.From(1, 2) },
        };

        Check("jogo tranca quando ninguém joga e o monte acabou",
            DominoRules.IsLocked(hands, turnOrder, 4, 5, 0));
        Check("jogo não tranca com peça no monte",
            !DominoRules.IsLocked(hands, turnOrder, 4, 5, 1));

        hands["3"] = new List<int> { DominoTileId.From(4, 6) };
        Check("jogo não tranca se alguém ainda pode jogar",
            !DominoRules.IsLocked(hands, turnOrder, 4, 5, 0));

        var pips = new Dictionary<string, int> { ["1"] = 8, ["2"] = 3, ["3"] = 11 };
        Check("jogo trancado é vencido por quem tem menos pinos",
            DominoRules.ResolveLock(pips, turnOrder, "1") == "2");

        // Ties must not depend on dictionary order: the winner is whoever sits first after the
        // player who blocked the game.
        var tiedPips = new Dictionary<string, int> { ["1"] = 5, ["2"] = 5, ["3"] = 9 };
        Check("empate de pinos vai para quem vem depois de quem travou",
            DominoRules.ResolveLock(tiedPips, turnOrder, "1") == "2");
        Check("empate de pinos dá a volta na ordem de turno",
            DominoRules.ResolveLock(tiedPips, turnOrder, "2") == "1");
        Check("empate com bloqueador ausente começa pelo primeiro assento",
            DominoRules.ResolveLock(tiedPips, turnOrder, null) == "1");
    }

    /// <summary>
    /// Plays a whole match with a fixed policy and pins the two invariants that matter: the 28
    /// tiles are conserved at every single step, and the match always ends. It also runs twice to
    /// prove the outcome is reproducible from the seed alone — the property the network relies on
    /// when it refuses to ever transmit the deal.
    /// </summary>
    private void TestScriptedGameTerminates()
    {
        var (winnerA, reasonA, turnsA, conservedA) = PlayScriptedGame(20250807UL);
        var (winnerB, reasonB, turnsB, _) = PlayScriptedGame(20250807UL);

        Check($"partida roteirizada termina ({reasonA}, vencedor {winnerA}, {turnsA} lances)",
            winnerA != null);
        Check("as 28 peças são conservadas em todos os lances", conservedA);
        Check("mesma semente produz exatamente a mesma partida",
            winnerA == winnerB && reasonA == reasonB && turnsA == turnsB);

        // A different seed has to be able to end in a lock too, otherwise the lock path is dead
        // code that only the unit test above ever reaches.
        var sawLock = false;
        var allEnded = true;
        for (var seed = 1UL; seed <= 60UL; seed++)
        {
            var (winner, reason, _, conserved) = PlayScriptedGame(seed);
            allEnded &= winner != null && conserved;
            sawLock |= reason == "locked";
        }

        Check("sessenta partidas com sementes diferentes terminam com peças conservadas", allEnded);
        Check("o caminho de jogo trancado é alcançado na prática", sawLock);
    }

    private static (string Winner, string Reason, int Turns, bool Conserved) PlayScriptedGame(ulong seed)
    {
        var turnOrder = new List<string> { "1", "2", "3", "4" };
        var deal = DominoDeal.Deal(turnOrder, seed);
        var boneyard = deal.Boneyard;
        var board = new DominoBoardState();

        var opener = DominoDeal.PickOpener(deal.Hands, turnOrder, out var openingTile);
        var current = turnOrder.IndexOf(opener);

        deal.Hands[opener].Remove(openingTile);
        board.TryPlay(opener, openingTile, ChainEnd.Right);

        var conserved = TilesAreConserved(deal.Hands, boneyard, board);
        var passes = 0;
        var lastPlayer = opener;

        for (var turn = 0; turn < 500; turn++)
        {
            current = (current + 1) % turnOrder.Count;
            var playerId = turnOrder[current];
            var hand = deal.Hands[playerId];

            // Draw until playable, then play the first legal move; pass only when truly stuck.
            while (!DominoRules.HasLegalMove(hand, board.LeftEnd, board.RightEnd) && boneyard.Count > 0)
            {
                hand.Add(boneyard[^1]);
                boneyard.RemoveAt(boneyard.Count - 1);
                conserved &= TilesAreConserved(deal.Hands, boneyard, board);
            }

            var moves = DominoRules.LegalMoves(hand, board.LeftEnd, board.RightEnd);
            if (moves.Count == 0)
            {
                passes++;
                if (passes >= turnOrder.Count)
                {
                    var pips = new Dictionary<string, int>();
                    foreach (var entry in deal.Hands)
                        pips[entry.Key] = DominoRules.PipTotal(entry.Value);

                    return (DominoRules.ResolveLock(pips, turnOrder, lastPlayer), "locked", turn, conserved);
                }

                continue;
            }

            passes = 0;
            lastPlayer = playerId;

            var move = moves[0];
            hand.Remove(move.TileId);
            board.TryPlay(playerId, move.TileId, move.End);
            conserved &= TilesAreConserved(deal.Hands, boneyard, board);

            if (hand.Count == 0)
                return (playerId, "domino", turn, conserved);
        }

        return (null, "timeout", 500, conserved);
    }

    private static bool TilesAreConserved(
        Dictionary<string, List<int>> hands, List<int> boneyard, DominoBoardState board)
    {
        var seen = new HashSet<int>();

        foreach (var hand in hands.Values)
        {
            foreach (var tileId in hand)
            {
                if (!seen.Add(tileId))
                    return false;
            }
        }

        foreach (var tileId in boneyard)
        {
            if (!seen.Add(tileId))
                return false;
        }

        foreach (var tileId in board.PlayedTiles())
        {
            if (!seen.Add(tileId))
                return false;
        }

        return seen.Count == DominoTileId.Count;
    }

    private static bool ConservesTheSet(DealResult deal, IReadOnlyList<string> playerIds)
    {
        var seen = new HashSet<int>();

        foreach (var playerId in playerIds)
        {
            foreach (var tileId in deal.Hands[playerId])
            {
                if (!seen.Add(tileId))
                    return false;
            }
        }

        foreach (var tileId in deal.Boneyard)
        {
            if (!seen.Add(tileId))
                return false;
        }

        return seen.Count == DominoTileId.Count;
    }

    private static bool SameDeal(DealResult a, DealResult b, IReadOnlyList<string> playerIds)
    {
        foreach (var playerId in playerIds)
        {
            var handA = a.Hands[playerId];
            var handB = b.Hands[playerId];

            if (handA.Count != handB.Count)
                return false;

            for (var i = 0; i < handA.Count; i++)
            {
                if (handA[i] != handB[i])
                    return false;
            }
        }

        return true;
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
