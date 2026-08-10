using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Locks down the transport-independent session contract, then proves it with a loopback ENet
/// handshake. The test never depends on Steam or an external network service.
/// </summary>
public partial class NetworkSessionLifecycleTest : Node
{
    private int _passed;
    private int _failed;

    public override async void _Ready()
    {
        GD.Print("=== Teste do ciclo de sessao multiplayer ===");

        TestClientSuccessIsDeferredUntilGodotConnects();
        TestHostingBecomesReadyOnlyAfterTransportSetup();
        TestServerDisconnectReturnsClientOffline();
        TestNetworkConfigurationIsExplicit();
        TestReconnectTokenValidation();
        TestWorldResetScheduling();
        await TestAttemptTimeoutAndHandshakeDisconnect();
        await TestRealEnetHandshake();

        GD.Print($"=== {_passed} passaram, {_failed} falharam ===");
        GetTree().Quit(_failed > 0 ? 1 : 0);
    }

    private void TestClientSuccessIsDeferredUntilGodotConnects()
    {
        var provider = CreateProvider();
        var joinedCount = 0;
        ulong joinedLobby = 0;
        var guestPeerId = 0;
        var hostPeerId = 0;
        provider.LobbySessionJoined += (lobbyId, guestId, hostId) =>
        {
            joinedCount++;
            joinedLobby = lobbyId;
            guestPeerId = guestId;
            hostPeerId = hostId;
        };

        var began = provider.BeginConnectionAttempt(321UL);
        Check("cliente entra em Connecting quando inicia", began
            && provider.SessionState == NetworkSessionState.Connecting);
        Check("join nao e anunciado antes do ConnectedToServer", joinedCount == 0);

        provider.CompleteConnection(42);
        Check("ConnectedToServer conclui a sessao uma unica vez",
            provider.SessionState == NetworkSessionState.Connected && joinedCount == 1);
        Check("join preserva lobby e usa o peer servidor do Godot",
            joinedLobby == 321UL && guestPeerId == 42 && hostPeerId == 1);

        provider.CompleteConnection(42);
        Check("callback duplicado nao anuncia outro join", joinedCount == 1);
        DestroyProvider(provider);
    }

    private void TestHostingBecomesReadyOnlyAfterTransportSetup()
    {
        var provider = CreateProvider();
        var createdCount = 0;
        var localPlayerCount = 0;
        provider.LobbyCreated += (_, _) => createdCount++;
        provider.PlayerConnected += _ => localPlayerCount++;

        var began = provider.BeginHostingAttempt();
        Check("host permanece StartingHost durante setup do transporte", began
            && provider.SessionState == NetworkSessionState.StartingHost
            && createdCount == 0);

        provider.CompleteHosting(654UL, 1);
        Check("host anuncia lobby apenas depois do setup",
            provider.SessionState == NetworkSessionState.Hosting && createdCount == 1);
        Check("listen-server anuncia seu jogador local uma vez", localPlayerCount == 1);
        DestroyProvider(provider);
    }

    private void TestServerDisconnectReturnsClientOffline()
    {
        var provider = CreateProvider();
        var disconnectedCount = 0;
        provider.ServerDisconnected += () => disconnectedCount++;

        provider.BeginConnectionAttempt(999UL);
        provider.CompleteConnection(42);
        provider.CompleteServerDisconnection();

        Check("queda do servidor encerra a sessao do cliente",
            provider.SessionState == NetworkSessionState.Offline && disconnectedCount == 1);
        DestroyProvider(provider);
    }

    private async Task TestAttemptTimeoutAndHandshakeDisconnect()
    {
        const string timeoutSetting = "network/session_attempt_timeout_seconds";
        var originalTimeout = ProjectSettings.GetSetting(timeoutSetting, 15.0);

        try
        {
            ProjectSettings.SetSetting(timeoutSetting, 0.05);

            var timedOutProvider = CreateProvider();
            var timeoutFailureCount = 0;
            timedOutProvider.ConnectionFailed += _ => timeoutFailureCount++;
            timedOutProvider.BeginConnectionAttempt(777UL);

            var deadline = Time.GetTicksMsec() + 1_000UL;
            while (Time.GetTicksMsec() < deadline
                && timedOutProvider.SessionState == NetworkSessionState.Connecting)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            Check("tentativa sem callback expira e libera a UI",
                timedOutProvider.SessionState == NetworkSessionState.Failed
                && timeoutFailureCount == 1);
            timedOutProvider.CompleteConnection(42);
            Check("callback atrasado não ressuscita tentativa expirada",
                timedOutProvider.SessionState == NetworkSessionState.Failed);
            DestroyProvider(timedOutProvider);

            var disconnectedDuringHandshake = CreateProvider();
            var handshakeFailureCount = 0;
            disconnectedDuringHandshake.ConnectionFailed += _ => handshakeFailureCount++;
            disconnectedDuringHandshake.BeginConnectionAttempt(778UL);
            disconnectedDuringHandshake.CompleteServerDisconnection();
            Check("queda durante handshake encerra Connecting",
                disconnectedDuringHandshake.SessionState == NetworkSessionState.Failed
                && handshakeFailureCount == 1);
            DestroyProvider(disconnectedDuringHandshake);
        }
        finally
        {
            ProjectSettings.SetSetting(timeoutSetting, originalTimeout);
        }
    }

    private void TestNetworkConfigurationIsExplicit()
    {
        Check("porta vem de network/port", NetworkManager.GetConfiguredPort() == 7777);
        Check("endereco padrao vem de ProjectSettings",
            NetworkManager.GetConfiguredHostAddress() == "127.0.0.1");
        Check("identidade de lobby e estavel", NetworkProvider.GameId == "9die");
        Check("versao do protocolo e publicada", NetworkProvider.ProtocolVersion == "1");
        Check("capacidade da sessão não excede os quatro spawns únicos",
            NetworkProvider.MaxPlayers == 4);
        Check("tentativas de sessão possuem timeout explícito",
            Math.Abs(NetworkProvider.SessionAttemptTimeoutSeconds - 15.0) < 0.001);
    }

    private void TestReconnectTokenValidation()
    {
        Check("token de reconexao aceita GUID sem separadores",
            ReconnectionManager.IsValidReconnectToken(Guid.NewGuid().ToString("N")));
        Check("token de reconexao rejeita GUID vazio",
            !ReconnectionManager.IsValidReconnectToken(Guid.Empty.ToString("N")));
        Check("token de reconexao rejeita formato com separadores",
            !ReconnectionManager.IsValidReconnectToken(Guid.NewGuid().ToString("D")));
        Check("token de reconexao rejeita entrada arbitraria",
            !ReconnectionManager.IsValidReconnectToken("not-a-token"));
    }

    private void TestWorldResetScheduling()
    {
        var main = new Main();
        Check("queda agenda uma única reconstrução limpa do mundo",
            main.ScheduleSessionWorldReload(deferReload: false)
            && !main.ScheduleSessionWorldReload(deferReload: false));
        main.Free();
    }

    private async Task TestRealEnetHandshake()
    {
        var hostRoot = new Node { Name = "EnetHost" };
        var clientRoot = new Node { Name = "EnetClient" };
        AddChild(hostRoot);
        AddChild(clientRoot);

        var hostApi = new SceneMultiplayer();
        var clientApi = new SceneMultiplayer();
        GetTree().SetMultiplayer(hostApi, hostRoot.GetPath());
        GetTree().SetMultiplayer(clientApi, clientRoot.GetPath());

        var host = new ENetNetworkProvider();
        var client = new ENetNetworkProvider();
        hostRoot.AddChild(host);
        clientRoot.AddChild(client);

        var hostSawRemotePeer = false;
        var clientSawServerPeer = false;
        var clientJoined = false;
        host.PlayerConnected += peerId => hostSawRemotePeer |= peerId != 1;
        client.PlayerConnected += peerId => clientSawServerPeer |= peerId == 1;
        client.LobbySessionJoined += (_, _, hostPeerId) => clientJoined |= hostPeerId == 1;

        var port = FindAvailableUdpPort();
        host.CreateHost(port);
        client.JoinSession(hostAddress: "127.0.0.1", port: port);

        var deadline = Time.GetTicksMsec() + 3_000UL;
        while (Time.GetTicksMsec() < deadline
            && (!clientJoined || !hostSawRemotePeer || !clientSawServerPeer))
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        Check("handshake ENet real conclui ConnectedToServer",
            clientJoined && client.SessionState == NetworkSessionState.Connected);
        Check("handshake ENet propaga os peers reais",
            hostSawRemotePeer && clientSawServerPeer && host.SessionState == NetworkSessionState.Hosting);

        var hostPath = hostRoot.GetPath();
        var clientPath = clientRoot.GetPath();
        var serverDisconnected = false;
        client.ServerDisconnected += () => serverDisconnected = true;

        RemoveChild(hostRoot);
        hostRoot.Free();
        GetTree().SetMultiplayer(null, hostPath);

        deadline = Time.GetTicksMsec() + 3_000UL;
        while (Time.GetTicksMsec() < deadline && !serverDisconnected)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Check("queda ENet real retorna o cliente para Offline",
            serverDisconnected && client.SessionState == NetworkSessionState.Offline);

        RemoveChild(clientRoot);
        clientRoot.Free();
        GetTree().SetMultiplayer(null, clientPath);
        hostApi.Dispose();
        clientApi.Dispose();
    }

    private static int FindAvailableUdpPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint).Port;
    }

    private NetworkProvider CreateProvider()
    {
        var provider = new NetworkProvider();
        AddChild(provider);
        return provider;
    }

    private static void DestroyProvider(NetworkProvider provider)
    {
        provider.GetParent()?.RemoveChild(provider);
        provider.Free();
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
