using Godot;

public partial class TvScreenShare
{
    private sealed class MediaRateWindow
    {
        public ulong StartedAtMs;
        public int VideoPackets;
        public int AudioBytes;
    }

    // Steam requires explicitly accepting an incoming P2P session the first time a remote peer
    // sends us anything on this API, otherwise the packets are silently dropped.
    private void OnP2PSessionRequest(ulong remoteSteamId)
    {
        if (_steamProvider == null || remoteSteamId == 0)
            return;

        var peerId = _steamProvider.GetPeerId(remoteSteamId);
        if (!IsKnownSessionPeer(peerId))
        {
            GD.PushWarning($"[TvScreenShare] Sessão P2P rejeitada para SteamID desconhecido {remoteSteamId}.");
            return;
        }

        _steam.Call("acceptP2PSessionWithUser", remoteSteamId);
    }

    // Drains both raw Steam P2P channels. Only relevant when running over Steam — over ENet,
    // incoming video/audio still arrives through the ordinary RPC methods below.
    private void ProcessSteamIncoming()
    {
        if (_steamProvider == null)
            return;

        DrainSteamChannel(SteamP2PVideoChannel, isAudio: false);
        DrainSteamChannel(SteamP2PAudioChannel, isAudio: true);
    }

    private void DrainSteamChannel(int channel, bool isAudio)
    {
        var packetsRead = 0;
        long bytesRead = 0;

        while (packetsRead < MaxSteamPacketsPerChannelPerFrame)
        {
            var availableSize = _steam.Call("getAvailableP2PPacketSize", channel).AsInt32();
            if (availableSize <= 0)
                return;

            // Always allow the first packet so an oversized invalid payload cannot sit at the
            // head forever. Subsequent packets wait for the next frame once the byte budget is
            // exhausted; this prevents a known peer's flood from monopolizing the main thread.
            if (packetsRead > 0
                && bytesRead + availableSize > MaxSteamBytesPerChannelPerFrame)
            {
                return;
            }

            var packet = _steam.Call("readP2PPacket", availableSize, channel).AsGodotDictionary();
            packetsRead++;
            bytesRead += availableSize;

            // getAvailableP2PPacketSize can report a packet that readP2PPacket then fails to
            // produce (e.g. it hasn't fully landed yet) — that comes back as an empty
            // Dictionary, not an exception, so this has to be checked explicitly rather than
            // indexing straight into it. Bail out for this frame and let the next _Process
            // tick retry instead of spinning on the same stale size.
            if (!packet.ContainsKey("data") || !packet.ContainsKey("remote_steam_id"))
                return;

            var data = packet["data"].AsByteArray();
            var remoteSteamId = packet["remote_steam_id"].AsUInt64();
            var senderId = _steamProvider.GetPeerId(remoteSteamId);

            if (!IsAuthorizedMediaSender(senderId))
                continue;

            if (!AcceptIncomingPayload(senderId, data, isAudio))
                continue;

            if (Multiplayer.IsServer())
            {
                if (isAudio)
                    PlayAudioChunk(data);
                else
                    _playoutBuffer?.Enqueue(data);

                // The relay hop applies the same per-peer congestion gates as the original send:
                // one slow viewer must only cost themselves frames, not back up the host's
                // queue to everyone else.
                foreach (var peerId in Multiplayer.GetPeers())
                    if (peerId != senderId)
                        SendSteamPacket(channel, peerId, data, isAudio ? AudioQueueLimitBytes : VideoQueueLimitBytes);
            }
            else
            {
                if (isAudio)
                    PlayAudioChunk(data);
                else
                    _playoutBuffer?.Enqueue(data);
            }
        }
    }

    // Mirrors the "server relays to every other peer, everyone else only talks to the host"
    // star topology the RPC path below uses, just addressed by Steam ID instead of Godot peer id.
    // queueLimitBytes: skip peers whose send queue is deeper than this; 0 means never skip.
    private void BroadcastSteamPacket(int channel, byte[] data, long queueLimitBytes = 0)
    {
        if (Multiplayer.IsServer())
        {
            foreach (var peerId in Multiplayer.GetPeers())
                SendSteamPacket(channel, peerId, data, queueLimitBytes);
        }
        else
        {
            SendSteamPacket(channel, 1, data, queueLimitBytes);
        }
    }

    private void SendSteamPacket(int channel, int peerId, byte[] data, long queueLimitBytes = 0)
    {
        if (!IsValidMediaPayload(data, channel == SteamP2PAudioChannel))
            return;

        var steamId = _steamProvider.GetSteamId(peerId);
        if (steamId == 0)
            return;

        // WebP frame sizes vary with what is on screen. A fixed 128 KiB limit discarded most
        // frames after a detailed frame entered Steam's reliable queue. Budget a few frames of
        // the size currently being sent so the queue remains live without becoming unbounded.
        if (channel == SteamP2PVideoChannel && queueLimitBytes > 0)
            queueLimitBytes = SteamVideoQueueLimitForPayload(data.Length);

        if (queueLimitBytes > 0 && IsSendQueueCongested(steamId, queueLimitBytes))
            return;

        _steam.Call("sendP2PPacket", steamId, data, _p2pSendReliableWithBuffering, channel);
    }

    private bool IsSendQueueCongested(ulong steamId, long thresholdBytes)
    {
        var state = _steam.Call("getP2PSessionState", steamId).AsGodotDictionary();

        // An empty dictionary means the P2P session simply doesn't exist yet — nothing is
        // queued and there is nothing to learn about the API shape, so don't warn off of it.
        if (state.Count == 0)
            return false;

        // Missing telemetry on a real session (a GodotSteam variant without the field) must not
        // silently mute the stream — only gate on a positive signal of congestion. But it also
        // must not fail silently into "never drop", which looks exactly like the pre-gating
        // unbounded-queue lag; say it once, loudly, so a bad test points here immediately.
        if (!state.ContainsKey("bytes_queued_for_send"))
        {
            if (!_sendQueueTelemetryVerified)
            {
                _sendQueueTelemetryVerified = true;
                GD.PushWarning("[TvScreenShare] getP2PSessionState não expõe 'bytes_queued_for_send' nesta versão do GodotSteam — o controle de congestionamento está INATIVO e a fila de envio pode crescer sem limite em links lentos.");
            }
            return false;
        }

        if (!_sendQueueTelemetryVerified)
        {
            _sendQueueTelemetryVerified = true;
            GD.Print("[TvScreenShare] Controle de congestionamento ativo (bytes_queued_for_send disponível).");
        }

        return state["bytes_queued_for_send"].AsInt64() > thresholdBytes;
    }

    // Encoded WebP frames vary substantially in size. Godot explicitly warns that variable-size
    // packets on UnreliableOrdered can overtake and discard one another. The pre-refactor stream
    // used reliable delivery on its dedicated media channel and did not exhibit that starvation.
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = FrameTransferChannel)]
    private void SubmitFrame(byte[] encodedBytes)
    {
        if (!Multiplayer.IsServer())
            return;

        var senderId = Multiplayer.GetRemoteSenderId();
        if (!IsAuthorizedMediaSender(senderId)
            || !AcceptIncomingPayload(senderId, encodedBytes, isAudio: false))
            return;

        _playoutBuffer?.Enqueue(encodedBytes);

        foreach (var peerId in Multiplayer.GetPeers())
            if (peerId != senderId)
                RpcId(peerId, MethodName.SendFrame, encodedBytes);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = FrameTransferChannel)]
    private void SendFrame(byte[] encodedBytes)
    {
        if (!AcceptIncomingPayload(ServerPeerId, encodedBytes, isAudio: false))
            return;

        _playoutBuffer?.Enqueue(encodedBytes);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = AudioTransferChannel)]
    private void SubmitAudioChunk(byte[] monoInt16)
    {
        if (!Multiplayer.IsServer())
            return;

        var senderId = Multiplayer.GetRemoteSenderId();
        if (!IsAuthorizedMediaSender(senderId)
            || !AcceptIncomingPayload(senderId, monoInt16, isAudio: true))
            return;

        PlayAudioChunk(monoInt16);

        foreach (var peerId in Multiplayer.GetPeers())
            if (peerId != senderId)
                RpcId(peerId, MethodName.SendAudioChunk, monoInt16);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = AudioTransferChannel)]
    private void SendAudioChunk(byte[] monoInt16)
    {
        if (!AcceptIncomingPayload(ServerPeerId, monoInt16, isAudio: true))
            return;

        PlayAudioChunk(monoInt16);
    }

    internal static bool IsValidMediaPayload(byte[] data, bool isAudio)
    {
        if (data == null || data.Length == 0)
            return false;

        if (isAudio)
            return data.Length <= MaxAudioChunkBytes && data.Length % sizeof(short) == 0;

        return data.Length <= MaxEncodedFrameBytes;
    }

    internal static bool IsSafeEncodedFrame(byte[] data)
    {
        return IsValidMediaPayload(data, isAudio: false)
            && TryReadWebpDimensions(data, out var width, out var height)
            && width == CaptureWidth
            && height == CaptureHeight;
    }

    /// <summary>
    /// Reads the canvas size without invoking a codec. WebP stores it in the first VP8, VP8L or
    /// VP8X chunk, so malformed and oversized frames can be rejected before allocating pixels.
    /// </summary>
    internal static bool TryReadWebpDimensions(byte[] data, out int width, out int height)
    {
        width = 0;
        height = 0;

        const int riffHeaderSize = 12;
        const int chunkHeaderSize = 8;
        const int chunkDataOffset = riffHeaderSize + chunkHeaderSize;
        if (data == null || data.Length < chunkDataOffset
            || !MatchesFourCc(data, 0, "RIFF")
            || !MatchesFourCc(data, 8, "WEBP"))
        {
            return false;
        }

        var declaredFileSize = ReadUInt32(data, 4);
        if ((ulong)declaredFileSize + 8UL != (ulong)data.Length)
            return false;

        var chunkSize = ReadUInt32(data, 16);
        if ((ulong)chunkDataOffset + chunkSize > (ulong)data.Length)
            return false;

        if (MatchesFourCc(data, 12, "VP8 "))
        {
            if (chunkSize < 10 || (data[chunkDataOffset] & 1) != 0
                || data[chunkDataOffset + 3] != 0x9d
                || data[chunkDataOffset + 4] != 0x01
                || data[chunkDataOffset + 5] != 0x2a)
            {
                return false;
            }

            width = (data[chunkDataOffset + 6] | data[chunkDataOffset + 7] << 8) & 0x3fff;
            height = (data[chunkDataOffset + 8] | data[chunkDataOffset + 9] << 8) & 0x3fff;
            return width > 0 && height > 0;
        }

        if (MatchesFourCc(data, 12, "VP8L"))
        {
            if (chunkSize < 5 || data[chunkDataOffset] != 0x2f)
                return false;

            var dimensionBits = ReadUInt32(data, chunkDataOffset + 1);
            // The top three bits are the format version and must currently be zero.
            if ((dimensionBits >> 29) != 0)
                return false;

            width = (int)(dimensionBits & 0x3fff) + 1;
            height = (int)((dimensionBits >> 14) & 0x3fff) + 1;
            return true;
        }

        if (MatchesFourCc(data, 12, "VP8X"))
        {
            if (chunkSize < 10)
                return false;

            const byte animationFlag = 0x02;
            if ((data[chunkDataOffset] & animationFlag) != 0)
                return false;

            width = ReadUInt24(data, chunkDataOffset + 4) + 1;
            height = ReadUInt24(data, chunkDataOffset + 7) + 1;
            return true;
        }

        return false;
    }

    private static bool MatchesFourCc(byte[] data, int offset, string expected)
    {
        if (data == null || offset < 0 || offset + 4 > data.Length || expected.Length != 4)
            return false;

        return data[offset] == expected[0]
            && data[offset + 1] == expected[1]
            && data[offset + 2] == expected[2]
            && data[offset + 3] == expected[3];
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        return (uint)(data[offset]
            | data[offset + 1] << 8
            | data[offset + 2] << 16
            | data[offset + 3] << 24);
    }

    private static int ReadUInt24(byte[] data, int offset)
    {
        return data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16;
    }

    private bool IsAuthorizedMediaSender(int senderId)
    {
        if (senderId <= 0 || SharerId == 0)
            return false;

        return Multiplayer.IsServer()
            ? senderId == SharerId
            : senderId == ServerPeerId;
    }

    private bool IsKnownSessionPeer(int peerId) => IsKnownSessionPeer(
        peerId,
        Multiplayer.GetUniqueId(),
        Multiplayer.GetPeers());

    internal static bool IsKnownSessionPeer(
        int peerId,
        int localPeerId,
        int[] connectedPeerIds)
    {
        if (peerId <= 0)
            return false;

        if (peerId == ServerPeerId || peerId == localPeerId)
            return true;

        return connectedPeerIds != null
            && System.Array.IndexOf(connectedPeerIds, peerId) >= 0;
    }

    private bool AcceptIncomingPayload(int senderId, byte[] data, bool isAudio)
    {
        if (isAudio ? !IsValidMediaPayload(data, isAudio: true) : !IsSafeEncodedFrame(data))
            return false;

        var now = Time.GetTicksMsec();
        if (!_mediaRateByPeer.TryGetValue(senderId, out var window)
            || now - window.StartedAtMs >= 1000)
        {
            window = new MediaRateWindow { StartedAtMs = now };
            _mediaRateByPeer[senderId] = window;
        }

        if (isAudio)
        {
            if (window.AudioBytes + data.Length > MaxAudioBytesPerSecond)
                return false;

            window.AudioBytes += data.Length;
            return true;
        }

        if (window.VideoPackets >= MaxVideoPacketsPerSecond)
            return false;

        window.VideoPackets++;
        return true;
    }
}
