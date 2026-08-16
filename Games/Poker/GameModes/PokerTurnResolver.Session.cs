using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;
using Poker.Rules;

/// <summary>Starts sessions, deals hands, rotates blinds and posts forced bets.</summary>
public partial class PokerTurnResolver
{
    // ---------------------------------------------------------------- the session

    /// <summary>Seats everyone with a starting stack and deals the first hand. Server only.</summary>
    public void BeginSession(Array players)
    {
        if (!Multiplayer.IsServer() || Game == null)
            return;

        _seatOrder.Clear();
        foreach (var playerVariant in players)
            _seatOrder.Add((string)playerVariant);

        if (_seatOrder.Count == 0)
            return;

        _bets.Clear();
        foreach (var playerId in _seatOrder)
            _bets.Add(new PlayerBetState { PlayerId = playerId, Stack = Game.StartingStack });

        _smallBlind = Mathf.Max(1, Game.SmallBlind);
        _bigBlind = Mathf.Max(_smallBlind + 1, Game.BigBlind);
        _buttonSeat = 0;
        _handNumber = 0;
        _actionSeq = 0;
        _lastAction = "";
        _lastPlayer = "";
        _lastAmount = 0;
        MatchRunning = true;

        StartHand();
    }

    /// <summary>
    /// Deals a hand: busts out anyone with nothing left, shuffles, posts the blinds and opens the
    /// pre-flop betting.
    /// </summary>
    private void StartHand()
    {
        if (!Multiplayer.IsServer() || Game == null || !MatchRunning)
            return;

        CancelPendingActionAdvance();

        DropBustedPlayers();

        if (_seatOrder.Count <= 1)
        {
            EndSession();
            return;
        }

        _handNumber++;
        RaiseBlindsIfDue();
        ResetPreparedWagerState();

        DealSeed = SecureSeed.Create();
        var dealOrder = PokerSeating.DealOrder(_seatOrder, _buttonSeat);
        var deal = PokerDeal.Deal(dealOrder, DealSeed);

        Hands.Clear();
        foreach (var entry in deal.HoleCards)
            Hands[entry.Key] = entry.Value;

        _board.Clear();
        _board.AddRange(PokerDeal.DealBoard(deal.Stub));

        ClearHandResult();
        _cardCleanupActive = false;
        _street = PokerStreet.Preflop;
        _handInProgress = true;

        _chipBanks.Clear();
        _roundChipRuns.Clear();
        _potChipRuns.Clear();
        _lastChipRuns.Clear();

        foreach (var bet in _bets)
        {
            bet.CommittedThisRound = 0;
            bet.CommittedThisHand = 0;
            bet.HasFolded = false;
            bet.HasActedThisRound = false;
            bet.BetLevelWhenLastActed = 0;
            _chipBanks[bet.PlayerId] = PokerChipStack.CreatePlayableBank(bet.Stack);
            _roundChipRuns[bet.PlayerId] = new List<ChipRun>();
        }

        foreach (var playerId in _seatOrder)
            SendHand(playerId);

        PostBlinds();

        // Posting a blind is not acting: the big blind still gets to raise their own blind when the
        // action comes back round, which is the whole reason "option" exists at a real table.
        foreach (var bet in _bets)
        {
            bet.HasActedThisRound = false;
            bet.BetLevelWhenLastActed = 0;
        }

        // Multiway action keeps the configured big blind as the bring-in. When only one funded
        // player remains, they match only what was actually posted; charging the nominal blind
        // would manufacture an unanswerable side pot and defer its avoidable refund to settlement.
        _currentBet = PokerBetting.BetToMatchAfterBlinds(_bets, _bigBlind);
        _minRaiseIncrement = _bigBlind;

        var first = PokerSeating.FirstToActPreflop(_buttonSeat, _seatOrder.Count);
        _actingSeat = PokerSeating.FirstAbleToActFrom(_bets, first);

        var ableToAct = PokerBetting.CountAbleToAct(_bets);
        var soleActorFacesAChoice = _actingSeat >= 0
                                   && _bets[_actingSeat].CommittedThisRound < _currentBet;
        if (_actingSeat < 0 || (ableToAct <= 1 && !soleActorFacesAChoice))
        {
            // Everybody is all-in, or the only player with chips has already covered the bet. There
            // is no meaningful wager left, so turn the board without requiring a ceremonial check.
            RunOutAndShowdown();
            return;
        }

        Game.ApplyNewTurn(_seatOrder[_actingSeat], BuildContext("deal", "", 0));
    }

    private void DropBustedPlayers()
    {
        for (var seat = _seatOrder.Count - 1; seat >= 0; seat--)
        {
            if (_bets[seat].Stack > 0)
                continue;

            // The button is an index, so removing a seat below it shifts it down with everyone else.
            if (seat < _buttonSeat)
                _buttonSeat--;

            Hands.Remove(_seatOrder[seat]);
            _seatOrder.RemoveAt(seat);
            _bets.RemoveAt(seat);
        }

        if (_seatOrder.Count > 0)
            _buttonSeat = ((_buttonSeat % _seatOrder.Count) + _seatOrder.Count) % _seatOrder.Count;
    }

    private void RaiseBlindsIfDue()
    {
        if (Game.BlindIncreaseEveryHands <= 0 || _handNumber <= 1)
            return;

        if ((_handNumber - 1) % Game.BlindIncreaseEveryHands != 0)
            return;

        _smallBlind *= 2;
        _bigBlind *= 2;
    }

    private void PostBlinds()
    {
        var seats = _seatOrder.Count;
        Post(PokerSeating.SmallBlindSeat(_buttonSeat, seats), _smallBlind);
        Post(PokerSeating.BigBlindSeat(_buttonSeat, seats), _bigBlind);
    }

    /// <summary>A blind is capped by the stack: a player too short simply posts all they have.</summary>
    private void Post(int seat, int amount)
    {
        if (seat < 0 || seat >= _bets.Count)
            return;

        var bet = _bets[seat];
        var paid = Mathf.Min(amount, bet.Stack);

        if (!_chipBanks.TryGetValue(bet.PlayerId, out var bank))
            bank = _chipBanks[bet.PlayerId] = PokerChipStack.CreatePlayableBank(bet.Stack);
        if (!PokerChipStack.TryTake(bank, paid, out var physical))
            physical = PokerChipStack.Decompose(paid);
        if (!_roundChipRuns.TryGetValue(bet.PlayerId, out var round))
            round = _roundChipRuns[bet.PlayerId] = new List<ChipRun>();
        PokerChipStack.AddRuns(round, physical);

        bet.Stack -= paid;
        bet.CommittedThisRound += paid;
        bet.CommittedThisHand += paid;
    }
}
