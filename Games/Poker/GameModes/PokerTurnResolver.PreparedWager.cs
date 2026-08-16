using System.Collections.Generic;
using System.Linq;
using Godot;
using Poker.Rules;

/// <summary>Validates and publishes reversible physical wagers without spending chips.</summary>
public partial class PokerTurnResolver
{
    /// <summary>
    /// Publishes the complete ordered preview after one local chip was selected or returned. This
    /// request never changes a stack: the authority only checks that the preview could be paid from
    /// the current bank and mirrors it through the ordinary ordered turn context.
    /// </summary>
    public void RequestPreparedWager(int turnToken, int revision, int[] denominations)
    {
        denominations ??= System.Array.Empty<int>();
        if (Multiplayer.IsServer())
            TryPrepareWager(Multiplayer.GetUniqueId(), turnToken, revision, denominations);
        else
            RpcId(1, MethodName.PrepareWagerOnServer, turnToken, revision, denominations);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PrepareWagerOnServer(int turnToken, int revision, int[] denominations)
    {
        if (!Multiplayer.IsServer())
            return;

        var requesterId = Multiplayer.GetRemoteSenderId();
        if (!_preparedWagerRequestLimiter.TryConsume(requesterId))
        {
            RejectPreparedWager(
                requesterId, turnToken, revision, "prepared_wager_rate_limited");
            return;
        }

        TryPrepareWager(requesterId, turnToken, revision,
            denominations ?? System.Array.Empty<int>());
    }

    /// <summary>Server/test seam with exactly the same identity and validation as the RPC.</summary>
    public bool ApplyPreparedWagerFor(
        string playerId, int turnToken, int revision, int[] denominations)
    {
        if (!Multiplayer.IsServer() || !int.TryParse(playerId, out var peerId))
            return false;
        return TryPrepareWager(peerId, turnToken, revision,
            denominations ?? System.Array.Empty<int>());
    }


    private bool TryPrepareWager(
        int requesterId, int turnToken, int revision, IReadOnlyList<int> denominations)
    {
        var playerId = requesterId.ToString();
        if (!TurnIsOpenFor(playerId, turnToken, out _, out var reason))
        {
            RejectPreparedWager(requesterId, turnToken, revision, reason);
            return false;
        }

        if (!_handInProgress)
        {
            RejectPreparedWager(
                requesterId, turnToken, revision, "hand_not_running");
            return false;
        }

        if (_actionAdvancePending)
        {
            RejectPreparedWager(
                requesterId, turnToken, revision, "action_animation_in_progress");
            return false;
        }

        var seat = _seatOrder.IndexOf(playerId);
        if (seat < 0 || seat != _actingSeat)
        {
            RejectPreparedWager(requesterId, turnToken, revision, "not_your_turn");
            return false;
        }

        if (revision <= 0 || denominations == null
            || denominations.Count > MaximumPreparedWagerChips)
        {
            RejectPreparedWager(
                requesterId, turnToken, revision, "invalid_prepared_wager");
            return false;
        }

        var sameRevisionScope = _preparedWagerPlayer == playerId
                                && _preparedWagerTurnToken == turnToken;
        if (sameRevisionScope && revision <= _preparedWagerRevision)
        {
            // A duplicated reliable packet is harmless and idempotent. Reusing the same revision for
            // different content, or travelling backwards, is a stale preview and is never published.
            if (revision == _preparedWagerRevision
                && OrderedDenominationsEqual(_preparedWagerDenominations, denominations))
                return true;

            // A delayed/duplicated request may never erase a newer accepted preview. The reliable
            // channel normally preserves order, but retaining the canonical state here also makes
            // retries and reconnect edges harmless. The owner can still explicitly cancel with a
            // newer empty snapshot.
            SendPreparedWagerRejection(
                requesterId, turnToken, revision, "stale_prepared_wager");
            return false;
        }

        long amount = 0;
        foreach (var denomination in denominations)
            amount += denomination;
        if (amount < 0 || amount > int.MaxValue)
        {
            RejectPreparedWager(
                requesterId, turnToken, revision, "invalid_prepared_wager");
            return false;
        }

        if (denominations.Count > 0)
        {
            if (!_chipBanks.TryGetValue(playerId, out var authoritativeBank))
            {
                RejectPreparedWager(
                    requesterId, turnToken, revision, "invalid_chip_selection");
                return false;
            }

            // TryTakeExact mutates its input on success. Probe a clone so merely showing chips on
            // the cloth can never spend them or affect a later authoritative action.
            var probe = authoritativeBank
                .Select(run => new ChipRun(run.Denomination, run.Count)).ToList();
            if (!PokerChipStack.TryTakeExact(
                    probe, denominations, (int)amount, out _))
            {
                RejectPreparedWager(
                    requesterId, turnToken, revision, "invalid_chip_selection");
                return false;
            }
        }

        _preparedWagerPlayer = playerId;
        _preparedWagerTurnToken = turnToken;
        _preparedWagerRevision = revision;
        _preparedWagerDenominations = denominations.ToArray();
        _preparedWagerCommittedActionSeq = -1;
        PublishPreparedWagerContext();
        return true;
    }

    private void RejectPreparedWager(
        int requesterId, int turnToken, int revision, string reason)
    {
        var playerId = requesterId.ToString();
        var cleared = _preparedWagerPlayer == playerId
                      && _preparedWagerTurnToken == turnToken
                      && _preparedWagerDenominations.Length > 0;
        if (cleared)
            ClearPreparedWagerPreview(keepRevision: true);

        SendPreparedWagerRejection(requesterId, turnToken, revision, reason);
        if (cleared)
            PublishPreparedWagerContext();
    }

    private void SendPreparedWagerRejection(
        int requesterId, int turnToken, int revision, string reason)
    {
        if (requesterId == Multiplayer.GetUniqueId())
            EmitSignal(SignalName.PreparedWagerRejected, turnToken, revision, reason);
        else
            RpcId(requesterId, MethodName.ReceivePreparedWagerRejected,
                turnToken, revision, reason);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceivePreparedWagerRejected(
        int turnToken, int revision, string reason)
    {
        EmitSignal(SignalName.PreparedWagerRejected, turnToken, revision, reason);
    }

    private void PublishPreparedWagerContext()
    {
        if (Game != null)
            Game.CallExtendCurrentTurn(
                BuildContext(_lastAction, _lastPlayer, _lastAmount, advanceTurn: false));
    }

    private static bool OrderedDenominationsEqual(
        IReadOnlyList<int> left, IReadOnlyList<int> right)
    {
        if (left == null || right == null || left.Count != right.Count)
            return false;
        for (var index = 0; index < left.Count; index++)
        {
            if (left[index] != right[index])
                return false;
        }
        return true;
    }

    private void ClearPreparedWagerPreview(bool keepRevision)
    {
        _preparedWagerDenominations = System.Array.Empty<int>();
        _preparedWagerCommittedActionSeq = -1;
        if (keepRevision)
            return;

        _preparedWagerPlayer = "";
        _preparedWagerTurnToken = -1;
        _preparedWagerRevision = -1;
    }

    private void ResetPreparedWagerState() => ClearPreparedWagerPreview(keepRevision: false);
}
