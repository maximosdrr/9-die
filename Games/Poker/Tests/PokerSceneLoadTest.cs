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
        TestOpponentHeldCards();
        TestRuntimeResourceManifest();
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

    private void TestOpponentHeldCards()
    {
        var spec = PokerLayoutSpec.Default;
        var first = PokerSeatPresenter.OpponentHeldCardTransform(0, spec);
        var second = PokerSeatPresenter.OpponentHeldCardTransform(1, spec);

        // PokerCard's local +Y is its printed face. In the grip, -Z points toward the owner and
        // +Z toward the room, so the face must point -Z and leave the back visible to observers.
        var firstFaceNormal = (first.Basis * Vector3.Up).Normalized();
        var secondFaceNormal = (second.Basis * Vector3.Up).Normalized();
        Check("as cartas 3P escondem as faces dos outros jogadores",
            firstFaceNormal.Dot(Vector3.Forward) > 0.999f
            && secondFaceNormal.Dot(Vector3.Forward) > 0.999f);

        var depthSeparation = Mathf.Abs((second.Origin - first.Origin).Z);
        Check("as cartas 3P têm camadas determinísticas sem z-fighting",
            depthSeparation >= Mathf.Max(spec.CardThickness * 2.0f, 0.0015f) - 0.00001f
            && Mathf.IsZeroApprox(second.Origin.Y - first.Origin.Y));

        var character = GD.Load<PackedScene>(
                "res://World/Player/Components/CharacterVisual.tscn")
            ?.Instantiate<CharacterVisual>();
        if (character != null)
            AddChild(character);

        var exportedPlaceholderGeometry = character?.RigRoot?
            .FindChildren("*", "GeometryInstance3D", recursive: true, owned: false)
            .Any(node => node.Name.ToString().ToLowerInvariant().Contains("placeholder")) == true;
        Check("o personagem 3P exporta só o marcador das cartas, sem placeholder visível",
            character != null && !exportedPlaceholderGeometry);
        var thirdPersonHand = character?.Skeleton?.FindBone("CC_Base_L_Hand") ?? -1;
        var thirdPersonGripBefore = character?.CardGrip?.GlobalTransform
                                    ?? Transform3D.Identity;
        if (character?.Skeleton != null && thirdPersonHand >= 0)
        {
            var movedHand = character.Skeleton.GetBoneGlobalPose(thirdPersonHand);
            movedHand.Origin += new Vector3(0.025f, 0.015f, -0.01f);
            character.Skeleton.SetBoneGlobalPose(thirdPersonHand, movedHand);
            character.Skeleton.EmitSignal(Skeleton3D.SignalName.SkeletonUpdated);
        }
        Check("as cartas 3P acompanham a pose final da mao animada",
            character?.CardGrip != null
            && !character.CardGrip.GlobalTransform.IsEqualApprox(thirdPersonGripBefore));
        character?.QueueFree();
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

        var runtimeAuthoring = game.ExperienceAuthoring;
        var previewCameraProperty = runtimeAuthoring?.Get(
            nameof(PokerExperienceAuthoring.PreviewCamera)) ?? default;
        var previewHandsProperty = runtimeAuthoring?.Get(
            nameof(PokerExperienceAuthoring.FirstPersonHandsPreview)) ?? default;
        Check("o preview do editor libera referencias exportadas sem objetos descartados",
            runtimeAuthoring != null
            && runtimeAuthoring.PreviewCamera == null
            && runtimeAuthoring.FirstPersonHandsPreview == null
            && previewCameraProperty.VariantType == Variant.Type.Nil
            && previewHandsProperty.VariantType == Variant.Type.Nil);

        Check("o jogo aponta para o controlador, a mesa e os assentos",
            game.GameControllerScene != null && game.BoardPresenter != null && game.Seats != null);
        Check("o jogo tem um resolvedor de poker ligado pelo GameModeHandler", game.Resolver != null);
        Check("a produção só transfere a vez depois da animação aceita",
            game.Resolver?.WaitAnimationsEndToNextTurn == true);
        Check("o showdown reserva leitura das mãos e cerca de oito segundos para o ranking",
            game.Resolver != null && game.Resolver.ShowdownSeconds >= 11.0f);
        Check("cartas e fichas podem ser trocadas em um único recurso",
            game.VisualAssets is { CardScene: not null, ChipScene: not null });
        Check("o pote usa a ficha selecionada nesse recurso",
            game.BoardPresenter?.PotPile?.ChipScene == game.VisualAssets?.ChipScene);
        Check("o apresentador da mesa sabe desenhar cartas",
            game.BoardPresenter != null && game.BoardPresenter.CardScene != null);
        Check("a cena expõe a bancada visual do poker",
            game.ExperienceAuthoring is
            {
                DeckAnchor: not null,
                CommunityCardsAnchor: not null,
                ActionGuideAnchor: not null,
                PreviewCamera: not null,
                FirstPersonHandsPreview: not null,
                FirstPersonCardsPose: not null,
                FirstPersonCard0Pose: not null,
                FirstPersonCard1Pose: not null
            });
        Check("baralho e comunitárias usam os marcadores editáveis",
            game.BoardPresenter?.DeckAnchor == game.ExperienceAuthoring?.DeckAnchor
            && game.BoardPresenter?.CommunityCardsAnchor
               == game.ExperienceAuthoring?.CommunityCardsAnchor);
        Check("PASSAR/DESISTIR e a ação central têm marcadores 3D independentes",
            game.ExperienceAuthoring is
            {
                PassFoldGuideAnchor: not null,
                WagerGuideAnchor: not null
            }
            && !ReferenceEquals(
                game.ExperienceAuthoring.PassFoldGuideAnchor,
                game.ExperienceAuthoring.WagerGuideAnchor));
        var deckUsesReaderFrame = game.BoardPresenter != null && game.Seats != null;
        var canonicalDeck = game.BoardPresenter?.DeckTransformFor(Vector2.Down)
                            ?? Transform3D.Identity;
        for (var seatIndex = 0; deckUsesReaderFrame && seatIndex < game.Seats.GetChildCount();
             seatIndex++)
        {
            if (game.Seats.GetChild(seatIndex) is not Node3D seat)
            {
                deckUsesReaderFrame = false;
                break;
            }

            var seatInBoard = game.BoardPresenter.ToLocal(seat.GlobalPosition);
            var facing = new Vector2(seatInBoard.X, seatInBoard.Z).Normalized();
            var expected = PokerTableLayout.ReaderAlignedFrame(
                canonicalDeck, facing, Vector2.Down);
            deckUsesReaderFrame &= game.BoardPresenter.DeckTransformFor(facing)
                .IsEqualApprox(expected);
        }
        Check("todo assento enxerga o baralho no mesmo frame relativo do assento 1",
            deckUsesReaderFrame);
        var tableSurfacePoint = Vector3.Zero;
        var tableSurfaceNormal = Vector3.Up;
        var hasLiveSurface = game.BoardPresenter != null
                             && game.BoardPresenter.TryGetTableSurface(
                                 game.BoardPresenter.GlobalPosition,
                                 out tableSurfacePoint,
                                 out tableSurfaceNormal);
        Check("o repouso das cartas usa a superficie fisica escalavel da mesa",
            hasLiveSurface
            && game.BoardPresenter.TableSurfaceCollider != null
            && game.BoardPresenter.TableSurfaceMesh != null
            && tableSurfaceNormal.Dot(Vector3.Up) > 0.999f
            && tableSurfacePoint.Y > game.BoardPresenter.GlobalPosition.Y + 0.05f);
        if (game.ExperienceAuthoring != null)
        {
            var previewHand = GD.Load<PackedScene>(
                    "res://Games/Poker/GameController/Views/PokerHand3DView.tscn")
                ?.Instantiate<PokerHand3DView>();
            game.ExperienceAuthoring.ApplyTo(previewHand);
            // The runtime cache is captured in _Ready before preview-only nodes are queued for
            // removal; ApplyTo is the public contract being verified here.
            Check("o marcador visual transfere posição e rotação ao controller em primeira pessoa",
                previewHand != null
                && previewHand.HandPose.Origin.IsFinite()
                && previewHand.HandPose.Basis.GetRotationQuaternion().IsFinite()
                && !previewHand.HandPose.Basis.IsEqualApprox(Basis.Identity));
            Check("o marcador das cartas transfere posição e rotação independentemente",
                previewHand != null
                && previewHand.CardsInHandPose.Origin.IsFinite()
                && previewHand.CardsInHandPose.Basis.GetRotationQuaternion().IsFinite()
                && !previewHand.CardsInHandPose.Basis.IsEqualApprox(Basis.Identity)
                && previewHand.Card0InHandPose.Origin.IsFinite()
                && previewHand.Card1InHandPose.Origin.IsFinite()
                && !previewHand.Card0InHandPose.IsEqualApprox(previewHand.Card1InHandPose));
            previewHand?.Free();
        }
        if (game.ExperienceAuthoring != null && game.BoardPresenter != null)
        {
            var passFrameBefore = game.ExperienceAuthoring.PassFoldGuideTransformFor(
                game.BoardPresenter, Vector2.Down);
            var wagerFrameBefore = game.ExperienceAuthoring.WagerGuideTransformFor(
                game.BoardPresenter, Vector2.Down);
            var passWorldBefore = game.BoardPresenter.GlobalTransform * passFrameBefore;
            var wagerWorldBefore = game.BoardPresenter.GlobalTransform * wagerFrameBefore;
            game.BoardPresenter.TryGetTableSurface(
                passWorldBefore.Origin, out var passSurface, out var passNormal);
            game.BoardPresenter.TryGetTableSurface(
                wagerWorldBefore.Origin, out var wagerSurface, out var wagerNormal);
            Check("os dois grupos são renderizados sobre o tampo físico da mesa",
                Mathf.Abs((passWorldBefore.Origin - passSurface).Dot(passNormal)) < 0.0001f
                && Mathf.Abs((wagerWorldBefore.Origin - wagerSurface).Dot(wagerNormal)) < 0.0001f);
            var wagerMarker = game.ExperienceAuthoring.WagerGuideAnchor;
            var wagerMarkerTransform = wagerMarker?.Transform ?? Transform3D.Identity;
            if (wagerMarker != null)
                wagerMarker.Position += Vector3.Right * 0.037f;
            var passFrameAfter = game.ExperienceAuthoring.PassFoldGuideTransformFor(
                game.BoardPresenter, Vector2.Down);
            var wagerFrameAfter = game.ExperienceAuthoring.WagerGuideTransformFor(
                game.BoardPresenter, Vector2.Down);
            if (wagerMarker != null)
                wagerMarker.Transform = wagerMarkerTransform;
            Check("mover o marcador da ação não desloca PASSAR/DESISTIR",
                passFrameAfter.IsEqualApprox(passFrameBefore)
                && wagerFrameAfter.Origin.DistanceTo(wagerFrameBefore.Origin) > 0.03f);

            var canonical = Vector2.Zero;
            var frame = game.ExperienceAuthoring.ActionGuideTransformFor(
                game.BoardPresenter, Vector2.Down);
            var onBoard = frame * new Vector3(canonical.X, 0.0f, canonical.Y);
            var remapped = game.ExperienceAuthoring.ActionGuideAimPoint(
                game.BoardPresenter, Vector2.Down, new Vector2(onBoard.X, onBoard.Z));
            Check("o clique acompanha a posição visual do arco",
                remapped.DistanceTo(canonical) < 0.0001f);

            var boardSeat0 = game.BoardPresenter.CommunityCardsTransformFor(Vector2.Down);
            var boardSeat1 = game.BoardPresenter.CommunityCardsTransformFor(Vector2.Right);
            var seatTurn = new Basis(Vector3.Up,
                PokerTableLayout.YawTowardCentre(Vector2.Right)
                - PokerTableLayout.YawTowardCentre(Vector2.Down));
            Check("as cartas comunitarias acompanham o mesmo lado de leitura do HUD",
                boardSeat1.Origin.DistanceTo(seatTurn * boardSeat0.Origin) < 0.0001f
                && boardSeat1.Basis.X.Dot((seatTurn * boardSeat0.Basis).X) > 0.9999f
                && boardSeat1.Basis.Z.Dot((seatTurn * boardSeat0.Basis).Z) > 0.9999f);

            var guidePlane = new Node3D { TopLevel = true };
            var aimCamera = new Camera3D();
            AddChild(guidePlane);
            AddChild(aimCamera);
            aimCamera.Current = true;
            guidePlane.GlobalTransform = game.BoardPresenter.GlobalTransform * frame;
            var checkCentre = new Vector3(
                game.ExperienceAuthoring.ActionZoneRadius * 0.45f,
                0.0f,
                -game.ExperienceAuthoring.ActionZoneRadius * 0.45f);
            var checkWorld = guidePlane.ToGlobal(checkCentre);
            var seatEye = game.Seats.GetChild(0)?.GetNodeOrNull<Node3D>("SeatView");
            aimCamera.GlobalPosition = seatEye?.GlobalPosition
                                       ?? checkWorld + new Vector3(0.0f, 0.5f, -0.5f);
            aimCamera.LookAt(checkWorld, Vector3.Up);
            aimCamera.ForceUpdateTransform();
            guidePlane.ForceUpdateTransform();

            var visibleRect = aimCamera.GetViewport().GetVisibleRect();
            Check("o raio usa sempre o centro exato da viewport da câmera",
                AimPlane.ScreenCentre(aimCamera).IsEqualApprox(
                    visibleRect.Position + visibleRect.Size * 0.5f));
            var guideScreenPoint = aimCamera.UnprojectPosition(checkWorld);
            Check("o hover intersecta o mesmo plano elevado que desenha PASSAR/DESISTIR",
                AimPlane.TryAimAt(aimCamera, guidePlane, guideScreenPoint, out var guideHit)
                && guideHit.DistanceTo(new Vector2(checkCentre.X, checkCentre.Z)) < 0.0001f);
            aimCamera.QueueFree();
            guidePlane.QueueFree();
        }

        TestRuntimeActionGuide(game);

        var seatPresenter = game.GetNodeOrNull<PokerSeatPresenter>("SeatPresenter");
        Check("a mesa tem o apresentador de assentos", seatPresenter != null);
        Check("o apresentador de assentos conhece a mesa e os assentos",
            seatPresenter is { BoardPresenter: not null, Seats: not null, CardScene: not null });
        var missingTurnState = game.BetStateOf(null);
        Check("leituras durante um handoff sem dono falham fechadas em vez de lancar excecao",
            game.StackOf(null) == 0
            && game.BetOf(null) == 0
            && game.ChipBankOf(null).Count == 0
            && missingTurnState.Stack == 0
            && missingTurnState.CommittedThisRound == 0);
        Check("pote e bancos recebem valores físicos com a mesma tipografia de giz",
            seatPresenter is
            {
                ChalkFont: not null,
                ChalkValueFontSize: >= 66 and <= 74,
                ChalkValuePixelSize: >= 0.00024f and <= 0.00028f,
                FloatingValueLabelMinimumHeight: >= 0.090f and <= 0.11f,
                FloatingValueLabelClearance: >= 0.020f and <= 0.040f,
                FloatingValueLabelBobDistance: >= 0.006f and <= 0.010f,
                FloatingValueLabelBobSeconds: >= 3.0f and <= 4.5f,
                PotValueLabelOffset: >= 0.055f and <= 0.080f
            });
        Check("os valores flutuantes usam branco puro",
            seatPresenter?.FloatingValueLabelColor is { R: >= 0.99f, G: >= 0.99f, B: >= 0.99f });
        Check("a flutuação desce e volta ao repouso sem saltos",
            Mathf.IsZeroApprox(PokerSeatPresenter.FloatingValueBobOffset(0.0f, 0.0f, 0.004f, 4.0f))
            && Mathf.IsEqualApprox(
                PokerSeatPresenter.FloatingValueBobOffset(2.0f, 0.0f, 0.004f, 4.0f), -0.004f)
            && Mathf.IsZeroApprox(
                PokerSeatPresenter.FloatingValueBobOffset(4.0f, 0.0f, 0.004f, 4.0f)));
        Check("o servidor e a apresentacao usam o mesmo perfil temporal",
            seatPresenter?.PresentationProfile != null
            && ReferenceEquals(seatPresenter.PresentationProfile, game.Resolver?.PresentationProfile));
        Check("a troca de mão reserva recolhimento e embaralhamento antes da próxima distribuição",
            seatPresenter?.PresentationProfile is
            {
                CardReturnSeconds: >= 1.0f,
                CardReturnStagger: >= 0.06f, DeckGatherHoldSeconds: >= 0.2f,
                DeckShuffleSeconds: >= 1.5f, MaximumCollectionCardSlots: >= 20,
                CardCleanupDuration: > 3.0f
            });
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
            seatPresenter is
            {
                ShowdownPairPositionJitter: > 0.0f,
                ShowdownPairAngleJitterDegrees: > 0.0f and <= 8.0f
            });
        Check($"saldo e apostas pendentes ocupam faixas diferentes das cartas "
              + $"(lado {seatPresenter?.StackSideOffset:F3}, dentro {seatPresenter?.StackInset:F3}, "
              + $"gap {seatPresenter?.BankColumnSpacing:F3}, diagonal "
              + $"{seatPresenter?.BankLaneAngleDegrees:F1}°)",
            seatPresenter is
            {
                BetSideOffset: >= 0.0f and <= 0.01f,
                StackSideOffset: >= 0.29f and <= 0.31f,
                StackInset: >= 0.10f and <= 0.13f,
                BankColumnSpacing: >= 0.044f,
                BankLaneAngleDegrees: >= 50.0f and <= 60.0f
            }
            && game.BoardPresenter.Spec.SeatBetRadius >= 0.30f);
        var editableStackAnchors = seatPresenter != null;
        for (var seatIndex = 0; editableStackAnchors && seatIndex < 4; seatIndex++)
        {
            var anchor = seatPresenter.GetNodeOrNull<Marker3D>($"ChipStackAnchor{seatIndex}");
            editableStackAnchors &= anchor != null
                                    && anchor.Position.IsFinite()
                                    && anchor.Basis.IsFinite();
        }
        Check("cada assento expõe uma pilha arrastável e rotacionável no editor",
            editableStackAnchors);
        Check("o turno usa um anel fino junto à borda da mesa",
            seatPresenter is
            {
                TurnRingRadius: >= 0.60f, TurnRingWidth: > 0.0f and <= 0.006f,
                TurnRingArcSteps: >= 12
            }
            && seatPresenter.ActiveTurnRingColor.A <= 0.50f
            && seatPresenter.OccupiedTurnRingColor.A <= 0.32f);
        var turnRingAnchor = seatPresenter?.GetNodeOrNull<Marker3D>("TurnRingAnchor");
        Check("a borda dos turnos tem preview e centro editável no cenário 3D",
            seatPresenter?.EditorPreviewTurnRing == true
            && turnRingAnchor != null
            && turnRingAnchor.Position.IsFinite()
            && turnRingAnchor.Basis.IsFinite());
        Check("o disco branco do dealer foi removido da apresentação",
            typeof(PokerSeatPresenter).GetField("_dealerButton",
                BindingFlags.Instance | BindingFlags.NonPublic) == null);
        Check("a aposta preparada não cria mais um HUD flutuante sobre a mesa",
            typeof(PokerSeatPresenter).GetField("_preparedWagerLabel",
                BindingFlags.Instance | BindingFlags.NonPublic) == null);
        if (seatPresenter != null && game.BoardPresenter != null)
        {
            var spec = game.BoardPresenter.Spec;
            var visualCard = game.BoardPresenter.CommunityCardSpec;
            var edgeCard = PokerTableLayout.BoardPosition(PokerDeal.BoardCount - 1, spec);
            var sideSeatBet = new Vector2(spec.SeatBetRadius, -seatPresenter.BetSideOffset);
            var gap = new Vector2(
                Mathf.Max(0.0f,
                    Mathf.Abs(sideSeatBet.X - edgeCard.X) - visualCard.CardWidth * 0.5f),
                Mathf.Max(0.0f,
                    Mathf.Abs(sideSeatBet.Y - edgeCard.Y) - visualCard.CardLength * 0.5f));
            Check($"até a aposta mais crítica deixa as comunitárias livres ({gap.Length() * 100.0f:F1} cm)",
                gap.Length() > 0.05f);

            var baseSpec = spec;
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
        Check("o embaralhamento intercala e esquadra um único maço",
            game.BoardPresenter is
            {
                ShuffleSplitDistance: >= 0.006f and <= 0.015f,
                ShuffleLift: >= 0.008f, ShuffleHalfYawDegrees: >= 3.0f
            });
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

    private void TestRuntimeActionGuide(PokerGame game)
    {
        const string playerId = "GuideRuntimePlayer";
        var previousTurnOrder = new Godot.Collections.Array(game.TurnOrder);
        game.TurnOrder = new Godot.Collections.Array { playerId };

        var player = new Player { Name = playerId };
        var camera = new GlobalCamera { Current = true };
        AddChild(camera);
        var runtimeSeat = game.SeatFor(playerId)
                          ?? game.Seats?.GetChildOrNull<Marker3D>(0);
        var seatView = runtimeSeat?.GetNodeOrNull<Node3D>("SeatView");
        if (seatView != null)
        {
            camera.GlobalTransform = seatView.GlobalTransform;
            camera.GlobalBasis *= Basis.FromEuler(new Vector3(
                Mathf.DegToRad(game.ExperienceAuthoring?.RestPitchDeg ?? -35.0f),
                0.0f, 0.0f));
        }
        camera.Fov = game.ExperienceAuthoring?.SeatFov ?? 65.0f;
        camera.ForceUpdateTransform();
        game.Camera = camera;
        var hand = GD.Load<PackedScene>(
                "res://Games/Poker/GameController/Views/PokerHand3DView.tscn")
            ?.Instantiate<PokerHand3DView>();
        if (hand == null)
        {
            Check("o guia de ações é gerado no runtime", false);
            player.Free();
            game.TurnOrder = previousTurnOrder;
            return;
        }

        AddChild(hand);
        hand.Setup(game, player);
        game.ExperienceAuthoring?.ApplyTo(hand);
        hand.Refresh(System.Array.Empty<int>(), new List<ActionOption>(), isYourTurn: true);

        var guide = hand.GetNodeOrNull<Node3D>("LocalPokerInteractionGuide");
        var passFold = guide?.GetNodeOrNull<Node3D>("PassFoldGuide");
        var wager = guide?.GetNodeOrNull<Node3D>("WagerGuide");
        var geometryCount = guide?.FindChildren(
            "*", "GeometryInstance3D", recursive: true, owned: false).Count ?? 0;
        Check("o guia de ações é gerado e visível no runtime",
            guide is { Visible: true }
            && passFold is { Visible: true }
            && wager is { Visible: true }
            && geometryCount >= 10);
        Check("os dois guias do runtime ficam sobre o tampo",
            passFold != null && wager != null
            && passFold.GlobalPosition.Y > game.BoardPresenter.GlobalPosition.Y
            && wager.GlobalPosition.Y > game.BoardPresenter.GlobalPosition.Y);
        var passWorld = passFold?.ToGlobal(new Vector3(
            0.0f, 0.004f,
            -hand.ActionZoneRadius * 0.5f))
            ?? Vector3.Zero;
        var wagerWorld = wager?.ToGlobal(new Vector3(
            0.0f, 0.004f,
            hand.ConfirmZoneOuterRadius))
            ?? Vector3.Zero;
        var passCamera = camera.GlobalBasis.Inverse()
                         * (passWorld - camera.GlobalPosition);
        var wagerCamera = camera.GlobalBasis.Inverse()
                          * (wagerWorld - camera.GlobalPosition);
        var verticalHalfFov = Mathf.DegToRad(camera.Fov * 0.5f);
        Check("os dois guias gerados ficam dentro da câmera sentada",
            passCamera.Z < 0.0f && wagerCamera.Z < 0.0f
            && Mathf.Abs(Mathf.Atan2(passCamera.Y, -passCamera.Z)) < verticalHalfFov
            && Mathf.Abs(Mathf.Atan2(wagerCamera.Y, -wagerCamera.Z)) < verticalHalfFov);

        hand.Free();
        player.Free();
        camera.Free();
        game.Camera = null;
        game.TurnOrder = previousTurnOrder;
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

        Check($"todo corpo recua para dentro da cadeira ({worstOffChair * 1000.0f:F0} mm)",
            worstOffChair is >= 0.045f and <= 0.075f);
        Check($"o ponto de vista coincide com os olhos do rig sentado "
              + $"({closestEye:F2} m do centro, {worstEyeOverTable:F2} m de altura)",
            closestEye is > 0.90f and < 1.05f
            && worstEyeOverTable is > 1.00f and < 1.05f);
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

        Check("troca de turno preserva a pose de cartas mantida pelo botao direito",
            view.GetNodeOrNull<PokerHandIdleState>("StateMachine/Idle")?.ClipName
                == PokerClips.GrossIdle
            && view.GetNodeOrNull<PokerHandLookingState>("StateMachine/Looking")?.ClipName
                == PokerClips.GrossIdle);

        Check("a view tem HUD, aviso e rig de mão",
            view.Hud?.NoticeLabel != null && view.HandRig != null
            && view.Hud.NoticeLabel.HorizontalAlignment == HorizontalAlignment.Center
            && view.Hud.NoticeLabel.VerticalAlignment == VerticalAlignment.Center
            && view.Hud.Layer > 3);
        Check("as duas malhas de mão têm encaixes independentes",
            view.CardHandVisualMount != null && view.ChipHandVisualMount != null);
        Check("as cartas abaixadas tem um marcador estavel separado da mao",
            view.CardDownAnchor != null
            && view.CardDownAnchor.GetParent() == view.HandRig
            && view.CardSlots?.TopLevel == true);
        Check("olhar as cartas e preparar o showdown usam uma transicao suave",
            Mathf.IsEqualApprox(view.PeekSpeed, 14.0f)
            && view.CardAttachmentBlendSeconds >= 0.40f
            && PokerClips.CardLookRaiseBlendSeconds >= 0.45f
            && PokerClips.CardLookLowerBlendSeconds >= 0.30f
            && PokerClips.CardLookPlaybackSpeed < 1.0f
            && PokerClips.ShowdownTransitionBlendSeconds
                >= PokerClips.CardLookRaiseBlendSeconds
            && PokerClips.ShowdownPreparationSeconds
                > PokerClips.ShowdownTransitionBlendSeconds);
        Check("os placeholders geométricos das mãos foram removidos",
            view.GetNodeOrNull<Node3D>("HandRig/Hand/CardHandPose/Mesh") == null
            && view.GetNodeOrNull<Node3D>("HandRig/LeftHand/Mesh") == null);

        // The rig is the swap point for the animated hand: when it arrives, the AnimationPlayer is
        // assigned here and nothing else in the feature moves.
        var gestures = new[]
        {
            PokerGesture.PickUpCards, PokerGesture.ThrowChips,
            PokerGesture.Knock, PokerGesture.Fold, PokerGesture.Reveal,
        };

        var productionHands = GD.Load<PackedScene>(
                "res://Games/Poker/Components/Hands/PlayerFirstPersonHands.tscn")
            ?.Instantiate<PlayerFirstPersonHands>();
        if (productionHands != null)
            AddChild(productionHands);
        var missingCoreClips = new[]
            {
                PokerClips.Idle, PokerClips.LookCards, PokerClips.PickUpCards,
                PokerClips.ThrowChips, PokerClips.Knock, PokerClips.RevealCards,
            }
            .Where(clip => productionHands?.Animator?.HasAnimation(clip) != true)
            .ToList();

        Check($"a mão tem os clipes centrais novos e os gestos opcionais "
              + $"({string.Join(", ", missingCoreClips)})",
            productionHands?.Animator != null
            && missingCoreClips.Count == 0);
        Check("os gestos novos estão ligados ao corpo FP de produção",
            productionHands?.ChipsClip == PokerClips.ThrowChips
            && productionHands.KnockClip == PokerClips.Knock
            && productionHands.RevealClip == PokerClips.RevealCards);
        Check("os gestos novos preservam suas durações autoradas e não repetem",
            productionHands?.Animator?.GetAnimation(PokerClips.ThrowChips) is { } betClip
            && productionHands.Animator.GetAnimation(PokerClips.Knock) is { } passClip
            && productionHands.Animator.GetAnimation(PokerClips.RevealCards) is { } revealClip
            && Mathf.Abs((float)betClip.Length - 2.0f) < 0.02f
            && Mathf.Abs((float)passClip.Length - PokerClips.PokerPassDurationSeconds) < 0.02f
            && Mathf.Abs((float)revealClip.Length - PokerClips.ShowdownDurationSeconds) < 0.02f
            && betClip.LoopMode == Animation.LoopModeEnum.None
            && passClip.LoopMode == Animation.LoopModeEnum.None
            && revealClip.LoopMode == Animation.LoopModeEnum.None);

        var handLimits = productionHands?.CameraLock;
        var leftDown = handLimits?.LimitFollowAnglesDegrees(
            new Vector2(0.0f, -30.0f), leftHand: true) ?? new Vector2(float.NaN, float.NaN);
        var leftUpAndOut = handLimits?.LimitFollowAnglesDegrees(
            new Vector2(100.0f, 60.0f), leftHand: true) ?? new Vector2(float.NaN, float.NaN);
        var leftInward = handLimits?.LimitFollowAnglesDegrees(
            new Vector2(-100.0f, 0.0f), leftHand: true) ?? new Vector2(float.NaN, float.NaN);
        var gentleLook = handLimits?.LimitFollowAnglesDegrees(
            new Vector2(5.0f, 5.0f), leftHand: true) ?? new Vector2(float.NaN, float.NaN);
        Check("o limite inferior do braco e exatamente a pose inicial sobre a mesa",
            handLimits != null && Mathf.IsZeroApprox(leftDown.Y));
        Check("o braco esquerdo para antes da cabeca e abre mais para fora do corpo",
            leftUpAndOut.X is > 27.0f and <= 28.0f
            && leftUpAndOut.Y is > 19.0f and <= 20.0f
            && leftInward.X is >= -18.0f and < -17.0f);
        Check("o movimento pequeno do braco continua praticamente igual ao da camera",
            Mathf.Abs(gentleLook.X - 5.0f) < 0.15f
            && Mathf.Abs(gentleLook.Y - 5.0f) < 0.15f);

        var restingCameraBasis = new Basis(Vector3.Right, Mathf.DegToRad(-35.0f));
        var downwardCameraBasis = new Basis(Vector3.Right, Mathf.DegToRad(-65.0f));
        var downwardArmDelta = handLimits?.LimitedWorldCameraDelta(
            restingCameraBasis, downwardCameraBasis, leftHand: true) ?? Basis.Identity;
        Check("olhar abaixo da pose inicial nao empurra a mao para dentro da mesa",
            handLimits != null && downwardArmDelta.IsEqualApprox(Basis.Identity));

        var liveHandCamera = new Node3D
        {
            Rotation = new Vector3(-0.12f, 0.25f, 0.0f),
        };
        AddChild(liveHandCamera);
        var heldCardRoot = new Node3D { Name = "HeldCardRootFollower" };
        AddChild(heldCardRoot);
        productionHands?.BindCardSlots(heldCardRoot);
        var heldCardRootBefore = heldCardRoot.GlobalTransform;
        productionHands?.ConfigureHandCameraModes(
            Transform3D.Identity,
            liveHandCamera,
            FirstPersonHandCameraMode.FollowCamera,
            FirstPersonHandCameraMode.Locked);
        Check("a trava de camera configura cada mao de forma independente",
            productionHands?.CameraLock is
            {
                LeftMode: FirstPersonHandCameraMode.FollowCamera,
                RightMode: FirstPersonHandCameraMode.Locked,
            }
            && productionHands.Skeleton?.FindBone("CC_Base_L_Upperarm") >= 0
            && productionHands.Skeleton.FindBone("CC_Base_R_Upperarm") >= 0);
        var leftHandBone = productionHands?.Skeleton?.FindBone("CC_Base_L_Hand") ?? -1;
        var rightHandBone = productionHands?.Skeleton?.FindBone("CC_Base_R_Hand") ?? -1;
        var leftBefore = leftHandBone >= 0
            ? productionHands.Skeleton.GetBoneGlobalPose(leftHandBone)
            : Transform3D.Identity;
        var rightBefore = rightHandBone >= 0
            ? productionHands.Skeleton.GetBoneGlobalPose(rightHandBone)
            : Transform3D.Identity;
        productionHands?.CameraLock?._ProcessModificationWithDelta(1.0);
        // SkeletonUpdated is the production seam: it fires after every deferred modifier. Emit it
        // explicitly here because this focused test invokes the modifier directly rather than
        // advancing a rendered frame.
        productionHands?.Skeleton?.EmitSignal(Skeleton3D.SignalName.SkeletonUpdated);
        var leftAfter = leftHandBone >= 0
            ? productionHands.Skeleton.GetBoneGlobalPose(leftHandBone)
            : Transform3D.Identity;
        var rightAfter = rightHandBone >= 0
            ? productionHands.Skeleton.GetBoneGlobalPose(rightHandBone)
            : Transform3D.Identity;
        Check("seguir a camera move somente a mao autorizada",
            leftHandBone >= 0
            && rightHandBone >= 0
            && !leftAfter.Basis.IsEqualApprox(leftBefore.Basis)
            && rightAfter.Basis.IsEqualApprox(rightBefore.Basis));
        Check("as cartas seguem a pose final da mao esquerda depois dos modifiers",
            productionHands?.CardGrip != null
            && heldCardRoot.GlobalTransform.IsEqualApprox(
                productionHands.CardGrip.GlobalTransform)
            && !heldCardRoot.GlobalTransform.IsEqualApprox(heldCardRootBefore));
        productionHands?.LockBothHands(immediate: true);
        Check("o inicio da cutscene zera imediatamente qualquer camera herdada",
            productionHands?.CameraLock is
            {
                LeftMode: FirstPersonHandCameraMode.Locked,
                RightMode: FirstPersonHandCameraMode.Locked,
                LeftFollowWeight: 0.0f,
                RightFollowWeight: 0.0f,
            });
        view.SetHandCameraModes(
            FirstPersonHandCameraMode.FollowCamera,
            FirstPersonHandCameraMode.Locked);
        Check("a view expoe uma politica unica e reutilizavel para as duas maos",
            view.LeftHandCameraMode == FirstPersonHandCameraMode.FollowCamera
            && view.RightHandCameraMode == FirstPersonHandCameraMode.Locked);
        view.SetHandCameraModes(
            FirstPersonHandCameraMode.Locked,
            FirstPersonHandCameraMode.Locked);
        liveHandCamera.QueueFree();
        heldCardRoot.QueueFree();
        Check("o gesto de fichas não é cortado pelo antigo limite de 0,35 s",
            (productionHands?.Play(PokerGesture.ThrowChips) ?? 0.0f) > 0.35f);
        Check("pegar as cartas acompanha o tempo completo de olhar e baixar",
            (productionHands?.Play(PokerGesture.PickUpCards) ?? 0.0f)
            + view.PickUpLookSeconds + view.PickUpSettleSeconds >= 3.0f);
        productionHands?.QueueFree();

        var extremeHands = GD.Load<PackedScene>(
                "res://Games/Poker/Components/Hands/PlayerFirstPersonHands.tscn")
            ?.Instantiate<PlayerFirstPersonHands>();
        var extremeCamera = new Node3D { Name = "ExtremeHandLimitCamera" };
        if (extremeHands != null)
            AddChild(extremeHands);
        AddChild(extremeCamera);
        var extremeLiveBasis = new Basis(Vector3.Up, Mathf.DegToRad(100.0f))
                               * new Basis(Vector3.Right, Mathf.DegToRad(25.0f));
        extremeCamera.GlobalBasis = extremeLiveBasis;
        extremeHands?.ConfigureHandCameraModes(
            new Transform3D(restingCameraBasis, Vector3.Zero),
            extremeCamera,
            FirstPersonHandCameraMode.FollowCamera,
            FirstPersonHandCameraMode.Locked);
        var extremeLeftArm = extremeHands?.Skeleton?.FindBone("CC_Base_L_Upperarm") ?? -1;
        var extremeRightArm = extremeHands?.Skeleton?.FindBone("CC_Base_R_Upperarm") ?? -1;
        var extremeLeftBefore = extremeLeftArm >= 0
            ? extremeHands.Skeleton.GetBoneGlobalPose(extremeLeftArm)
            : Transform3D.Identity;
        var extremeRightBefore = extremeRightArm >= 0
            ? extremeHands.Skeleton.GetBoneGlobalPose(extremeRightArm)
            : Transform3D.Identity;
        var expectedWorldLimit = extremeHands?.CameraLock?.LimitedWorldCameraDelta(
            restingCameraBasis, extremeLiveBasis, leftHand: true) ?? Basis.Identity;
        extremeHands?.CameraLock?._ProcessModificationWithDelta(1.0);
        var extremeLeftAfter = extremeLeftArm >= 0
            ? extremeHands.Skeleton.GetBoneGlobalPose(extremeLeftArm)
            : Transform3D.Identity;
        var extremeRightAfter = extremeRightArm >= 0
            ? extremeHands.Skeleton.GetBoneGlobalPose(extremeRightArm)
            : Transform3D.Identity;
        var extremeSkeletonBasis = extremeHands?.Skeleton?.GlobalBasis.Orthonormalized()
                                   ?? Basis.Identity;
        var expectedSkeletonLimit = extremeSkeletonBasis.Inverse()
                                    * expectedWorldLimit
                                    * extremeSkeletonBasis;
        var expectedLeftBasis = expectedSkeletonLimit * extremeLeftBefore.Basis;
        Check("os limites superior e lateral chegam ao osso sem mover a outra mao",
            extremeLeftArm >= 0
            && extremeRightArm >= 0
            && extremeLeftAfter.Basis.IsEqualApprox(expectedLeftBasis)
            && extremeRightAfter.Basis.IsEqualApprox(extremeRightBefore.Basis));
        extremeCamera.QueueFree();
        extremeHands?.QueueFree();

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

        var remoteSeat = controller.GetNodeOrNull<RemoteTransform3D>(
            "LookRig/LookPitch/RemoteSeat");
        var remoteTop = controller.GetNodeOrNull<RemoteTransform3D>("TopRig/RemoteTop");
        Check("o controlador tem o rig do assento", remoteSeat != null);
        Check("o controlador tem o rig de cima", remoteTop != null);
        Check("os rigs remotos nascem inertes até uma câmera ser escolhida",
            remoteSeat is { UpdatePosition: false, UpdateRotation: false, UpdateScale: false }
            && remoteTop is { UpdatePosition: false, UpdateRotation: false, UpdateScale: false });
        Check("os dois rigs são independentes do corpo do jogador",
            controller.GetNode<Node3D>("LookRig").TopLevel
            && controller.GetNode<Node3D>("TopRig").TopLevel);

        var stableHandsFrame = controller.StableSeatViewTransform;
        controller.GetNode<Node3D>("LookRig").Rotation = new Vector3(0.0f, 0.7f, 0.0f);
        controller.GetNode<Node3D>("LookRig/LookPitch").Rotation =
            new Vector3(-0.4f, 0.0f, 0.0f);
        Check("o frame travado das maos nao acompanha a rotacao livre da camera",
            controller.StableSeatViewTransform.IsEqualApprox(stableHandsFrame));

        Check($"o giro do pescoço é limitado ({controller.MaxYawDeg}°)",
            controller.MaxYawDeg is > 0.0f and <= 180.0f);
        Check($"a inclinação é limitada ({controller.MinPitchDeg}° a {controller.MaxPitchDeg}°)",
            Mathf.IsEqualApprox(controller.MinPitchDeg, -65.0f)
            && controller.MinPitchDeg < controller.MaxPitchDeg
            && controller.RestPitchDeg >= controller.MinPitchDeg
            && controller.RestPitchDeg <= controller.MaxPitchDeg);
        Check($"a câmera sentada não soma um deslocamento ao ponto dos olhos "
              + $"({controller.SeatViewOffset})",
            controller.SeatViewOffset.IsZeroApprox());
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
        game.BoardPresenter.TryGetTableSurface(
            game.BoardPresenter.GlobalPosition, out var clothPoint, out _);
        var clothY = clothPoint.Y;
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
                    var slot = view.HeldCardPoseAt(card, peek);

                    // A card is width on its own X and length on its own Z — see PokerCard.Apply.
                    for (var corner = 0; corner < 4; corner++)
                    {
                        var local = new Vector3(
                            (corner % 2 == 0 ? -0.5f : 0.5f) * spec.CardWidth,
                            0.0f,
                            (corner < 2 ? -0.5f : 0.5f) * spec.CardLength);

                        var inHand = view.HandPose * slot * local;
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

        var sharedDefaults = new SeatedTableController();
        Check("o poker herda o enquadramento padrão do controlador sentado",
            Mathf.IsEqualApprox(poker.SeatFov, sharedDefaults.SeatFov)
            && poker.SeatViewOffset.IsEqualApprox(sharedDefaults.SeatViewOffset)
            && Mathf.IsEqualApprox(poker.MinPitchDeg, -65.0f)
            && Mathf.IsEqualApprox(poker.MinPitchDeg, sharedDefaults.MinPitchDeg)
            && Mathf.IsEqualApprox(poker.RestPitchDeg, sharedDefaults.RestPitchDeg)
            && Mathf.IsEqualApprox(poker.TopFov, sharedDefaults.TopFov)
            && Mathf.IsEqualApprox(poker.TopHeight, sharedDefaults.TopHeight));
        Check("o dominó preserva o enquadramento próprio definido em sua cena",
            !Mathf.IsEqualApprox(domino.SeatFov, sharedDefaults.SeatFov)
            && !Mathf.IsEqualApprox(domino.TopHeight, sharedDefaults.TopHeight));
        sharedDefaults.Free();

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
            hand is
            {
                Crosshair: not null,
                InteractionZoneCenterRadius: >= 0.55f and <= 0.59f,
                ActionZoneRadius: >= 0.10f and <= 0.13f,
                ConfirmZoneCenterRadius: >= 0.30f and <= 0.34f,
                ConfirmZoneInnerRadius: >= 0.06f and <= 0.08f,
                ConfirmZoneOuterRadius: >= 0.10f and <= 0.12f,
                ConfirmLabelSpanPi: >= 0.10f and <= 0.16f
            }
            && hand.ConfirmZoneOuterRadius > hand.ConfirmZoneInnerRadius
            && Mathf.IsEqualApprox(
                hand.ConfirmZoneCenterRadius, PokerLayoutSpec.Default.SeatBetRadius)
            && hand.ConfirmZoneCenterRadius + hand.ConfirmZoneOuterRadius
               < hand.InteractionZoneCenterRadius - hand.ActionZoneRadius
            && typeof(PokerHand3DView).GetMethod("HandleTableClick")?.GetParameters().Length == 0);
        Check("um único arco reúne CALL, AUTO, APOSTAR e o hold de ALL-IN",
            hand is
            {
                CallHoverOpacity: >= 0.30f and <= 0.38f,
                CallClickMaxSeconds: >= 0.30f and <= 0.40f,
                CallLabelCycleSeconds: >= 1.9f and <= 2.1f,
                CallLabelFadeSeconds: >= 0.18f and <= 0.32f,
                AllInHoldSeconds: >= 1.4f and <= 1.6f,
                AllInVisualDelaySeconds: >= 0.30f and <= 0.40f,
                AllInHoldOpacity: > 0.4f and <= 0.7f
            }
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
        Check("o clique rápido não mostra a barra; o hold de 1,5 segundo a completa",
            PokerHand3DView.AllInHoldVisualProgress(0.12f, 0.35f, 1.5f) == 0.0f
            && PokerHand3DView.AllInHoldVisualProgress(0.35f, 0.35f, 1.5f) == 0.0f
            && PokerHand3DView.AllInHoldVisualProgress(0.36f, 0.35f, 1.5f) > 0.0f
            && PokerHand3DView.AllInHoldVisualProgress(1.5f, 0.35f, 1.5f) > 0.99f);
        Check("o verde começa em zero e percorre o mesmo arco em todas as cadeiras",
            PokerHand3DView.SectorProgressUv(0, 20, outer: false) == Vector2.Zero
            && Mathf.IsEqualApprox(PokerHand3DView.SectorProgressUv(10, 20, true).X, 0.5f)
            && PokerHand3DView.SectorProgressUv(20, 20, true) == Vector2.One);
        Check("a confirmação curta e legível agora se chama APOSTAR",
            PokerHand3DView.ConfirmBetLabelText == "APOSTAR");
        Check("o texto curvo das acoes corre da esquerda para a direita",
            PokerHand3DView.CurvedLabelAngle(0, 4, 0.4f) < 0.0f
            && PokerHand3DView.CurvedLabelAngle(3, 4, 0.4f) > 0.0f);
        var curvedCentreBasis = PokerHand3DView.CurvedLabelBasis(
            Vector2.Down, Vector2.Right, 0.0f);
        Check("as letras curvas acompanham a orientacao legivel do HUD",
            curvedCentreBasis.X.Dot(Vector3.Right) > 0.999f
            && curvedCentreBasis.Y.Dot(Vector3.Back) > 0.999f
            && curvedCentreBasis.Z.Dot(Vector3.Down) > 0.999f);
        Check("CALL só aceita uma liberação realmente rápida",
            PokerHand3DView.IsQuickCallRelease(0.12f, hand?.CallClickMaxSeconds ?? 0.0f)
            && PokerHand3DView.IsQuickCallRelease(0.35f, hand?.CallClickMaxSeconds ?? 0.0f)
            && !PokerHand3DView.IsQuickCallRelease(0.36f, hand?.CallClickMaxSeconds ?? 0.0f)
            && !PokerHand3DView.IsQuickCallRelease(1.5f, hand?.CallClickMaxSeconds ?? 0.0f)
            && !PokerHand3DView.IsQuickCallRelease(1.0f, hand?.CallClickMaxSeconds ?? 0.0f));
        Check("os comandos usam giz procedural, fonte grande e divisões finas",
            hand is
            {
                ChalkFont: not null, ChalkHoverShader: not null,
                ChalkGuideFontSize: >= 68, ChalkGuidePixelSize: >= 0.00018f,
                ChalkHoverOpacity: > 0.0f and <= 0.30f,
                InteractionGuideThickness: <= 0.0015f
            });
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
            hud.Root != null && hud.HintsLabel != null && hud.NoticeLabel != null
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

        // Use a real local eye so the upright phase can prove that it faces the player, not merely
        // an arbitrary table axis. Runtime peers each perform this same presentation locally.
        var camera = new GlobalCamera { Current = true };
        game.AddChild(camera);
        var seat0Frame = board.CommunityCardsTransformFor(Vector2.Down);
        var readerDirection = (board.GlobalBasis * seat0Frame.Basis.Z).Normalized();
        camera.GlobalPosition = board.ToGlobal(seat0Frame.Origin)
                                + readerDirection * 0.9f + Vector3.Up * 0.65f;
        game.SetCamera(camera);
        Check("o giro padrão é deliberadamente suave e não um estalo",
            board.VerticalRevealSpinSeconds >= 1.4f);
        board.FlipSeconds = 0.35f;
        board.VerticalRevealSpinSeconds = 0.80f;
        board.VerticalRevealBeforeSpinSeconds = 0.15f;
        board.VerticalRevealSpinSmoothness = 0.85f;
        board.VerticalRevealAfterSpinSeconds = 0.60f;
        var linearQuarterSpin = PokerBoardPresenter.EaseVerticalRevealSpin(0.25f, 0.0f);
        var softQuarterSpin = PokerBoardPresenter.EaseVerticalRevealSpin(0.25f, 1.0f);
        Check("a suavidade do Inspector altera a curva sem alterar o tempo total",
            Mathf.Abs(linearQuarterSpin - 0.25f) < 0.0001f
            && softQuarterSpin > 0.0f
            && softQuarterSpin < linearQuarterSpin
            && Mathf.Abs(PokerBoardPresenter.EaseVerticalRevealSpin(1.0f, 1.0f) - 1.0f)
               < 0.0001f);

        // A hand is dealt with nothing turned over yet.
        board.Sync(new List<int>(), 0, handNumber: 1, PokerStreet.Preflop);

        Check($"as cinco comunitárias entram no começo da mão ({VisibleCards(board)})",
            VisibleCards(board) == PokerDeal.BoardCount);
        Check("elas começam a mão ainda chegando", !board.Settled);

        Settle(board);
        Check("depois de entrarem, a mesa assenta", board.Settled);
        Check($"e todas estão de costas ({FaceUpCards(board)} viradas)", FaceUpCards(board) == 0);
        var flatReference = board.BoardCardNodeAt(0).GlobalBasis;
        var flatAligned = true;
        for (var index = 1; index < PokerDeal.BoardCount; index++)
        {
            var basis = board.BoardCardNodeAt(index).GlobalBasis;
            flatAligned &= basis.X.Normalized().Dot(flatReference.X.Normalized()) > 0.9999f
                           && basis.Z.Normalized().Dot(flatReference.Z.Normalized()) > 0.9999f;
        }
        Check("as cinco cartas deitadas formam uma fileira perfeitamente reta", flatAligned);

        // The flop turns exactly three.
        board.Sync(new List<int> { 0, 1, 2 }, 0, 1, PokerStreet.Flop);

        board._Process(
            board.FlipSeconds + board.VerticalRevealBeforeSpinSeconds * 0.5f);
        var waitingToSpin = board.BoardCardNodeAt(0);
        board.TryGetTableSurface(
            waitingToSpin.GlobalPosition, out _, out var preSpinNormal);
        var preSpinTowardEye = camera.GlobalPosition - waitingToSpin.GlobalPosition;
        preSpinTowardEye -= preSpinNormal * preSpinTowardEye.Dot(preSpinNormal);
        Check("o tempo antes do giro mantém as cartas em pé e de frente",
            (-waitingToSpin.GlobalBasis.Z.Normalized()).Dot(preSpinNormal.Normalized()) > 0.98f
            && waitingToSpin.GlobalBasis.Y.Normalized()
                   .Dot(preSpinTowardEye.Normalized()) > 0.95f);

        // At the middle of its authored full turn, the first card faces away; after completing the
        // revolution it returns to the exact shared camera-facing plane.
        board._Process(
            board.VerticalRevealBeforeSpinSeconds * 0.5f
            + board.VerticalRevealSpinSeconds * 0.5f);
        var first = board.BoardCardNodeAt(0);
        board.TryGetTableSurface(first.GlobalPosition, out _, out var spinNormal);
        var firstTowardEye = camera.GlobalPosition - first.GlobalPosition;
        firstTowardEye -= spinNormal * firstTowardEye.Dot(spinNormal);
        var midSpinFacing = first.GlobalBasis.Y.Normalized()
            .Dot(firstTowardEye.Normalized());
        Check($"a carta revelada dá uma volta real no próprio eixo "
              + $"({midSpinFacing:F3} no meio do giro)",
            midSpinFacing < -0.95f);
        var synchronizedMidTurn = true;
        for (var index = 1; index < 3; index++)
        {
            var basis = board.BoardCardNodeAt(index).GlobalBasis;
            synchronizedMidTurn &= basis.X.Normalized()
                                       .Dot(first.GlobalBasis.X.Normalized()) > 0.9999f
                                   && basis.Y.Normalized()
                                       .Dot(first.GlobalBasis.Y.Normalized()) > 0.9999f;
        }
        Check("as três cartas do flop levantam e giram no mesmo quadro", synchronizedMidTurn);

        var allFrontTime = board.FlipSeconds + board.VerticalRevealBeforeSpinSeconds
                          + board.VerticalRevealSpinSeconds + 0.04f;
        var elapsed = board.FlipSeconds + board.VerticalRevealBeforeSpinSeconds
                     + board.VerticalRevealSpinSeconds * 0.5f;
        AdvanceFor(board, allFrontTime - elapsed);
        var upright = true;
        var facingCamera = true;
        var tangentToFelt = true;
        var aligned = true;
        var displayReference = board.BoardCardNodeAt(0).GlobalBasis;
        for (var index = 0; index < 3; index++)
        {
            var card = board.BoardCardNodeAt(index);
            board.TryGetTableSurface(card.GlobalPosition, out var surfacePoint, out var normal);
            normal = normal.Normalized();
            var towardEye = camera.GlobalPosition - card.GlobalPosition;
            towardEye -= normal * towardEye.Dot(normal);
            towardEye = towardEye.Normalized();
            upright &= (-card.GlobalBasis.Z.Normalized()).Dot(normal) > 0.98f;
            facingCamera &= card.GlobalBasis.Y.Normalized().Dot(towardEye) > 0.95f;
            aligned &= card.GlobalBasis.X.Normalized()
                           .Dot(displayReference.X.Normalized()) > 0.9999f
                       && card.GlobalBasis.Y.Normalized()
                           .Dot(displayReference.Y.Normalized()) > 0.9999f;
            tangentToFelt &= card.TryGetVisibleProjectionRange(normal, out var minimum, out _)
                             && Mathf.Abs(minimum - surfacePoint.Dot(normal)
                                 - board.RevealSurfaceClearance) < 0.001f;
        }
        Check("o flop levanta as três cartas e as mantém na vertical", upright);
        Check("as cartas em pé compartilham um único plano reto, sem formar leque", aligned);
        Check("as cartas em pé ficam diretamente de frente para a câmera local", facingCamera);
        Check("a borda inferior das cartas em pé permanece apoiada no feltro", tangentToFelt);

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

    private static void AdvanceFor(PokerBoardPresenter board, float seconds)
    {
        var frames = Mathf.CeilToInt(Mathf.Max(0.0f, seconds) * 60.0f);
        for (var frame = 0; frame < frames; frame++)
            board._Process(1.0 / 60.0);
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
    /// and a third-person one. Both imported rigs now contain the authored clips; keep their
    /// semantic names reachable and distinct so gestures cannot silently share an animation.
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
            && PokerClips.ForAction("raise") == PokerGesture.ThrowChips);

        Check("passar, apostar e desistir reservam toda a duração de seus gestos",
            Mathf.Abs(PokerClips.DurationForAction(PokerActionKind.Check)
                      - PokerClips.PokerPassDurationSeconds) < 0.001f
            && Mathf.Abs(PokerClips.DurationForAction(PokerActionKind.Call)
                         - PokerClips.PokerBetDurationSeconds) < 0.001f
            && Mathf.Abs(PokerClips.DurationForAction(PokerActionKind.Raise)
                         - PokerClips.PokerBetDurationSeconds) < 0.001f
            && Mathf.Abs(PokerClips.DurationForAction(PokerActionKind.Fold)
                         - PokerClips.PokerFoldDurationSeconds) < 0.001f);

        Check("uma ação que não é um gesto não anima nada",
            PokerClips.ForAction("deal") == PokerGesture.None
            && PokerClips.ForAction("show") == PokerGesture.None
            && PokerClips.ForAction("showdown") == PokerGesture.None
            && PokerClips.ForAction("") == PokerGesture.None);

        // The body gesture is replayed by the seat presenter from the accepted public context, so
        // the production Player hook must remain callable from both semantic overloads.
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
        Check("o som duplo foi espaçado para os dois contatos de PokerPass",
            game.SeatPresenter?.KnockSound?.GetLength() is > 0.60 and < 0.64);
        game.SeatPresenter?.ScheduleLocalPokerPass(
            "1", turnToken: 7, gestureDuration: PokerClips.PokerPassDurationSeconds);
        game.SeatPresenter?._Process(0.45);
        Check("o som de passar aguarda o primeiro contato com a madeira",
            game.SeatPresenter?.ScheduledKnockCount == 1);
        game.SeatPresenter?._Process(0.08);
        Check("uma batida otimista permanece silenciosa antes da confirmacao",
            game.SeatPresenter?.ScheduledKnockCount == 1);
        game.SeatPresenter?.ConfirmLocalPokerPass("1", turnToken: 7);
        game.SeatPresenter?._Process(0.001);
        Check("o som de passar dispara no contato autorado",
            game.SeatPresenter?.ScheduledKnockCount == 0);
        game.SeatPresenter?.ScheduleLocalPokerPass(
            "1", turnToken: 8, gestureDuration: PokerClips.PokerPassDurationSeconds);
        Check("uma confirmacao de outro turno nao reaproveita a batida antiga",
            game.SeatPresenter?.ConfirmLocalPokerPass("1", turnToken: 9) == false
            && game.SeatPresenter.ScheduledKnockCount == 1);
        game.SeatPresenter?.CancelLocalPokerPass("1", turnToken: 8);
        game.SeatPresenter?._Process(1.0);
        Check("uma acao recusada cancela a batida antes de ela tocar",
            game.SeatPresenter?.ScheduledKnockCount == 0);
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

    /// <summary>Path-loaded production assets must be rooted in the explicit export manifest.</summary>
    private void TestRuntimeResourceManifest()
    {
        var manifest = GD.Load<RuntimeResourceManifest>(
            "res://Shared/Resources/RuntimeResourceManifest.tres");
        var exportedPaths = manifest?.Resources
            .Where(resource => resource != null)
            .Select(resource => resource.ResourcePath)
            .ToHashSet();

        var dynamicallyLoadedAssets = new[]
        {
            PokerChipAssetMeshes.AssetPath,
            PokerChipMeshes.AssetPath,
            PokerCardAssetMeshes.AssetPath,
            PokerCardFaces.AtlasPath,
            PokerCardFaces.PreferredBackPath,
            PokerCardFaces.FallbackBackPath,
        };
        foreach (var path in dynamicallyLoadedAssets.Where(
                     path => ResourceLoader.Exists(path)))
        {
            Check($"o recurso dinamico {path.GetFile()} entra na exportacao Release",
                exportedPaths?.Contains(path) == true);
        }
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
