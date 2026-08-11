using System.Collections.Generic;
using System.Linq;
using Godot;
using Poker.Rules;

/// <summary>Accepts authenticated betting commands and applies their authoritative chip effects.</summary>
public partial class PokerTurnResolver
{
    // ---------------------------------------------------------------- player requests

    public void RequestAction(int turnToken, int actionKind, int total, int[] denominations = null)
    {
        denominations ??= System.Array.Empty<int>();
        if (Multiplayer.IsServer())
            TryAction(Multiplayer.GetUniqueId(), turnToken, actionKind, total, denominations);
        else
            RpcId(1, MethodName.ActOnServer, turnToken, actionKind, total, denominations);
    }


    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ActOnServer(int turnToken, int actionKind, int total, int[] denominations)
    {
        if (!Multiplayer.IsServer())
            return;

        var requesterId = Multiplayer.GetRemoteSenderId();
        if (!TryConsumeActionRequest(requesterId))
            return;

        TryAction(requesterId, turnToken, actionKind, total,
            denominations ?? System.Array.Empty<int>());
    }

    private bool TryConsumeActionRequest(int requesterId) =>
        _actionRequestLimiter.TryConsume(requesterId);

    internal bool TryConsumeActionRequest(int requesterId, ulong nowMilliseconds) =>
        _actionRequestLimiter.TryConsume(requesterId, nowMilliseconds);


    /// <summary>
    /// The server acting on a named seat's behalf, validated exactly like anything else.
    ///
    /// This is NOT a way round the identity check. A real player's request always arrives through
    /// <see cref="ActOnServer"/>, where the sender comes from the transport and can never be chosen
    /// by the payload; this is guarded by <c>IsServer</c> and so is unreachable from a client at all.
    /// It exists for the things the server itself has to do for a seat — folding a player who ran
    /// out of time, playing a seat nobody is sitting in — and for driving a whole session headlessly.
    /// </summary>
    public void ApplyActionFor(
        string playerId, int turnToken, int actionKind, int total, int[] denominations = null)
    {
        if (!Multiplayer.IsServer() || !int.TryParse(playerId, out var seatPeerId))
            return;

        TryAction(seatPeerId, turnToken, actionKind, total,
            denominations ?? System.Array.Empty<int>());
    }

    // ---------------------------------------------------------------- server rulings

    /// <summary>
    /// Structural failures answer before rules ones: a malformed request should be told it is
    /// malformed rather than handed an answer about poker it cannot use.
    /// </summary>
    private void TryAction(
        int requesterId, int turnToken, int actionKind, int total, int[] requestedDenominations)
    {
        var playerId = requesterId.ToString();

        if (!TurnIsOpenFor(playerId, turnToken, out _, out var reason))
        {
            RejectAction(requesterId, turnToken, reason);
            return;
        }

        if (!_handInProgress)
        {
            RejectAction(requesterId, turnToken, "hand_not_running");
            return;
        }

        if (actionKind < (int)PokerActionKind.Fold || actionKind > (int)PokerActionKind.Raise)
        {
            RejectAction(requesterId, turnToken, "invalid_action");
            return;
        }

        var seat = _seatOrder.IndexOf(playerId);
        if (seat < 0 || seat != _actingSeat)
        {
            RejectAction(requesterId, turnToken, "not_your_turn");
            return;
        }

        var bet = _bets[seat];
        var kind = (PokerActionKind)actionKind;

        if ((requestedDenominations?.Length ?? 0) > MaximumPreparedWagerChips)
        {
            RejectAction(requesterId, turnToken, "invalid_chip_selection");
            return;
        }

        // The same pure function the client used to build the choices, re-run from scratch.
        var anotherPlayerCanAct = _bets.Any(other => other != bet && other.CanAct);
        if (!PokerBetting.IsLegal(
                bet, kind, total, _currentBet, _minRaiseIncrement,
                anotherPlayerCanAct, out var illegal))
        {
            RejectAction(requesterId, turnToken, illegal);
            return;
        }

        var added = kind is PokerActionKind.Call or PokerActionKind.Raise
            ? Mathf.Min(total - bet.CommittedThisRound, bet.Stack) : 0;
        var preparedForAction = _preparedWagerPlayer == playerId
                                && _preparedWagerTurnToken == turnToken
                                && _preparedWagerDenominations.Length > 0;
        if (added > 0 && (requestedDenominations?.Length ?? 0) > 0
            && (!preparedForAction
                || !OrderedDenominationsEqual(
                    _preparedWagerDenominations, requestedDenominations)))
        {
            RejectAction(requesterId, turnToken, "prepared_wager_mismatch");
            return;
        }
        if (added > 0 && (requestedDenominations?.Length ?? 0) == 0 && preparedForAction)
        {
            RejectAction(requesterId, turnToken, "prepared_wager_mismatch");
            return;
        }
        if (!TryTakeAuthoritativePayment(
                bet.PlayerId, added, requestedDenominations, out var physicalPayment))
        {
            RejectAction(requesterId, turnToken, "invalid_chip_selection");
            return;
        }

        ApplyAction(bet, kind, total);
        bet.BetLevelWhenLastActed = _currentBet;
        if (!_roundChipRuns.TryGetValue(playerId, out var round))
            round = _roundChipRuns[playerId] = new List<ChipRun>();
        PokerChipStack.AddRuns(round, physicalPayment);
        bet.HasActedThisRound = true;

        _lastAction = kind.ToString().ToLowerInvariant();
        _lastPlayer = playerId;
        _lastAmount = total;
        _lastChipRuns = physicalPayment;

        // Counts PLAYER actions, not contexts. A gesture has to fire exactly once per action, and
        // the turn stamp cannot be used for that — several contexts can carry the same action, and
        // one action can produce several contexts.
        _actionSeq++;

        if (preparedForAction && added > 0)
            _preparedWagerCommittedActionSeq = _actionSeq;
        else
            ClearPreparedWagerPreview(keepRevision: true);

        // Every action gets a moment of its own, before anything is decided on top of it.
        //
        // Advance can settle the whole hand — a fold that leaves one player standing does exactly
        // that — and settling overwrites the action code with "won". Without this the commonest fold
        // at a heads-up table was the one nobody ever saw: no knock, no throw, no cards in the muck.
        // The turn does not move here, so this is purely "here is what just happened".
        Game.CallExtendCurrentTurn(BuildContext(_lastAction, _lastPlayer, _lastAmount, advanceTurn: false));

        // The committed preview exists for exactly the accepted-action context. Every peer captures
        // and adopts those actors from that snapshot; subsequent contexts must no longer present it
        // as a reversible wager.
        ClearPreparedWagerPreview(keepRevision: true);

        Advance();
    }

    private void RejectAction(int requesterId, int turnToken, string reason)
    {
        var playerId = requesterId.ToString();
        var cleared = _preparedWagerPlayer == playerId
                      && _preparedWagerTurnToken == turnToken
                      && _preparedWagerDenominations.Length > 0;
        if (cleared)
            ClearPreparedWagerPreview(keepRevision: true);

        Reject(requesterId, turnToken, reason);
        if (cleared)
            PublishPreparedWagerContext();
    }

    private bool TryTakeAuthoritativePayment(
        string playerId, int amount, IReadOnlyList<int> requested, out List<ChipRun> payment)
    {
        payment = new List<ChipRun>();
        if (amount <= 0)
            return requested == null || requested.Count == 0;

        if (!_chipBanks.TryGetValue(playerId, out var bank))
            return false;

        // A physical click supplies exact denominations. Keyboard/server actions supply none and use
        // the authority's deterministic bank instead. In both cases the chosen result is published.
        if (requested is { Count: > 0 })
            return PokerChipStack.TryTakeExact(bank, requested, amount, out payment);
        return PokerChipStack.TryTake(bank, amount, out payment);
    }

    private void ApplyAction(PlayerBetState bet, PokerActionKind kind, int total)
    {
        switch (kind)
        {
            case PokerActionKind.Fold:
                bet.HasFolded = true;
                break;

            case PokerActionKind.Check:
                break;

            case PokerActionKind.Call:
            case PokerActionKind.Raise:
                {
                    var added = Mathf.Min(total - bet.CommittedThisRound, bet.Stack);
                    bet.Stack -= added;
                    bet.CommittedThisRound += added;
                    bet.CommittedThisHand += added;

                    if (bet.CommittedThisRound <= _currentBet)
                        break;

                    // A raise reopens the action, but only if it is a FULL one. A short all-in may be
                    // called, yet it does not hand players who already acted a fresh chance to re-raise.
                    var full = PokerBetting.IsFullRaise(bet.CommittedThisRound, _currentBet, _minRaiseIncrement);
                    if (full)
                    {
                        _minRaiseIncrement = bet.CommittedThisRound - _currentBet;
                        foreach (var other in _bets)
                        {
                            if (other != bet && !other.HasFolded)
                            {
                                other.HasActedThisRound = false;
                                other.BetLevelWhenLastActed = 0;
                            }
                        }
                    }

                    _currentBet = bet.CommittedThisRound;
                    break;
                }
        }
    }
}
