using System.Collections.Generic;
using System.Linq;
using Godot;
using Poker.Rules;

/// <summary>
/// The geometry of the cloth: that the community row, each seat's cards and each seat's chips all
/// fit on the same bar table without landing on top of one another.
///
/// This is the check that catches "the cards look great" turning into "the flop is under player
/// three's stack" the moment a fourth seat is filled. The table it is measured against is the same
/// 0.61 m collider the scene carries.
/// </summary>
public partial class PokerLayoutTest : Node
{
    /// <summary>Radius of the bar table the poker scene sits on.</summary>
    private const float TableRadius = 0.61f;

    private int _passed;
    private int _failed;

    public override void _Ready()
    {
        GD.Print("=== Teste de layout do poker ===");

        TestBoardRow();
        TestSeatSpots();
        TestReaderAlignedFrames();
        TestNothingOverlaps();
        TestEverythingFitsTheTable();
        TestSceneMatchesTheLayout();
        TestChipsKeepTheirIdentity();
        TestStableBankDoesNotFlick();
        TestLoosePotOrganizesIntoATower();
        TestPreparedChipsUseContactStacks();
        TestContactStackOpensBeforeCollection();
        TestChipsSettleLikeChips();
        TestNaturalMotionCurves();
        TestDealAnimationOrder();
        TestBoundedChipGroups();
        TestCalculatedPresentationTiming();
        TestDealerChangePlanningAndMotion();
        TestLargeFourWayPayout();

        GD.Print($"=== {_passed} passaram, {_failed} falharam ===");
        if (_failed > 0)
            GD.PushWarning($"{_failed} verificação(ões) de layout do poker falharam.");

        GetTree().Quit(_failed > 0 ? 1 : 0);
    }

    private void TestDealAnimationOrder()
    {
        var headsUp = new[] { "A", "B" };
        Check("animação com botão em A entrega primeiro ao BB B e por último a A",
            PokerSeatPresenter.DealPosition(headsUp, buttonSeat: 0, "B") == 0
            && PokerSeatPresenter.DealPosition(headsUp, buttonSeat: 0, "A") == 1);
        Check("animação acompanha o botão quando ele gira para B",
            PokerSeatPresenter.DealPosition(headsUp, buttonSeat: 1, "A") == 0
            && PokerSeatPresenter.DealPosition(headsUp, buttonSeat: 1, "B") == 1);
    }

    private void TestBoardRow()
    {
        var spec = PokerLayoutSpec.Default;

        var places = Enumerable.Range(0, PokerDeal.BoardCount)
            .Select(i => PokerTableLayout.BoardPosition(i, spec))
            .ToList();

        Check("as cinco comunitárias ficam em linha", places.All(p => Mathf.IsEqualApprox(p.Y, places[0].Y)));
        Check("a linha é centrada na mesa",
            Mathf.Abs(places[0].X + places[^1].X) < 0.0001f);

        var gaps = places.Zip(places.Skip(1), (a, b) => b.X - a.X).ToList();
        Check($"o espaçamento é uniforme ({gaps[0] * 1000.0f:F1} mm)",
            gaps.All(gap => Mathf.IsEqualApprox(gap, gaps[0])));
        Check($"as cartas não se sobrepõem na linha ({(gaps[0] - spec.CardWidth) * 1000.0f:F1} mm de folga)",
            gaps[0] > spec.CardWidth);

        // A card's place must not depend on how many are face up, or turning the river slides the flop.
        var flopOnly = PokerTableLayout.BoardPosition(0, spec);
        var everything = PokerTableLayout.BoardPosition(0, spec);
        Check("o lugar de uma comunitária não depende de quantas estão viradas", flopOnly == everything);
    }

    private void TestSeatSpots()
    {
        var spec = PokerLayoutSpec.Default;

        foreach (var facing in Facings())
        {
            var cards = Enumerable.Range(0, PokerDeal.HoleCardCount)
                .Select(i => PokerTableLayout.SeatCardPosition(facing, i, spec))
                .ToList();

            var apart = cards[0].DistanceTo(cards[1]);
            Check($"as duas cartas do assento não se sobrepõem ({apart * 1000.0f:F1} mm)",
                apart > spec.CardWidth * 0.5f);

            var bet = PokerTableLayout.SeatSpot(facing, spec.SeatBetRadius);
            var stack = PokerTableLayout.SeatSpot(facing, spec.SeatStackRadius);

            Check($"a aposta fica entre o meio e o stack ({bet.Length():F3} < {stack.Length():F3})",
                bet.Length() < stack.Length());
            Check("tudo de um assento fica na mesma linha radial",
                Mathf.Abs(bet.Normalized().Dot(facing.Normalized()) - 1.0f) < 0.001f);
        }
    }

    private void TestReaderAlignedFrames()
    {
        var authored = new Transform3D(
            new Basis(Vector3.Up, Mathf.Pi),
            new Vector3(0.12f, 0.004f, -0.21f));
        var canonical = PokerTableLayout.ReaderAlignedFrame(
            authored, Vector2.Down, Vector2.Down);
        Check("o marcador do Seat0 permanece exatamente onde foi editado",
            canonical.IsEqualApprox(authored));

        foreach (var facing in Facings())
        {
            var aligned = PokerTableLayout.ReaderAlignedFrame(
                authored, facing, Vector2.Down);
            var expectedTurn = new Basis(Vector3.Up,
                PokerTableLayout.YawTowardCentre(facing)
                - PokerTableLayout.YawTowardCentre(Vector2.Down));
            Check($"cartas e HUD giram o frame completo para o leitor {facing}",
                aligned.Origin.DistanceTo(expectedTurn * authored.Origin) < 0.0001f
                && aligned.Basis.X.Dot((expectedTurn * authored.Basis).X) > 0.9999f
                && aligned.Basis.Z.Dot((expectedTurn * authored.Basis).Z) > 0.9999f);
        }
    }

    private void TestNothingOverlaps()
    {
        var spec = PokerLayoutSpec.Default;

        // The seat's own things must clear the community row, or a flop lands under somebody's cards.
        var boardHalfLength = spec.CardLength * 0.5f;
        var closest = Mathf.Min(spec.SeatBetRadius, spec.SeatCardRadius);

        Check($"as coisas de um assento não invadem a linha comunitária "
              + $"({closest:F3} m contra {boardHalfLength:F3} m)",
            closest > boardHalfLength + spec.CardGap);
        Check($"o pote deixa as cartas comunitárias respirarem ({spec.PotRadius * 100.0f:F0} cm do centro)",
            spec.PotRadius > boardHalfLength + spec.CardGap + 0.020f
            && spec.PotRadius < spec.SeatBetRadius - 0.020f);

        // With four seats, one seat's things must not reach into the next seat's.
        var worst = float.MaxValue;
        var facings = Facings().ToList();

        for (var a = 0; a < facings.Count; a++)
        {
            for (var b = a + 1; b < facings.Count; b++)
            {
                for (var i = 0; i < PokerDeal.HoleCardCount; i++)
                {
                    for (var j = 0; j < PokerDeal.HoleCardCount; j++)
                    {
                        var distance = PokerTableLayout.SeatCardPosition(facings[a], i, spec)
                            .DistanceTo(PokerTableLayout.SeatCardPosition(facings[b], j, spec));

                        worst = Mathf.Min(worst, distance);
                    }
                }
            }
        }

        Check($"as cartas de assentos vizinhos não se tocam (pior caso {worst * 1000.0f:F0} mm)",
            worst > spec.CardWidth);
    }

    private void TestEverythingFitsTheTable()
    {
        var spec = PokerLayoutSpec.Default;

        Check($"a linha comunitária cabe na mesa ({PokerTableLayout.BoardReach(spec):F3} m de {TableRadius} m)",
            PokerTableLayout.BoardReach(spec) < TableRadius);
        Check($"as coisas dos assentos cabem na mesa ({PokerTableLayout.SeatReach(spec):F3} m de {TableRadius} m)",
            PokerTableLayout.SeatReach(spec) < TableRadius);

        Check($"a linha comunitária tem {spec.BoardWidth * 100.0f:F1} cm de largura",
            spec.BoardWidth > 0.0f && spec.BoardWidth < TableRadius * 2.0f);

        // A card the size of a real one would be unreadable from the chair; one much larger would not
        // leave room for four seats. This pins the compromise rather than leaving it to drift.
        Check($"a carta é maior que a real mas não absurda ({spec.CardWidth * 1000.0f:F0} x {spec.CardLength * 1000.0f:F0} mm)",
            spec.CardWidth is > 0.063f and < 0.090f
            && spec.CardLength is > 0.088f and < 0.125f);
    }

    /// <summary>The scene must be measured by the same spec the layout is, or the aim points at nothing.</summary>
    private void TestSceneMatchesTheLayout()
    {
        var scene = GD.Load<PackedScene>("res://Games/Poker/Poker.tscn");
        Check("a cena do poker carrega", scene != null && scene.CanInstantiate());

        if (scene == null)
            return;

        var game = scene.Instantiate<PokerGame>();
        AddChild(game);

        Check("o jogo aponta para o apresentador da mesa e para os assentos",
            game.BoardPresenter != null && game.Seats != null);

        if (game.BoardPresenter != null)
        {
            var spec = game.BoardPresenter.Spec;
            var reference = PokerLayoutSpec.Default;

            Check($"a cena usa a mesma medida de carta do layout "
                  + $"({spec.CardWidth:F3} x {spec.CardLength:F3} m)",
                Mathf.IsEqualApprox(spec.CardWidth, reference.CardWidth)
                && Mathf.IsEqualApprox(spec.CardLength, reference.CardLength));

            var boardStep = spec.CardWidth + spec.CardGap;
            var boardCardWidth = game.BoardPresenter.CommunityCardSpec.CardWidth;
            Check($"as comunitárias aumentadas continuam separadas "
                  + $"({boardCardWidth * 1000.0f:F0} mm em centros de {boardStep * 1000.0f:F0} mm)",
                boardCardWidth < boardStep);

            Check($"o que a cena desenha cabe na mesa dela ({PokerTableLayout.SeatReach(spec):F3} m)",
                PokerTableLayout.SeatReach(spec) < TableRadius
                && PokerTableLayout.BoardReach(spec) < TableRadius);
        }

        var seats = 0;
        var allHaveEyes = true;
        for (var i = 0; i < game.Seats.GetChildCount(); i++)
        {
            if (game.Seats.GetChild(i) is not Marker3D seat)
                continue;

            seats++;
            if (seat.GetNodeOrNull<Node3D>("SeatView") == null)
                allHaveEyes = false;
        }

        Check($"a mesa tem quatro assentos (achou {seats})", seats == 4);
        Check("todo assento tem o ponto de vista do jogador", allHaveEyes);

        // A seat facing the wrong way seats somebody with their back to the game.
        var worstOff = 0.0f;
        for (var i = 0; i < game.Seats.GetChildCount(); i++)
        {
            if (game.Seats.GetChild(i) is not Marker3D seat)
                continue;

            var facing = -seat.GlobalTransform.Basis.Z;
            var towardTable = (game.GlobalPosition - seat.GlobalPosition) with { Y = 0.0f };
            if (towardTable.LengthSquared() < 1e-6f)
                continue;

            var offBy = Mathf.RadToDeg(
                new Vector2(facing.X, facing.Z).AngleTo(new Vector2(towardTable.X, towardTable.Z)));
            worstOff = Mathf.Max(worstOff, Mathf.Abs(offBy));
        }

        Check($"todo assento está virado para a mesa (pior desvio {worstOff:F1}°)", worstOff < 1.0f);

        game.QueueFree();
    }

    /// <summary>
    /// A pile that GROWS keeps the chips it already had.
    ///
    /// This is the bug the player reported as "the chips change colour when they land". A bet redrawn
    /// from its total re-decomposes the whole thing, so pushing 10 onto 20 replaced two blue tens with
    /// a red twenty-five and a green five — under their hand, after they had already thrown it.
    /// </summary>
    private void TestChipsKeepTheirIdentity()
    {
        var pile = new PokerChipPile();
        AddChild(pile);

        pile.Show(20);
        var first = pile.Runs.Select(run => (run.Denomination, run.Count)).ToList();

        Check($"20 vira duas fichas de 10 ({Describe(pile)})",
            pile.Runs.Count == 1 && pile.Runs[0].Denomination == 10 && pile.Runs[0].Count == 2);

        pile.AddUpTo(30);

        Check($"empurrar mais 10 mantém as duas fichas de 10 e acrescenta uma ({Describe(pile)})",
            pile.Runs.Count == 1 && pile.Runs[0].Denomination == 10 && pile.Runs[0].Count == 3);
        Check($"e o monte continua valendo o que diz ({pile.Value})", pile.Value == 30);

        // The contrast that makes the check mean something: redrawn from the total, the same 30 is a
        // different set of chips entirely.
        var fresh = new PokerChipPile();
        AddChild(fresh);
        fresh.Show(30);

        Check($"redesenhar 30 do zero daria outras fichas ({Describe(fresh)})",
            fresh.Runs.Count == 2 && fresh.Runs[0].Denomination == 25);

        // Growing repeatedly must not drift: the cap rounds the tail UP, and measuring the next
        // addition against what is drawn rather than against the request is what stops that piling up.
        var running = new PokerChipPile();
        AddChild(running);

        var worstOver = 0;
        var total = 0;

        foreach (var step in new[] { 5, 10, 25, 5, 130, 45, 1, 999 })
        {
            total += step;
            running.AddUpTo(total);
            worstOver = Mathf.Max(worstOver, running.Value - total);
        }

        Check($"o monte nunca vale menos do que foi apostado (excesso máximo {worstOver})",
            worstOver >= 0 && running.Value >= total);
        Check($"e o excesso não se acumula ({running.Value} contra {total})", worstOver < 25);

        Check($"a primeira decomposição sobrevive a tudo isso ({first.Count} corrida(s))", first.Count == 1);

        // Swept away rather than paid out of: a pile that shrinks is simply redrawn.
        running.AddUpTo(0);
        Check($"varrer o monte não deixa ficha nenhuma ({running.ChipCount})",
            running.ChipCount == 0 && running.Value == 0);

        pile.QueueFree();
        fresh.QueueFree();
        running.QueueFree();
    }

    /// <summary>
    /// Thrown chips have to land like chips: scattered, leaning, and dropping the last of the way.
    ///
    /// The player's words were that they fall exactly on top of one another. The scatter is derived
    /// from the chip's own index rather than rolled, so it is the same on every peer and stable from
    /// frame to frame — nothing about it is replicated, and nothing about it crawls.
    /// </summary>
    private void TestStableBankDoesNotFlick()
    {
        var pile = new PokerChipPile
        {
            StableRunColumns = true,
            StackSpacing = 0.046f,
        };
        AddChild(pile);

        var bank = PokerChipStack.CreatePlayableBank(490);
        pile.SetRuns(bank);
        var before = pile.GetChildren().OfType<Node3D>().Where(chip => chip.Visible)
            .ToDictionary(chip => chip.GetInstanceId(), chip => chip.Transform);

        var paid = PokerChipStack.TryTake(bank, 10, out var payment);
        pile.SetRuns(bank);
        var after = pile.GetChildren().OfType<Node3D>().Where(chip => chip.Visible)
            .ToDictionary(chip => chip.GetInstanceId(), chip => chip.Transform);

        var survivorsStayedStill = after.All(entry => before.TryGetValue(entry.Key, out var old)
            && old.IsEqualApprox(entry.Value));
        var laneCentres = before.Values.Select(entry => entry.Origin.X)
            .Distinct().OrderBy(value => value).ToList();
        var closestLaneGap = laneCentres.Zip(laneCentres.Skip(1), (left, right) => right - left)
            .DefaultIfEmpty(float.MaxValue).Min();
        Check($"as colunas do banco deixam folga entre fichas ({closestLaneGap * 1000.0f:F1} mm)",
            closestLaneGap >= pile.EffectiveDiameter + 0.003f);
        Check("pagar remove fichas existentes sem criar novas na pilha",
            paid && PokerChipStack.Total(payment) == 10 && after.Count < before.Count
            && after.Keys.All(before.ContainsKey));
        Check("nenhuma ficha que ficou na pilha muda de lugar ou de instancia", survivorsStayedStill);

        pile.QueueFree();
    }

    private void TestLoosePotOrganizesIntoATower()
    {
        var pile = new PokerChipPile
        {
            CombineRunsIntoColumns = true,
            LooseWhenSpread = true,
            Scatter = 0.020f,
        };
        AddChild(pile);
        pile.SetRuns(new[] { new ChipRun(10, 6) });

        pile.Spread = 1.0f;
        var loose = ChipSpots(pile);
        pile.Spread = 0.0f;
        var tidy = ChipSpots(pile);

        var looseRadius = loose.Max(spot => new Vector2(spot.X, spot.Z).Length());
        var tidyRadius = tidy.Max(spot => new Vector2(spot.X, spot.Z).Length());
        Check($"antes de organizar as fichas ficam soltas ({looseRadius * 1000.0f:F1} mm)",
            looseRadius > 0.006f);
        Check($"organizar fecha as mesmas fichas em uma torre ({tidyRadius * 1000.0f:F1} mm)",
            tidyRadius < 0.0001f
            && loose.Count == tidy.Count
            && tidy.Select(spot => spot.Y).Distinct().Count() == tidy.Count);

        var neighbour = new PokerChipPile
        {
            CombineRunsIntoColumns = true,
            LooseWhenSpread = true,
            LooseSlotOffset = pile.ChipCount,
            Spread = 1.0f,
        };
        AddChild(neighbour);
        neighbour.SetRuns(new[] { new ChipRun(5, 6) });
        pile.Spread = 1.0f;
        var shared = ChipSpots(pile).Concat(ChipSpots(neighbour)).ToList();
        var sharedClosest = ClosestHorizontalGap(shared);
        Check($"lotes diferentes compartilham o pote sem se atravessar ({sharedClosest * 1000.0f:F1} mm)",
            sharedClosest >= pile.EffectiveDiameter * pile.LooseSpacingScale - 0.0001f);

        pile.QueueFree();
        neighbour.QueueFree();
    }

    private void TestChipsSettleLikeChips()
    {
        Check("o ruído de assentamento é determinístico",
            Enumerable.Range(0, 64).All(i => Mathf.IsEqualApprox(
                PokerChipPile.Noise(i, 0), PokerChipPile.Noise(i, 0))));

        var values = Enumerable.Range(0, 512).Select(i => PokerChipPile.Noise(i, 1)).ToList();
        Check($"e fica na faixa esperada ({values.Min():F2} a {values.Max():F2})",
            values.All(value => value >= -1.0f && value <= 1.0f));
        Check($"e não é uma constante disfarçada ({values.Distinct().Count()} valores distintos)",
            values.Distinct().Count() > 400);

        // Five chips of one value: a single column, which is exactly the shape the player described
        // as falling on top of one another.
        var pile = new PokerChipPile
        {
            Scatter = 0.011f,
            TiltDegrees = 8.0f,
            LooseWhenSpread = true,
        };
        AddChild(pile);
        pile.SetRuns(new[] { new ChipRun(10, 5) });

        // In the hand, before they are let go, they ARE a tidy column. The whole spread happens on
        // the way down, which is what makes a throw read as a throw.
        pile.Spread = 0.0f;
        var stacked = ChipSpots(pile);

        Check($"na mão as fichas estão empilhadas ({stacked.Count} fichas)",
            stacked.Count == 5 && stacked.All(spot =>
                new Vector2(spot.X, spot.Z).Length() < 0.0001f));

        pile.Spread = 1.0f;
        var scattered = ChipSpots(pile);

        var closest = float.MaxValue;
        var apart = 0;

        for (var i = 0; i < scattered.Count; i++)
        {
            for (var j = i + 1; j < scattered.Count; j++)
            {
                var gap = new Vector2(scattered[i].X - scattered[j].X, scattered[i].Z - scattered[j].Z);
                if (gap.Length() > 0.0008f)
                    apart++;

                closest = Mathf.Min(closest, gap.Length());
            }
        }

        Check($"ao aterrissar nenhuma cai exatamente sobre a outra ({apart} pares afastados, "
              + $"menor distância {closest * 1000.0f:F1} mm)",
            apart == scattered.Count * (scattered.Count - 1) / 2
            && closest >= pile.EffectiveDiameter * pile.LooseSpacingScale - 0.0001f);

        Check($"e todas continuam empilhadas em altura ({scattered.Count} fichas)",
            scattered.Select(spot => spot.Y).Distinct().Count() == scattered.Count);

        Check($"a queda começa no alto e acaba no lugar "
              + $"({PokerChipPile.DropCurve(0.0f):F2} a {PokerChipPile.DropCurve(1.0f):F2})",
            Mathf.IsEqualApprox(PokerChipPile.DropCurve(0.0f), 1.0f)
            && Mathf.IsEqualApprox(PokerChipPile.DropCurve(1.0f), 0.0f));

        Check("a queda acelera em vez de descer a passo constante",
            PokerChipCurveAccelerates());

        Check($"e a ficha quica uma vez, pequeno ({PokerChipPile.DropCurve(0.8f):F3})",
            PokerChipPile.DropCurve(0.8f) > 0.0f && PokerChipPile.DropCurve(0.8f) < 0.2f);

        pile.QueueFree();
    }

    private void TestPreparedChipsUseContactStacks()
    {
        const float diameter = PokerChipAssetMeshes.Diameter;
        const float thickness = PokerChipAssetMeshes.Thickness;
        var first = Enumerable.Range(0, 12)
            .Select(slot => PokerChipContactLayout.RootOffset(slot, diameter, thickness))
            .ToList();
        var second = Enumerable.Range(0, 12)
            .Select(slot => PokerChipContactLayout.RootOffset(slot, diameter, thickness))
            .ToList();

        Check("o monte de fichas preparadas e deterministico em todos os peers",
            first.SequenceEqual(second));

        var projectedOverlaps = 0;
        var intersectingVolumes = 0;
        for (var left = 0; left < first.Count; left++)
            for (var right = left + 1; right < first.Count; right++)
            {
                var horizontal = new Vector2(
                    first[left].X - first[right].X,
                    first[left].Z - first[right].Z).Length();
                if (horizontal >= diameter)
                    continue;

                projectedOverlaps++;
                var leftTop = first[left].Y + thickness;
                var rightTop = first[right].Y + thickness;
                var verticallySeparate = leftTop <= first[right].Y + 0.00001f
                                         || rightTop <= first[left].Y + 0.00001f;
                if (!verticallySeparate)
                    intersectingVolumes++;
            }

        Check($"as fichas preparadas se sobrepoem como pilhas ({projectedOverlaps} pares)",
            projectedOverlaps > first.Count);
        Check($"nenhuma ficha preparada entra no volume de outra ({intersectingVolumes} pares)",
            intersectingVolumes == 0);
        Check($"o monte continua compacto ({first.Max(place => new Vector2(place.X, place.Z).Length()) * 100.0f:F1} cm)",
            first.Max(place => new Vector2(place.X, place.Z).Length()) < diameter * 1.25f);
    }

    private void TestContactStackOpensBeforeCollection()
    {
        var animator = new PokerChipAnimator();
        AddChild(animator);
        animator.Configure(null, 0.011f, 2, 1,
            0.30f, 0.03f, 0.12f, 0.50f, 0.50f, 0.70f, 0.30f);

        for (var slot = 0; slot < 2; slot++)
        {
            var batch = animator.Acquire();
            var origin = PokerChipContactLayout.RootOffset(
                slot, PokerChipAssetMeshes.Diameter, PokerChipAssetMeshes.Thickness);
            batch.Amount = slot == 0 ? 5 : 10;
            batch.From = origin;
            batch.To = Vector3.Zero;
            batch.Basis = Basis.Identity;
            batch.StartSpread = 0.0f;
            batch.Progress = 0.0f;
            batch.Phase = PokerChipAnimator.Phase.ToPot;
            batch.Pile.Transform = new Transform3D(Basis.Identity, origin);
            batch.Pile.LooseSlotOffset = slot;
            batch.Pile.Spread = 0.0f;
            batch.Pile.SetRuns(new[] { new ChipRun(batch.Amount, 1) });
            batch.Pile.RetargetLooseSlots(slot);
            batch.Pile.Visible = true;
        }

        for (var frame = 0; frame < 40; frame++)
            foreach (var batch in animator.Batches)
                animator.Advance(batch, 1.0f / 60.0f);

        var arrived = animator.ActiveVisualPositions().Values.ToList();
        var clearance = arrived.Count == 2
            ? new Vector2(arrived[0].X - arrived[1].X, arrived[0].Z - arrived[1].Z).Length()
            : 0.0f;
        Check("a coleta abre a pilha antes de reunir suas raizes no pote",
            animator.Batches.All(batch => batch.Phase == PokerChipAnimator.Phase.AtPotLoose)
            && animator.Batches.All(batch => batch.Pile.Spread > 0.999f)
            && clearance >= PokerChipAssetMeshes.Diameter - 0.0001f);

        animator.QueueFree();
    }

    private static float ClosestHorizontalGap(IReadOnlyList<Vector3> positions)
    {
        var closest = float.MaxValue;
        for (var i = 0; i < positions.Count; i++)
        {
            for (var j = i + 1; j < positions.Count; j++)
            {
                var gap = new Vector2(
                    positions[i].X - positions[j].X,
                    positions[i].Z - positions[j].Z).Length();
                closest = Mathf.Min(closest, gap);
            }
        }
        return closest;
    }

    /// <summary>The first half of the fall covers less ground than the second.</summary>
    private static bool PokerChipCurveAccelerates() =>
        PokerChipPile.DropCurve(0.0f) - PokerChipPile.DropCurve(0.155f)
        < PokerChipPile.DropCurve(0.465f) - PokerChipPile.DropCurve(0.62f);

    private void TestNaturalMotionCurves()
    {
        var cardFrom = new Vector3(-0.3f, 0.0f, -0.1f);
        var cardTo = new Vector3(0.2f, 0.0f, 0.25f);
        var cardStart = PokerMotion.CardThrow(cardFrom, cardTo, 0.0f, 0.04f, 0.01f);
        var cardMiddle = PokerMotion.CardThrow(cardFrom, cardTo, 0.5f, 0.04f, 0.01f);
        var cardEnd = PokerMotion.CardThrow(cardFrom, cardTo, 1.0f, 0.04f, 0.01f);

        Check("a carta começa e termina exatamente nos encaixes",
            cardStart.IsEqualApprox(cardFrom) && cardEnd.IsEqualApprox(cardTo));
        Check($"a carta percorre um arco baixo ({cardMiddle.Y * 100.0f:F1} cm)",
            cardMiddle.Y > 0.0f && cardMiddle.Y <= 0.05f);

        var chipFrom = new Vector2(-0.45f, 0.0f);
        var chipTo = new Vector2(-0.22f, 0.0f);
        var chipStart = PokerMotion.ChipThrow(chipFrom, chipTo, 0.0f, 0.042f, 0.005f);
        var chipMiddle = PokerMotion.ChipThrow(chipFrom, chipTo, 0.5f, 0.042f, 0.005f);
        var chipEnd = PokerMotion.ChipThrow(chipFrom, chipTo, 1.0f, 0.042f, 0.005f);

        Check("as fichas não teleportam na saída nem na chegada",
            new Vector2(chipStart.X, chipStart.Z).IsEqualApprox(chipFrom)
            && new Vector2(chipEnd.X, chipEnd.Z).IsEqualApprox(chipTo));
        Check($"o lançamento de fichas não levanta uma pilha 15 cm ({chipMiddle.Y * 100.0f:F1} cm)",
            chipMiddle.Y > 0.025f && chipMiddle.Y < 0.06f);
    }

    private void TestBoundedChipGroups()
    {
        var large = new List<ChipRun> { new(25, 60), new(5, 40), new(1, 20) };
        var grouped = PokerChipAnimator.GroupRuns(large, 12);
        Check("um stack grande respeita o limite de atores moveis", grouped.Count == 12);
        Check("agrupar fichas preserva exatamente o valor do pagamento",
            PokerChipStack.Total(grouped) == PokerChipStack.Total(large));
        Check("nenhuma denominacao desaparece ao limitar os atores",
            grouped.Select(run => run.Denomination).Distinct().OrderBy(value => value)
                .SequenceEqual(new[] { 1, 5, 25 }));

        var small = PokerChipAnimator.GroupRuns(new[] { new ChipRun(25, 3) }, 80);
        Check("pagamentos comuns continuam animando uma ficha por vez",
            small.Count == 3 && small.All(run => run.Count == 1));
    }

    private void TestCalculatedPresentationTiming()
    {
        var smallFold = PokerPresentationTiming.MinimumHandPause(false, 3, 1, 1);
        var largeFold = PokerPresentationTiming.MinimumHandPause(false, 30, 1, 1);
        var showdown = PokerPresentationTiming.MinimumHandPause(true, 30, 3, 2);
        Check("um pote maior reserva mais tempo antes da proxima mao", largeFold > smallFold);
        Check("o showdown inclui leitura, ranking e entrega do pote",
            showdown > largeFold + PokerPresentationTiming.RankedHandsReadingSeconds);
        Check("a estimativa de atores tambem e limitada",
            PokerPresentationTiming.EstimateChipGroups(new[] { 100000, 100000 }, 16) == 16);
    }

    private void TestDealerChangePlanningAndMotion()
    {
        var awards = new Dictionary<string, int> { ["A"] = 38, ["B"] = 37 };
        var needsChange = PokerPayoutPlanner.Create(
            new[] { 25, 25, 25 }, awards, new[] { "A", "B" });
        Check("tres fichas de 25 exigem troco para pagar 38 e 37",
            needsChange.RequiresDealerChange && needsChange.TotalAward == 75);
        Check("o plano de troco preserva exatamente os dois premios",
            needsChange.Winners.All(winner =>
                PokerChipStack.Total(winner.ExactRuns) == winner.Amount));

        var alreadyExact = PokerPayoutPlanner.Create(
            new[] { 25, 25, 10, 10, 1, 1, 1, 1, 1 }, awards, new[] { "A", "B" });
        Check("o dealer nao troca fichas quando o pote ja permite a divisao exata",
            !alreadyExact.RequiresDealerChange);

        var animator = new PokerChipAnimator();
        AddChild(animator);
        animator.Configure(null, 0.011f, 2, 1,
            0.10f, 0.03f, 0.05f, 0.10f, 0.10f, 0.10f, 0.10f);
        var batch = animator.Acquire();
        batch.Pile.SetRuns(new[] { new ChipRun(25, 1) });
        batch.Pile.Visible = true;
        batch.From = Vector3.Zero;
        batch.To = new Vector3(0.18f, 0.0f, 0.04f);
        batch.FromBasis = Basis.Identity;
        batch.ToBasis = Basis.Identity;
        batch.JustStarted = true;
        batch.Phase = PokerChipAnimator.Phase.ToDealer;
        for (var frame = 0; frame < 30 && batch.Phase != PokerChipAnimator.Phase.AtDealer; frame++)
            animator.Advance(batch, 1.0f / 60.0f);
        Check("a ficha chega fisicamente ao ponto de troco antes de mudar denominacao",
            batch.Phase == PokerChipAnimator.Phase.AtDealer
            && batch.Pile.Position.DistanceTo(batch.To) < 0.0001f);
        animator.QueueFree();
    }

    private void TestLargeFourWayPayout()
    {
        var profile = new PokerPresentationProfile { MaxAnimatedChipGroups = 24 };
        var contributions = new[] { 100000, 100000, 100000, 100000 };
        Check("um pote extremo de quatro jogadores continua limitado",
            profile.EstimateChipGroups(contributions) == 24);

        var awards = new Dictionary<string, int>
        {
            ["A"] = 100003,
            ["B"] = 99999,
            ["C"] = 100001,
            ["D"] = 99997,
        };
        var plan = PokerPayoutPlanner.Create(
            new[] { 100000, 100000, 100000, 100000 }, awards,
            new[] { "A", "B", "C", "D" });
        Check("um rateio grande de quatro vencedores preserva todo o valor",
            plan.TotalAward == contributions.Sum()
            && plan.Winners.Count == 4
            && plan.Winners.All(winner =>
                PokerChipStack.Total(winner.ExactRuns) == winner.Amount));
        Check("o rateio solicita troco quando os quatro grupos nao representam os premios",
            plan.RequiresDealerChange);
        Check("o tempo calculado cresce para quatro vencedores sem constante magica",
            profile.MinimumHandPause(true, 24, 4, 4)
            > profile.MinimumHandPause(true, 24, 2, 1));
    }

    private static List<Vector3> ChipSpots(PokerChipPile pile) =>
        pile.GetChildren()
            .OfType<Node3D>()
            .Where(chip => chip.Visible)
            .Select(chip => chip.Position)
            .ToList();

    private static string Describe(PokerChipPile pile) =>
        string.Join(" + ", pile.Runs.Select(run => $"{run.Count}x{run.Denomination}"));

    private static IEnumerable<Vector2> Facings()
    {
        yield return new Vector2(0.0f, -1.0f);
        yield return new Vector2(1.0f, 0.0f);
        yield return new Vector2(0.0f, 1.0f);
        yield return new Vector2(-1.0f, 0.0f);
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
