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
        TestRpcContract();
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
            player?.GetNodeOrNull<AnimationPlayer>("FirstPerson/Model3D/AnimationPlayer") != null
            && player.SeatedGestureAnimator != null);
        Check("UI local não faz parte do avatar replicado",
            player?.FindChild("PlayerHud", recursive: true, owned: false) == null
            && player?.FindChild("TvShareButton", recursive: true, owned: false) == null);
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
