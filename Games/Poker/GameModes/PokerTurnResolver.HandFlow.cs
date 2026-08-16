using System.Collections.Generic;
using System.Linq;
using Godot;
using Poker.Rules;

/// <summary>Advances streets, runs showdown, settles pots and guards delayed hand transitions.</summary>
public partial class PokerTurnResolver
{
    public void RequestShowdownReveal()
    {
        if (Multiplayer.IsServer())
            TryShowdownReveal(Multiplayer.GetUniqueId());
        else
            RpcId(1, MethodName.ShowdownRevealOnServer);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ShowdownRevealOnServer()
    {
        if (!Multiplayer.IsServer())
            return;
        var requesterId = Multiplayer.GetRemoteSenderId();
        if (TryConsumeActionRequest(requesterId))
            TryShowdownReveal(requesterId);
    }

    /// <summary>Server/test seam with the same identity rules as the network request.</summary>
    public void ApplyShowdownRevealFor(string playerId)
    {
        if (Multiplayer.IsServer() && int.TryParse(playerId, out var peerId))
            TryShowdownReveal(peerId);
    }

    private void TryShowdownReveal(int requesterId)
    {
        var playerId = requesterId.ToString();
        if (!_awaitingShowdownReveals || !_pendingShowdownReveals.Remove(playerId))
            return;

        RevealHand(playerId);
        _lastAction = "show";
        _lastPlayer = playerId;
        _lastAmount = 0;
        _actionSeq++;
        Game.CallExtendCurrentTurn(BuildContext(_lastAction, _lastPlayer, 0, advanceTurn: false));

        if (_pendingShowdownReveals.Count == 0)
            CompleteShowdown();
    }


    // ---------------------------------------------------------------- moving the hand along

    private void Advance()
    {
        // Everyone folded to one player: the hand is over with no cards shown.
        if (PokerBetting.CountLive(_bets) <= 1)
        {
            FinishHand(showdown: false);
            return;
        }

        if (!PokerBetting.RoundIsComplete(_bets, _currentBet))
        {
            var next = PokerSeating.NextAbleToAct(_bets, _actingSeat);
            if (next < 0)
            {
                RunOutAndShowdown();
                return;
            }

            _actingSeat = next;
            Game.ApplyNewTurn(_seatOrder[_actingSeat], BuildContext(_lastAction, _lastPlayer, _lastAmount));
            return;
        }

        CollectRound();

        if (_street >= PokerStreet.River)
        {
            RunOutAndShowdown();
            return;
        }

        // Nobody left with a decision to make: turn the rest of the board and go to showdown.
        if (PokerBetting.CountAbleToAct(_bets) <= 1)
        {
            RunOutAndShowdown();
            return;
        }

        OpenStreet(PokerDeal.NextStreet(_street));
    }

    /// <summary>Sweeps the street's bets into the hand total and clears the round.</summary>
    private void CollectRound()
    {
        CollectRoundChipLedger();
        foreach (var bet in _bets)
        {
            bet.CommittedThisRound = 0;
            bet.HasActedThisRound = false;
            bet.BetLevelWhenLastActed = 0;
        }

        _currentBet = 0;
        _minRaiseIncrement = _bigBlind;
    }

    private void CollectRoundChipLedger()
    {
        foreach (var playerId in _seatOrder)
        {
            if (!_roundChipRuns.TryGetValue(playerId, out var runs) || runs.Count == 0)
                continue;
            PokerChipStack.AddRuns(_potChipRuns, runs);
            runs.Clear();
        }
    }

    private void OpenStreet(PokerStreet street)
    {
        _street = street;

        var first = PokerSeating.FirstToActPostflop(_buttonSeat, _seatOrder.Count);
        _actingSeat = PokerSeating.FirstAbleToActFrom(_bets, first);

        if (_actingSeat < 0)
        {
            RunOutAndShowdown();
            return;
        }

        // Carries the action that CLOSED the previous street rather than wiping it.
        //
        // An action that ends a betting round never gets a context of its own — Advance goes
        // straight here — so overwriting the code with "street" made that action invisible to every
        // peer. Whoever acted last on a street was silent: no gesture, no knock on the table. The
        // street itself is already in the context as a field, so the code is free to say something
        // more useful.
        Game.ApplyNewTurn(_seatOrder[_actingSeat], BuildContext(_lastAction, _lastPlayer, _lastAmount));
    }

    /// <summary>Turns whatever board is left and settles the hand at a showdown.</summary>
    private void RunOutAndShowdown()
    {
        CollectRound();
        _street = PokerStreet.Showdown;
        var allInShowdown = _bets.Any(bet => !bet.HasFolded && bet.IsAllIn);
        BeginShowdownDecision(revealAllImmediately: allInShowdown);
    }

    private void BeginShowdownDecision(bool revealAllImmediately)
    {
        _handInProgress = false;
        _actingSeat = -1;
        _awaitingShowdownReveals = true;
        _showdownRevealElapsed = 0.0f;
        _publishedShowdownCountdown = -1;
        _reveals.Clear();
        _showdownRanks.Clear();
        _awards.Clear();
        _pendingShowdownReveals.Clear();

        foreach (var bet in _bets)
        {
            if (!bet.HasFolded)
                _pendingShowdownReveals.Add(bet.PlayerId);
        }

        // Once an all-in has ended all betting, every live hand is tabled immediately. There is no
        // strategic muck decision left and delaying exposure behind the normal grace/countdown can
        // leave peers watching an unexplained board runout.
        if (revealAllImmediately || _pendingShowdownReveals.Count <= 1)
        {
            foreach (var playerId in _pendingShowdownReveals.ToArray())
                RevealHand(playerId);
            _pendingShowdownReveals.Clear();
            CompleteShowdown();
            return;
        }

        _lastAction = "showdown_prompt";
        _lastPlayer = "";
        _lastAmount = 0;
        Game.CallExtendCurrentTurn(BuildContext(_lastAction, "", 0, advanceTurn: false));
    }

    private void RevealHand(string playerId)
    {
        var hole = Hands.GetValueOrDefault(playerId);
        if (hole is { Count: >= PokerDeal.HoleCardCount })
            _reveals[playerId] = new[] { hole[0], hole[1] };
    }

    internal void AdvanceShowdownRevealClock(float delta)
    {
        if (!_awaitingShowdownReveals)
            return;

        _showdownRevealElapsed += Mathf.Max(0.0f, delta);
        var grace = Mathf.Max(0.0f, ShowdownRevealGraceSeconds);
        var countdownLength = Mathf.Max(0.0f, ShowdownRevealCountdownSeconds);
        var countdown = _showdownRevealElapsed < grace
            ? -1
            : Mathf.Max(0, Mathf.CeilToInt(grace + countdownLength - _showdownRevealElapsed));

        if (countdown != _publishedShowdownCountdown)
        {
            _publishedShowdownCountdown = countdown;
            Game.CallExtendCurrentTurn(BuildContext(
                "showdown_wait", "", countdown, advanceTurn: false));
        }

        if (_showdownRevealElapsed < grace + countdownLength)
            return;

        foreach (var playerId in _pendingShowdownReveals.ToArray())
            RevealHand(playerId);
        _pendingShowdownReveals.Clear();
        _lastAction = "showdown_auto";
        CompleteShowdown();
    }

    private void CompleteShowdown()
    {
        _awaitingShowdownReveals = false;
        _publishedShowdownCountdown = int.MinValue;
        FinishHand(showdown: true);
    }

    // ---------------------------------------------------------------- settling up

    private void FinishHand(bool showdown)
    {
        _handInProgress = false;
        _actingSeat = -1;

        // A fold can finish before the normal street sweep. The physical ledger still has to move
        // every committed chip into the same authoritative pot.
        CollectRoundChipLedger();

        var contributions = _bets.ToDictionary(bet => bet.PlayerId, bet => bet.CommittedThisHand);
        var contenders = _bets.Where(bet => !bet.HasFolded).Select(bet => bet.PlayerId).ToList();

        var ranks = new System.Collections.Generic.Dictionary<string, PokerHandRank>();
        if (!showdown)
            _reveals.Clear();
        _showdownRanks.Clear();
        _awards.Clear();

        if (showdown && contenders.Count > 1)
        {
            _street = PokerStreet.Showdown;

            foreach (var playerId in contenders)
            {
                var hole = Hands.GetValueOrDefault(playerId);
                ranks[playerId] = PokerHandEvaluator.Evaluate(hole, _board);
                _showdownRanks[playerId] = ranks[playerId];

            }
        }
        else
        {
            // Uncontested: whoever is left wins without showing anything.
            foreach (var playerId in contenders)
                ranks[playerId] = new PokerHandRank(HandCategory.HighCard, CardId.Ace);
        }

        var pots = PokerPot.Build(contributions, contenders);
        var awards = PokerPot.Award(pots, ranks, PokerSeating.OddChipOrder(_seatOrder, _buttonSeat));

        foreach (var bet in _bets)
        {
            var won = awards.GetValueOrDefault(bet.PlayerId);
            if (won > 0)
                _awards[bet.PlayerId] = won;

            bet.Stack += won;
            bet.CommittedThisRound = 0;
            bet.CommittedThisHand = 0;
        }

        // Settled/reconnecting peers should see the awarded stacks, not the pre-award banks. Existing
        // peers keep animating the exact pot actors they already own until the next hand snaps cleanly.
        foreach (var bet in _bets)
            _chipBanks[bet.PlayerId] = PokerChipStack.CreatePlayableBank(bet.Stack);

        _lastAction = showdown && contenders.Count > 1 ? "showdown" : "won";
        _lastPlayer = awards.Count > 0
            ? awards.OrderByDescending(entry => entry.Value).First().Key
            : "";
        _lastAmount = awards.Values.Sum();
        _lastChipRuns.Clear();

        // Broadcast the settled hand without moving the turn, so every peer can see the board, the
        // shown cards and the new stacks before anything is dealt over the top of them.
        Game.CallExtendCurrentTurn(BuildContext(_lastAction, _lastPlayer, _lastAmount, advanceTurn: false));

        // Every scheduled continuation belongs to this exact finished hand. Starting a new session
        // increments the revision in ResetSecretState, making callbacks from the old one harmless.
        var pauseRevision = ++_handPauseRevision;

        // Deterministic visual checks hold this exact result and advance their local presentation by
        // hand. Avoid creating an orphaned SceneTreeTimer when such a harness restarts or exits.
        if (!AutoAdvanceHands)
            return;

        // The LAST hand of a session gets the same pause as any other. Ending the match the instant
        // the chips moved cut straight to the results screen, so nobody ever saw the hand that won
        // the whole thing — the one hand they most wanted to look at.
        var hasShowdown = showdown && contenders.Count > 1;
        var configuredFloor = hasShowdown ? ShowdownSeconds : FoldedHandSeconds;
        var chipGroups = Profile.EstimateChipGroups(contributions.Values);
        var cleanupCardSlots = hasShowdown
            ? contenders.Count * 5 + Mathf.Max(0, _seatOrder.Count - contenders.Count) * 2
            : PokerDeal.BoardCount + _seatOrder.Count * PokerDeal.HoleCardCount;
        _scheduledCardCleanupSeconds = Profile.CardCleanupDurationFor(cleanupCardSlots);
        var calculated = Profile.MinimumHandPause(
            hasShowdown, chipGroups, contenders.Count, _awards.Count, cleanupCardSlots);
        // Zero remains an intentional instant/headless mode. Positive values are floors rather than
        // brittle exact delays: the physical presentation may extend them for a large or split pot.
        var pause = configuredFloor <= 0.0f ? 0.0f : Mathf.Max(configuredFloor, calculated);

        // Zero deals straight on. That is a real setting — a table with no pause between hands — and
        // it is also what lets a headless test play a whole session without waiting on the clock.
        if (pause <= 0.0f)
        {
            CompleteHandPause(pauseRevision);
            return;
        }

        if (CountWithChips() <= 1)
        {
            var finalTimer = GetTree().CreateTimer(pause);
            finalTimer.Timeout += () => CompleteHandPause(pauseRevision);
            return;
        }

        // Keep the result readable first. The authoritative next hand may only replace it after all
        // peers have received the cleanup phase and finished returning the same card nodes to deck.
        var cleanup = Mathf.Min(pause, _scheduledCardCleanupSeconds);
        var reading = Mathf.Max(0.0f, pause - cleanup);
        if (reading <= 0.0f)
        {
            BeginCardCleanup(pauseRevision);
            return;
        }

        var readingTimer = GetTree().CreateTimer(reading);
        readingTimer.Timeout += () => BeginCardCleanup(pauseRevision);
    }

    private void BeginCardCleanup(ulong pauseRevision)
    {
        if (!HandPauseIsCurrent(pauseRevision))
            return;

        if (CountWithChips() <= 1 || _scheduledCardCleanupSeconds <= 0.0f)
        {
            CompleteHandPause(pauseRevision);
            return;
        }

        _cardCleanupActive = true;
        _lastAction = "card_cleanup";
        _lastPlayer = "";
        _lastAmount = 0;
        Game.CallExtendCurrentTurn(BuildContext(
            _lastAction, _lastPlayer, _lastAmount, advanceTurn: false));

        var cleanupTimer = GetTree().CreateTimer(_scheduledCardCleanupSeconds);
        cleanupTimer.Timeout += () => CompleteHandPause(pauseRevision);
    }

    internal ulong HandPauseRevision => _handPauseRevision;

    /// <summary>
    /// Completes only the finished hand that scheduled this continuation. Internal so the lifecycle
    /// regression can deliver a deliberately stale callback without waiting on wall-clock time.
    /// </summary>
    internal void CompleteHandPause(ulong pauseRevision)
    {
        if (!HandPauseIsCurrent(pauseRevision))
            return;

        // Consume the continuation before it can start another hand. SceneTreeTimer normally emits
        // once, but duplicated delivery from a test seam or a future scheduling wrapper must remain
        // idempotent instead of clearing the result of the hand it just created.
        _handPauseRevision++;

        // Decided here rather than before the pause, so the finished hand is on the table for the
        // same length of time whether or not it was the last one.
        if (CountWithChips() <= 1)
        {
            EndSession();
            return;
        }

        _buttonSeat = PokerSeating.NextButtonSeat(_bets, _buttonSeat);
        _cardCleanupActive = false;
        ClearHandResult();
        StartHand();
    }

    private bool HandPauseIsCurrent(ulong pauseRevision) =>
        GodotObject.IsInstanceValid(this)
        && pauseRevision == _handPauseRevision
        && IsInsideTree()
        && Multiplayer.IsServer()
        && MatchRunning
        && Game != null;

    /// <summary>
    /// Wipes the finished hand's result.
    ///
    /// It is deliberately NOT wiped when a session ends — the last hand has to stay on the table
    /// while the results are read. That is exactly why it has to be wiped when a new match is set
    /// up: a session that started carrying the previous one's winners had every peer believing a
    /// hand was already settled, and nobody could act at all.
    /// </summary>
    private void ClearHandResult()
    {
        _reveals.Clear();
        _showdownRanks.Clear();
        _awards.Clear();
        _pendingShowdownReveals.Clear();
        _awaitingShowdownReveals = false;
        _showdownRevealElapsed = 0.0f;
        _publishedShowdownCountdown = int.MinValue;
    }

    private int CountWithChips() => _bets.Count(bet => bet.Stack > 0);

    private void EndSession()
    {
        CancelPendingActionAdvance();
        _handInProgress = false;
        _actingSeat = -1;
        _awaitingShowdownReveals = false;
        _pendingShowdownReveals.Clear();
        _cardCleanupActive = false;

        var winner = _bets.FirstOrDefault(bet => bet.Stack > 0)?.PlayerId
                     ?? (_seatOrder.Count > 0 ? _seatOrder[0] : null);

        MatchRunning = false;
        Game.ApplyMatchOver(winner, BuildContext("session_over", winner ?? "", 0, advanceTurn: false));
    }
}
