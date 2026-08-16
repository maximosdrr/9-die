using System;
using Godot;

/// <summary>
/// Owns the player-profile trust boundary. Clients may request a nickname, but only the server
/// stores and distributes the value consumed by the rest of the game.
/// </summary>
public partial class Player : CharacterBody3D
{
    private bool _hasAuthoritativeNickname;

    public bool HasAuthoritativeNickname => _hasAuthoritativeNickname;

    /// <summary>
    /// Establishes a safe spawn value before MultiplayerSpawner captures spawn properties.
    /// </summary>
    public void ConfigureNetworkProfile(int owningPeerId)
    {
        Id = owningPeerId;
        Nickname = PlayerProfileProtocol.SanitizeNickname(null, owningPeerId);
        _hasAuthoritativeNickname = false;
    }

    /// <summary>
    /// Keeps spawn snapshots under server authority after the player node itself is assigned to
    /// the peer that owns input. This prevents ownership from implicitly granting write access to
    /// any property listed in the synchronizer.
    /// </summary>
    public void ConfigureServerAuthoritativeReplication()
    {
        GetNode<MultiplayerSynchronizer>("MultiplayerSynchronizer")
            .SetMultiplayerAuthority(PlayerProfileProtocol.ServerPeerId);
    }

    private void InitializeNetworkProfile()
    {
        // Scene instances outside the multiplayer spawner still start with a display-safe value.
        Nickname = PlayerProfileProtocol.SanitizeNickname(Nickname, Id);

        if (!IsMultiplayerAuthority())
            return;

        // Deferring gives the reliable spawn message time to establish the same node path on all
        // peers before the owner submits its profile.
        CallDeferred(MethodName.SubmitLocalNickname);
    }

    private void SubmitLocalNickname()
    {
        if (!IsInsideTree() || !IsMultiplayerAuthority())
            return;

        var candidate = Global.Instance?.LocalNickname ?? string.Empty;
        if (Multiplayer.IsServer())
        {
            TryApplyNickname(Multiplayer.GetUniqueId(), candidate);
            return;
        }

        if (Multiplayer.MultiplayerPeer != null
            && Array.IndexOf(Multiplayer.GetPeers(), PlayerProfileProtocol.ServerPeerId) >= 0)
        {
            RpcId(PlayerProfileProtocol.ServerPeerId, MethodName.SubmitNicknameOnServer,
                candidate);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SubmitNicknameOnServer(string candidate)
    {
        if (Multiplayer.IsServer())
            TryApplyNickname(Multiplayer.GetRemoteSenderId(), candidate);
    }

    private bool TryApplyNickname(int senderPeerId, string candidate)
    {
        if (!Multiplayer.IsServer()
            || _hasAuthoritativeNickname
            || !PlayerProfileProtocol.IsExpectedOwner(senderPeerId, Id))
        {
            return false;
        }

        Nickname = PlayerProfileProtocol.SanitizeNickname(candidate, Id);
        _hasAuthoritativeNickname = true;
        BroadcastNickname();
        return true;
    }

    private void BroadcastNickname()
    {
        if (Multiplayer.MultiplayerPeer == null || !Multiplayer.IsServer())
            return;

        foreach (var peerId in Multiplayer.GetPeers())
            RpcId(peerId, MethodName.ReceiveNicknameFromServer, Nickname);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveNicknameFromServer(string nickname)
    {
        if (Multiplayer.IsServer()
            || Multiplayer.GetRemoteSenderId() != PlayerProfileProtocol.ServerPeerId)
        {
            return;
        }

        // The server already sanitized the value. Applying the same bounded sanitizer here also
        // makes this endpoint fail closed if the transport contract is changed later.
        Nickname = PlayerProfileProtocol.SanitizeNickname(nickname, Id);
        _hasAuthoritativeNickname = true;
    }
}
