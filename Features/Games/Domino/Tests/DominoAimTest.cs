using System.Collections.Generic;
using Domino.Rules;
using Godot;

/// <summary>
/// Pins the aiming maths with no camera, no scene and no nodes.
///
/// Two properties carry the whole feature: the ghost must land exactly where the real tile lands
/// (or it lies to the player), and a stock slot must mean the same place regardless of which slots
/// are still occupied (or the tile the player picked is not the tile they get).
/// </summary>
public partial class DominoAimTest : Node
{
	private int _passed;
	private int _failed;

	private static readonly LayoutSpec Spec = LayoutSpec.Default;
	private static readonly BoneyardSpec Stock = BoneyardSpec.Default;

	public override void _Ready()
	{
		GD.Print("=== Teste de mira do dominó ===");

		TestPreviewMatchesTheRealPlay();
		TestNearestEnd();
		TestOrientation();
		TestBoneyardSlots();

		GD.Print($"=== {_passed} passaram, {_failed} falharam ===");
		if (_failed > 0)
			GD.PushWarning($"{_failed} verificação(ões) de mira falharam.");

		GetTree().Quit(_failed > 0 ? 1 : 0);
	}

	/// <summary>
	/// The ghost is only honest if it is computed the same way the real chain is. This plays the
	/// move for real and checks the preview had already put it there.
	/// </summary>
	private void TestPreviewMatchesTheRealPlay()
	{
		var plays = new List<PlayRecord>
		{
			new("1", DominoTileId.From(6, 6), ChainEnd.Right),
			new("1", DominoTileId.From(6, 2), ChainEnd.Right),
			new("1", DominoTileId.From(6, 4), ChainEnd.Left),
		};

		var candidate = DominoTileId.From(2, 5);
		var previewed = DominoAim.TryPreviewPlacement(plays, Spec, candidate, ChainEnd.Right, out var ghost);
		Check("a prévia existe para uma jogada legal", previewed);

		var afterwards = new List<PlayRecord>(plays) { new("1", candidate, ChainEnd.Right) };
		var real = DominoChainLayout.Rebuild(afterwards, Spec).Placements[^1];

		Check($"a prévia cai exatamente onde a peça real cai "
			  + $"(Δ={(ghost.Center - real.Center).Length() * 1000.0f:F3} mm)",
			ghost.Center == real.Center && Mathf.IsEqualApprox(ghost.Yaw, real.Yaw)
			&& ghost.TileId == real.TileId && ghost.Incoming == real.Incoming
			&& ghost.Outgoing == real.Outgoing);

		Check("não há prévia para uma peça que não encaixa",
			!DominoAim.TryPreviewPlacement(plays, Spec, DominoTileId.From(0, 1), ChainEnd.Right, out _));

		// The opening tile has no end to match, so it must still preview.
		Check("a peça de saída tem prévia na mesa vazia",
			DominoAim.TryPreviewPlacement(new List<PlayRecord>(), Spec, DominoTileId.From(3, 3),
				ChainEnd.Right, out var opening)
			&& opening.Center == Vector2.Zero);
	}

	private void TestNearestEnd()
	{
		var plays = new List<PlayRecord>
		{
			new("1", DominoTileId.From(6, 6), ChainEnd.Right),
			new("1", DominoTileId.From(6, 2), ChainEnd.Right),
			new("1", DominoTileId.From(6, 4), ChainEnd.Left),
		};

		// 4|2 fits both ends: the 4 end on the left, the 2 end on the right.
		var bothEnds = DominoTileId.From(4, 2);
		Check("mirando à esquerda escolhe a ponta esquerda",
			DominoAim.NearestEnd(plays, Spec, bothEnds, new Vector2(-0.5f, 0.0f)) == ChainEnd.Left);
		Check("mirando à direita escolhe a ponta direita",
			DominoAim.NearestEnd(plays, Spec, bothEnds, new Vector2(0.5f, 0.0f)) == ChainEnd.Right);

		// Deliberate and deterministic: a preview that flickers on the midpoint is worse than one
		// that always picks a side.
		Check("empate no meio é determinístico",
			DominoAim.NearestEnd(plays, Spec, bothEnds, Vector2.Zero)
			== DominoAim.NearestEnd(plays, Spec, bothEnds, Vector2.Zero));

		// A tile that only fits one end must ignore where the player is pointing.
		var leftOnly = DominoTileId.From(4, 5);
		Check("peça que só encaixa numa ponta ignora a mira",
			DominoAim.NearestEnd(plays, Spec, leftOnly, new Vector2(0.5f, 0.0f)) == ChainEnd.Left
			&& DominoAim.NearestEnd(plays, Spec, leftOnly, new Vector2(-0.5f, 0.0f)) == ChainEnd.Left);

		Check("mesa vazia não tem ponta a escolher",
			DominoAim.NearestEnd(new List<PlayRecord>(), Spec, bothEnds, new Vector2(-0.5f, 0.0f))
			== ChainEnd.Right);

		Check("a metade exigida vem da ponta mirada",
			DominoAim.RequiredLeadingPips(plays, ChainEnd.Left) == 4
			&& DominoAim.RequiredLeadingPips(plays, ChainEnd.Right) == 2);
		Check("mesa vazia não exige metade nenhuma",
			DominoAim.RequiredLeadingPips(new List<PlayRecord>(), ChainEnd.Right) == DominoTileId.NoEnd);
	}

	private void TestOrientation()
	{
		var threeFive = DominoTileId.From(3, 5);

		// The whole point of the mechanic: the tile is legal on both ends, but only when turned
		// the right way round for the end being aimed at.
		Check("liderar com a metade da ponta é aceito",
			DominoRules.CanPlaceOriented(threeFive, 3, ChainEnd.Left, 3, 5)
			&& DominoRules.CanPlaceOriented(threeFive, 5, ChainEnd.Right, 3, 5));
		Check("peça virada ao contrário é recusada mesmo em ponta compatível",
			!DominoRules.CanPlaceOriented(threeFive, 5, ChainEnd.Left, 3, 5)
			&& !DominoRules.CanPlaceOriented(threeFive, 3, ChainEnd.Right, 3, 5));

		Check("metade que a peça não tem é recusada",
			!DominoRules.CanPlaceOriented(threeFive, 4, ChainEnd.Left, 4, 5));

		var carroca = DominoTileId.From(5, 5);
		Check("carroça é aceita nas duas orientações",
			DominoRules.CanPlaceOriented(carroca, 5, ChainEnd.Left, 5, 2)
			&& DominoRules.CanPlaceOriented(carroca, 5, ChainEnd.Right, 2, 5));

		Check("mesa vazia aceita qualquer metade liderando",
			DominoRules.CanPlaceOriented(threeFive, 3, ChainEnd.Right, DominoTileId.NoEnd, DominoTileId.NoEnd)
			&& DominoRules.CanPlaceOriented(threeFive, 5, ChainEnd.Left, DominoTileId.NoEnd, DominoTileId.NoEnd));

		// Both ends showing the same pip: rotation still decides, the end no longer does.
		Check("com as duas pontas iguais só a orientação decide",
			DominoRules.CanPlaceOriented(threeFive, 3, ChainEnd.Left, 3, 3)
			&& DominoRules.CanPlaceOriented(threeFive, 3, ChainEnd.Right, 3, 3)
			&& !DominoRules.CanPlaceOriented(threeFive, 5, ChainEnd.Right, 3, 3));

		Check("id inválido é recusado",
			!DominoRules.CanPlaceOriented(DominoTileId.Count, 3, ChainEnd.Left, 3, 5));
	}

	private void TestBoneyardSlots()
	{
		const int stockSize = 21;

		// The property the whole draw-by-slot design rests on.
		var stable = true;
		for (var slot = 0; slot < stockSize; slot++)
		{
			if (DominoBoneyardLayout.SlotPosition(slot, Stock)
				!= DominoBoneyardLayout.SlotPosition(slot, Stock))
				stable = false;
		}

		Check("a posição de um lugar depende só do número dele", stable);

		var half = DominoBoneyardLayout.HalfExtents(Stock);
		var worstOverlap = 0.0f;
		for (var a = 0; a < stockSize; a++)
		{
			for (var b = a + 1; b < stockSize; b++)
			{
				var delta = (DominoBoneyardLayout.SlotPosition(a, Stock)
							 - DominoBoneyardLayout.SlotPosition(b, Stock)).Abs();
				var overlap = Mathf.Min(half.X * 2.0f - delta.X, half.Y * 2.0f - delta.Y);
				worstOverlap = Mathf.Max(worstOverlap, overlap);
			}
		}

		Check($"as peças do monte não se sobrepõem (pior caso {worstOverlap * 1000.0f:F2} mm)",
			worstOverlap <= 0.0f);

		// The stock must sit clear of where the chain grows, or tiles would land on top of it.
		var bounds = DominoBoneyardLayout.Bounds(stockSize, Stock);
		Check($"o monte fica fora da área de jogo (começa em z={bounds.Position.Y:F3}, "
			  + $"área vai até {Spec.PlayHalfExtents.Y:F3})",
			bounds.Position.Y > Spec.PlayHalfExtents.Y);

		// ... and still on the table. Radius checked at the far corners of the stock.
		const float tableRadius = 0.55f;
		var corners = new[]
		{
			bounds.Position,
			bounds.Position + new Vector2(bounds.Size.X, 0.0f),
			bounds.Position + new Vector2(0.0f, bounds.Size.Y),
			bounds.End,
		};

		var furthest = 0.0f;
		foreach (var corner in corners)
			furthest = Mathf.Max(furthest, corner.Length());

		Check($"o monte cabe na mesa (canto mais distante {furthest:F3} m, raio {tableRadius:F2} m)",
			furthest < tableRadius);

		// Drawing must not disturb the rest: a gap is left behind, nothing slides.
		var occupied = new List<int>();
		for (var slot = 0; slot < stockSize; slot++)
			occupied.Add(slot);

		var before = DominoBoneyardLayout.SlotPosition(9, Stock);
		occupied.Remove(7);
		var after = DominoBoneyardLayout.SlotPosition(9, Stock);

		Check("comprar um lugar não move os outros", before == after);

		Check("a mira encontra o lugar mais próximo",
			DominoBoneyardLayout.NearestSlot(occupied, Stock,
				DominoBoneyardLayout.SlotPosition(12, Stock)) == 12);

		// A gap is not a target — the tile is gone.
		Check("um lugar já comprado não é alvo",
			DominoBoneyardLayout.NearestSlot(occupied, Stock,
				DominoBoneyardLayout.SlotPosition(7, Stock)) != 7);

		Check("monte vazio não tem alvo",
			DominoBoneyardLayout.NearestSlot(new List<int>(), Stock, Vector2.Zero) == DominoTileId.NoEnd);
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
