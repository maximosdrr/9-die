using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Godot;

/// <summary>
/// Exercises the movement trust boundary without timing, sockets or a rendered level.
/// </summary>
public partial class PlayerMovementSecurityTest : Node
{
    private int _passed;
    private int _failed;

    public override void _Ready()
    {
        GD.Print("=== Teste do movimento autoritativo ===");

        TestInputValidation();
        TestSequenceRateAndTimeout();
        TestPredictionCorrectionRetargeting();
        TestOwningSnapshotPresentation();
        TestNativeWindowHandleConversion();
        TestCameraMountLifecycle();
        TestRpcContract();
        TestSeatApproachTransition();
        TestCameraLookContract();
        TestPokerPoseRpcContract();
        TestMovementModeRateLimiter();
        TestProfileValidation();
        TestProfileRpcContract();
        TestSceneReplicationContract();

        GD.Print($"=== {_passed} passaram, {_failed} falharam ===");
        GetTree().Quit(_failed > 0 ? 1 : 0);
    }

    private void TestInputValidation()
    {
        var guard = new PlayerMovementInputGuard(expectedPeerId: 7, maxPacketsPerSecond: 10);

        Check("um peer não pode mover o corpo de outro",
            !guard.TryAccept(8, 1, Vector2.Up, 0.0f, 10));
        Check("o peer correto envia uma intenção finita",
            guard.TryAccept(7, 1, new Vector2(0.6f, 0.8f), 0.25f, 10));
        Check("a intenção aceita nunca excede magnitude um",
            guard.LatestIntent.Length() <= 1.00001f);
        Check("vetor acima da magnitude permitida é rejeitado",
            !guard.TryAccept(7, 2, new Vector2(1.1f, 0.0f), 0.0f, 11));
        Check("NaN no vetor é rejeitado",
            !guard.TryAccept(7, 3, new Vector2(float.NaN, 0.0f), 0.0f, 12));
        Check("NaN na rotação é rejeitado",
            !guard.TryAccept(7, 4, Vector2.Zero, float.NaN, 13));
        Check("snapshot não finito é rejeitado",
            !PlayerMovementProtocol.IsValidSnapshot(
                new Vector3(float.PositiveInfinity, 0.0f, 0.0f), 0.0f, Vector3.Zero));
        Check("o host listen passa pela mesma validação de identidade",
            PlayerMovementProtocol.IsExpectedSender(
                PlayerMovementProtocol.ServerPeerId, PlayerMovementProtocol.ServerPeerId));
    }

    private void TestSequenceRateAndTimeout()
    {
        var sequenceGuard = new PlayerMovementInputGuard(4, 10);
        Check("a primeira sequência é aceita",
            sequenceGuard.TryAccept(4, 1, Vector2.Right, 0.0f, 100));
        Check("sequência repetida é rejeitada",
            !sequenceGuard.TryAccept(4, 1, Vector2.Left, 0.0f, 101));
        Check("sequência anterior é rejeitada",
            !sequenceGuard.TryAccept(4, 0, Vector2.Left, 0.0f, 102));
        Check("a intenção expira quando o cliente para de enviar",
            sequenceGuard.ActiveIntent(
                100 + PlayerMovementProtocol.DefaultInputTimeoutMilliseconds + 1,
                PlayerMovementProtocol.DefaultInputTimeoutMilliseconds) == Vector2.Zero);

        var rateGuard = new PlayerMovementInputGuard(9, maxPacketsPerSecond: 2);
        Check("primeiro pacote cabe no orçamento",
            rateGuard.TryAccept(9, 1, Vector2.Zero, 0.0f, 0));
        Check("segundo pacote cabe no orçamento",
            rateGuard.TryAccept(9, 2, Vector2.Zero, 0.0f, 1));
        Check("rajada acima do orçamento é rejeitada",
            !rateGuard.TryAccept(9, 3, Vector2.Zero, 0.0f, 2));
        Check("o orçamento reabre na janela seguinte",
            rateGuard.TryAccept(9, 4, Vector2.Zero, 0.0f, 1_000));
    }

    private void TestPredictionCorrectionRetargeting()
    {
        var correction = new PlayerPredictionCorrection();
        correction.Retarget(new Vector3(0.4f, 0.0f, -0.2f));

        // A later snapshot says that the peer is already aligned. It must cancel the stale
        // correction instead of leaving movement after input has stopped.
        correction.Retarget(Vector3.Zero);
        Check("snapshot alinhado cancela correção posicional antiga no peer",
            correction.PositionError == Vector3.Zero);

        correction.Retarget(new Vector3(0.6f, 0.0f, 0.0f));
        var firstPositionStep = correction.ConsumePositionStep(0.2f);
        correction.Retarget(new Vector3(-0.1f, 0.0f, 0.0f));
        var newestPositionStep = correction.ConsumePositionStep(0.2f);
        Check("snapshot novo substitui correção posicional pendente",
            firstPositionStep.IsEqualApprox(new Vector3(0.2f, 0.0f, 0.0f))
            && newestPositionStep.IsEqualApprox(new Vector3(-0.1f, 0.0f, 0.0f))
            && correction.PositionError == Vector3.Zero);

        const float stoppedLocalYaw = 0.75f;
        var yawAfterOppositeSnapshot = PlayerMovementProtocol.ResolveOwningClientYaw(
            stoppedLocalYaw,
            serverYaw: -1.2f);
        var yawAfterAlignedSnapshot = PlayerMovementProtocol.ResolveOwningClientYaw(
            yawAfterOppositeSnapshot,
            serverYaw: 0.0f);
        Check("snapshot de yaw nunca move a câmera local após o mouse parar",
            Mathf.IsEqualApprox(yawAfterOppositeSnapshot, stoppedLocalYaw)
            && Mathf.IsEqualApprox(yawAfterAlignedSnapshot, stoppedLocalYaw));
        Check("yaw local inválido recua para o valor seguro do servidor",
            Mathf.IsEqualApprox(
                PlayerMovementProtocol.ResolveOwningClientYaw(float.NaN, -0.4f),
                -0.4f));

        var predictedAtAcknowledgement = new Vector3(2.0f, 0.0f, 1.0f);
        var currentPrediction = new Vector3(4.0f, 0.0f, 1.5f);
        var serverAtAcknowledgement = new Vector3(1.5f, 0.0f, 1.0f);
        correction.Retarget(serverAtAcknowledgement - predictedAtAcknowledgement);
        var correctedCurrent = currentPrediction + correction.ConsumePositionStep(10.0f);
        var unacknowledgedDelta = currentPrediction - predictedAtAcknowledgement;
        Check("reconciliação preserva movimento posterior ao ack",
            correctedCurrent.IsEqualApprox(serverAtAcknowledgement + unacknowledgedDelta));
    }

    private void TestOwningSnapshotPresentation()
    {
        var reconcile = typeof(Player).GetMethod(
            "ReconcilePrediction",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var applyCorrection = typeof(Player).GetMethod(
            "ApplyPredictionCorrection",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var scene = GD.Load<PackedScene>("res://World/Player/Player.tscn");
        var player = scene?.Instantiate<Player>();

        if (player == null || reconcile == null || applyCorrection == null)
        {
            Check("fluxo real de reconciliação do peer está disponível", false);
            player?.Free();
            return;
        }

        // A different authority keeps this fixture on the observer path while it enters the tree;
        // the calls below then exercise exactly the owning-client reconciliation methods.
        player.SetMultiplayerAuthority(7);
        AddChild(player);
        player.SetPhysicsProcess(false);
        player.PredictionCorrectionSpeed = 4.0f;
        player.HardCorrectionDistance = 1.5f;
        player.GlobalPosition = Vector3.Zero;
        player.GlobalRotation = new Vector3(0.0f, 0.75f, 0.0f);

        reconcile.Invoke(player, new object[]
        {
            0,
            new Vector3(0.4f, 0.0f, 0.0f),
            -1.2f,
            Vector3.Zero
        });
        applyCorrection.Invoke(player, new object[] { 0.05f });
        var positionAfterFirstStep = player.GlobalPosition;
        var yawAfterOppositeSnapshot = player.GlobalRotation.Y;
        Check("fluxo real aplica somente o passo posicional permitido",
            positionAfterFirstStep.IsEqualApprox(new Vector3(0.2f, 0.0f, 0.0f)));

        // The newest snapshot is aligned and carries a different yaw. It must cancel remaining
        // positional debt and must not become another local camera input.
        reconcile.Invoke(player, new object[]
        {
            0,
            positionAfterFirstStep,
            0.0f,
            Vector3.Zero
        });
        applyCorrection.Invoke(player, new object[] { 1.0f });

        Check("fluxo real do peer para quando o snapshot mais novo está alinhado",
            player.GlobalPosition.IsEqualApprox(positionAfterFirstStep));
        Check("fluxo real do peer não reaplica yaw remoto como input local",
            Mathf.IsEqualApprox(yawAfterOppositeSnapshot, 0.75f)
            && Mathf.IsEqualApprox(player.GlobalRotation.Y, 0.75f));

        // Even a teleport-sized position correction preserves local mouselook. Server yaw remains
        // authoritative for its own body and for observers, not for this owner camera.
        var hardCorrectionPosition = positionAfterFirstStep + new Vector3(2.0f, 0.0f, 0.0f);
        reconcile.Invoke(player, new object[]
        {
            0,
            hardCorrectionPosition,
            -2.0f,
            Vector3.Zero
        });
        Check("hard correction reposiciona sem girar a câmera do dono",
            player.GlobalPosition.IsEqualApprox(hardCorrectionPosition)
            && Mathf.IsEqualApprox(player.GlobalRotation.Y, 0.75f));

        player.Free();
    }

    private void TestNativeWindowHandleConversion()
    {
        Check("handle nativo nulo é rejeitado",
            !TvShareButton.TryConvertNativeWindowHandle(0, out var nullHandle)
            && nullHandle == IntPtr.Zero);
        Check("handle nativo válido preserva o valor",
            TvShareButton.TryConvertNativeWindowHandle(42, out var validHandle)
            && validHandle == new IntPtr(42));

        var beyond32Bits = (long)uint.MaxValue + 1L;
        var acceptedBeyond32Bits = TvShareButton.TryConvertNativeWindowHandle(
            beyond32Bits,
            out var wideHandle);
        Check("conversão de handle respeita a largura do processo",
            IntPtr.Size == sizeof(long)
                ? acceptedBeyond32Bits && wideHandle.ToInt64() == beyond32Bits
                : !acceptedBeyond32Bits && wideHandle == IntPtr.Zero);
    }

    private void TestCameraMountLifecycle()
    {
        var rig = new Node3D { Name = "CameraTestRig" };
        var mount = new RemoteTransform3D
        {
            Name = "CameraMount",
            UpdatePosition = false,
            UpdateRotation = false,
            UpdateScale = false
        };
        var camera = new GlobalCamera { Name = "CameraUnderTest" };

        AddChild(rig);
        rig.AddChild(mount);
        AddChild(camera);

        camera.TransitionTo(mount);
        Check("mount local só ativa quando a câmera assume controle",
            camera.CurrentRemote == mount
            && mount.RemotePath == camera.GetPath()
            && mount.UpdatePosition
            && mount.UpdateRotation
            && !mount.UpdateScale);

        camera.TransitionTo(null);
        Check("liberar a câmera desativa o mount anterior",
            camera.CurrentRemote == null
            && !mount.UpdatePosition
            && !mount.UpdateRotation
            && !mount.UpdateScale);

        camera.Free();
        rig.Free();
    }

    private void TestRpcContract()
    {
        CheckMovementRpc("SubmitMovementInput");
        CheckMovementRpc("ReceiveMovementSnapshot");

        var modeRequest = typeof(Player).GetMethod("RequestMovementModeOnServer",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var rpc = modeRequest?.GetCustomAttribute<RpcAttribute>();
        var parameters = modeRequest?.GetParameters();
        Check("sentar/levantar usa pedido confiável validado no servidor",
            rpc != null
            && rpc.Mode == MultiplayerApi.RpcMode.AnyPeer
            && rpc.TransferMode == MultiplayerPeer.TransferModeEnum.Reliable);
        Check("o pedido de assento não transporta coordenadas",
            parameters is { Length: 1 } && parameters[0].ParameterType == typeof(bool));

        var modeSnapshot = typeof(Player).GetMethod("ReceiveMovementModeFromServer",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var modeSnapshotRpc = modeSnapshot?.GetCustomAttribute<RpcAttribute>();
        Check("a posição final da cadeira chega de forma confiável em host e clientes",
            modeSnapshotRpc != null
            && modeSnapshotRpc.TransferMode == MultiplayerPeer.TransferModeEnum.Reliable
            && modeSnapshotRpc.Mode == MultiplayerApi.RpcMode.AnyPeer);
    }

    private void TestMovementModeRateLimiter()
    {
        var player = new Player();
        var acceptedWindow = true;
        for (var request = 0; request < Player.MovementModeRequestsPerSecond; request++)
            acceptedWindow &= player.TryConsumeMovementModeRequest(71, 100);

        Check("pedidos reliable de sentar/levantar têm orçamento por peer",
            acceptedWindow && !player.TryConsumeMovementModeRequest(71, 100));
        Check("orçamento de sentar/levantar é independente por peer",
            player.TryConsumeMovementModeRequest(72, 100));
        Check("janela de sentar/levantar reabre deterministicamente",
            player.TryConsumeMovementModeRequest(71, 1_100));

        for (var peer = 1_000;
            peer < 1_000 + Player.MaximumTrackedMovementModePeers * 2;
            peer++)
        {
            player.TryConsumeMovementModeRequest(peer, 1_100);
        }

        Check("tabela de rate limit de movimento permanece limitada",
            player.TrackedMovementModePeerCount <= Player.MaximumTrackedMovementModePeers);
        player.Free();
    }

    private void TestSeatApproachTransition()
    {
        var player = GD.Load<PackedScene>("res://World/Player/Player.tscn")
            .Instantiate<Player>();
        player.Position = Vector3.Zero;
        player.Rotation = Vector3.Zero;
        AddChild(player);
        var target = new Vector3(0.8f, 0.0f, -0.4f);
        player.BeginMovementModeTransition(target, 1.2f, 0.5f);
        player._Process(0.1);

        var movedWithoutTeleporting = player.Position.DistanceTo(Vector3.Zero) > 0.01f
            && player.Position.DistanceTo(target) > 0.01f;
        for (var frame = 0; frame < 5; frame++)
            player._Process(0.1);

        Check("host e cliente percorrem a aproximação antes de sentar",
            movedWithoutTeleporting
            && player.Position.DistanceTo(target) < 0.001f
            && Mathf.Abs(Mathf.AngleDifference(player.Rotation.Y, 1.2f)) < 0.001f
            && !player.MovementModeTransitionActive);
        RemoveChild(player);
        player.Free();
    }

    private void TestCameraLookContract()
    {
        foreach (var methodName in new[] { "SubmitCameraLook", "ReceiveCameraLook" })
        {
            var method = typeof(Player).GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            var rpc = method?.GetCustomAttribute<RpcAttribute>();
            Check($"{methodName} replica o pescoço sem congestionar movimento",
                rpc != null
                && rpc.Mode == MultiplayerApi.RpcMode.AnyPeer
                && !rpc.CallLocal
                && rpc.TransferMode == MultiplayerPeer.TransferModeEnum.UnreliableOrdered
                && rpc.TransferChannel == 4);
        }

        var visualScene = GD.Load<PackedScene>(
            "res://World/Player/Components/CharacterVisual.tscn");
        var visual = visualScene?.Instantiate<CharacterVisual>();
        if (visual != null)
            AddChild(visual);

        visual?.SetCameraLook(Mathf.Pi, -Mathf.Pi);
        Check("o movimento do pescoço usa um osso real e limites humanos",
            visual?.NeckModifier?.BoneName == "CC_Base_NeckTwist02"
            && visual.Skeleton.FindBone(visual.NeckModifier.BoneName) >= 0
            && visual.MaximumNeckYawDegrees is > 0.0f and <= 90.0f
            && visual.MinimumNeckPitchDegrees >= -60.0f
            && visual.MaximumNeckPitchDegrees <= 60.0f);
        Check("a inclinação vertical da câmera é convertida para o eixo do rig",
            CameraNeckModifier.RigPitchFromCamera(-0.35f) > 0.0f
            && CameraNeckModifier.RigPitchFromCamera(0.35f) < 0.0f);

        visual?.QueueFree();

        var playerScene = GD.Load<PackedScene>("res://World/Player/Player.tscn");
        var eyePlayer = playerScene?.Instantiate<Player>();
        if (eyePlayer != null)
            AddChild(eyePlayer);
        eyePlayer?.CharacterVisual?.Animator?.Play(CharacterVisual.Clips.Idle);
        eyePlayer?.CharacterVisual?.Animator?.Seek(0.1, update: true);
        var skeleton = eyePlayer?.CharacterVisual?.Skeleton;
        var leftEye = skeleton?.FindBone("CC_Base_L_Eye") ?? -1;
        var rightEye = skeleton?.FindBone("CC_Base_R_Eye") ?? -1;
        var eyeCentre = leftEye >= 0 && rightEye >= 0
            ? (skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(leftEye)).Origin
              .Lerp((skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(rightEye)).Origin, 0.5f)
            : Vector3.Inf;
        Check("a câmera livre nasce entre os olhos do personagem",
            eyePlayer?.HeadPivot != null
            && eyePlayer.HeadPivot.GlobalPosition.DistanceTo(eyeCentre) < 0.02f);
        eyePlayer?.QueueFree();

        var player = new Player();
        var acceptedWindow = true;
        for (var request = 0; request < Player.CameraLookRequestsPerSecond; request++)
            acceptedWindow &= player.TryConsumeCameraLookRequest(7, 100);
        Check("a rotação remota do pescoço possui orçamento por peer",
            acceptedWindow && !player.TryConsumeCameraLookRequest(7, 100));
        for (var peer = 1_000;
             peer < 1_000 + Player.MaximumTrackedCameraLookPeers * 2;
             peer++)
        {
            player.TryConsumeCameraLookRequest(peer, 1_100);
        }
        Check("a tabela de rotação do pescoço permanece limitada",
            player.TrackedCameraLookPeerCount <= Player.MaximumTrackedCameraLookPeers);
        player.Free();
    }

    private void TestPokerPoseRpcContract()
    {
        foreach (var methodName in new[] { "SubmitPokerCardLook", "ReceivePokerCardLook" })
        {
            var method = typeof(Player).GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            var rpc = method?.GetCustomAttribute<RpcAttribute>();
            Check($"{methodName} replica somente a borda visual do peek",
                rpc != null
                && rpc.Mode == MultiplayerApi.RpcMode.AnyPeer
                && !rpc.CallLocal
                && rpc.TransferMode == MultiplayerPeer.TransferModeEnum.Reliable
                && rpc.TransferChannel == Player.PokerPoseTransferChannel);
        }

        Check("ENet reserva o canal exclusivo da pose de poker",
            ProjectSettings.GetSetting("network/max_channels", 0).AsInt32()
                >= Player.PokerPoseTransferChannel);
        Check("Steam reserva o canal exclusivo da pose de poker",
            ProjectSettings.GetSetting("steam/multiplayer_peer/max_channels", 0).AsInt32()
                >= Player.PokerPoseTransferChannel);

        var playerScene = GD.Load<PackedScene>("res://World/Player/Player.tscn");
        var posePlayer = playerScene?.Instantiate<Player>();
        if (posePlayer != null)
            AddChild(posePlayer);
        var applyPose = typeof(Player).GetMethod("ApplyPokerCardLook",
            BindingFlags.Instance | BindingFlags.NonPublic);
        applyPose?.Invoke(posePlayer, new object[] { true });
        var raised = posePlayer?.CharacterVisual?.Animator?.CurrentAnimation;
        applyPose?.Invoke(posePlayer, new object[] { false });
        var lowered = posePlayer?.CharacterVisual?.Animator?.CurrentAnimation;
        Check("a borda recebida realmente alterna a animacao 3P entre olhar e baixar",
            raised == CharacterVisual.Clips.IdleSitHoldingCards
            && lowered == CharacterVisual.Clips.IdleHoldingCardsDown);
        posePlayer?.QueueFree();
    }

    private void TestProfileValidation()
    {
        Check("um peer não pode publicar o nickname de outro",
            !PlayerProfileProtocol.IsExpectedOwner(8, 7));
        Check("o dono pode solicitar seu próprio nickname",
            PlayerProfileProtocol.IsExpectedOwner(7, 7));
        Check("nickname vazio recebe fallback seguro",
            PlayerProfileProtocol.SanitizeNickname(" \t\r\n", 7) == "Player 7");
        Check("espaços são normalizados e controles são removidos",
            PlayerProfileProtocol.SanitizeNickname("  Ana\n\0  Maria  ", 7) == "Ana Maria");

        var longNickname = new string('a', PlayerProfileProtocol.MaximumNicknameTextElements + 8);
        Check("nickname é limitado a 24 caracteres visíveis",
            new StringInfo(PlayerProfileProtocol.SanitizeNickname(longNickname, 7)).LengthInTextElements
                == PlayerProfileProtocol.MaximumNicknameTextElements);

        var emojiNickname = string.Concat(
            Enumerable.Repeat("🎱", PlayerProfileProtocol.MaximumNicknameTextElements + 1));
        var sanitizedEmoji = PlayerProfileProtocol.SanitizeNickname(emojiNickname, 7);
        Check("limite Unicode não corta pares substitutos",
            new StringInfo(sanitizedEmoji).LengthInTextElements
                == PlayerProfileProtocol.MaximumNicknameTextElements
            && !char.IsHighSurrogate(sanitizedEmoji[^1]));
    }

    private void TestProfileRpcContract()
    {
        CheckReliableProfileRpc("SubmitNicknameOnServer");
        CheckReliableProfileRpc("ReceiveNicknameFromServer");

        var submit = typeof(Player).GetMethod("SubmitNicknameOnServer",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var parameters = submit?.GetParameters();
        Check("cliente envia somente o texto do nickname",
            parameters is { Length: 1 } && parameters[0].ParameterType == typeof(string));
    }

    private void CheckReliableProfileRpc(string methodName)
    {
        var method = typeof(Player).GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        var rpc = method?.GetCustomAttribute<RpcAttribute>();

        Check($"{methodName} usa RPC confiável com validação explícita",
            rpc != null
            && rpc.Mode == MultiplayerApi.RpcMode.AnyPeer
            && !rpc.CallLocal
            && rpc.TransferMode == MultiplayerPeer.TransferModeEnum.Reliable);
    }

    private void CheckMovementRpc(string methodName)
    {
        var method = typeof(Player).GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        var rpc = method?.GetCustomAttribute<RpcAttribute>();

        Check($"{methodName} usa unreliable ordered no canal de movimento",
            rpc != null
            && rpc.Mode == MultiplayerApi.RpcMode.AnyPeer
            && !rpc.CallLocal
            && rpc.TransferMode == MultiplayerPeer.TransferModeEnum.UnreliableOrdered
            && rpc.TransferChannel == PlayerMovementProtocol.TransferChannel);
    }

    private void TestSceneReplicationContract()
    {
        var scene = GD.Load<PackedScene>("res://World/Player/Player.tscn");
        var player = scene?.Instantiate<Player>();
        var synchronizer = player?.GetNodeOrNull<MultiplayerSynchronizer>("MultiplayerSynchronizer");
        var config = synchronizer?.ReplicationConfig;

        Check("a cena do jogador carrega o sincronizador", config != null);
        Check("dependências fixas do jogador são ligadas pelo Inspector",
            player?.HeadPivot != null
            && player.GameHandler != null
            && player.PlayerModel != null
            && player.StateMachine != null
            && player.BodyCollision != null);
        Check("o componente visual mantém o AnimationPlayer esperado",
            player?.CharacterVisual?.Animator != null
            && player.SeatedGestureAnimator != null);
        Check("UI local não faz parte do avatar replicado",
            player?.FindChild("PlayerHud", recursive: true, owned: false) == null
            && player?.FindChild("TvShareButton", recursive: true, owned: false) == null);
        var cameraMount = player?.GetNodeOrNull<RemoteTransform3D>(
            "FirstPerson/HeadPivot/RemoteFPS");
        Check("câmera remota nasce desconectada até pertencer ao peer local",
            cameraMount != null
            && string.IsNullOrEmpty(cameraMount.RemotePath.ToString())
            && !cameraMount.UpdatePosition
            && !cameraMount.UpdateRotation
            && !cameraMount.UpdateScale);

        if (config != null)
        {
            var position = new NodePath(".:position");
            var rotation = new NodePath(".:rotation");
            var nickname = new NodePath(".:Nickname");

            Check("posição nasce no spawn, mas nunca é publicada pelo cliente",
                config.PropertyGetSpawn(position)
                && config.PropertyGetReplicationMode(position)
                    == SceneReplicationConfig.ReplicationMode.Never);
            Check("rotação nasce no spawn, mas nunca é publicada pelo cliente",
                config.PropertyGetSpawn(rotation)
                && config.PropertyGetReplicationMode(rotation)
                    == SceneReplicationConfig.ReplicationMode.Never);
            Check("nickname nasce no spawn para late join, mas o cliente nunca o publica",
                config.PropertyGetSpawn(nickname)
                && config.PropertyGetReplicationMode(nickname)
                    == SceneReplicationConfig.ReplicationMode.Never);

            player.SetMultiplayerAuthority(7);
            player.ConfigureServerAuthoritativeReplication();
            Check("autoridade de input alcança apenas o HeadPivot do dono",
                player.HeadPivot.GetMultiplayerAuthority() == 7);
            Check("o sincronizador permanece sob autoridade do servidor",
                synchronizer.GetMultiplayerAuthority() == PlayerProfileProtocol.ServerPeerId);
        }

        player?.EnterSeatedGameMode();
        Check("qualquer cópia para a física ao sentar",
            player != null && !player.IsPhysicsProcessing());
        player?.ExitSeatedGameMode();
        Check("host e remoto retomam a física ao levantar",
            player != null && player.IsPhysicsProcessing());

        player?.Free();
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
