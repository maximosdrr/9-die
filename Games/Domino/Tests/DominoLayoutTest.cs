using System.Collections.Generic;
using Domino.Rules;
using Godot;

/// <summary>
/// Pins the geometry of the played chain. Two things here are load-bearing for the network:
/// the layout is a pure function of the play list (so no transform is ever transmitted and every
/// peer draws the same table), and adjacent halves always show matching pips (so an orientation
/// bug can never make a legal chain look illegal).
/// </summary>
public partial class DominoLayoutTest : Node
{
    private int _passed;
    private int _failed;

    private static readonly LayoutSpec Spec = LayoutSpec.Default;

    public override void _Ready()
    {
        GD.Print("=== Teste de layout da corrente ===");

        TestExactPlacements();
        TestDoubleBeforeCorner();
        TestDeterminismAndIncrementalGrowth();
        TestFullChainFits();

        GD.Print($"=== {_passed} passaram, {_failed} falharam ===");
        if (_failed > 0)
            GD.PushWarning($"{_failed} verificação(ões) de layout falharam.");

        GetTree().Quit(_failed > 0 ? 1 : 0);
    }

    /// <summary>
    /// Regression for a double near the rail. With the ordinary reserve the follower turned
    /// immediately and lay parallel beside it. The double must reserve that follower in advance
    /// and become the corner itself when both no longer fit straight.
    /// </summary>
    private void TestDoubleBeforeCorner()
    {
        var plays = new List<PlayRecord>
        {
            new("1", DominoTileId.From(0, 1), ChainEnd.Right),
            new("1", DominoTileId.From(1, 2), ChainEnd.Right),
            new("1", DominoTileId.From(2, 3), ChainEnd.Right),
            new("1", DominoTileId.From(3, 4), ChainEnd.Right),
            new("1", DominoTileId.From(4, 4), ChainEnd.Right),
            new("1", DominoTileId.From(4, 5), ChainEnd.Right),
            new("1", DominoTileId.From(5, 6), ChainEnd.Right),
        };

        var layout = DominoChainLayout.Rebuild(plays, Spec);
        var laidDouble = layout.Placements[4];
        var follower = layout.Placements[5];
        var doubleLongAxisIsX = laidDouble.HalfExtents.X > laidDouble.HalfExtents.Y;
        var followerLongAxisIsX = follower.HalfExtents.X > follower.HalfExtents.Y;

        Check("a peça depois da carroça não fica deitada ao lado dela",
            doubleLongAxisIsX != followerLongAxisIsX);
        Check("a carroça próxima do limite inicia a dobra antes da seguidora",
            Mathf.Abs(laidDouble.Center.Y) > 0.0f || Mathf.Abs(follower.Center.Y) > 0.0f);
        Check("a correção da carroça continua dentro da mesa",
            !layout.OverflowedTable);
    }

    /// <summary>
    /// Three tiles whose transforms can be worked out by hand. The rotations are what decide
    /// which half of a tile faces the chain, so they are pinned to the exact quarter turn rather
    /// than to a tolerance.
    /// </summary>
    private void TestExactPlacements()
    {
        var opening = DominoTileId.From(3, 5);
        var straight = DominoTileId.From(2, 5);
        var carroca = DominoTileId.From(5, 5);

        var layout = DominoChainLayout.Rebuild(new List<PlayRecord>
        {
            new("1", opening, ChainEnd.Right),
            new("2", straight, ChainEnd.Right),
        }, Spec);

        var first = layout.Placements[0];
        Check("peça de saída fica no centro da área de jogo",
            first.Center.IsEqualApprox(Vector2.Zero));
        Check("peça de saída aponta a metade alta para o ramo direito",
            Mathf.IsEqualApprox(first.Yaw, Mathf.Pi * 0.5f));
        Check("peça de saída divide as pontas entre os dois ramos",
            first.Incoming == 3 && first.Outgoing == 5);
        Check("peça de saída deitada ocupa o comprimento em X",
            first.HalfExtents.IsEqualApprox(new Vector2(Spec.TileLength * 0.5f, Spec.TileWidth * 0.5f)));

        // Nose to tail: half of each tile plus the gap.
        var second = layout.Placements[1];
        var expectedX = Spec.TileLength * 0.5f + Spec.TileLength * 0.5f + Spec.Gap;
        Check($"segunda peça encosta na primeira (x={second.Center.X:F4})",
            second.Center.IsEqualApprox(new Vector2(expectedX, 0.0f)));
        Check("segunda peça casa 5 com 5 e deixa 2 na ponta",
            second.Incoming == 5 && second.Outgoing == 2);

        // The 5 half has to face BACK down the chain, which flips the tile a half turn relative
        // to the opening one. Getting this wrong is invisible in the rules and glaring on screen.
        Check("peça cuja metade baixa fica na ponta é virada meia volta",
            Mathf.IsEqualApprox(second.Yaw, -Mathf.Pi * 0.5f));

        var withDouble = DominoChainLayout.Rebuild(new List<PlayRecord>
        {
            new("1", opening, ChainEnd.Right),
            new("2", carroca, ChainEnd.Right),
        }, Spec);

        var laid = withDouble.Placements[1];
        Check("carroça é deitada atravessada",
            Mathf.IsEqualApprox(laid.Yaw, 0.0f)
            && laid.HalfExtents.IsEqualApprox(new Vector2(Spec.TileWidth * 0.5f, Spec.TileLength * 0.5f)));
        Check("carroça avança só a própria largura",
            laid.Center.IsEqualApprox(
                new Vector2(Spec.TileLength * 0.5f + Spec.TileWidth * 0.5f + Spec.Gap, 0.0f)));
        Check("carroça devolve o mesmo valor para a ponta",
            laid.Incoming == 5 && laid.Outgoing == 5);

        // The left branch mirrors the right one.
        var leftward = DominoChainLayout.Rebuild(new List<PlayRecord>
        {
            new("1", opening, ChainEnd.Right),
            new("2", DominoTileId.From(3, 1), ChainEnd.Left),
        }, Spec);
        Check("o ramo esquerdo cresce para o lado oposto",
            leftward.Placements[1].Center.X < 0.0f
            && Mathf.IsEqualApprox(leftward.Placements[1].Center.X, -expectedX));
    }

    private void TestDeterminismAndIncrementalGrowth()
    {
        var plays = BuildFullChain();

        var once = DominoChainLayout.Rebuild(plays, Spec);
        var twice = DominoChainLayout.Rebuild(plays, Spec);

        var identical = once.Placements.Count == twice.Placements.Count;
        for (var i = 0; identical && i < once.Placements.Count; i++)
        {
            var a = once.Placements[i];
            var b = twice.Placements[i];
            identical = a.TileId == b.TileId
                        && a.Center == b.Center
                        && a.Yaw == b.Yaw
                        && a.HalfExtents == b.HalfExtents;
        }

        // Bit-identical, not approximately equal: this is what lets the chain travel as a list of
        // tile ids with no transforms attached.
        Check("reconstruir a mesma corrente dá exatamente o mesmo resultado", identical);

        // Appending one tile must never disturb the ones already on the table, otherwise the
        // incremental presenter and a late joiner's full rebuild would draw different tables.
        var prefixesMatch = true;
        for (var n = 1; n <= plays.Count; n++)
        {
            var prefix = DominoChainLayout.Rebuild(plays.GetRange(0, n), Spec);
            if (prefix.Placements.Count != n)
            {
                prefixesMatch = false;
                break;
            }

            for (var i = 0; i < n; i++)
            {
                if (prefix.Placements[i].Center != once.Placements[i].Center
                    || prefix.Placements[i].Yaw != once.Placements[i].Yaw)
                {
                    prefixesMatch = false;
                    break;
                }
            }

            if (!prefixesMatch)
                break;
        }

        Check("acrescentar uma peça não move nenhuma das já postas", prefixesMatch);
    }

    private void TestFullChainFits()
    {
        var plays = BuildFullChain();
        var layout = DominoChainLayout.Rebuild(plays, Spec);

        Check($"a corrente completa usa as 28 peças (achou {layout.Placements.Count})",
            layout.Placements.Count == DominoTileId.Count);
        Check("a corrente completa cabe na mesa sem transbordar", !layout.OverflowedTable);
        Check($"cada ramo dobra poucas esquinas (máximo {layout.MaxTurnsPerBranch})",
            layout.MaxTurnsPerBranch <= 3);

        var allInside = true;
        foreach (var placement in layout.Placements)
        {
            if (Mathf.Abs(placement.Center.X) + placement.HalfExtents.X > Spec.PlayHalfExtents.X + 1e-5f
                || Mathf.Abs(placement.Center.Y) + placement.HalfExtents.Y > Spec.PlayHalfExtents.Y + 1e-5f)
            {
                allInside = false;
                break;
            }
        }

        Check("nenhuma peça sai da área de jogo", allInside);

        // Every tile is axis aligned, so overlap is an exact rectangle test rather than an
        // approximation. This is what catches a corner that turns on top of its own branch.
        var worstOverlap = 0.0f;
        var worstA = -1;
        var worstB = -1;
        for (var i = 0; i < layout.Placements.Count; i++)
        {
            for (var j = i + 1; j < layout.Placements.Count; j++)
            {
                var overlap = Overlap(layout.Placements[i], layout.Placements[j]);
                if (overlap > worstOverlap)
                {
                    worstOverlap = overlap;
                    worstA = i;
                    worstB = j;
                }
            }
        }

        var overlapDetails = worstA >= 0
            ? $"; A={layout.Placements[worstA].Center}/{layout.Placements[worstA].HalfExtents}/"
              + $"{layout.Placements[worstA].Branch}/double={layout.Placements[worstA].IsDouble}, "
              + $"B={layout.Placements[worstB].Center}/{layout.Placements[worstB].HalfExtents}/"
              + $"{layout.Placements[worstB].Branch}/double={layout.Placements[worstB].IsDouble}"
            : "";
        Check($"nenhuma peça se sobrepõe a outra "
            + $"(pior caso {worstOverlap * 1000.0f:F2} mm entre {worstA} e {worstB}{overlapDetails})",
            worstOverlap <= 0.0f);

        Check("as metades vizinhas mostram o mesmo número nos dois ramos",
            PipsMatchAlongBranch(layout, ChainEnd.Right) && PipsMatchAlongBranch(layout, ChainEnd.Left));

        var doubles = 0;
        var allCrosswise = true;
        foreach (var placement in layout.Placements)
        {
            if (!placement.IsDouble || placement.IsOpening)
                continue;

            // At a corner the double may align with the tile arriving at it; what matters is that
            // it lies crosswise to the branch leaving it, so compare it with its follower.
            var longSideIsX = placement.HalfExtents.X > placement.HalfExtents.Y;
            var follower = FollowerInBranch(layout, placement);
            if (!follower.HasValue)
                continue;

            doubles++;
            var followerLongSideIsX = follower.Value.HalfExtents.X > follower.Value.HalfExtents.Y;
            if (longSideIsX == followerLongSideIsX)
                allCrosswise = false;
        }

        Check($"as carroças ficam atravessadas em relação à vizinha (verificadas {doubles})",
            doubles > 0 && allCrosswise);
    }

    /// <summary>
    /// Walks one branch and checks the pip that each tile presents backwards is the pip the tile
    /// before it presents forwards. The opening tile faces both ways at once: its high half serves
    /// the right branch and its low half the left.
    /// </summary>
    private static bool PipsMatchAlongBranch(ChainLayout layout, ChainEnd branch)
    {
        var opening = layout.Placements[0];
        var previousOutgoing = branch == ChainEnd.Right ? opening.Outgoing : opening.Incoming;

        for (var i = 1; i < layout.Placements.Count; i++)
        {
            var placement = layout.Placements[i];
            if (placement.Branch != branch)
                continue;

            if (placement.Incoming != previousOutgoing)
                return false;

            previousOutgoing = placement.Outgoing;
        }

        return true;
    }

    private static TilePlacement? FollowerInBranch(ChainLayout layout, TilePlacement placement)
    {
        var found = false;

        foreach (var candidate in layout.Placements)
        {
            if (!found)
            {
                found = candidate.TileId == placement.TileId;
                continue;
            }

            if (candidate.Branch == placement.Branch)
                return candidate;
        }

        return null;
    }

    /// <summary>Deepest penetration between two axis-aligned footprints; zero or less means clear.</summary>
    private static float Overlap(TilePlacement a, TilePlacement b)
    {
        var x = a.HalfExtents.X + b.HalfExtents.X - Mathf.Abs(a.Center.X - b.Center.X);
        var y = a.HalfExtents.Y + b.HalfExtents.Y - Mathf.Abs(a.Center.Y - b.Center.Y);
        return Mathf.Min(x, y);
    }

    /// <summary>
    /// A legal chain using all 28 tiles, alternating branches so both corners get exercised.
    ///
    /// The double-six set is the complete graph on seven vertices plus a loop at each, so every
    /// vertex has even degree and an Eulerian circuit through all 28 edges exists. Hierholzer's
    /// algorithm finds one; because it is a CIRCUIT, the leftover path after the opening tile has
    /// loose ends matching the board's two open ends exactly, which is what lets tiles be consumed
    /// from either side.
    /// </summary>
    private static List<PlayRecord> BuildFullChain()
    {
        var walk = EulerianCircuit();
        var plays = new List<PlayRecord>(DominoTileId.Count);

        var openingTile = DominoTileId.From(walk[0], walk[1]);
        plays.Add(new PlayRecord("1", openingTile, ChainEnd.Right));

        // The opening splits low to the left branch and high to the right, so whichever branch
        // carries walk[1] is where the forward path continues.
        var frontEnd = DominoTileId.High(openingTile) == walk[1] ? ChainEnd.Right : ChainEnd.Left;
        var backEnd = frontEnd == ChainEnd.Right ? ChainEnd.Left : ChainEnd.Right;

        var lo = 1;
        var hi = DominoTileId.Count - 1;
        var takeFront = true;

        while (lo <= hi)
        {
            if (takeFront)
            {
                plays.Add(new PlayRecord("1", DominoTileId.From(walk[lo], walk[lo + 1]), frontEnd));
                lo++;
            }
            else
            {
                plays.Add(new PlayRecord("1", DominoTileId.From(walk[hi], walk[hi + 1]), backEnd));
                hi--;
            }

            takeFront = !takeFront;
        }

        return plays;
    }

    private static List<int> EulerianCircuit()
    {
        var used = new bool[DominoTileId.Count];
        var stack = new Stack<int>();
        var circuit = new List<int>();

        stack.Push(0);

        while (stack.Count > 0)
        {
            var vertex = stack.Peek();
            var advanced = false;

            for (var other = 0; other <= DominoTileId.MaxPips; other++)
            {
                var tileId = DominoTileId.From(vertex, other);
                if (used[tileId])
                    continue;

                used[tileId] = true;
                stack.Push(other);
                advanced = true;
                break;
            }

            if (!advanced)
                circuit.Add(stack.Pop());
        }

        return circuit;
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
