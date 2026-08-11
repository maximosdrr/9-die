using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using Poker.Rules;

/// <summary>
/// The wiring the rules tests cannot see: that the scenes parse and point at each other, that every
/// RPC carries the mode the protocol assumes, that NO BROADCAST CAN CARRY A HOLE CARD, and that the
/// hand view is still a swappable seam.
///
/// The secrecy checks matter more here than they did for the dominoes. A leaked domino spoils a
/// match; a leaked hole card makes the entire game pointless and is invisible to everyone but the
/// person exploiting it.
/// </summary>
public partial class PokerSceneLoadTest : Node
{
    private int _passed;
    private int _failed;

    public override async void _Ready()
    {
        GD.Print("=== Teste de integração do poker ===");

        TestScenesLoad();
        TestGameScene();
        TestRpcModes();
        TestActionRateLimiter();
        TestHoleCardsNeverBroadcast();
        TestHandViewIsASeam();
        TestControllerIsASeatedController();
        TestHandStateMachine();
        TestCameraRigs();
        TestCardsNeverReachTheTable();
        TestFovIsCoherent();
        TestHud();
        TestGestureVocabulary();
        TestBoardDealAndFlip();
        TestChipPack();
        TestCardAtlas();
        TestPlayerCountGate();
        FreeRemainingFixtures();

        GD.Print($"=== {_passed} passaram, {_failed} falharam ===");
        if (_failed > 0)
            GD.PushWarning($"{_failed} verificação(ões) de integração do poker falharam.");

        // ProcessFrame resumes at the start of a frame. Waiting for the following signal leaves
        // one complete frame for QueueFree() to drain before shutdown diagnostics are collected.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Quit(_failed > 0 ? 1 : 0);
    }

    private void TestScenesLoad()
    {
        LoadScene("res://Games/Poker/Poker.tscn");
        LoadScene("res://Games/Poker/GameController/PokerController.tscn");
        LoadScene("res://Games/Poker/GameController/Views/PokerHand3DView.tscn");
        LoadScene("res://Games/Poker/Components/Cards/PokerCard.tscn");
        LoadScene("res://Games/Poker/Components/Chips/PokerChip.tscn");
        Check("carrega o pacote central de assets",
            GD.Load<PokerVisualAssets>("res://Games/Poker/PokerVisualAssets.tres") != null);
        Check("carrega o perfil temporal compartilhado",
            GD.Load<PokerPresentationProfile>(
                "res://Games/Poker/PokerPresentationProfile.tres") != null);
    }

    private PackedScene LoadScene(string path)
    {
        var scene = GD.Load<PackedScene>(path);
        var ok = scene != null && scene.CanInstantiate();
        Check($"carrega {path.GetFile()}", ok);
        return ok ? scene : null;
    }

    private void TestGameScene()
    {
        var scene = GD.Load<PackedScene>("res://Games/Poker/Poker.tscn");
        if (scene == null)
            return;

        var game = scene.Instantiate<PokerGame>();
        AddChild(game);

        Check("o jogo aponta para o controlador, a mesa e os assentos",
            game.GameControllerScene != null && game.BoardPresenter != null && game.Seats != null);
        Check("o jogo tem um resolvedor de poker ligado pelo GameModeHandler", game.Resolver != null);
        Check("o showdown reserva leitura das mãos e cerca de oito segundos para o ranking",
            game.Resolver != null && game.Resolver.ShowdownSeconds >= 11.0f);
        Check("cartas e fichas podem ser trocadas em um único recurso",
            game.VisualAssets is { CardScene: not null, ChipScene: not null });
        Check("o pote usa a ficha selecionada nesse recurso",
            game.BoardPresenter?.PotPile?.ChipScene == game.VisualAssets?.ChipScene);
        Check("o apresentador da mesa sabe desenhar cartas",
            game.BoardPresenter != null && game.BoardPresenter.CardScene != null);

        var seatPresenter = game.GetNodeOrNull<PokerSeatPresenter>("SeatPresenter");
        Check("a mesa tem o apresentador de assentos", seatPresenter != null);
        Check("o apresentador de assentos conhece a mesa e os assentos",
            seatPresenter is { BoardPresenter: not null, Seats: not null, CardScene: not null });
        Check("pote e bancos recebem valores físicos com a mesma tipografia de giz",
            seatPresenter is { ChalkFont: not null,
                ChalkValueFontSize: >= 62 and <= 70,
                ChalkValuePixelSize: >= 0.00021f and <= 0.00025f,
                PotValueLabelOffset: >= 0.055f and <= 0.080f,
                StackValueLabelSideOffset: >= 0.055f and <= 0.070f });
        Check("o servidor e a apresentacao usam o mesmo perfil temporal",
            seatPresenter?.PresentationProfile != null
            && ReferenceEquals(seatPresenter.PresentationProfile, game.Resolver?.PresentationProfile));
        Check("a troca de mão reserva recolhimento e embaralhamento antes da próxima distribuição",
            seatPresenter?.PresentationProfile is { CardReturnSeconds: >= 1.0f,
                CardReturnStagger: >= 0.06f, DeckGatherHoldSeconds: >= 0.2f,
                DeckShuffleSeconds: >= 1.5f, MaximumCollectionCardSlots: >= 20,
                CardCleanupDuration: > 3.0f });
        Check("showdown e pagamento vivem em componentes independentes",
            seatPresenter?.GetNodeOrNull<PokerShowdownPresenter>("ShowdownPresenter") != null
            && seatPresenter.GetNodeOrNull<PokerPayoutSequencer>("PayoutSequencer") != null
            && seatPresenter.GetNodeOrNull<PokerChipAnimator>("ChipAnimator") != null);
        Check("o par revelado se sobrepõe de forma controlada, sem ficar coplanar",
            seatPresenter != null
            && seatPresenter.ShowdownPairSpacing > game.BoardPresenter.Spec.CardWidth * 0.5f
            && seatPresenter.ShowdownPairSpacing < game.BoardPresenter.Spec.CardWidth
            && seatPresenter.ShowdownPairLayerSeparation > game.BoardPresenter.Spec.CardThickness);
        Check("o par revelado recebe variação visual moderada",
            seatPresenter is { ShowdownPairPositionJitter: > 0.0f,
                ShowdownPairAngleJitterDegrees: > 0.0f and <= 8.0f });
        Check($"saldo e apostas pendentes ocupam faixas diferentes das cartas "
              + $"(lado {seatPresenter?.StackSideOffset:F3}, dentro {seatPresenter?.StackInset:F3}, "
              + $"gap {seatPresenter?.BankColumnSpacing:F3}, diagonal "
              + $"{seatPresenter?.BankLaneAngleDegrees:F1}°)",
            seatPresenter is { BetSideOffset: >= 0.0f and <= 0.01f,
                StackSideOffset: >= 0.22f and <= 0.23f,
                StackInset: >= 0.10f and <= 0.13f,
                BankColumnSpacing: >= 0.044f,
                BankLaneAngleDegrees: >= 50.0f and <= 60.0f }
            && game.BoardPresenter.Spec.SeatBetRadius >= 0.30f);
        Check("o turno usa um anel fino junto à borda da mesa",
            seatPresenter is { TurnRingRadius: >= 0.60f, TurnRingWidth: > 0.0f and <= 0.006f,
                TurnRingArcSteps: >= 12 }
            && seatPresenter.ActiveTurnRingColor.A <= 0.50f
            && seatPresenter.OccupiedTurnRingColor.A <= 0.32f);
        Check("o disco branco do dealer foi removido da apresentação",
            typeof(PokerSeatPresenter).GetField("_dealerButton",
                BindingFlags.Instance | BindingFlags.NonPublic) == null);
        Check("a aposta preparada não cria mais um HUD flutuante sobre a mesa",
            typeof(PokerSeatPresenter).GetField("_preparedWagerLabel",
                BindingFlags.Instance | BindingFlags.NonPublic) == null);
        if (seatPresenter != null && game.BoardPresenter != null)
        {
            var spec = game.BoardPresenter.CommunityCardSpec;
            var edgeCard = PokerTableLayout.BoardPosition(PokerDeal.BoardCount - 1, spec);
            var sideSeatBet = new Vector2(spec.SeatBetRadius, -seatPresenter.BetSideOffset);
            var gap = new Vector2(
                Mathf.Max(0.0f, Mathf.Abs(sideSeatBet.X - edgeCard.X) - spec.CardWidth * 0.5f),
                Mathf.Max(0.0f, Mathf.Abs(sideSeatBet.Y - edgeCard.Y) - spec.CardLength * 0.5f));
            Check($"até a aposta mais crítica deixa as comunitárias livres ({gap.Length() * 100.0f:F1} cm)",
                gap.Length() > 0.05f);

            var baseSpec = game.BoardPresenter.Spec;
            var chipRadius = 0.020f;
            var cardRadius = new Vector2(
                baseSpec.CardWidth * 0.5f, baseSpec.CardLength * 0.5f).Length();
            var deckGap = new Vector2(baseSpec.SeatBetRadius, 0.0f)
                .DistanceTo(new Vector2(
                    game.BoardPresenter.DeckOffset.X, game.BoardPresenter.DeckOffset.Z))
                - cardRadius - chipRadius;
            var ownCardsGap = baseSpec.SeatCardRadius - baseSpec.CardLength * 0.5f
                - baseSpec.SeatBetRadius - chipRadius;
            Check($"a aposta lateral também deixa o baralho livre ({deckGap * 100.0f:F1} cm)",
                deckGap > 0.04f);
            Check($"a aposta fica diante das cartas do dono sem entrar nelas ({ownCardsGap * 100.0f:F1} cm)",
                ownCardsGap > 0.005f);
        }
        Check("o embaralhamento divide, intercala e esquadra o maço",
            game.BoardPresenter is { ShuffleSplitDistance: >= 0.025f,
                ShuffleLift: >= 0.008f, ShuffleHalfYawDegrees: >= 3.0f });
        Check("o futuro dealer tem pontos de extensao para animacao e som",
            typeof(PokerPayoutSequencer).GetEvent("DealerChangeStarted") != null
            && typeof(PokerSeatPresenter).GetField("DealerAnimator") != null
            && typeof(PokerSeatPresenter).GetField("DealerChangeSound") != null);

        Check($"as apostas são coerentes ({game.SmallBlind}/{game.BigBlind} com stack {game.StartingStack})",
            game.SmallBlind > 0 && game.BigBlind > game.SmallBlind
            && game.StartingStack >= game.BigBlind * 10);

        // Without this a cautious session between two players never ends.
        Check($"os blinds sobem para garantir que a sessão acaba ({game.BlindIncreaseEveryHands} mãos)",
            game.BlindIncreaseEveryHands > 0);

        TestChairsAndSeats(game);

        game.QueueFree();
    }

    /// <summary>
    /// The seats are DERIVED from the chairs at load, so this checks the thing that actually decides
    /// where a player ends up rather than the transforms typed into the scene.
    /// </summary>
    private void TestChairsAndSeats(PokerGame game)
    {
        var anchors = game.Seats as TableSeatAnchors;
        Check("os assentos são dirigidos pelas cadeiras", anchors != null);

        if (anchors == null)
            return;

        Check($"há uma cadeira por assento ({anchors.Chairs.Count})", anchors.Chairs.Count == 4);

        var chairsHaveColliders = true;
        var worstFloorGap = 0.0f;

        foreach (var chair in anchors.Chairs)
        {
            var collider = chair?.GetNodeOrNull<StaticBody3D>("Collision");
            chairsHaveColliders &= collider != null
                && collider.CollisionLayer == 4
                && collider.GetNodeOrNull<CollisionShape3D>("CollisionShape3D")?.Shape != null;

            if (chair is MeshInstance3D mesh && mesh.Mesh != null)
            {
                var bottom = mesh.GlobalPosition.Y + mesh.GetAabb().Position.Y * mesh.Scale.Y;
                worstFloorGap = Mathf.Max(worstFloorGap, Mathf.Abs(bottom));
            }
        }

        Check("as quatro cadeiras bloqueiam o jogador com colisões próprias", chairsHaveColliders);
        Check($"nenhuma cadeira está flutuando ({worstFloorGap * 1000.0f:F0} mm do chão)",
            worstFloorGap < 0.02f);

        // Every seat has to end up on a chair, facing in, with the eye leaning over the cloth —
        // which is what keeps the cards readable from a chair a metre out.
        var worstOffChair = 0.0f;
        var worstEyeOverTable = 0.0f;
        var closestEye = float.MaxValue;

        for (var i = 0; i < 4 && i < anchors.Chairs.Count; i++)
        {
            if (game.Seats.GetChild(i) is not Marker3D seat || anchors.Chairs[i] == null)
                continue;

            worstOffChair = Mathf.Max(worstOffChair,
                seat.GlobalPosition.DistanceTo(anchors.Chairs[i].GlobalPosition));

            var eye = seat.GetNodeOrNull<Node3D>("SeatView");
            if (eye == null)
                continue;

            var flat = eye.GlobalPosition with { Y = 0.0f };
            closestEye = Mathf.Min(closestEye, flat.DistanceTo(game.GlobalPosition with { Y = 0.0f }));
            worstEyeOverTable = Mathf.Max(worstEyeOverTable, eye.GlobalPosition.Y);
        }

        Check($"todo assento cai na sua cadeira (pior desvio {worstOffChair * 1000.0f:F0} mm)",
            worstOffChair < 0.05f);
        Check($"o olho se inclina sobre a mesa ({closestEye:F2} m do centro, {worstEyeOverTable:F2} m de altura)",
            closestEye < 0.80f && worstEyeOverTable is > 1.0f and < 1.3f);
    }

    private void TestRpcModes()
    {
        // A request must be AnyPeer or a client could never send it; a server message must be
        // Authority or a client would refuse it. Everything is reliable: a dropped action stalls the
        // hand. The three receivers are inherited from SecretHandTurnResolver — reflection walks the
        // derived type, so this pins what poker actually dispatches.
        CheckRpc<PokerTurnResolver>("ActOnServer", MultiplayerApi.RpcMode.AnyPeer, false);
        CheckRpc<PokerTurnResolver>("PrepareWagerOnServer", MultiplayerApi.RpcMode.AnyPeer, false);
        CheckRpc<PokerTurnResolver>("ReceivePreparedWagerRejected",
            MultiplayerApi.RpcMode.Authority, false);
        CheckRpc<PokerTurnResolver>("ShowdownRevealOnServer", MultiplayerApi.RpcMode.AnyPeer, false);
        CheckRpc<PokerTurnResolver>("ReceiveHand", MultiplayerApi.RpcMode.Authority, false);
        CheckRpc<PokerTurnResolver>("ReceiveActionRejected", MultiplayerApi.RpcMode.Authority, false);
        CheckRpc<PokerTurnResolver>("ReceiveFullState", MultiplayerApi.RpcMode.Authority, false);
    }

    private void CheckRpc<T>(string methodName, MultiplayerApi.RpcMode mode, bool callLocal)
    {
        var method = typeof(T).GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        var attribute = method?.GetCustomAttribute<RpcAttribute>();

        Check($"{methodName} é [Rpc({mode}, CallLocal={callLocal}, Reliable)]",
            attribute != null
            && attribute.Mode == mode
            && attribute.CallLocal == callLocal
            && attribute.TransferMode == MultiplayerPeer.TransferModeEnum.Reliable);
    }

    private void TestActionRateLimiter()
    {
        var resolver = new PokerTurnResolver();
        var acceptedWindow = true;
        for (var request = 0; request < PokerTurnResolver.ActionRequestsPerSecond; request++)
            acceptedWindow &= resolver.TryConsumeActionRequest(21, 100);

        Check($"poker aceita {PokerTurnResolver.ActionRequestsPerSecond} ações por peer/segundo",
            acceptedWindow);
        Check("poker descarta silenciosamente a ação excedente",
            !resolver.TryConsumeActionRequest(21, 100));
        Check("o limite de poker é independente por peer",
            resolver.TryConsumeActionRequest(22, 100));
        Check("a janela de ações do poker reabre deterministicamente",
            resolver.TryConsumeActionRequest(21, 1100));
        resolver.Free();

        var bounded = new PeerRequestRateLimiter(requestLimit: 1,
            windowMilliseconds: 1000, maxTrackedPeers: 2);
        var boundedAtCapacity = bounded.TryConsume(1, 10)
            && bounded.TryConsume(2, 10)
            && !bounded.TryConsume(3, 10)
            && bounded.TrackedPeerCount == 2;
        var reusesExpiredSlot = bounded.TryConsume(3, 1010)
            && bounded.TrackedPeerCount == 2;

        Check("o limitador nunca cresce além da capacidade configurada", boundedAtCapacity);
        Check("uma janela expirada é reutilizada sem aumentar a tabela", reusesExpiredSlot);
    }

    /// <summary>
    /// The secrecy rule, enforced statically. Physical chip denominations may also be int arrays, so
    /// only parameters explicitly named as cards/items count as secret-hand carriers.
    /// </summary>
    private void TestHoleCardsNeverBroadcast()
    {
        var carriers = typeof(PokerTurnResolver)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => method.GetCustomAttribute<RpcAttribute>() != null)
            .Where(method => method.GetParameters().Any(parameter =>
                (parameter.ParameterType == typeof(int[]) || parameter.ParameterType == typeof(long[]))
                && (parameter.Name?.Contains("item", System.StringComparison.OrdinalIgnoreCase) == true
                    || parameter.Name?.Contains("card", System.StringComparison.OrdinalIgnoreCase) == true)))
            .Select(method => method.Name)
            .ToList();

        Check($"só ReceiveHand transporta cartas ({string.Join(", ", carriers)})",
            carriers.Count == 1 && carriers[0] == "ReceiveHand");

        // The seed is worse than any one hand: it reconstructs every hand at once.
        var leaksSeed = typeof(PokerTurnResolver)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => method.GetCustomAttribute<RpcAttribute>() != null)
            .Any(method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(ulong)));

        Check("nenhuma RPC transporta a semente do embaralhamento", !leaksSeed);

        // The public state PokerGame holds must have nowhere to put somebody else's cards. Two int
        // arrays are allowed, each for a stated reason, and anything else here has to be justified
        // rather than slipped in:
        //   Board          — the community cards, public by definition.
        //   LocalHoleCards — this peer's OWN cards, which arrived through the targeted ReceiveHand.
        var allowed = new HashSet<string>
            { nameof(PokerGame.Board), nameof(PokerGame.LocalHoleCards) };

        var publicIntArrays = typeof(PokerGame)
            .GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Where(field => field.FieldType == typeof(int[]))
            .Select(field => field.Name)
            .Where(name => !allowed.Contains(name))
            .ToList();

        Check($"o estado público não tem onde guardar carta alheia ({string.Join(", ", publicIntArrays)})",
            publicIntArrays.Count == 0);

        // The one deliberate exception, and it must be explicit rather than accidental: the showdown.
        Check("o showdown tem um lugar declarado para as cartas que são reveladas",
            typeof(PokerGame).GetField(nameof(PokerGame.RevealedHoleCards)) != null);
    }

    private void TestHandViewIsASeam()
    {
        Check("a mão em 3D é uma PokerHandView",
            typeof(PokerHandView).IsAssignableFrom(typeof(PokerHand3DView)));
        Check("a abstração do poker é uma SeatedHandView do Core",
            typeof(SeatedHandView).IsAssignableFrom(typeof(PokerHandView)));

        var overrides = new[]
        {
            "Refresh", "SetInteractive", "SetTopViewActive", "ShowNotice", "ShowRejection", "Clear",
            "CancelPreparedWager",
        };

        var allOverridden = overrides.All(name =>
            typeof(PokerHand3DView).GetMethod(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.DeclaringType == typeof(PokerHand3DView));

        Check("a mão em 3D implementa toda a superfície da abstração", allOverridden);

        // FlattenHierarchy because SurrenderRequested is declared on SeatedHandView — leaving a match
        // is not a poker idea. What matters is that both reach the controller through the abstraction.
        var signals = new[] { "ActionRequested", "SurrenderRequested" };
        var allDeclared = signals.All(name =>
            typeof(PokerHandView).GetNestedType("SignalName", BindingFlags.Public)
                ?.GetField(name,
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy) != null);

        Check("a abstração declara os sinais de intenção", allDeclared);

        var controllerScene = GD.Load<PackedScene>(
            "res://Games/Poker/GameController/PokerController.tscn");
        var wired = controllerScene?.Instantiate<PokerController>();
        var wiredHandView = wired?.HandViewScene?.Instantiate();
        Check("o controlador aponta para a mão em 3D",
            wiredHandView is PokerHand3DView);
        wiredHandView?.Free();
        wired?.Free();

        // The controller must not know the CONCRETE view either, or swapping in the animated rig
        // becomes a rewrite instead of one PackedScene in the inspector.
        var concreteViewFields = typeof(PokerController)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => typeof(PokerHandView).IsAssignableFrom(field.FieldType)
                            && field.FieldType != typeof(PokerHandView))
            .Select(field => field.Name)
            .ToList();

        Check($"o controlador não conhece a view concreta ({string.Join(", ", concreteViewFields)})",
            concreteViewFields.Count == 0);
    }

    private void TestControllerIsASeatedController()
    {
        Check("o controlador de poker é um SeatedTableController",
            typeof(SeatedTableController).IsAssignableFrom(typeof(PokerController)));
        Check("e portanto um GameController, que é o que o jogador equipa",
            typeof(GameController).IsAssignableFrom(typeof(PokerController)));

        var controller = new PokerController();
        Check("o poker mantém o alternador de controle (E) habilitado", controller.AllowsControlSwitch);
        controller.Free();
    }

    private void TestHandStateMachine()
    {
        var scene = GD.Load<PackedScene>(
            "res://Games/Poker/GameController/Views/PokerHand3DView.tscn");
        if (scene == null)
            return;

        var view = scene.Instantiate<PokerHand3DView>();
        AddChild(view);

        var machine = view.GetNodeOrNull<StateMachine>("StateMachine");
        Check("a mão tem uma máquina de estados", machine != null);

        if (machine == null)
        {
            view.QueueFree();
            return;
        }

        // A typo in a Type registers a state nobody can reach, and the machine sits in whatever it
        // started in with no error at all.
        var expected = new[]
        {
            StatesRef.PokerHandIdle, StatesRef.PokerHandLooking, StatesRef.PokerHandActing,
        };

        var missing = expected.Where(type => !machine.States.ContainsKey(type)).ToList();
        Check($"todos os estados da mão estão registrados ({string.Join(", ", missing)})",
            missing.Count == 0);

        Check($"a mão começa parada ({machine.InitialState})",
            machine.InitialState == StatesRef.PokerHandIdle);
        Check("a máquina só ouve entrada de quem tem autoridade",
            machine.CheckForMultiplayerAuthorityOnStateHandleInput);

        Check("a view tem HUD, aviso e rig de mão",
            view.Hud != null && view.MessageLabel != null && view.HandRig != null);
        Check("as duas malhas de mão têm encaixes independentes",
            view.CardHandVisualMount != null && view.ChipHandVisualMount != null
            && view.CardHandPlaceholder != null && view.ChipHandPlaceholder != null);

        // The rig is the swap point for the animated hand: when it arrives, the AnimationPlayer is
        // assigned here and nothing else in the feature moves.
        var gestures = new[]
        {
            PokerGesture.PickUpCards, PokerGesture.ThrowChips,
            PokerGesture.Knock, PokerGesture.Fold, PokerGesture.Reveal,
        };

        var missingClips = gestures
            .Select(PokerClips.FirstPerson)
            .Append(PokerClips.Idle)
            .Where(clip => view.AnimationPlayer?.HasAnimation(clip) != true)
            .ToList();

        Check($"a mão tem um clipe para cada gesto ({string.Join(", ", missingClips)})",
            view.AnimationPlayer != null && missingClips.Count == 0);
        Check("o gesto de fichas não é cortado pelo antigo limite de 0,35 s",
            view.PlayGesture(PokerGesture.ThrowChips) > 0.35f);
        Check("pegar as cartas acompanha o tempo completo de olhar e baixar",
            view.PlayGesture(PokerGesture.PickUpCards) >= 2.3f);

        // Chips and the table knock are left-handed, so the rig needs a second hand for them.
        Check("o rig tem uma mão esquerda para as fichas e o toque na mesa",
            view.HandRig?.GetNodeOrNull<Node3D>("LeftHand") != null);

        view.QueueFree();
    }

    private void TestCameraRigs()
    {
        var scene = GD.Load<PackedScene>(
            "res://Games/Poker/GameController/PokerController.tscn");
        if (scene == null)
            return;

        var controller = scene.Instantiate<PokerController>();
        AddChild(controller);

        Check("o controlador tem o rig do assento",
            controller.GetNodeOrNull<RemoteTransform3D>("LookRig/LookPitch/RemoteSeat") != null);
        Check("o controlador tem o rig de cima",
            controller.GetNodeOrNull<RemoteTransform3D>("TopRig/RemoteTop") != null);
        Check("os dois rigs são independentes do corpo do jogador",
            controller.GetNode<Node3D>("LookRig").TopLevel
            && controller.GetNode<Node3D>("TopRig").TopLevel);

        Check($"o giro do pescoço é limitado ({controller.MaxYawDeg}°)",
            controller.MaxYawDeg is > 0.0f and <= 180.0f);
        Check($"a inclinação é limitada ({controller.MinPitchDeg}° a {controller.MaxPitchDeg}°)",
            controller.MinPitchDeg < controller.MaxPitchDeg
            && controller.RestPitchDeg >= controller.MinPitchDeg
            && controller.RestPitchDeg <= controller.MaxPitchDeg);
        Check($"a câmera sentada recua e sobe a partir da cadeira ({controller.SeatViewOffset})",
            controller.SeatViewOffset.Z >= 0.15f && controller.SeatViewOffset.Y >= 0.10f);
        Check($"a câmera sentada olha a mesa de um ângulo mais alto ({controller.RestPitchDeg}°)",
            controller.RestPitchDeg <= -34.0f);
        Check($"a vista superior se aproxima da área de jogo ({controller.TopHeight:F2} m)",
            controller.TopHeight is >= 0.75f and <= 0.95f);
        Check($"sair da mesa exige segurar ({controller.LeaveHoldSeconds:F1}s)",
            controller.LeaveHoldSeconds >= 0.5f);

        controller.QueueFree();
    }

    /// <summary>
    /// THE invariant behind the held cards: across the whole pitch range and the whole peek gesture,
    /// no corner of a card may reach the tabletop.
    ///
    /// This was a real bug — the hand offset was longer than the drop from the eye to the cloth, so
    /// past about 57 degrees of looking down the cards sank into the table. Tuning it by eye fixes it
    /// until the next person touches the fan, the eye height or the pitch clamp, which is why it is a
    /// swept measurement instead: it reports the worst clearance it found, in millimetres.
    /// </summary>
    private void TestCardsNeverReachTheTable()
    {
        var viewScene = GD.Load<PackedScene>(
            "res://Games/Poker/GameController/Views/PokerHand3DView.tscn");
        var controllerScene = GD.Load<PackedScene>(
            "res://Games/Poker/GameController/PokerController.tscn");
        var gameScene = GD.Load<PackedScene>("res://Games/Poker/Poker.tscn");

        if (viewScene == null || controllerScene == null || gameScene == null)
            return;

        var view = viewScene.Instantiate<PokerHand3DView>();
        var controller = controllerScene.Instantiate<PokerController>();
        var game = gameScene.Instantiate<PokerGame>();
        AddChild(view);
        AddChild(controller);
        AddChild(game);

        // The real numbers from the real scenes: the eye off the seat, the cloth off the presenter.
        var seat = game.Seats.GetChild(0) as Marker3D;
        var eye = seat?.GetNodeOrNull<Node3D>("SeatView");
        var eyeY = eye == null
            ? 1.13f + controller.SeatViewOffset.Y
            : (eye.GlobalTransform.Origin
               + eye.GlobalTransform.Basis.Orthonormalized() * controller.SeatViewOffset).Y;
        var clothY = game.BoardPresenter.GlobalPosition.Y;
        var spec = game.BoardPresenter.Spec;

        var worstClearance = float.MaxValue;
        var worstPitch = 0.0f;
        var worstPeek = 0.0f;

        for (var step = 0; step <= 40; step++)
        {
            var pitch = Mathf.DegToRad(Mathf.Lerp(controller.MinPitchDeg, controller.MaxPitchDeg, step / 40.0f));

            // Yaw never changes world height, so pitch alone decides how far the hand swings down.
            var camera = new Basis(Vector3.Right, pitch);

            for (var peekStep = 0; peekStep <= 10; peekStep++)
            {
                var peek = peekStep / 10.0f;
                var fan = view.FanSpecAt(peek);

                for (var card = 0; card < PokerDeal.HoleCardCount; card++)
                {
                    var slot = HandFan.SlotTransform(
                        card, HandFan.NaturalCentre(PokerDeal.HoleCardCount), false, fan);

                    // A card is width on its own X and length on its own Z — see PokerCard.Apply.
                    for (var corner = 0; corner < 4; corner++)
                    {
                        var local = new Vector3(
                            (corner % 2 == 0 ? -0.5f : 0.5f) * spec.CardWidth,
                            0.0f,
                            (corner < 2 ? -0.5f : 0.5f) * spec.CardLength);

                        var inHand = view.HandOffset + slot * local;
                        var worldY = eyeY + (camera * inHand).Y;
                        var clearance = worldY - clothY;

                        if (clearance >= worstClearance)
                            continue;

                        worstClearance = clearance;
                        worstPitch = Mathf.RadToDeg(pitch);
                        worstPeek = peek;
                    }
                }
            }
        }

        Check($"as cartas nunca alcançam a mesa (pior folga {worstClearance * 1000.0f:F0} mm "
              + $"a {worstPitch:F0}° com espiada {worstPeek:F1})",
            worstClearance > 0.02f);

        view.QueueFree();
        controller.QueueFree();
        game.QueueFree();
    }

    private void TestFovIsCoherent()
    {
        var poker = GD.Load<PackedScene>("res://Games/Poker/GameController/PokerController.tscn")
            ?.Instantiate<PokerController>();
        var domino = GD.Load<PackedScene>("res://Games/Domino/GameController/DominoController.tscn")
            ?.Instantiate<DominoController>();

        if (poker == null || domino == null)
            return;

        AddChild(poker);
        AddChild(domino);

        Check($"o FOV sentado preserva uma perspectiva natural ({poker.SeatFov}°)",
            poker.SeatFov >= 45.0f && poker.SeatFov < 75.0f);

        // Toggling to the overhead view has to LOOK like something happened. Equal FOVs made it
        // read as if nothing had changed but the angle.
        Check($"a vista de cima muda o enquadramento ({poker.SeatFov}° para {poker.TopFov}°)",
            !Mathf.IsEqualApprox(poker.SeatFov, poker.TopFov));

        Check($"o assento é mais fechado que a câmera de caminhar ({poker.SeatFov}° de 75°)",
            poker.SeatFov < 75.0f);

        Check($"a vista superior usa enquadramento próximo ({poker.TopFov}° a {poker.TopHeight:F2} m)",
            poker.TopFov <= 52.0f && poker.TopHeight <= 0.90f);

        poker.QueueFree();
        domino.QueueFree();
    }

    /// <summary>
    /// The corner panel, and the keys it promises.
    ///
    /// The keys are the whole interface now, so the thing worth pinning is that what the HUD PRINTS
    /// and what the states LISTEN FOR cannot drift apart — both read <see cref="PokerInput"/>, and
    /// every one of those actions has to actually exist in the input map or the key does nothing.
    /// </summary>
    private void TestHud()
    {
        Check("a janela de desenvolvimento usa a escala original (1152x648)",
            (int)ProjectSettings.GetSetting("display/window/size/viewport_width", 0) == 1152
            && (int)ProjectSettings.GetSetting("display/window/size/viewport_height", 0) == 648
            && (int)ProjectSettings.GetSetting("display/window/size/window_width_override", 0) == 1152
            && (int)ProjectSettings.GetSetting("display/window/size/window_height_override", 0) == 648);

        var actions = new[]
        {
            (PokerInput.ShowdownReveal, PokerInput.ShowdownRevealKey, Key.S),
        };

        foreach (var (action, printed, key) in actions)
        {
            var mapped = InputMap.HasAction(action)
                         && InputMap.ActionGetEvents(action)
                             .OfType<InputEventKey>()
                             .Any(entry => entry.PhysicalKeycode == key);

            Check($"{action} está mapeada na tecla que a HUD mostra ({printed})", mapped);
        }

        Check("espiar as cartas está no botão direito",
            InputMap.HasAction(PokerInput.Peek)
            && InputMap.ActionGetEvents(PokerInput.Peek)
                .OfType<InputEventMouseButton>()
                .Any(entry => entry.ButtonIndex == MouseButton.Right));

        var handScene = GD.Load<PackedScene>(
            "res://Games/Poker/GameController/Views/PokerHand3DView.tscn");
        var hand = handScene?.Instantiate<PokerHand3DView>();
        var farCornerRadius = hand == null
            ? float.MaxValue
            : Mathf.Max(
                Mathf.Sqrt(hand.InteractionZoneCenterRadius * hand.InteractionZoneCenterRadius
                           + hand.ActionZoneRadius * hand.ActionZoneRadius),
                hand.ConfirmZoneCenterRadius + hand.ConfirmZoneOuterRadius);
        Check("a confirmação fica atrás das fichas e separada de passar/desistir",
            hand is { Crosshair: not null,
                InteractionZoneCenterRadius: >= 0.55f and <= 0.59f,
                ActionZoneRadius: >= 0.10f and <= 0.13f,
                ConfirmZoneCenterRadius: >= 0.30f and <= 0.34f,
                ConfirmZoneInnerRadius: >= 0.06f and <= 0.08f,
                ConfirmZoneOuterRadius: >= 0.10f and <= 0.12f,
                ConfirmLabelSpanPi: >= 0.10f and <= 0.16f }
            && hand.ConfirmZoneOuterRadius > hand.ConfirmZoneInnerRadius
            && Mathf.IsEqualApprox(
                hand.ConfirmZoneCenterRadius, PokerLayoutSpec.Default.SeatBetRadius)
            && hand.ConfirmZoneCenterRadius + hand.ConfirmZoneOuterRadius
               < hand.InteractionZoneCenterRadius - hand.ActionZoneRadius
            && typeof(PokerHand3DView).GetMethod("HandleTableClick")?.GetParameters().Length == 0);
        Check("um único arco reúne CALL, AUTO, APOSTAR e o hold de ALL-IN",
            hand is {
                CallHoverOpacity: >= 0.30f and <= 0.38f,
                CallClickMaxSeconds: >= 0.30f and <= 0.40f,
                CallLabelCycleSeconds: >= 1.9f and <= 2.1f,
                CallLabelFadeSeconds: >= 0.18f and <= 0.32f,
                AllInHoldSeconds: >= 0.9f and <= 1.1f,
                AllInVisualDelaySeconds: >= 0.30f and <= 0.40f,
                AllInHoldOpacity: > 0.4f and <= 0.7f }
            && hand.AllInVisualDelaySeconds >= hand.CallClickMaxSeconds
            && hand.AllInVisualDelaySeconds < hand.AllInHoldSeconds
            && hand.ChalkHoverShader?.Code.Contains("fill_progress") == true
            && typeof(PokerHand3DView).GetField("CallZoneLength") == null
            && typeof(PokerHand3DView).GetField("CallZoneWidth") == null
            && typeof(PokerHand3DView).GetField("CallZoneSideOffset") == null
            && typeof(PokerSeatPresenter).GetMethod("TryPrepareAutomaticWager") != null
            && typeof(PokerHand3DView).GetMethod("TryConsumeTableGesture") != null
            && typeof(PokerHand3DView).GetMethod("HandleTableRelease") != null
            && typeof(PokerHand3DView).GetMethod("AdvanceCallHold") != null
            && typeof(PokerHand3DView).GetMethod("AdvanceCallLabelCycle") != null);
        Check("CALL/AUTO alterna com SEGURE ALL-IN sem juntar os textos",
            PokerHand3DView.CallLabelForHover(0.0f, 2.0f) == "CALL"
            && PokerHand3DView.CallLabelForHover(1.99f, 2.0f) == "CALL"
            && PokerHand3DView.CallLabelForHover(2.0f, 2.0f) == "SEGURE ALL-IN"
            && PokerHand3DView.CallLabelForHover(3.99f, 2.0f) == "SEGURE ALL-IN"
            && PokerHand3DView.CallLabelForHover(4.0f, 2.0f) == "CALL"
            && PokerHand3DView.CallLabelForHover("AUTO", 0.0f, 2.0f) == "AUTO"
            && PokerHand3DView.CallLabelForHover("AUTO", 2.0f, 2.0f) == "SEGURE ALL-IN"
            && PokerHand3DView.CallLabelOpacityForHover(0.0f, 2.0f, 0.24f) > 0.99f
            && PokerHand3DView.CallLabelOpacityForHover(2.0f, 2.0f, 0.24f) < 0.01f);
        var callOptions = new List<ActionOption>
        {
            new(PokerActionKind.Call, 10, 10),
            new(PokerActionKind.Raise, 20, 100),
        };
        var autoOptions = new List<ActionOption>
        {
            new(PokerActionKind.Check, 0, 0),
            new(PokerActionKind.Raise, 10, 100),
        };
        Check("CALL paga primeiro; sem CALL, AUTO faz exatamente a aposta mínima",
            PokerHand3DView.TryAutomaticWagerOption(
                callOptions, out var callKind, out var callTotal)
            && callKind == PokerActionKind.Call && callTotal == 10
            && PokerHand3DView.AutomaticWagerLabel(callOptions) == "CALL"
            && PokerHand3DView.TryAutomaticWagerOption(
                autoOptions, out var autoKind, out var autoTotal)
            && autoKind == PokerActionKind.Raise && autoTotal == 10
            && PokerHand3DView.AutomaticWagerLabel(autoOptions) == "AUTO"
            && PokerHand3DView.WagerButtonLabel(callOptions, hasPreparedChips: true) == "APOSTAR"
            && PokerHand3DView.WagerButtonLabel(callOptions, hasPreparedChips: false) == "CALL"
            && PokerHand3DView.WagerButtonLabel(autoOptions, hasPreparedChips: false) == "AUTO");
        Check("o clique rápido não mostra a barra; o hold de um segundo a completa",
            PokerHand3DView.AllInHoldVisualProgress(0.12f, 0.35f, 1.0f) == 0.0f
            && PokerHand3DView.AllInHoldVisualProgress(0.35f, 0.35f, 1.0f) == 0.0f
            && PokerHand3DView.AllInHoldVisualProgress(0.36f, 0.35f, 1.0f) > 0.0f
            && PokerHand3DView.AllInHoldVisualProgress(1.0f, 0.35f, 1.0f) > 0.99f);
        Check("a confirmação curta e legível agora se chama APOSTAR",
            PokerHand3DView.ConfirmBetLabelText == "APOSTAR");
        Check("CALL só aceita uma liberação realmente rápida",
            PokerHand3DView.IsQuickCallRelease(0.12f, hand?.CallClickMaxSeconds ?? 0.0f)
            && PokerHand3DView.IsQuickCallRelease(0.35f, hand?.CallClickMaxSeconds ?? 0.0f)
            && !PokerHand3DView.IsQuickCallRelease(0.36f, hand?.CallClickMaxSeconds ?? 0.0f)
            && !PokerHand3DView.IsQuickCallRelease(1.5f, hand?.CallClickMaxSeconds ?? 0.0f)
            && !PokerHand3DView.IsQuickCallRelease(1.0f, hand?.CallClickMaxSeconds ?? 0.0f));
        Check("os comandos usam giz procedural, fonte grande e divisões finas",
            hand is { ChalkFont: not null, ChalkHoverShader: not null,
                ChalkGuideFontSize: >= 68, ChalkGuidePixelSize: >= 0.00018f,
                ChalkHoverOpacity: > 0.0f and <= 0.30f,
                InteractionGuideThickness: <= 0.0015f });
        Check("o pote não mantém uma área ou um contorno amarelo próprio",
            typeof(PokerHand3DView).GetField("PotGuideColor") == null
            && typeof(PokerHand3DView).GetField("PotClickRadius") == null);
        Check($"até os cantos das ações ficam dentro do tampo ({farCornerRadius:F3} m)",
            farCornerRadius < 0.60f);
        hand?.Free();

        var scene = GD.Load<PackedScene>("res://Games/Poker/GameController/Views/PokerHud.tscn");
        Check("a cena da HUD carrega", scene != null && scene.CanInstantiate());

        if (scene == null)
            return;

        var hud = scene.Instantiate<PokerHud>();
        AddChild(hud);

        Check("a HUD encontra os avisos e dicas que ainda vivem na tela",
            hud.Root != null && hud.HintsLabel != null
            && hud.ShowdownAnnouncement != null && hud.ShowdownTitle != null
            && hud.ShowdownPrompt != null && hud.ShowdownBell?.Stream != null);
        Check("o aviso de showdown usa a tipografia de giz",
            hud.ShowdownTitle?.GetThemeFont("font") != null
            && hud.ShowdownPrompt?.GetThemeFont("font") != null);

        Check($"a HUD fica acima das outras camadas ({hud.Layer})", hud.Layer > 1);

        Check("a HUD de status do canto foi removida por completo",
            hud.Root.GetNodeOrNull("Panel") == null
            && typeof(PokerHud).GetField("TurnLabel") == null
            && typeof(PokerHud).GetField("StakesLabel") == null
            && typeof(PokerHud).GetField("PreparedWagerLabel") == null
            && typeof(PokerHud).GetField("ActionList") == null
            && typeof(PokerHud).GetField("ResultLabel") == null);

        Check("a HUD começa escondida", !hud.Root.Visible);

        // A Control that swallows the click breaks the recapture SeatedTableController does after
        // Escape — the player would be left with a loose cursor and no way to get it back.
        var greedy = new List<string>();
        CollectGreedyControls(hud.Root, greedy);

        Check($"nenhum Control da HUD engole o clique ({string.Join(", ", greedy)})", greedy.Count == 0);

        hud.QueueFree();
    }

    private static void CollectGreedyControls(Node node, List<string> into)
    {
        if (node is Control control && control.MouseFilter != Control.MouseFilterEnum.Ignore)
            into.Add((string)control.Name);

        foreach (var child in node.GetChildren())
            CollectGreedyControls(child, into);
    }

    /// <summary>
    /// The community row: all five dealt face down at the start of the hand, turned over a street at
    /// a time.
    ///
    /// The reveal is entirely local — the server says only HOW MANY are face up — so this also pins
    /// the thing that would leak: a card must not be face up before the server has sent it.
    /// </summary>
    private void TestBoardDealAndFlip()
    {
        var scene = GD.Load<PackedScene>("res://Games/Poker/Poker.tscn");
        if (scene == null)
            return;

        var game = scene.Instantiate<PokerGame>();
        AddChild(game);

        var board = game.BoardPresenter;
        if (board == null)
        {
            game.QueueFree();
            return;
        }

        // A hand is dealt with nothing turned over yet.
        board.Sync(new List<int>(), 0, handNumber: 1, PokerStreet.Preflop);

        Check($"as cinco comunitárias entram no começo da mão ({VisibleCards(board)})",
            VisibleCards(board) == PokerDeal.BoardCount);
        Check("elas começam a mão ainda chegando", !board.Settled);

        Settle(board);
        Check("depois de entrarem, a mesa assenta", board.Settled);
        Check($"e todas estão de costas ({FaceUpCards(board)} viradas)", FaceUpCards(board) == 0);

        // The flop turns exactly three.
        board.Sync(new List<int> { 0, 1, 2 }, 0, 1, PokerStreet.Flop);
        Settle(board);
        Check($"o flop vira três ({FaceUpCards(board)})", FaceUpCards(board) == 3);

        board.Sync(new List<int> { 0, 1, 2, 3 }, 0, 1, PokerStreet.Turn);
        Settle(board);
        Check($"o turn vira a quarta ({FaceUpCards(board)})", FaceUpCards(board) == 4);

        board.Sync(new List<int> { 0, 1, 2, 3, 4 }, 0, 1, PokerStreet.River);
        Settle(board);
        Check($"o river vira a quinta ({FaceUpCards(board)})", FaceUpCards(board) == 5);

        // The next hand takes the whole row back off and deals again face down.
        board.Sync(new List<int>(), 0, handNumber: 2, PokerStreet.Preflop);
        Settle(board);
        Check($"a mão seguinte recomeça com todas de costas ({FaceUpCards(board)})",
            FaceUpCards(board) == 0);

        game.QueueFree();
    }

    /// <summary>Runs the presenter's animation to a standstill, as frames would.</summary>
    private static void Settle(PokerBoardPresenter board)
    {
        var frame = 0;
        for (; frame < 900 && !board.Settled; frame++)
            board._Process(1.0 / 60.0);

        if (!board.Settled)
            GD.Print($"  ---- mesa não assentou em {frame} quadros: {board.DebugState()}");
    }

    private static int VisibleCards(PokerBoardPresenter board)
    {
        var count = 0;
        foreach (var child in board.GetChildren())
        {
            if (child is PokerCard { Visible: true })
                count++;
        }

        return count;
    }

    /// <summary>
    /// A card is face up when its own +Y still points up. The turn is half a revolution about the
    /// long axis, so a back-up card has that axis inverted.
    /// </summary>
    private static int FaceUpCards(PokerBoardPresenter board)
    {
        var count = 0;
        foreach (var child in board.GetChildren())
        {
            if (child is PokerCard { Visible: true } card && card.Transform.Basis.Y.Y > 0.5f)
                count++;
        }

        return count;
    }

    /// <summary>
    /// The gesture vocabulary: one name per thing a player does, resolving to a first-person clip
    /// and a third-person one.
    ///
    /// The third-person clips do not exist yet, which is exactly why this is worth pinning — the
    /// names have to be reachable and distinct NOW so the animator has somewhere to deliver them,
    /// and so a body gesture cannot silently resolve to the same clip as another.
    /// </summary>
    private void TestGestureVocabulary()
    {
        var gestures = new[]
        {
            PokerGesture.PickUpCards, PokerGesture.ThrowChips,
            PokerGesture.Knock, PokerGesture.Fold, PokerGesture.Reveal,
        };

        var bodyClips = gestures.Select(PokerClips.ThirdPerson).ToList();
        Check($"cada gesto tem um clipe de terceira pessoa distinto ({bodyClips.Count} nomes)",
            bodyClips.Distinct().Count() == gestures.Length);

        Check("nenhum gesto cai no idle do corpo por engano",
            bodyClips.All(clip => clip != PokerClips.BodyIdle));

        // A gesture nobody made resolves to idle in both, which is what "nothing happened" means.
        Check("sem gesto, corpo e mão ficam parados",
            PokerClips.ThirdPerson(PokerGesture.None) == PokerClips.BodyIdle
            && PokerClips.FirstPerson(PokerGesture.None) == PokerClips.Idle);

        // Every action code the server can put in a context has to map to something, or a peer
        // watching somebody else would see them do nothing at all.
        Check("as ações do servidor viram gestos",
            PokerClips.ForAction("fold") == PokerGesture.Fold
            && PokerClips.ForAction("check") == PokerGesture.Knock
            && PokerClips.ForAction("call") == PokerGesture.ThrowChips
            && PokerClips.ForAction("raise") == PokerGesture.ThrowChips
            && PokerClips.ForAction("showdown") == PokerGesture.Reveal);

        Check("uma ação que não é um gesto não anima nada",
            PokerClips.ForAction("deal") == PokerGesture.None
            && PokerClips.ForAction("") == PokerGesture.None);

        // The body gesture is replayed by the seat presenter from the context, so the hook it calls
        // has to exist on Player — it is a no-op today and must stay callable.
        Check("o corpo sentado tem por onde receber um gesto",
            typeof(Player).GetMethod(
                nameof(Player.PlaySeatedGesture), new[] { typeof(string) }) != null
            && typeof(Player).GetMethod(
                nameof(Player.PlaySeatedGesture),
                new[] { typeof(string), typeof(Vector3) }) != null);

        var scene = GD.Load<PackedScene>("res://Games/Poker/Poker.tscn");
        var game = scene?.Instantiate<PokerGame>();

        if (game == null)
            return;

        AddChild(game);
        Check("o toque na mesa tem som", game.SeatPresenter?.KnockSound != null);
        Check("as fichas têm som próprio de impacto",
            game.SeatPresenter?.ChipLandingSound != null);

        var chipSoundscape = game.SeatPresenter?
            .GetNodeOrNull<PokerChipSoundscape>("ChipSoundscape");
        Check("impactos próximos usam um mixer limitado",
            chipSoundscape is { VoiceLimit: <= 3 });
        if (chipSoundscape != null)
        {
            var oneChip = chipSoundscape.VolumeForImpact(1, 1);
            var largeDrop = chipSoundscape.VolumeForImpact(40, 12);
            Check($"muitas fichas ficam mais presentes sem estourar ({oneChip:F1} a {largeDrop:F1} dB)",
                largeDrop > oneChip
                && largeDrop <= game.SeatPresenter.ChipMaximumImpactDb);
            var organizeBefore = chipSoundscape.OrganizationCueCount;
            chipSoundscape.PlayOrganization(
                game.SeatPresenter.GlobalPosition, 24, game.SeatPresenter.ChipOrganizeSeconds);
            chipSoundscape._Process(0.5);
            Check("a organizacao do pote agenda um chocalho limitado",
                chipSoundscape.OrganizationCueCount > organizeBefore);
        }
        game.QueueFree();
    }

    /// <summary>Every logical denomination resolves to one optimized, correctly sized mesh.</summary>
    private void TestChipPack()
    {
        Check("o pacote otimizado de fichas carrega", PokerChipAssetMeshes.IsAvailable);

        if (!PokerChipAssetMeshes.IsAvailable)
            return;

        PokerChipAssetMeshes.TryGet(25, out var measuredChip, out _);
        var measuredSize = measuredChip?.GetAabb().Size ?? Vector3.Zero;
        var measuredDiameter = Mathf.Max(measuredSize.X, measuredSize.Z);
        var measuredThickness = measuredSize.Y;
        Check($"a ficha fica com o tamanho de uma ficha ({measuredDiameter * 1000.0f:F0} x "
              + $"{measuredThickness * 1000.0f:F1} mm)",
            measuredDiameter is > 0.030f and < 0.050f
            && measuredThickness is > 0.002f and < 0.006f);

        var denominations = new[] { 1, 5, 10, 25, 50, 100, 500, 1000, 5000, 10000 };
        var allValuesExist = denominations.All(value =>
            PokerChipAssetMeshes.TryGet(value, out var mesh, out var bodySurface)
            && mesh != null
            && bodySurface >= 0);
        Check("cada denominação tem malha e superfície colorível próprias", allValuesExist);

        var pile = new PokerChipPile();
        AddChild(pile);
        pile.Show(225);

        Check($"a pilha empilha pela espessura real da ficha ({pile.EffectiveThickness * 1000.0f:F1} mm)",
            Mathf.IsEqualApprox(pile.EffectiveThickness, PokerChipAssetMeshes.Thickness));
        Check($"a pilha desenha as fichas de 225 ({pile.GetChildCount()} fichas)",
            pile.GetChildCount() == 3);
        var visibleMesh = pile.GetChildren().OfType<Node3D>()
            .SelectMany(chip => chip.GetChildren().OfType<MeshInstance3D>())
            .FirstOrDefault(mesh => mesh.Visible);
        Check($"a ficha usa um perfil mais encorpado ({PokerChipAssetMeshes.Thickness * 1000.0f:F1} mm)",
            PokerChipAssetMeshes.Thickness >= 0.0044f
            && visibleMesh != null
            && Mathf.IsEqualApprox(visibleMesh.Scale.Y, PokerChipAssetMeshes.HeightScale));

        pile.QueueFree();
    }

    /// <summary>
    /// The atlas mapping. The pack's columns are NOT in rank order, so this is the one place a
    /// differently-ordered sheet would show up — and it must show up here rather than as a player
    /// quietly holding the wrong card.
    /// </summary>
    private void TestCardAtlas()
    {
        var importedMeshes = new HashSet<ulong>();
        var importedSurfacesAreComplete = true;
        var importedMaterialsAreSharpAtAnAngle = true;
        for (var card = 0; card < CardId.Count; card++)
        {
            if (!PokerCardAssetMeshes.TryGet(card, out var mesh, out _))
                continue;

            importedMeshes.Add(mesh.GetInstanceId());
            importedSurfacesAreComplete &= mesh.GetSurfaceCount() == 2;
            for (var surface = 0; surface < mesh.GetSurfaceCount(); surface++)
            {
                importedMaterialsAreSharpAtAnAngle &= mesh.SurfaceGetMaterial(surface)
                    is BaseMaterial3D
                    {
                        TextureFilter: BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                    };
            }
        }

        Check($"o GLB fornece uma malha distinta para cada uma das 52 cartas ({importedMeshes.Count})",
            PokerCardAssetMeshes.IsAvailable && importedMeshes.Count == CardId.Count);
        Check("cada carta importada preserva as superfícies de frente e verso",
            PokerCardAssetMeshes.IsAvailable && importedSurfacesAreComplete);
        Check("as cartas usam filtragem anisotrópica para permanecerem legíveis em perspectiva",
            PokerCardAssetMeshes.IsAvailable && importedMaterialsAreSharpAtAnAngle);

        var patches = new HashSet<Vector3>();
        for (var card = 0; card < CardId.Count; card++)
            patches.Add(PokerCardFaces.UvOffset(card));

        Check($"as 52 cartas apontam para 52 recortes distintos do atlas ({patches.Count})",
            patches.Count == CardId.Count);

        var scale = PokerCardFaces.UvScale;
        Check($"o recorte é 1/13 por 1/4 ({scale.X:F4} x {scale.Y:F4})",
            Mathf.IsEqualApprox(scale.X, 1.0f / PokerCardFaces.Columns)
            && Mathf.IsEqualApprox(scale.Y, 1.0f / PokerCardFaces.Rows));

        var inside = true;
        foreach (var patch in patches)
        {
            if (patch.X < 0.0f || patch.X > 1.0f - scale.X + 0.001f
                || patch.Y < 0.0f || patch.Y > 1.0f - scale.Y + 0.001f)
                inside = false;
        }

        Check("nenhum recorte cai fora da folha", inside);

        // The pack runs A,2,…,10,J,K,Q: the ace is the FIRST column and the queen the last.
        Check("o ás está na primeira coluna",
            Mathf.IsZeroApprox(PokerCardFaces.UvOffset(CardId.From(CardId.Ace, CardId.Clubs)).X));
        Check("a dama vem DEPOIS do rei, como no pacote",
            PokerCardFaces.UvOffset(CardId.From(CardId.Queen, CardId.Clubs)).X
            > PokerCardFaces.UvOffset(CardId.From(CardId.King, CardId.Clubs)).X);

        // Rows are suits, so two cards of the same suit share a row and two of the same rank a column.
        var sameSuitRow = Mathf.IsEqualApprox(
            PokerCardFaces.UvOffset(CardId.From(CardId.Two, CardId.Hearts)).Y,
            PokerCardFaces.UvOffset(CardId.From(CardId.Ace, CardId.Hearts)).Y);

        Check("cartas do mesmo naipe ficam na mesma linha", sameSuitRow);

        // With no art the cards must still be playable rather than blank.
        var cardScene = GD.Load<PackedScene>("res://Games/Poker/Components/Cards/PokerCard.tscn");
        var sample = cardScene?.Instantiate<PokerCard>();
        if (sample != null)
        {
            AddChild(sample);
            sample.Configure(CardId.From(CardId.Ace, CardId.Spades), PokerLayoutSpec.Default);

            Check("a carta configurável usa a malha importada sem alterar a cena de gameplay",
                sample.AssetVisual is { Visible: true, Mesh: not null }
                && sample.Face is { Visible: false }
                && sample.Back is { Visible: false });

            sample.QueueFree();
        }
    }

    private void TestPlayerCountGate()
    {
        var game = new PokerGame();

        game.AllowSoloDebug = false;
        Check("sem o modo solo, uma pessoa não abre a mesa", !game.CanStartWith(1));
        Check("duas pessoas abrem a mesa", game.CanStartWith(2));

        game.AllowSoloDebug = true;
        Check("com o modo solo, uma pessoa abre a mesa", game.CanStartWith(1));

        Check($"a mesa recusa mais gente do que tem cadeira ({game.MaxPlayers})",
            game.CanStartWith(game.MaxPlayers) && !game.CanStartWith(game.MaxPlayers + 1));

        game.Free();
    }

    /// <summary>Release scene fixtures before the engine starts its own shutdown cleanup.</summary>
    private void FreeRemainingFixtures()
    {
        foreach (var child in GetChildren())
        {
            if (IsInstanceValid(child))
                child.Free();
        }
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
