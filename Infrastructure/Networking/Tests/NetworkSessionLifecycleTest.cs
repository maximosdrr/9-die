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
        TestStableSeatAssignments();
        TestNonCurrentRemovalPublishesSnapshot();
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
        Check("versao do protocolo e publicada", NetworkProvider.ProtocolVersion == "2");
        Check("Steam aceita salas da versao atual",
            SteamNetworkProvider.IsCompatibleLobbyMetadata("9die", "2"));
        Check("Steam mantem salas legadas do mesmo jogo descobríveis",
            SteamNetworkProvider.IsCompatibleLobbyMetadata("9die", ""));
        Check("Steam rejeita sala de outro jogo",
            !SteamNetworkProvider.IsCompatibleLobbyMetadata("outro-jogo", "2"));
        Check("Steam rejeita protocolo explicitamente incompatível",
            !SteamNetworkProvider.IsCompatibleLobbyMetadata("9die", "999"));
        var friendGame = new Godot.Collections.Dictionary { ["lobby"] = 123UL };
        Check("Steam extrai a sala real anunciada por um amigo",
            SteamNetworkProvider.ExtractFriendLobbyId(friendGame) == 123UL);
        Check("Steam ignora amigo que nao esta em uma sala",
            SteamNetworkProvider.ExtractFriendLobbyId(new Godot.Collections.Dictionary()) == 0UL);
        Check("host Steam vincula o peer ao lobby pesquisavel",
            SteamNetworkProvider.HostWithLobbyMethod == "host_with_lobby");
        Check("cliente Steam conecta pelo mesmo lobby pesquisavel",
            SteamNetworkProvider.ConnectToLobbyMethod == "connect_to_lobby");
        Check("capacidade da sessão não excede os quatro spawns únicos",
            NetworkProvider.MaxPlayers == 4);
        Check("ENet reserva uma das quatro vagas para o host",
            ENetNetworkProvider.MaxRemoteClients == NetworkProvider.MaxPlayers - 1);
        Check("tentativas de sessão possuem timeout explícito",
            Math.Abs(NetworkProvider.SessionAttemptTimeoutSeconds - 15.0) < 0.001);
        Check("Steam configurado falha de forma controlada sem o plugin",
            SteamGlobals.ValidateConfiguration(480, hasSteamSingleton: false) != null);
        Check("Steam exige App ID antes de inicializar",
            SteamGlobals.ValidateConfiguration(0, hasSteamSingleton: true) != null);
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

        var manager = new ReconnectionManager();
        var withinBudget = true;
        for (var request = 0; request < 4; request++)
            withinBudget &= manager.TryConsumeTokenRequest(7, nowMilliseconds: 0);
        Check("registro de token limita flood confiavel por peer",
            withinBudget
            && !manager.TryConsumeTokenRequest(7, nowMilliseconds: 0)
            && manager.TryConsumeTokenRequest(7, nowMilliseconds: 1_000));

        Check("token conectado nao pode ser roubado por outro peer",
            !ReconnectionManager.CanClaimConnectedToken(8, 7, ownerIsDisconnecting: false));
        Check("reconexao rapida pode substituir o peer que ja desconectou",
            ReconnectionManager.CanClaimConnectedToken(8, 7, ownerIsDisconnecting: true));
        manager.Free();
    }

    private void TestWorldResetScheduling()
    {
        var main = new Main();
        Check("queda agenda uma única reconstrução limpa do mundo",
            main.ScheduleSessionWorldReload(deferReload: false)
            && !main.ScheduleSessionWorldReload(deferReload: false));
        main.Free();
    }

    private void TestStableSeatAssignments()
    {
        var table = new Table();
        foreach (var playerId in new[] { "1", "2", "3", "4" })
            table.PlayersOnMatch.Add(playerId);

        var game = new TableGame { Table = table };
        AddChild(game);
        game.PrepareMatch(new Godot.Collections.Array { "1", "2", "3", "4" }, "1");

        var seats = new Node3D { Name = "Seats" };
        game.AddChild(seats);
        for (var seatIndex = 0; seatIndex < 4; seatIndex++)
        {
            var seat = new Marker3D
            {
                Name = $"Seat{seatIndex}",
                Position = new Vector3(seatIndex * 2.0f, 0.0f, 0.0f),
            };
            seat.AddChild(new Marker3D
            {
                Name = "StandExit",
                Position = new Vector3(0.0f, 0.0f, 0.75f),
            });
            seats.AddChild(seat);
        }

        var leavingPlayer = new Player { Name = "2" };
        var poseProbe = new SeatPoseProbeController();
        poseProbe.Configure(leavingPlayer, game, seats);
        poseProbe.CacheAssignedStandExit();
        var resolvedWalkingExit = false;
        var allowedRemovedPlayerToSit = true;
        var walkingExit = Vector3.Zero;
        var observedVacantSeatDuringTeardown = false;
        game.PlayerRemovedFromMatch += (playerId, ignoredTurnOrder) =>
        {
            if (playerId != "2")
                return;

            resolvedWalkingExit = poseProbe.TryGetAuthoritativePose(
                seated: false, out walkingExit, out _);
            allowedRemovedPlayerToSit = poseProbe.TryGetAuthoritativePose(
                seated: true, out _, out _);
            observedVacantSeatDuringTeardown = game.PlayerIdAtSeat(1) == "";
        };
        game.ApplyPlayerRemoved("2", new Godot.Collections.Array { "1", "3", "4" });

        Check("remover assento central nao desloca as cadeiras fisicas restantes",
            game.SeatIndexFor("1") == 0
            && game.SeatIndexFor("2") == 1
            && game.SeatIndexFor("3") == 2
            && game.SeatIndexFor("4") == 3
            && game.PlayerIdAtSeat(1) == ""
            && game.PlayerIdAtSeat(2) == "3");
        Check("remocao ainda resolve o StandExit original antes de liberar a cadeira",
            resolvedWalkingExit
            && walkingExit.IsEqualApprox(new Vector3(2.0f, 0.0f, 0.75f))
            && !allowedRemovedPlayerToSit
            && observedVacantSeatDuringTeardown);

        var seatSnapshot = game.BuildSeatSlotSnapshot();
        var lateTable = new Table();
        foreach (var playerId in new[] { "1", "3", "4" })
            lateTable.PlayersOnMatch.Add(playerId);
        var lateGame = new TableGame { Table = lateTable };
        lateGame.StageSeatSlotSnapshot(seatSnapshot);
        lateGame.PrepareMatch(new Godot.Collections.Array { "1", "3", "4" }, "1");
        Check("peer tardio reconstrui a lacuna fisica em vez de compactar cadeiras",
            lateGame.SeatIndexFor("1") == 0
            && lateGame.SeatIndexFor("2") == 1
            && lateGame.PlayerIdAtSeat(1) == ""
            && lateGame.SeatIndexFor("3") == 2
            && lateGame.SeatIndexFor("4") == 3);
        lateGame.Free();
        lateTable.Free();

        game.PrepareMatch(new Godot.Collections.Array { "1", "2", "3", "4" }, "1");
        var reclaimContext = new Godot.Collections.Dictionary();
        game.ApplyPlayerReclaimed(
            "2", "22", new Godot.Collections.Array { "1", "22", "3", "4" }, reclaimContext);

        Check("reconexao transfere exatamente o mesmo assento ao novo peer",
            game.SeatIndexFor("2") == -1
            && game.SeatIndexFor("22") == 1
            && game.PlayerIdAtSeat(1) == "22"
            && game.SeatIndexFor("3") == 2);

        var replacementPlayer = new Player { Name = "22" };
        Check("reclaim tardio reanexa a identidade local quando o Player finalmente nasce",
            game.AttachLocalReclaimedPlayer("22", replacementPlayer, localPeerId: 22)
            && game.Player == replacementPlayer
            && !game.AttachLocalReclaimedPlayer("22", replacementPlayer, localPeerId: 23));

        reclaimContext.Dispose();
        replacementPlayer.Free();
        poseProbe.Free();
        leavingPlayer.Free();
        RemoveChild(game);
        game.Free();
        table.Free();
    }

    private void TestNonCurrentRemovalPublishesSnapshot()
    {
        var table = new Table();
        table.PlayersOnMatch.Add("1");
        table.PlayersOnMatch.Add("2");
        table.PlayersOnMatch.Add("3");

        var resolver = new RemovalSnapshotResolver();
        var mode = new GameMode { TurnResolver = resolver };
        var handler = new GameModeHandler { CurrentGameMode = mode };
        var game = new TableGame { Table = table, GameModeHandler = handler };
        AddChild(game);

        game.PrepareMatch(new Godot.Collections.Array { "1", "2", "3" }, "1");
        var extensionCount = 0;
        Godot.Collections.Dictionary publishedContext = null;
        game.TurnExtended += context =>
        {
            extensionCount++;
            publishedContext = context;
        };
        game.RemovePlayerFromMatch("3", "test");

        Check("remover jogador fora da vez publica snapshot sem trocar o turno",
            extensionCount == 1
            && game.TurnOwnerId == "1"
            && game.TurnOrder.Count == 2
            && !game.TurnOrder.Contains("3")
            && resolver.OutgoingPlayerId == "3"
            && publishedContext != null
            && (int)publishedContext["snapshot_revision"] == 1);

        RemoveChild(game);
        game.Free();
        handler.Free();
        mode.Free();
        resolver.Free();
        table.Free();

        var advancingTable = new Table();
        foreach (var playerId in new[] { "1", "2", "3" })
            advancingTable.PlayersOnMatch.Add(playerId);

        var advancingResolver = new RemovalSnapshotResolver();
        var advancingMode = new GameMode { TurnResolver = advancingResolver };
        var advancingHandler = new GameModeHandler { CurrentGameMode = advancingMode };
        var advancingGame = new TableGame
        {
            Table = advancingTable,
            GameModeHandler = advancingHandler,
        };
        AddChild(advancingGame);
        advancingGame.PrepareMatch(new Godot.Collections.Array { "1", "2", "3" }, "1");

        var turnChangedCount = 0;
        advancingGame.TurnChanged += (_, _) => turnChangedCount++;
        advancingGame.PlayerRemovedFromMatch += (playerId, _) =>
        {
            if (playerId == "1")
            {
                advancingGame.ApplyNewTurn("2", new Godot.Collections.Dictionary
                {
                    ["published_by_mode"] = true,
                });
            }
        };

        advancingGame.RemovePlayerFromMatch("1", "test");
        Check("handler do modo que ja avancou o turno nao recebe publicacao duplicada",
            turnChangedCount == 1
            && advancingGame.TurnOwnerId == "2"
            && advancingResolver.BuildCount == 0);

        RemoveChild(advancingGame);
        advancingGame.Free();
        advancingHandler.Free();
        advancingMode.Free();
        advancingResolver.Free();
        advancingTable.Free();
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

internal partial class RemovalSnapshotResolver : TurnResolver
{
    public string OutgoingPlayerId { get; private set; } = "";
    public int BuildCount { get; private set; }

    public override Godot.Collections.Dictionary BuildHandoffContext(string outgoingPlayerId)
    {
        OutgoingPlayerId = outgoingPlayerId;
        BuildCount++;
        return new Godot.Collections.Dictionary { ["snapshot_revision"] = 1 };
    }
}

internal partial class SeatPoseProbeController : SeatedTableController
{
    private Node3D _seats;

    internal void Configure(Player player, TableGame table, Node3D seats)
    {
        Player = player;
        Table = table;
        _seats = seats;
    }

    protected override Marker3D SeatFor(string playerId)
    {
        var seatIndex = Table?.SeatIndexFor(playerId) ?? -1;
        return seatIndex >= 0 && _seats != null && seatIndex < _seats.GetChildCount()
            ? _seats.GetChild(seatIndex) as Marker3D
            : null;
    }

    protected override Node3D SeatsRoot => _seats;
}
