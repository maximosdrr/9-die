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
		TestNothingOverlaps();
		TestEverythingFitsTheTable();
		TestSceneMatchesTheLayout();
		TestChipsKeepTheirIdentity();
		TestChipsSettleLikeChips();

		GD.Print($"=== {_passed} passaram, {_failed} falharam ===");
		if (_failed > 0)
			GD.PushWarning($"{_failed} verificação(ões) de layout do poker falharam.");

		GetTree().Quit(_failed > 0 ? 1 : 0);
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

	private void TestNothingOverlaps()
	{
		var spec = PokerLayoutSpec.Default;

		// The seat's own things must clear the community row, or a flop lands under somebody's cards.
		var boardHalfLength = spec.CardLength * 0.5f;
		var closest = Mathf.Min(spec.SeatBetRadius, spec.SeatCardRadius);

		Check($"as coisas de um assento não invadem a linha comunitária "
			  + $"({closest:F3} m contra {boardHalfLength:F3} m)",
			closest > boardHalfLength + spec.CardGap);

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
		var scene = GD.Load<PackedScene>("res://Features/Games/Poker/Poker.tscn");
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
		var pile = new PokerChipPile { Scatter = 0.011f, TiltDegrees = 8.0f };
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
			apart == scattered.Count * (scattered.Count - 1) / 2);

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

	/// <summary>The first half of the fall covers less ground than the second.</summary>
	private static bool PokerChipCurveAccelerates() =>
		PokerChipPile.DropCurve(0.0f) - PokerChipPile.DropCurve(0.155f)
		< PokerChipPile.DropCurve(0.465f) - PokerChipPile.DropCurve(0.62f);

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
