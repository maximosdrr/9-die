using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;
using Poker.Rules;

/// <summary>
/// Plays a whole SESSION through the real thing: a real Table spawning a real Poker scene and every
/// action going through PokerTurnResolver's server path, hand after hand until one player holds
/// every chip.
///
/// The rules tests prove the rules; this proves the wiring — that a deal reaches a hand, that a
/// context survives being packed and unpacked, that the turn stamp rejects a stale request, and
/// that the button, the blinds and the pots keep the money straight across dozens of hands.
///
/// The check it exists for is CHIP CONSERVATION. It is asserted after every single action, not just
/// at the end, because a session that leaks a chip per hand looks completely normal while it is
/// happening and is unrecoverable by the time anyone notices.
/// </summary>
public partial class PokerMatchTest : Node
{
    private const int Seats = 2;
    private const int Stack = 60;

    private int _passed;
    private int _failed;

    private string _lastRejection;
    private string _winner;
    private bool _matchOver;

    private int _conservationBreaks;
    private int _worstTotal;

    /// <summary>
    /// Every action code the table was told about, in order, one entry per action.
    ///
    /// The gesture and the sound a peer plays are read off this and nothing else, so a code that is
    /// never published is an action nobody outside the server ever sees.
    /// </summary>
    private readonly List<string> _actionsPublished = new();

    private PokerGame _game;
    private int _lastSeqSeen = -1;
    private readonly HashSet<ulong> _heldCardIds = new();

    public override void _Ready()
    {
        GD.Print("=== Teste de sessão de poker ===");

        var players = BuildPlayers("1", "2");
        var table = BuildTable();
        var game = table.CurrentTableGame as PokerGame;

        if (game == null)
        {
            Check("a mesa instanciou um jogo de poker", false);
            Finish();
            return;
        }

        var camera = new GlobalCamera();
        AddChild(camera);
        game.SetCamera(camera);

        game.AllowSoloDebug = true;
        game.StartingStack = Stack;
        game.SmallBlind = 5;
        game.BigBlind = 10;
        game.BlindIncreaseEveryHands = 6;

        _game = game;
        SignalUtil.ConnectGuarded(game, PokerGame.SignalName.HudStateUpdated,
            new Callable(this, MethodName.OnPublicStateChanged));

        var order = new Array { "1", "2" };
        game.TurnOrder = order;
        game.TurnOwner = players["1"];
        game.Player = players["1"];
        game.MatchOver += OnMatchOver;

        var resolver = game.Resolver;
        Check("o jogo tem um resolvedor de poker ligado pelo GameModeHandler", resolver != null);
        if (resolver == null)
        {
            Finish();
            return;
        }

        // No clock in a headless run: zero makes each hand deal straight into the next.
        resolver.ShowdownSeconds = 0.0f;
        resolver.FoldedHandSeconds = 0.0f;

        SignalUtil.ConnectGuarded(resolver, SecretHandTurnResolver.SignalName.ActionRejected,
            new Callable(this, MethodName.OnActionRejected));

        _worstTotal = Seats * Stack;
        game.SetupMatch(order, "1");

        TestDeal(game);
        TestPickUpGesture(game);
        TestFoldThrowsTheCards(game);
        TestSecrecy(game);
        TestRejections(game, resolver);
        TestClosingCallPresentation(game, resolver);

        // The focused animation scenario deliberately advanced the real first hand. Restart the same
        // real session fixture so the long-running coverage keeps its original deterministic path.
        _actionsPublished.Clear();
        _lastSeqSeen = -1;
        _winner = null;
        _matchOver = false;
        resolver.AutoAdvanceHands = true;
        resolver.ShowdownSeconds = 0.0f;
        game.SetupMatch(order, "1");

        TestSessionRunsToTheEnd(game, resolver);
        TestEveryActionIsPublished();
        TestSecondSessionIsPlayable(game, resolver, order);

        Finish();
    }

    // ---------------------------------------------------------------- the deal

    private void TestDeal(PokerGame game)
    {
        Check($"o jogador local recebeu duas cartas ({game.LocalHoleCards.Length})",
            game.LocalHoleCards.Length == PokerDeal.HoleCardCount);
        Check("as duas cartas são válidas e distintas",
            game.LocalHoleCards.All(CardId.IsValid)
            && game.LocalHoleCards.Distinct().Count() == game.LocalHoleCards.Length);

        Check($"a mesa começa sem cartas comunitárias ({game.Board.Length})", game.Board.Length == 0);
        Check($"a mão começa no pré-flop ({game.Street})", game.Street == PokerStreet.Preflop);
        Check($"a sessão está na primeira mão ({game.HandNumber})", game.HandNumber == 1);

        Check($"os blinds foram pagos: o pote vale {game.PotTotal}",
            game.PotTotal == game.ActiveSmallBlind + game.ActiveBigBlind);
        Check($"a aposta corrente é o big blind ({game.CurrentBet})",
            game.CurrentBet == game.ActiveBigBlind);

        // Heads-up: the button posts the SMALL blind and speaks first before the flop.
        var buttonPlayer = game.SeatOrder[game.ButtonSeat];
        Check($"mão a mão, o botão pagou o small blind ({buttonPlayer} com {game.BetOf(buttonPlayer)})",
            game.BetOf(buttonPlayer) == game.ActiveSmallBlind);
        Check($"mão a mão, o botão fala primeiro ({game.TurnOwnerId})",
            game.TurnOwnerId == buttonPlayer);

        Check($"o servidor carimbou a vez ({game.TurnToken})", game.TurnToken > 0);
        Check($"ninguém está all-in nem desistiu na largada",
            game.Folded.Count == 0 && game.AllIn.Count == 0);

        // The chips a player has bet THIS street sit in front of them, not in the middle. Drawing the
        // hand's total in the middle as well put every one of those chips on the table twice.
        Check($"o meio da mesa não repete as fichas que estão na frente dos jogadores "
              + $"(pote {game.PotTotal}, no meio {game.PotInMiddle})",
            game.PotInMiddle == 0);

        CheckConservation(game, "logo após a distribuição");
    }

    /// <summary>
    /// The opening look has to run BY ITSELF and has to finish.
    ///
    /// Every action is gated behind it, so a look that never completes is a dead table — which is
    /// how it failed in a two-peer run when the gesture was manual and depended on the mouse being
    /// captured. Driven here by ticking the real view and the real presenter, with no input at all:
    /// if this needs a button press to pass, it has regressed to the thing that broke.
    /// </summary>
    private void TestPickUpGesture(PokerGame game)
    {
        var controller = game.Player?.GameHandler.CurrentController as PokerController;
        Check("o jogador local recebeu um controlador de poker", controller != null);

        var view = controller?.GetNodeOrNull<PokerHand3DView>("HandView");
        Check("o controlador criou a mão em 3D", view != null);
        Check("o jogo aponta para o apresentador de assentos", game.SeatPresenter != null);

        if (view == null || game.SeatPresenter == null)
            return;

        Check("a mão começa com as cartas na mesa", !view.HasPickedUpCards && !game.LocalPickedUpCards);

        // No input whatsoever: the look is a cutscene, not a gesture.
        var frames = 0;
        var leftTheCloth = -1;

        for (; frames < 600 && !view.HasPickedUpCards; frames++)
        {
            game.SeatPresenter._Process(1.0 / 60.0);
            view._Process(1.0 / 60.0);

            if (leftTheCloth < 0 && game.LocalPickedUpCards)
                leftTheCloth = frames;
        }

        Check($"a olhada acontece sozinha e termina ({frames} quadros)", view.HasPickedUpCards);
        Check($"as cartas saem da mesa antes de a olhada acabar ({leftTheCloth} quadros)",
            leftTheCloth >= 0 && leftTheCloth < frames);
        Check($"e voltam abaixadas ao terminar (espiada {view.PeekAmount:F2})",
            view.PeekAmount < 0.05f);

        var heldCards = view.GetNode<Node3D>("HandRig/Hand/CardSlots").GetChildren()
            .OfType<PokerCard>().ToList();
        _heldCardIds.Clear();
        foreach (var card in heldCards)
            _heldCardIds.Add(card.GetInstanceId());
        Check("a mão segura as duas instâncias que vieram da mesa",
            _heldCardIds.Count == PokerDeal.HoleCardCount);
        Check($"as cartas já chegam à mão com suas faces configuradas "
            + $"({string.Join(",", heldCards.Select(card => card.CardId))} de "
            + $"{string.Join(",", game.LocalHoleCards)})",
            heldCards.Select(card => card.CardId).OrderBy(id => id)
                .SequenceEqual(game.LocalHoleCards.OrderBy(id => id)));

        // The presenter has to stop drawing the pair once it is in hand, which needs no signal —
        // this is the check that catches it silently leaving them on the cloth.
        var stillOnCloth = 0;
        foreach (var child in game.SeatPresenter.GetChildren())
        {
            if (child is PokerCard { Visible: true })
                stillOnCloth++;
        }

        Check($"a mesa não fica com as cartas de quem já as pegou ({stillOnCloth} visíveis)",
            stillOnCloth == 0);
    }

    /// <summary>
    /// Giving up THROWS the cards. They go into the middle and stay there, face down, for the rest of
    /// the hand — they do not simply stop being drawn.
    ///
    /// Driven straight at the public state the presenter reads rather than through a real fold: heads
    /// up, folding ends the hand at once and the muck would be swept before anyone could look at it.
    /// What is under test is the presenter, and this is exactly what a fold hands it.
    /// </summary>
    private void TestFoldThrowsTheCards(PokerGame game)
    {
        var presenter = game.SeatPresenter;
        var board = game.BoardPresenter;

        if (presenter == null || board == null || game.Player == null)
        {
            Check("a mesa tem apresentadores para desenhar o descarte", false);
            return;
        }

        var me = (string)game.Player.Name;

        // Through the signal the server's context would raise, not by poking the presenter: the hand
        // in front of the eye and the cards on the cloth are two different listeners, and a fold has
        // to reach both.
        game.Folded.Add(me);
        game.EmitSignal(PokerGame.SignalName.HudStateUpdated);

        for (var frame = 0; frame < 120; frame++)
            presenter._Process(1.0 / 60.0);

        var thrown = presenter.GetChildren()
            .OfType<PokerCard>()
            .Where(card => card.Visible)
            .ToList();

        Check($"desistir devolve as duas cartas para a mesa ({thrown.Count} visíveis)",
            thrown.Count == PokerDeal.HoleCardCount);
        Check("o descarte usa exatamente as mesmas cartas que estavam na mão",
            _heldCardIds.Count == PokerDeal.HoleCardCount
            && _heldCardIds.SetEquals(thrown.Select(card => card.GetInstanceId())));

        var muck = board.MuckPosition;
        var worst = thrown.Count == 0
            ? float.MaxValue
            : thrown.Max(card => new Vector2(
                card.Position.X - muck.X, card.Position.Z - muck.Z).Length());

        Check($"e elas param no descarte, longe do assento ({worst * 100.0f:F1} cm do ponto)",
            worst < 0.10f);

        Check("e ficam viradas para baixo", thrown.All(card => card.IsFaceDown));

        // The hand in front of the eye has to let go at the same moment, or the player is left
        // holding cards they just gave up.
        var view = game.Player.GameHandler.CurrentController?.GetNodeOrNull<PokerHand3DView>("HandView");
        var held = view == null
            ? -1
            : view.GetNode<Node3D>("HandRig/Hand/CardSlots").GetChildren()
                .OfType<PokerCard>().Count(card => card.Visible);

        Check($"e a mão de quem desistiu fica vazia ({held} cartas)", held == 0);

        // Put it back: the next context from the server rebuilds this set anyway, and nothing after
        // this point should inherit a fold that never happened.
        game.Folded.Remove(me);
        game.EmitSignal(PokerGame.SignalName.HudStateUpdated);
    }

    /// <summary>
    /// A call which closes the pre-flop arrives in two public snapshots: first as the action, then as
    /// the newly opened flop. The visual event must survive the second snapshot, land, be swept using
    /// the same chip nodes, and only then let the community cards turn.
    /// </summary>
    private void TestClosingCallPresentation(PokerGame game, PokerTurnResolver resolver)
    {
        var presenter = game.SeatPresenter;
        var board = game.BoardPresenter;
        if (presenter == null || board == null)
        {
            Check("a apresentação física da aposta está disponível", false);
            return;
        }
        resolver.AutoAdvanceHands = false;
        var chipAnimator = presenter.GetNodeOrNull<PokerChipAnimator>("ChipAnimator");
        var landingEvents = 0;
        var landedChips = 0;
        chipAnimator.ChipsLanded += (_, count) =>
        {
            landingEvents++;
            landedChips += count;
        };
        var warmedBatchSizes = chipAnimator.Batches.Select(batch => batch.Pile)
            .ToDictionary(pile => pile.Name.ToString(), pile => pile.GetChildCount());

        var raiser = game.TurnOwnerId;
        var raiseTotal = game.CurrentBet + game.MinRaiseIncrement;
        resolver.ApplyActionFor(raiser, game.TurnToken, (int)PokerActionKind.Raise, raiseTotal);
        AdvancePresentation(presenter, board, 180, stopWhenReady: true);
        Check("as fichas apostadas permanecem soltas antes da coleta",
            presenter.BetsAreVisuallyLoose);

        var caller = game.TurnOwnerId;
        var displayedBeforeCall = presenter.DisplayedStackOf(caller);
        var bankBeforeCall = presenter.StackChipTransforms(caller);
        var bankPositionsBeforeCall = presenter.StackChipPositions(caller);
        var activeBeforeCall = presenter.ActiveChipVisualPositions();
        resolver.ApplyActionFor(caller, game.TurnToken, (int)PokerActionKind.Call, game.CurrentBet);
        var authoritativeAfterCall = game.StackOf(caller);
        Check("a pilha nao perde fichas um quadro antes do lancamento",
            presenter.DisplayedStackOf(caller) == displayedBeforeCall
            && authoritativeAfterCall < displayedBeforeCall);
        presenter._Process(1.0 / 60.0);
        board._Process(1.0 / 60.0);
        Check("a pilha e o lote lancado mudam no mesmo quadro",
            presenter.DisplayedStackOf(caller) == authoritativeAfterCall);
        Check("o primeiro quadro do pagamento fica na origem, nunca no destino",
            presenter.NewlyStartedBatchesAreAtTheirOrigin);
        var bankAfterCall = presenter.StackChipTransforms(caller);
        var bankPositionsAfterCall = presenter.StackChipPositions(caller);
        Check("o pagamento apenas remove fichas da pilha visual",
            bankAfterCall.Count < bankBeforeCall.Count
            && bankAfterCall.Keys.All(bankBeforeCall.ContainsKey));
        Check("as fichas que ficaram nao pulam nem trocam de instancia",
            bankAfterCall.All(entry => bankBeforeCall.TryGetValue(entry.Key, out var before)
                && before.IsEqualApprox(entry.Value)));
        var removedPositions = bankPositionsBeforeCall
            .Where(entry => !bankPositionsAfterCall.ContainsKey(entry.Key))
            .Select(entry => entry.Value).ToList();
        var newMovingPositions = presenter.ActiveChipVisualPositions()
            .Where(entry => !activeBeforeCall.ContainsKey(entry.Key))
            .Select(entry => entry.Value).ToList();
        var takeoffGap = removedPositions.Count == 0 || newMovingPositions.Count == 0
            ? float.MaxValue
            : removedPositions.Min(removed => newMovingPositions.Min(moving => removed.DistanceTo(moving)));
        var worstTakeoffGap = removedPositions.Count == 0 || newMovingPositions.Count == 0
            ? float.MaxValue
            : newMovingPositions.Max(moving => removedPositions.Min(removed => removed.DistanceTo(moving)));
        Check($"a ficha movel nasce na ficha retirada ({takeoffGap * 100.0f:F2} cm de diferenca)",
            takeoffGap < 0.012f && worstTakeoffGap < 0.005f);

        var inFlightIds = presenter.ActiveChipVisualIds().ToHashSet();
        Check("nenhuma ficha e instanciada durante o pagamento",
            chipAnimator.Batches.Select(batch => batch.Pile)
                .All(pile => warmedBatchSizes.GetValueOrDefault(pile.Name.ToString())
                    == pile.GetChildCount()));
        Check("o call que fecha a rodada não é engolido",
            inFlightIds.Count > 0 && !presenter.PresentationReadyForAction);
        Check("o lançamento não disputa com uma segunda queda interna",
            chipAnimator.Batches.Select(batch => batch.Pile)
                .All(pile => Mathf.IsZeroApprox(pile.SettleSeconds)));
        Check("o flop espera as fichas pousarem e serem recolhidas",
            game.Street == PokerStreet.Flop && board.VisibleFaceUpCount == 0);

        var previousPositions = presenter.ActiveChipVisualPositions();
        var maximumFrameStep = 0.0f;
        var sawLooseOrganization = false;
        for (var frame = 0; frame < 600; frame++)
        {
            presenter._Process(1.0 / 60.0);
            board._Process(1.0 / 60.0);
            var currentPositions = presenter.ActiveChipVisualPositions();
            foreach (var entry in currentPositions)
            {
                if (previousPositions.TryGetValue(entry.Key, out var before))
                    maximumFrameStep = Mathf.Max(maximumFrameStep, before.DistanceTo(entry.Value));
            }
            previousPositions = currentPositions;
            sawLooseOrganization |= presenter.PotIsLooseWhileOrganizing;
            if (presenter.PresentationReadyForAction)
                break;
        }
        var potIds = presenter.ActiveChipVisualIds().ToHashSet();
        Check($"nenhuma ficha salta entre quadros ({maximumFrameStep * 100.0f:F2} cm no pior quadro)",
            maximumFrameStep < 0.025f);
        Check("o pote chega solto antes de ser organizado", sawLooseOrganization);
        Check("a organização termina em colunas compactas", presenter.PotIsOrganizedTower);
        Check("as mesmas fichas chegam ao pote sem troca de instância",
            inFlightIds.Count > 0 && inFlightIds.SetEquals(potIds));
        Check($"o áudio acompanha impactos físicos reais ({landingEvents} eventos, {landedChips} fichas)",
            landingEvents > 0 && landedChips >= inFlightIds.Count);
        Check("o flop só aparece depois de o pote terminar a organização",
            presenter.PresentationReadyForAction && board.VisibleFaceUpCount == PokerDeal.BoardSize(PokerStreet.Flop));

        // Simulate a reconnect/new public snapshot while a newly accepted action is still queued. The
        // client must not replay that stale local event over the authoritative bets it just received.
        var recoveryActor = game.TurnOwnerId;
        var recoveryOptions = PokerBetting.LegalActions(
            game.BetStateOf(recoveryActor), game.CurrentBet, game.MinRaiseIncrement);
        var recoveryRaise = recoveryOptions.FirstOrDefault(option => option.Kind == PokerActionKind.Raise);
        if (recoveryRaise.Kind == PokerActionKind.Raise)
            resolver.ApplyActionFor(recoveryActor, game.TurnToken,
                (int)recoveryRaise.Kind, recoveryRaise.MinTotal);
        presenter.SnapToAuthoritativeState();
        Check("uma reconexao descarta a animacao local que ficou pendente",
            recoveryRaise.Kind != PokerActionKind.Raise || presenter.LastRecoveryDiscardedAnimation);
        Check("a reconexao reaparece diretamente no estado publico atual",
            presenter.PresentationReadyForAction
            && board.VisibleFaceUpCount == PokerDeal.BoardSize(game.Street)
            && presenter.ActiveChipGroupCount <= presenter.MaxAnimatedChipGroups);

        var safety = 0;
        while (!game.ShowdownWaiting && !game.HandSettled && safety++ < 20)
        {
            var actor = game.TurnOwnerId;
            var options = PokerBetting.LegalActions(
                game.BetStateOf(actor), game.CurrentBet, game.MinRaiseIncrement);
            var action = options.FirstOrDefault(option => option.Kind == PokerActionKind.Check);
            if (action.Kind == PokerActionKind.None)
                action = options.First(option => option.Kind == PokerActionKind.Call);

            resolver.ApplyActionFor(actor, game.TurnToken, (int)action.Kind, action.MinTotal);
            AdvancePresentation(presenter, board, 600, stopWhenReady: true);
        }

        Check("o showdown abre uma decisão antes de revelar ou pagar",
            game.ShowdownWaiting && game.Winners.Count == 0
            && game.RevealedHoleCards.Count == 0 && game.PendingShowdownReveals.Count > 1);

        var revealOrder = game.PendingShowdownReveals.ToArray();
        resolver.ApplyShowdownRevealFor(revealOrder[0]);
        Check("cada jogador revela somente a própria mão quando pressiona S",
            game.ShowdownWaiting && game.RevealedHoleCards.Count == 1
            && !game.PendingShowdownReveals.Contains(revealOrder[0]));

        resolver.AdvanceShowdownRevealClock(9.9f);
        Check("os primeiros dez segundos não poluem a interface com contagem",
            game.ShowdownCountdown < 0);
        resolver.AdvanceShowdownRevealClock(0.2f);
        Check("depois da tolerância começa uma contagem regressiva de dez segundos",
            game.ShowdownCountdown == 10);

        resolver.AdvanceShowdownRevealClock(10.0f);
        Check("quem não decide a tempo é revelado automaticamente e o showdown é pago",
            game.HandSettled && game.PendingShowdownReveals.Count == 0
            && game.RevealedHoleCards.Count == revealOrder.Length);

        var revealSafety = 0;
        var sawRemoteRevealMotion = false;
        while (presenter.ShowdownRevealHoldElapsed <= 0.0f && revealSafety++ < 300)
        {
            presenter._Process(1.0 / 60.0);
            board._Process(1.0 / 60.0);
            sawRemoteRevealMotion |= presenter.RemoteRevealedHandsInMotion > 0;
        }
        Check("a mão remota viaja das mãos estimadas até a mesa no showdown",
            sawRemoteRevealMotion);
        Check("as mãos reveladas permanecem na mesa antes do ranking",
            presenter.ShowdownRevealHoldElapsed > 0.0f
            && presenter.ShowdownDisplayedCardCount == 0);

        var holdFrames = Mathf.Max(0, Mathf.FloorToInt(
            (presenter.ShowdownRevealHoldSeconds - presenter.ShowdownRevealHoldElapsed) * 60.0f) - 2);
        for (var frame = 0; frame < holdFrames; frame++)
        {
            presenter._Process(1.0 / 60.0);
            board._Process(1.0 / 60.0);
        }
        Check("o intervalo de leitura não é cortado antes do tempo configurado",
            presenter.ShowdownDisplayedCardCount == 0);

        for (var frame = 0; frame < 900 && !presenter.ShowdownPresentationSettled; frame++)
        {
            presenter._Process(1.0 / 60.0);
            board._Process(1.0 / 60.0);
        }

        Check("o showdown monta cinco cartas para cada mão revelada",
            presenter.ShowdownPresentationSettled
            && presenter.ShowdownDisplayedCardCount == game.RevealedHoleCards.Count * 5);

        var ordered = presenter.ShowdownDisplayOrder.ToList();
        var bestFirst = true;
        for (var i = 1; i < ordered.Count; i++)
        {
            var before = PokerHandEvaluator.Evaluate(game.RevealedHoleCards[ordered[i - 1]], game.Board);
            var after = PokerHandEvaluator.Evaluate(game.RevealedHoleCards[ordered[i]], game.Board);
            if (after > before)
                bestFirst = false;
        }
        Check("as mãos aparecem da melhor para a pior", ordered.Count > 0 && bestFirst);
        Check("todo vencedor fica identificado pela melhor sequência mostrada",
            game.Winners.Keys.All(winner => ordered.Contains(winner)));
        Check("o pagamento nao comeca no mesmo instante em que o ranking termina",
            !presenter.PayoutStarted);
        var rankingReadFrames = Mathf.Max(0, Mathf.FloorToInt(
            presenter.PresentationProfile.RankedHandsReadingSeconds * 60.0f) - 2);
        for (var frame = 0; frame < rankingReadFrames; frame++)
        {
            presenter._Process(1.0 / 60.0);
            board._Process(1.0 / 60.0);
        }
        Check("o ranking permanece sozinho na mesa durante o tempo de leitura",
            !presenter.PayoutStarted);

        // Force one payout topology that the current denominations cannot express. This exercises the
        // complete pot -> dealer -> exact replacement -> winners path, not only the pure planner.
        var potGroups = presenter.PotPhysicalGroupValues();
        var potValue = potGroups.Sum();
        var forcedPlayers = game.SeatOrder.Take(2).ToArray();
        var forcedAwards = new System.Collections.Generic.Dictionary<string, int>();
        for (var first = 1; first < potValue && forcedPlayers.Length == 2; first++)
        {
            var candidate = new System.Collections.Generic.Dictionary<string, int>
            {
                [forcedPlayers[0]] = first,
                [forcedPlayers[1]] = potValue - first,
            };
            if (!PokerPayoutPlanner.Create(potGroups, candidate, game.SeatOrder).RequiresDealerChange)
                continue;
            forcedAwards = candidate;
            break;
        }
        if (forcedAwards.Count == 2)
        {
            game.Winners.Clear();
            foreach (var award in forcedAwards)
                game.Winners[award.Key] = award.Value;
        }

        var payoutIds = presenter.ActiveChipVisualIds().ToHashSet();
        Check($"o pote organizado separa as denominações ({presenter.PotDenominationColumnCount} colunas)",
            presenter.PotDenominationColumnCount >= 2);
        var payoutPrevious = presenter.ActiveChipVisualPositions();
        var payoutMaximumStep = 0.0f;
        var sawLoosePayout = false;
        var sawWinnerOrganization = false;
        for (var frame = 0; frame < 900 && !presenter.PayoutCompleted; frame++)
        {
            presenter._Process(1.0 / 60.0);
            board._Process(1.0 / 60.0);
            sawLoosePayout |= presenter.PayoutHasLooseDelivery;
            sawWinnerOrganization |= presenter.WinnerOrganizationInProgress;
            var current = presenter.ActiveChipVisualPositions();
            foreach (var entry in current)
            {
                if (payoutPrevious.TryGetValue(entry.Key, out var before))
                    payoutMaximumStep = Mathf.Max(
                        payoutMaximumStep, before.DistanceTo(entry.Value));
            }
            payoutPrevious = current;
        }

        Check("o pote é transferido fisicamente antes de encerrar a apresentação",
            presenter.PayoutStarted && presenter.PayoutCompleted
            && presenter.ChipsDeliveredToWinners > 0);
        Check("todo jogador premiado recebe fichas visíveis",
            presenter.PayoutRecipientCount == game.Winners.Count);
        Check("as fichas chegam soltas antes de serem empilhadas", sawLoosePayout);
        Check("a pilha dos vencedores só se forma depois da entrega",
            sawWinnerOrganization);
        Check("o pote percorre o ponto do dealer quando precisa de troco",
            forcedAwards.Count != 2 || presenter.DealerChangeCompleted);
        var splitPlan = PokerPayoutPlanner.Create(
            new[] { 25, 25, 25 },
            new System.Collections.Generic.Dictionary<string, int> { ["A"] = 38, ["B"] = 37 },
            new[] { "A", "B" });
        Check("um empate impossivel com as fichas atuais solicita troco do dealer",
            splitPlan.RequiresDealerChange);
        Check("o troco monta fisicamente premios exatos de 38 e 37",
            splitPlan.Winners.All(winner =>
                PokerChipStack.Total(winner.ExactRuns) == winner.Amount));
        Check("o valor fisico entregue coincide com cada premio publicado",
            game.Winners.All(winner =>
                presenter.PayoutPhysicalValueOf(winner.Key) == winner.Value));
        Check("a transferência preserva as mesmas instâncias das fichas",
            presenter.DealerChangeCompleted || payoutIds.SetEquals(presenter.ActiveChipVisualIds()));
        Check($"o pagamento também não pisca entre quadros ({payoutMaximumStep * 100.0f:F2} cm)",
            payoutMaximumStep < 0.025f);
        Check("o saldo visual só alcança o resultado depois que as fichas chegam",
            game.Winners.Keys.All(winner => presenter.DisplayedStackOf(winner) == game.StackOf(winner)));

        game.CardsCleaningUp = true;
        presenter.Refresh();
        Check("a troca de mão recolhe as cartas visíveis sem recriá-las",
            presenter.CardsReturningToDeck && board.CardCleanupActive
            && presenter.ReturningCardCount > 0);
        var cleanupFrames = Mathf.CeilToInt(
            presenter.PresentationProfile.CardCleanupDuration * 60.0f) + 2;
        var sawGatherHold = false;
        var sawDetailedShuffle = false;
        for (var frame = 0; frame < cleanupFrames; frame++)
        {
            presenter._Process(1.0 / 60.0);
            board._Process(1.0 / 60.0);
            sawGatherHold |= board.DeckGatherHoldInProgress
                && presenter.ReturningCardCount > 0;
            sawDetailedShuffle |= board.DeckShuffleInProgress
                && board.DeckCardSpread >= board.ShuffleSplitDistance;
        }
        Check("as cartas repousam juntas sobre o maço antes do embaralhamento", sawGatherHold);
        Check("o maço se divide fisicamente antes de ser intercalado", sawDetailedShuffle);
        Check("o embaralhamento termina dentro da janela reservada pelo servidor",
            !board.CardCleanupActive && presenter.ReturningCardCount == 0);
        game.CardsCleaningUp = false;
        presenter.Refresh();
        Check("a apresentação fica pronta para receber a próxima distribuição",
            !presenter.CardsReturningToDeck);

        var interrupted = chipAnimator.Batches.FirstOrDefault(batch =>
            batch.Phase == PokerChipAnimator.Phase.AtWinner);
        if (interrupted != null)
            interrupted.Phase = PokerChipAnimator.Phase.ToWinner;
        presenter.SnapToAuthoritativeState();
        Check("uma reconexao durante o premio cancela a sequencia incompleta",
            interrupted == null || (presenter.LastRecoveryDiscardedAnimation
                && presenter.PayoutCompleted && presenter.ActiveChipGroupCount == 0));
        Check("depois da reconexao o saldo visual coincide com o servidor",
            game.SeatOrder.All(player => presenter.DisplayedStackOf(player) == game.StackOf(player)));

    }

    private static void AdvancePresentation(
        PokerSeatPresenter presenter, PokerBoardPresenter board, int maximumFrames, bool stopWhenReady)
    {
        for (var frame = 0; frame < maximumFrames; frame++)
        {
            presenter._Process(1.0 / 60.0);
            board._Process(1.0 / 60.0);
            if (stopWhenReady && presenter.PresentationReadyForAction)
                break;
        }
    }

    /// <summary>
    /// Nothing about the OTHER player's cards may be reachable from public state — that is the whole
    /// game. Checked against the actual dictionaries a peer holds, not against the protocol.
    /// </summary>
    private void TestSecrecy(PokerGame game)
    {
        Check("nenhuma carta é revelada fora do showdown", game.RevealedHoleCards.Count == 0);

        var publicCardArrays = typeof(PokerGame)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .Where(field => field.FieldType == typeof(int[]))
            .Select(field => field.Name)
            .ToList();

        // Two int arrays are allowed, each for a stated reason:
        //   Board            — the community cards, which are public by definition.
        //   LocalHoleCards   — this peer's OWN cards, which arrived through the targeted channel.
        var allowed = new HashSet<string> { nameof(PokerGame.Board), nameof(PokerGame.LocalHoleCards) };
        var unexpected = publicCardArrays.Where(name => !allowed.Contains(name)).ToList();

        Check($"o estado público não tem onde guardar carta alheia ({string.Join(", ", unexpected)})",
            unexpected.Count == 0);
    }

    // ---------------------------------------------------------------- refusals

    private void TestRejections(PokerGame game, PokerTurnResolver resolver)
    {
        var owner = game.TurnOwnerId;
        var before = Snapshot(game);

        _lastRejection = null;
        resolver.ApplyActionFor(owner, game.TurnToken - 1, (int)PokerActionKind.Call, game.CurrentBet);
        Check($"ação com carimbo vencido é recusada ({_lastRejection})", _lastRejection == "stale_turn");

        _lastRejection = null;
        resolver.ApplyActionFor(owner, game.TurnToken, 99, 0);
        Check($"ação inexistente é recusada ({_lastRejection})", _lastRejection == "invalid_action");

        _lastRejection = null;
        resolver.ApplyActionFor(owner, game.TurnToken, (int)PokerActionKind.Check, 0);
        Check($"passar diante do big blind é recusado ({_lastRejection})",
            _lastRejection == "action_not_available");

        _lastRejection = null;
        resolver.ApplyActionFor(owner, game.TurnToken, (int)PokerActionKind.Raise, 999999);
        Check($"aumentar além do stack é recusado ({_lastRejection})",
            _lastRejection == "amount_out_of_range");

        _lastRejection = null;
        resolver.ApplyActionFor(owner, game.TurnToken, (int)PokerActionKind.Raise, game.CurrentBet + 1);
        Check($"aumentar abaixo do mínimo é recusado ({_lastRejection})",
            _lastRejection == "amount_out_of_range");

        Check("nenhuma recusa mexeu na mesa, no pote nem na vez",
            Snapshot(game) == before && game.TurnOwnerId == owner);
        CheckConservation(game, "depois das recusas");
    }

    // ---------------------------------------------------------------- the whole session

    private void TestSessionRunsToTheEnd(PokerGame game, PokerTurnResolver resolver)
    {
        var actions = 0;
        var handsSeen = new HashSet<int>();
        var streetsSeen = new HashSet<PokerStreet>();
        var raisesMade = 0;
        var foldsMade = 0;
        var boardEverShrank = false;
        var lastBoardLength = game.Board.Length;
        var lastHand = game.HandNumber;

        while (!_matchOver && actions < 4000)
        {
            handsSeen.Add(game.HandNumber);
            streetsSeen.Add(game.Street);

            // The board only ever grows within a hand — a peer that missed a message catches up
            // rather than having to unwind anything.
            if (game.HandNumber == lastHand && game.Board.Length < lastBoardLength)
                boardEverShrank = true;

            lastBoardLength = game.Board.Length;
            lastHand = game.HandNumber;

            var actor = game.TurnOwnerId;
            if (string.IsNullOrEmpty(actor))
                break;

            var state = game.BetStateOf(actor);
            var options = PokerBetting.LegalActions(state, game.CurrentBet, game.MinRaiseIncrement);
            if (options.Count == 0)
                break;

            // A bot that mixes: raise when it can, otherwise call or check, and fold now and then so
            // the uncontested path is exercised too.
            var chosen = PickAction(options, actions, ref raisesMade, ref foldsMade);

            var tokenBefore = game.TurnToken;
            resolver.ApplyActionFor(actor, game.TurnToken, (int)chosen.Kind, chosen.MinTotal);
            RevealAllAtShowdown(game, resolver);
            actions++;

            CheckConservation(game, $"após a ação {actions}");

            if (game.TurnToken == tokenBefore && !_matchOver)
                break;
        }

        Check($"a sessão termina sozinha ({actions} ações, {handsSeen.Count} mãos)", _matchOver);
        Check($"a sessão passou por várias mãos ({handsSeen.Count})", handsSeen.Count >= 2);
        Check($"o jogo chegou depois do pré-flop ({string.Join(", ", streetsSeen)})",
            streetsSeen.Count >= 2);
        Check($"houve aumento e desistência no caminho ({raisesMade} aumentos, {foldsMade} desistências)",
            raisesMade > 0 && foldsMade > 0);
        Check("a mesa comunitária nunca encolheu dentro de uma mão", !boardEverShrank);

        Check($"a sessão tem vencedor ({_winner})", !string.IsNullOrEmpty(_winner));
        Check($"o vencedor ficou com todas as fichas ({game.StackOf(_winner)} de {Seats * Stack})",
            game.StackOf(_winner) == Seats * Stack);
        Check($"todos os outros zeraram",
            game.SeatOrder.Where(id => id != _winner).All(id => game.StackOf(id) == 0));

        Check($"as fichas nunca sumiram nem apareceram ({_conservationBreaks} desvios, "
              + $"pior total {_worstTotal})",
            _conservationBreaks == 0);

        _lastRejection = null;
        resolver.ApplyActionFor("1", game.TurnToken, (int)PokerActionKind.Call, 0);
        Check($"ação depois do fim da sessão é recusada ({_lastRejection})",
            _lastRejection == "match_not_running");
    }

    /// <summary>
    /// Every action a player makes has to reach the table as an action.
    ///
    /// This is the "the sound only plays on the host" report. A gesture and its sound are derived
    /// from the published action code and nothing else, and two paths were throwing that code away
    /// before anybody was told: opening a street replaced it with "street", and settling a hand
    /// replaced it with "won". Between them they silenced whoever acted LAST on a street and every
    /// fold that ended a hand — which, heads-up, is every fold there is.
    ///
    /// One peer here, so this cannot prove delivery across the wire; the contexts checked are the
    /// same ones the bridge mirrors verbatim, and what broke was that they never carried the action
    /// in the first place.
    /// </summary>
    private void TestEveryActionIsPublished()
    {
        var actions = _actionsPublished.Where(code => PokerClips.ForAction(code) != PokerGesture.None)
            .ToList();

        var folds = actions.Count(code => code == "fold");
        var knocks = actions.Count(code => code == "check");
        var throws = actions.Count(code => code is "call" or "raise");

        Check($"as desistências chegaram à mesa como desistências ({folds})", folds > 0);
        Check($"os \"passar\" chegaram à mesa, e não só ao host ({knocks})", knocks > 0);
        Check($"as apostas chegaram à mesa ({throws})", throws > 0);

        Check($"nenhum código de ação virou \"street\" ou \"won\" no caminho "
              + $"({_actionsPublished.Count(code => code is "street")} viraram rua)",
            !_actionsPublished.Contains("street"));

        // The counter is what a peer compares against to fire a gesture exactly once. If it moved
        // without an action, tables would knock on the wood at random.
        Check($"o contador de ações acompanha as ações publicadas "
              + $"({_game.ActionSeq} contra {_actionsPublished.Count} contextos novos)",
            _game.ActionSeq > 0 && _actionsPublished.Count >= _game.ActionSeq);
    }

    /// <summary>Records the action carried by each new context, once per action.</summary>
    private void OnPublicStateChanged()
    {
        if (_game == null || _game.ActionSeq == _lastSeqSeen)
            return;

        _lastSeqSeen = _game.ActionSeq;
        _actionsPublished.Add(_game.LastAction);
    }

    private static ActionOption PickAction(
        IReadOnlyList<ActionOption> options, int step, ref int raises, ref int folds)
    {
        var raise = options.FirstOrDefault(o => o.Kind == PokerActionKind.Raise);
        var fold = options.FirstOrDefault(o => o.Kind == PokerActionKind.Fold);

        // Deterministic on purpose: a session that fails must fail the same way next run.
        if (step % 7 == 3 && fold.Kind == PokerActionKind.Fold)
        {
            folds++;
            return fold;
        }

        if (step % 3 == 0 && raise.Kind == PokerActionKind.Raise)
        {
            raises++;
            return raise;
        }

        return options.First(o => o.Kind is PokerActionKind.Check or PokerActionKind.Call);
    }

    /// <summary>
    /// A table has to be playable again after a session ends.
    ///
    /// It was not. The finished session deliberately leaves its result on the table so the winning
    /// hand can be read, and the reset that runs when a new match is set up did not clear it — so
    /// every peer opened the next session believing a hand was already settled, and NOBODY could
    /// act. It looked exactly like a dead table.
    /// </summary>
    private void TestSecondSessionIsPlayable(PokerGame game, PokerTurnResolver resolver, Array order)
    {
        Check($"a sessão anterior deixou o resultado na mesa ({game.Winners.Count} vencedor(es))",
            game.Winners.Count > 0 && game.HandSettled);

        _matchOver = false;
        _winner = null;
        game.SetupMatch(order, "1");

        Check($"a segunda sessão distribui ({game.LocalHoleCards.Length} cartas, mão {game.HandNumber})",
            game.LocalHoleCards.Length == PokerDeal.HoleCardCount && game.HandNumber == 1);

        Check($"o resultado da sessão anterior não vaza para a nova ({game.Winners.Count})",
            game.Winners.Count == 0 && !game.HandSettled);

        // Stacks plus pot, because the blinds are already down by the time the deal is visible.
        Check($"todo mundo volta com a compra completa "
              + $"({game.StackOf("1")} + {game.StackOf("2")} + pote {game.PotTotal})",
            game.StackOf("1") + game.StackOf("2") + game.PotTotal == Seats * Stack);

        // The check that actually matters: somebody can act.
        var actor = game.TurnOwnerId;
        var options = PokerBetting.LegalActions(
            game.BetStateOf(actor), game.CurrentBet, game.MinRaiseIncrement);

        Check($"há um jogador da vez com ações disponíveis ({actor}: {options.Count})",
            !string.IsNullOrEmpty(actor) && options.Count > 0);

        var tokenBefore = game.TurnToken;
        var call = options.First(o => o.Kind is PokerActionKind.Check or PokerActionKind.Call);
        _lastRejection = null;
        resolver.ApplyActionFor(actor, game.TurnToken, (int)call.Kind, call.MinTotal);

        Check($"e a ação dele é aceita ({_lastRejection ?? "sem recusa"})",
            _lastRejection == null && game.TurnToken != tokenBefore);
    }

    // ---------------------------------------------------------------- conservation

    /// <summary>
    /// Chips in stacks plus chips in the pot must always be exactly what was bought in for. Every
    /// bug in the pot maths shows up here first.
    /// </summary>
    private void CheckConservation(PokerGame game, string moment)
    {
        var total = game.SeatOrder.Sum(id => game.StackOf(id)) + game.PotTotal;
        if (total == Seats * Stack)
            return;

        _conservationBreaks++;
        if (System.Math.Abs(total - Seats * Stack) > System.Math.Abs(_worstTotal - Seats * Stack))
            _worstTotal = total;

        if (_conservationBreaks <= 3)
            GD.Print($"  ---- fichas fora do lugar {moment}: {total} em vez de {Seats * Stack}");
    }

    private static string Snapshot(PokerGame game) =>
        $"{game.PotTotal}|{game.CurrentBet}|{game.Street}|{game.Board.Length}|{game.HandNumber}";

    private static void RevealAllAtShowdown(PokerGame game, PokerTurnResolver resolver)
    {
        if (!game.ShowdownWaiting)
            return;

        foreach (var playerId in game.PendingShowdownReveals.ToArray())
            resolver.ApplyShowdownRevealFor(playerId);
    }

    // ---------------------------------------------------------------- harness

    private System.Collections.Generic.Dictionary<string, Player> BuildPlayers(params string[] ids)
    {
        var container = new Node3D { Name = "PlayersContainer" };
        AddChild(container);
        PlayerRegistry.Instance.PlayersContainer = container;

        var scene = GD.Load<PackedScene>("res://World/Player/Player.tscn");
        var players = new System.Collections.Generic.Dictionary<string, Player>();

        foreach (var id in ids)
        {
            var player = scene.Instantiate<Player>();
            player.Name = id;
            player.Id = int.Parse(id);
            container.AddChild(player);
            players[id] = player;
        }

        return players;
    }

    private Table BuildTable()
    {
        var table = GD.Load<PackedScene>("res://Shared/Table/Table.tscn").Instantiate<Table>();

        // No peer in a headless test, so the turn bridge would have nothing to broadcast to.
        table.EnableNetworkTurnSynchronization = false;
        table.TableGameScene = GD.Load<PackedScene>("res://Games/Poker/Poker.tscn");

        AddChild(table);
        return table;
    }

    private void OnMatchOver(string winner, Dictionary context)
    {
        _matchOver = true;
        _winner = winner;
    }

    private void OnActionRejected(string reason) => _lastRejection = reason;

    private async void Finish()
    {
        GD.Print($"=== {_passed} passaram, {_failed} falharam ===");
        if (_failed > 0)
            GD.PushWarning($"{_failed} verificação(ões) de sessão de poker falharam.");

        var registry = PlayerRegistry.Instance;
        if (registry?.PlayersContainer?.GetParent() == this)
            registry.PlayersContainer = null;

        // The end-to-end fixture owns a full table, players, cameras and active audio players.
        // Free them deterministically so a successful session cannot leave native playbacks alive.
        foreach (var child in GetChildren())
        {
            if (IsInstanceValid(child))
                child.Free();
        }

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
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
