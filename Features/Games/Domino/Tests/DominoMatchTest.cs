using Domino.Rules;
using Godot;
using Godot.Collections;

/// <summary>
/// Plays a whole match through the real thing: a real Table spawning a real Domino scene, a real
/// Player being seated by a real controller, and every move going through DominoTurnResolver's
/// server path.
///
/// The rules tests prove the rules and the layout test proves the geometry; this proves the wiring
/// between them — that the deal reaches a hand, that a turn context survives being packed and
/// unpacked, that the turn stamp actually rejects a stale request, and that a win tears the match
/// down instead of hanging.
///
/// It runs as a solo match on purpose: with one peer every message takes the local path, so the
/// whole server ruling is exercised without a second process.
/// </summary>
public partial class DominoMatchTest : Node
{
	private int _passed;
	private int _failed;

	private string _lastRejection;
	private string _winner;
	private bool _matchOver;

	public override void _Ready()
	{
		GD.Print("=== Teste de partida de dominó ===");

		var player = BuildPlayer("1");
		var table = BuildTable();
		var game = table.CurrentTableGame as DominoGame;

		if (game == null)
		{
			Check("a mesa instanciou um jogo de dominó", false);
			Finish();
			return;
		}

		game.AllowSoloDebug = true;
		game.TurnOrder = new Array { "1" };
		game.TurnOwner = player;
		game.Player = player;

		game.MatchOver += OnMatchOver;

		var resolver = game.Resolver;
		SignalUtil.ConnectGuarded(resolver, DominoTurnResolver.SignalName.ActionRejected,
			new Callable(this, MethodName.OnActionRejected));

		game.SetupMatch(new Array { "1" }, "1");

		TestDealReachedTheHand(game);
		TestOpeningTileIsDown(game);
		TestSeating(game, player);
		TestRejections(game, resolver);
		TestMatchRunsToTheEnd(game, resolver);

		Finish();
	}

	private void TestDealReachedTheHand(DominoGame game)
	{
		// Seven each below a full table, minus the one the opener was forced to lead.
		Check($"a mão chegou ao jogador ({game.LocalHand.Length} peças)",
			game.LocalHand.Length == DominoDeal.HandSize(1) - 1);

		Check($"o monte ficou com o resto ({game.BoneyardCount})",
			game.BoneyardCount == DominoTileId.Count - DominoDeal.HandSize(1));

		Check("a contagem pública da mão bate com a mão real",
			game.HandCounts.TryGetValue("1", out var count) && count == game.LocalHand.Length);

		Check("o servidor carimbou a vez", game.TurnToken > 0);
	}

	private void TestOpeningTileIsDown(DominoGame game)
	{
		Check($"a peça de saída está na mesa ({game.Plays.Count} jogada)", game.Plays.Count == 1);

		Check("as pontas abriram com as duas metades da peça de saída",
			game.Plays.Count == 1
			&& game.LeftEnd == DominoTileId.Low(game.Plays[0].TileId)
			&& game.RightEnd == DominoTileId.High(game.Plays[0].TileId));

		// Nothing but the tile id and the end travelled; the position came from the layout.
		Check("o apresentador desenhou a peça de saída",
			game.ChainPresenter.GetChildCount() == game.Plays.Count);

		Check("a saída foi anunciada como abertura", game.LastAction == "open");
	}

	private void TestSeating(DominoGame game, Player player)
	{
		var seat = game.SeatFor("1");
		Check("o jogador tem assento", seat != null);

		if (seat != null)
		{
			Check($"o jogador foi sentado na cadeira (dist {player.GlobalPosition.DistanceTo(seat.GlobalPosition):F3} m)",
				player.GlobalPosition.DistanceTo(seat.GlobalPosition) < 1e-3f);
		}

		Check("o corpo do jogador foi travado para a partida",
			player.CurrentControlState == Player.ControllerStatesEnum.Game);

		Check("o controlador equipado é o de dominó",
			player.GameHandler.CurrentController is DominoController);

		// Seated for the whole match, so the walk/play toggle has nothing to do.
		Check("a troca de controle fica desligada enquanto sentado",
			!player.GameHandler.CurrentController.AllowsControlSwitch);
	}

	private void TestRejections(DominoGame game, DominoTurnResolver resolver)
	{
		int beforePlays;
		int beforeToken;

		// The opener leads a forced tile, so what is left may legitimately not fit either end.
		// Draw up to a playable hand first — the checks below need a legal move to exist.
		var guard = 0;
		while (DominoRules.LegalMoves(game.LocalHand, game.LeftEnd, game.RightEnd).Count == 0
			   && game.BoneyardCount > 0
			   && guard++ < DominoTileId.Count)
		{
			resolver.RequestDrawTile(game.TurnToken);
		}

		beforePlays = game.Plays.Count;
		beforeToken = game.TurnToken;

		var moves = DominoRules.LegalMoves(game.LocalHand, game.LeftEnd, game.RightEnd);
		if (moves.Count == 0)
		{
			Check("a mão tem jogada após comprar (pré-requisito das rejeições)", false);
			return;
		}

		// A stamp from a turn that already went by is the whole defence against a double click and
		// against a replayed packet.
		_lastRejection = null;
		resolver.RequestPlayTile(game.TurnToken + 99, moves[0].TileId, (int)moves[0].End);
		Check($"jogada com carimbo vencido é recusada ({_lastRejection})", _lastRejection == "stale_turn");

		var notHeld = FindTileNotInHand(game);
		_lastRejection = null;
		resolver.RequestPlayTile(game.TurnToken, notHeld, (int)ChainEnd.Right);
		Check($"jogar peça que não está na mão é recusado ({_lastRejection})",
			_lastRejection is "tile_not_in_hand" or "tile_does_not_match");

		_lastRejection = null;
		resolver.RequestDrawTile(game.TurnToken);
		Check($"comprar com jogada na mão é recusado ({_lastRejection})", _lastRejection == "has_legal_move");

		_lastRejection = null;
		resolver.RequestPass(game.TurnToken);
		Check($"passar com o monte cheio é recusado ({_lastRejection})", _lastRejection == "must_draw");

		Check("nenhuma recusa mexeu na mesa nem na vez",
			game.Plays.Count == beforePlays && game.TurnToken == beforeToken);
	}

	private void TestMatchRunsToTheEnd(DominoGame game, DominoTurnResolver resolver)
	{
		var draws = 0;
		var plays = 0;
		var steps = 0;

		while (!_matchOver && steps++ < 400)
		{
			var moves = DominoRules.LegalMoves(game.LocalHand, game.LeftEnd, game.RightEnd);

			if (moves.Count > 0)
			{
				plays++;
				resolver.RequestPlayTile(game.TurnToken, moves[0].TileId, (int)moves[0].End);
			}
			else if (game.BoneyardCount > 0)
			{
				draws++;
				resolver.RequestDrawTile(game.TurnToken);
			}
			else
			{
				resolver.RequestPass(game.TurnToken);
			}
		}

		Check($"a partida chega ao fim ({plays} jogadas, {draws} compras, {steps} lances)", _matchOver);
		Check($"a partida tem vencedor ({_winner})", _winner == "1");

		// The board every peer draws is rebuilt from the play list alone, so it has to still agree
		// with the ends the server reported.
		var rebuilt = DominoBoardState.FromPlays(game.Plays);
		Check($"as pontas continuam coerentes com as jogadas ({game.LeftEnd}/{game.RightEnd})",
			rebuilt.LeftEnd == game.LeftEnd && rebuilt.RightEnd == game.RightEnd);

		Check($"o apresentador tem uma peça por jogada ({game.ChainPresenter.GetChildCount()}/{game.Plays.Count})",
			game.ChainPresenter.GetChildCount() == game.Plays.Count);

		Check($"nenhuma peça foi duplicada ou perdida "
			  + $"(mesa {game.Plays.Count} + mão {game.LocalHand.Length} + monte {game.BoneyardCount})",
			game.Plays.Count + game.LocalHand.Length + game.BoneyardCount == DominoTileId.Count);

		Check("o jogador foi solto quando a partida acabou",
			game.Player.GameHandler.CurrentController == null);

		// A request that lands after the final tile must not restart anything.
		_lastRejection = null;
		resolver.RequestDrawTile(game.TurnToken);
		Check($"ação após o fim da partida é recusada ({_lastRejection})",
			_lastRejection == "match_not_running");
	}

	private static int FindTileNotInHand(DominoGame game)
	{
		for (var tileId = 0; tileId < DominoTileId.Count; tileId++)
		{
			if (System.Array.IndexOf(game.LocalHand, tileId) < 0)
				return tileId;
		}

		return 0;
	}

	private Player BuildPlayer(string playerId)
	{
		var container = new Node3D { Name = "PlayersContainer" };
		AddChild(container);
		PlayerRegistry.Instance.PlayersContainer = container;

		var player = GD.Load<PackedScene>("res://Features/Player/Player.tscn").Instantiate<Player>();
		player.Name = playerId;
		player.Id = int.Parse(playerId);

		// Player logs one error here: TvShareButton.Initialize dereferences TvScreen without a
		// guard, and only the level ever supplies one. Everything this test measures is set up
		// before that point, so the run is unaffected.
		container.AddChild(player);

		return player;
	}

	private Table BuildTable()
	{
		var table = GD.Load<PackedScene>("res://Core/Table/Table.tscn").Instantiate<Table>();

		// No peer in a headless test, so the turn bridge would have nothing to broadcast to.
		table.EnableNetworkTurnSyncronization = false;
		table.TableGameScene = GD.Load<PackedScene>("res://Features/Games/Domino/Domino.tscn");

		AddChild(table);
		return table;
	}

	private void OnMatchOver(string winner, Dictionary context)
	{
		_matchOver = true;
		_winner = winner;
	}

	private void OnActionRejected(string reason)
	{
		_lastRejection = reason;
	}

	private void Finish()
	{
		GD.Print($"=== {_passed} passaram, {_failed} falharam ===");
		if (_failed > 0)
			GD.PushWarning($"{_failed} verificação(ões) de partida falharam.");

		GetTree().Quit(_failed > 0 ? 1 : 0);
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
