using Godot;
using Poker.Rules;

/// <summary>Removes departed seats and safely remaps reconnecting players.</summary>
public partial class PokerTurnResolver
{
    // ---------------------------------------------------------------- leaving and coming back

    /// <summary>
    /// A player left for good. They fold out of the hand and give up their seat; their chips stay in
    /// whatever pot they had already built, exactly as if they had folded.
    /// </summary>
    public void RemoveFromSession(string playerId)
    {
        if (!Multiplayer.IsServer())
            return;

        var seat = _seatOrder.IndexOf(playerId);
        if (seat < 0)
            return;

        if (_preparedWagerPlayer == playerId)
            ResetPreparedWagerState();

        _bets[seat].HasFolded = true;
        _bets[seat].Stack = 0;
        Hands.Remove(playerId);

        if (_awaitingShowdownReveals)
        {
            _pendingShowdownReveals.Remove(playerId);
            if (_pendingShowdownReveals.Count == 0)
                CompleteShowdown();
            else
                Game.CallExtendCurrentTurn(BuildContext(
                    "showdown_left", playerId, 0, advanceTurn: false));
            return;
        }

        if (!_handInProgress)
            return;

        // If it was their turn, the hand has to move on without them.
        if (_actingSeat == seat)
            Advance();
        else if (PokerBetting.CountLive(_bets) <= 1)
            FinishHand(showdown: false);
    }

    public void ReissueStateTo(string oldPlayerId, string newPlayerId)
    {
        if (!Multiplayer.IsServer())
            return;

        if (Hands.Remove(oldPlayerId, out var hand))
            Hands[newPlayerId] = hand;

        var seat = _seatOrder.IndexOf(oldPlayerId);
        if (seat >= 0)
        {
            _seatOrder[seat] = newPlayerId;
            _bets[seat].PlayerId = newPlayerId;
        }

        if (_lastPlayer == oldPlayerId)
            _lastPlayer = newPlayerId;

        if (_pendingShowdownReveals.Remove(oldPlayerId))
            _pendingShowdownReveals.Add(newPlayerId);

        if (_reveals.Remove(oldPlayerId, out var revealed))
            _reveals[newPlayerId] = revealed;

        if (_showdownRanks.Remove(oldPlayerId, out var rank))
            _showdownRanks[newPlayerId] = rank;

        if (_awards.Remove(oldPlayerId, out var award))
            _awards[newPlayerId] = award;

        if (_chipBanks.Remove(oldPlayerId, out var bank))
            _chipBanks[newPlayerId] = bank;
        if (_roundChipRuns.Remove(oldPlayerId, out var roundRuns))
            _roundChipRuns[newPlayerId] = roundRuns;

        // A preview is reversible client intent, not settled poker state. A reclaimed connection
        // starts from the authoritative bank instead of inheriting an interaction it did not make.
        if (_preparedWagerPlayer == oldPlayerId)
        {
            ResetPreparedWagerState();
            PublishPreparedWagerContext();
        }

        ReissueTo(newPlayerId);
    }
}
