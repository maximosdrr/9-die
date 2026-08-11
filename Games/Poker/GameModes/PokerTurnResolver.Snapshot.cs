using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;
using Poker.Rules;

/// <summary>Serializes the complete public table state without exposing private cards or the deal seed.</summary>
public partial class PokerTurnResolver
{
    // ---------------------------------------------------------------- the public context

    /// <summary>
    /// The whole public table, packed for the turn context. Flat parallel arrays because that is
    /// what Variant carries cleanly, and nothing here can identify a card any player still holds.
    /// </summary>
    private Dictionary BuildContext(string action, string player, int amount, bool advanceTurn = true)
    {
        if (advanceTurn)
        {
            TurnStamp++;
            // A preview belongs to one stamped turn. Any path that moves the turn without consuming
            // it (disconnect, timeout or hand transition) cancels it in that same ordered context.
            ResetPreparedWagerState();
        }

        var visible = PokerDeal.BoardSize(_street);
        var board = _board.Take(Mathf.Min(visible, _board.Count)).ToArray();

        var revealPlayers = _reveals.Keys.ToArray();
        var revealCards = new List<int>(revealPlayers.Length * PokerDeal.HoleCardCount);
        foreach (var playerId in revealPlayers)
            revealCards.AddRange(_reveals[playerId]);

        var bankPlayers = new List<string>();
        var bankDenominations = new List<int>();
        var bankCounts = new List<int>();
        var roundChipPlayers = new List<string>();
        var roundChipDenominations = new List<int>();
        var roundChipCounts = new List<int>();
        foreach (var playerId in _seatOrder)
        {
            AppendRuns(playerId, _chipBanks.GetValueOrDefault(playerId),
                bankPlayers, bankDenominations, bankCounts);
            AppendRuns(playerId, _roundChipRuns.GetValueOrDefault(playerId),
                roundChipPlayers, roundChipDenominations, roundChipCounts);
        }

        return new Dictionary
        {
            ["board"] = board,
            ["street"] = (int)_street,
            ["seat_order"] = _seatOrder.ToArray(),

            ["stack_players"] = _seatOrder.ToArray(),
            ["stacks"] = _bets.Select(bet => bet.Stack).ToArray(),
            ["round_players"] = _seatOrder.ToArray(),
            ["round_bets"] = _bets.Select(bet => bet.CommittedThisRound).ToArray(),
            ["hand_players"] = _seatOrder.ToArray(),
            ["hand_bets"] = _bets.Select(bet => bet.CommittedThisHand).ToArray(),
            ["acted_players"] = _bets.Where(bet => bet.HasActedThisRound)
                .Select(bet => bet.PlayerId).ToArray(),
            ["acted_bet_levels"] = _bets.Where(bet => bet.HasActedThisRound)
                .Select(bet => bet.BetLevelWhenLastActed).ToArray(),

            ["folded"] = _bets.Where(bet => bet.HasFolded).Select(bet => bet.PlayerId).ToArray(),
            ["all_in"] = _bets.Where(bet => bet.IsAllIn).Select(bet => bet.PlayerId).ToArray(),

            ["current_bet"] = _currentBet,
            ["min_raise"] = _minRaiseIncrement,
            ["button_seat"] = _buttonSeat,
            ["pot_total"] = _bets.Sum(bet => bet.CommittedThisHand),
            ["hand_number"] = _handNumber,
            ["small_blind"] = _smallBlind,
            ["big_blind"] = _bigBlind,
            ["turn_token"] = TurnStamp,

            ["last_action"] = action ?? "",
            ["last_player"] = player ?? "",
            ["last_amount"] = amount,
            ["action_seq"] = _actionSeq,
            ["last_chip_denominations"] = PokerChipStack.Expand(_lastChipRuns),

            ["prepared_player"] = _preparedWagerDenominations.Length > 0
                ? _preparedWagerPlayer : "",
            ["prepared_turn_token"] = _preparedWagerTurnToken,
            ["prepared_revision"] = _preparedWagerRevision,
            ["prepared_denominations"] = _preparedWagerDenominations,
            ["prepared_committed_action_seq"] = _preparedWagerCommittedActionSeq,

            ["chip_bank_players"] = bankPlayers.ToArray(),
            ["chip_bank_denominations"] = bankDenominations.ToArray(),
            ["chip_bank_counts"] = bankCounts.ToArray(),
            ["round_chip_players"] = roundChipPlayers.ToArray(),
            ["round_chip_denominations"] = roundChipDenominations.ToArray(),
            ["round_chip_counts"] = roundChipCounts.ToArray(),
            ["pot_chip_denominations"] = _potChipRuns.Select(run => run.Denomination).ToArray(),
            ["pot_chip_counts"] = _potChipRuns.Select(run => run.Count).ToArray(),

            ["reveal_players"] = revealPlayers,
            ["reveal_cards"] = revealCards.ToArray(),
            ["showdown_waiting"] = _awaitingShowdownReveals,
            ["showdown_pending"] = _pendingShowdownReveals.ToArray(),
            ["showdown_countdown"] = _publishedShowdownCountdown == int.MinValue
                ? -1 : _publishedShowdownCountdown,
            ["card_cleanup"] = _cardCleanupActive,

            // What each shown hand WAS, and who collected what. Categories rather than the packed
            // rank value: the table needs to say "full house", not compare anything.
            ["showdown_players"] = _showdownRanks.Keys.ToArray(),
            ["showdown_categories"] = _showdownRanks.Values.Select(rank => (int)rank.Category).ToArray(),
            ["win_players"] = _awards.Keys.ToArray(),
            ["win_amounts"] = _awards.Values.ToArray(),
        };
    }

    private static void AppendRuns(
        string playerId, IReadOnlyList<ChipRun> runs, List<string> players,
        List<int> denominations, List<int> counts)
    {
        if (runs == null)
            return;
        foreach (var run in runs)
        {
            players.Add(playerId);
            denominations.Add(run.Denomination);
            counts.Add(run.Count);
        }
    }

    /// <summary>Context handed to the next player when the current one leaves.</summary>
    public override Dictionary BuildHandoffContext(string outgoingPlayerId)
    {
        // A genuine owner handoff advances the stamp, so requests issued by the departed connection
        // can never be replayed after the turn moves on. Removing a non-actor leaves the live actor's
        // stamped turn (and any prepared chips) intact.
        var outgoingOwnsTurn = Game?.IsTurnOwner(outgoingPlayerId) == true;
        return BuildContext("left", outgoingPlayerId, 0, advanceTurn: outgoingOwnsTurn);
    }

    /// <summary>
    /// A reclaim transfers the same seat rather than removing it. It still consumes an acting
    /// player's turn stamp so packets from the old connection cannot be replayed, but publishes an
    /// explicit neutral event instead of making every peer animate or describe the player as left.
    /// </summary>
    public override Dictionary BuildReclaimContext(string outgoingPlayerId)
    {
        var outgoingOwnsTurn = Game?.IsTurnOwner(outgoingPlayerId) == true;
        return BuildContext(
            "reclaimed", outgoingPlayerId, 0, advanceTurn: outgoingOwnsTurn);
    }
}
