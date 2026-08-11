using System.Collections.Generic;
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

        // The real path hands the table a camera; without one the controller rightly complains that
        // seating the player would not move their view.
        var camera = new GlobalCamera();
        AddChild(camera);
        game.SetCamera(camera);

        game.AllowSoloDebug = true;
        // The scene may temporarily request a larger hand for presentation checks. This test pins the
        // official match flow, so keep that visual-only override out of it.
        game.DebugStartingHandSize = 0;
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

        // THE STOCK SECRECY PIN. Right after the deal the public places must be exactly 0..N-1.
        // If anyone ever swaps places for tile ids here, this breaks for essentially any shuffle —
        // which is the point: it is the one line standing between "pick a face-down tile" and
        // "every peer knows the whole stock".
        var placesInOrder = true;
        for (var i = 0; i < game.BoneyardSlots.Length; i++)
        {
            if (game.BoneyardSlots[i] != i)
                placesInOrder = false;
        }

        Check($"o monte é publicado como lugares 0..N-1, nunca como peças "
              + $"([{string.Join(",", game.BoneyardSlots)}])", placesInOrder);
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
        Check("a física do personagem fica desligada enquanto ele está sentado",
            player.IsInSeatedGameMode && !player.IsPhysicsProcessing());

        Check("o controlador equipado é o de dominó",
            player.GameHandler.CurrentController is DominoController);

        var controller = player.GameHandler.CurrentController as DominoController;
        Check("E pode alternar entre a cadeira e o controle livre",
            controller is { AllowsControlSwitch: true, CanTakeControl: true });

        if (controller == null || seat == null)
            return;

        var standExit = seat.GetNodeOrNull<Marker3D>("StandExit");
        Check("a pose autoritativa sentada vem do assento atribuído",
            controller.TryGetAuthoritativePose(
                seated: true, out var seatedPosition, out var seatedYaw)
            && seatedPosition.IsEqualApprox(seat.GlobalPosition)
            && Mathf.IsZeroApprox(Mathf.AngleDifference(seatedYaw, seat.GlobalRotation.Y)));
        Check("a pose autoritativa de saída permanece no cache confiável do assento",
            standExit != null
            && controller.TryGetAuthoritativePose(
                seated: false, out var standingPosition, out var standingYaw)
            && standingPosition.IsEqualApprox(standExit.GlobalPosition)
            && Mathf.IsZeroApprox(Mathf.AngleDifference(
                standingYaw, standExit.GlobalRotation.Y)));

        controller.GiveControl();
        player.TakeControl();
        Check("levantar restaura movimento, física e colisão do personagem",
            player.CurrentControlState == Player.ControllerStatesEnum.Player
            && !player.IsInSeatedGameMode && player.IsPhysicsProcessing());
        Check("o jogador levanta fora da colisão da cadeira",
            standExit != null && player.GlobalPosition.DistanceTo(standExit.GlobalPosition) < 1e-3f);

        player.GiveControl();
        controller.TakeControl();
        Check("pressionar E novamente devolve o jogador ao mesmo assento",
            player.IsInSeatedGameMode && !player.IsPhysicsProcessing()
            && player.GlobalPosition.DistanceTo(seat.GlobalPosition) < 1e-3f);
    }

    private void TestRejections(DominoGame game, DominoTurnResolver resolver)
    {
        int beforePlays;
        int beforeToken;

        // The opener leads a forced tile, so what is left may legitimately not fit either end.
        // Draw up to a playable hand first — the checks below need a legal move to exist.
        // A place that never existed is refused as malformed regardless of whether the player has a
        // move — the structural check runs first, so this does not depend on the shuffle.
        _lastRejection = null;
        resolver.RequestDrawTile(game.TurnToken, 9999);
        Check($"comprar um lugar que não existe é recusado ({_lastRejection})",
            _lastRejection == "invalid_slot");

        var guard = 0;
        var drewSomething = false;

        while (DominoRules.LegalMoves(game.LocalHand, game.LeftEnd, game.RightEnd).Count == 0
               && game.BoneyardCount > 0
               && guard++ < DominoTileId.Count)
        {
            // Picking from the middle rather than the top proves the stock is addressed by place.
            var chosen = game.BoneyardSlots[game.BoneyardSlots.Length / 2];
            var handBefore = game.LocalHand.Length;

            resolver.RequestDrawTile(game.TurnToken, chosen);
            drewSomething = true;

            Check($"comprar o lugar {chosen} tira exatamente aquele lugar do monte",
                game.LocalHand.Length == handBefore + 1
                && System.Array.IndexOf(game.BoneyardSlots, chosen) < 0);

            // The gap it left must not be a target either — that tile is gone.
            _lastRejection = null;
            resolver.RequestDrawTile(game.TurnToken, chosen);
            Check($"comprar o lugar {chosen}, agora vazio, é recusado ({_lastRejection})",
                _lastRejection == "invalid_slot");
        }

        if (!drewSomething)
            GD.Print("  (a mão inicial já tinha jogada; compra por lugar coberta no laço da partida)");

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
        resolver.RequestDrawTile(game.TurnToken, game.BoneyardSlots[0]);
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
                resolver.RequestDrawTile(game.TurnToken, game.BoneyardSlots[^1]);
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

        // Places are never recycled while occupied, so what is left must still be a set of distinct
        // places — a duplicate would mean two face-down tiles stacked on one spot.
        var distinctPlaces = new HashSet<int>(game.BoneyardSlots).Count;
        Check($"os lugares restantes do monte continuam distintos ({distinctPlaces}/{game.BoneyardCount})",
            distinctPlaces == game.BoneyardCount);

        Check("o jogador foi solto quando a partida acabou",
            game.Player.GameHandler.CurrentController == null
            && !game.Player.IsInSeatedGameMode
            && game.Player.IsPhysicsProcessing());

        // A request that lands after the final tile must not restart anything.
        _lastRejection = null;
        resolver.RequestDrawTile(game.TurnToken, 0);
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

        var player = GD.Load<PackedScene>("res://World/Player/Player.tscn").Instantiate<Player>();
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
        var table = GD.Load<PackedScene>("res://Shared/Table/Table.tscn").Instantiate<Table>();

        // No peer in a headless test, so the turn bridge would have nothing to broadcast to.
        table.EnableNetworkTurnSynchronization = false;
        table.TableGameScene = GD.Load<PackedScene>("res://Games/Domino/Domino.tscn");

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
