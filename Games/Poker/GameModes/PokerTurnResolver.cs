using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;
using Poker.Rules;

/// <summary>
/// Runs a Hold'em session server side.
///
/// The private half — the hole cards, the deal seed, the turn stamp and the one targeted channel a
/// holding travels on — is <see cref="SecretHandTurnResolver"/>'s. Added here is the game: blinds,
/// streets, the betting round, the showdown and the session that runs until one player has
/// everything.
///
/// <see cref="_board"/> joins the inherited secrets. All five community cards are dealt at the
/// moment of the shuffle and simply revealed a street at a time — no card is ever CHOSEN after
/// somebody has seen a bet, which is the property that makes the deal defensible.
/// </summary>
[GlobalClass]
public partial class PokerTurnResolver : SecretHandTurnResolver
{
    internal const int ActionRequestsPerSecond = 8;
    internal const int PreparedWagerRequestsPerSecond = 24;
    internal const int MaximumPreparedWagerChips = 64;
    internal const int MaximumTrackedActionPeers = 16;

    [Export] public PokerPresentationProfile PresentationProfile;
    private PokerPresentationProfile Profile =>
        PresentationProfile ??= new PokerPresentationProfile();

    /// <summary>How long the table sits on a finished hand before the next one is dealt.</summary>
    // Includes the exposed-hand reading beat and still leaves roughly eight seconds for the ranked
    // best-five comparison before the next hand clears the cloth.
    [Export] public float ShowdownSeconds = 12.0f;

    /// <summary>Pause after a hand that ended with everyone folding — nothing to read, so shorter.</summary>
    [Export] public float FoldedHandSeconds = 4.5f;

    /// <summary>Production advances automatically; visual harnesses may hold a result indefinitely.</summary>
    [Export] public bool AutoAdvanceHands = true;

    [ExportGroup("Showdown decision")]
    /// <summary>Time to reveal voluntarily before the visible countdown starts.</summary>
    [Export] public float ShowdownRevealGraceSeconds = 10.0f;
    /// <summary>Visible final window; pending hands are exposed automatically when it reaches zero.</summary>
    [Export] public float ShowdownRevealCountdownSeconds = 10.0f;

    /// <summary>
    /// A reversible chip-preview request has its own revision in addition to the turn stamp. Keeping
    /// both values in the refusal lets the local controller ignore an old rejection after the player
    /// has already corrected the selection.
    /// </summary>
    [Signal]
    public delegate void PreparedWagerRejectedEventHandler(
        int turnToken, int revision, string reason);

    public PokerGame Game => Table as PokerGame;
    private readonly PeerRequestRateLimiter _actionRequestLimiter = new(
        ActionRequestsPerSecond, 1000, MaximumTrackedActionPeers);
    private readonly PeerRequestRateLimiter _preparedWagerRequestLimiter = new(
        PreparedWagerRequestsPerSecond, 1000, MaximumTrackedActionPeers);

    /// <summary>All five community cards, dealt up front and revealed a street at a time.</summary>
    private readonly List<int> _board = new();

    /// <summary>Betting state per seat, parallel to <see cref="_seatOrder"/>.</summary>
    private readonly List<PlayerBetState> _bets = new();

    // Monetary totals decide the rules; this ledger decides which physical chips represent them.
    // It is server-owned so peers never independently decompose the same amount into different chips.
    private readonly System.Collections.Generic.Dictionary<string, List<ChipRun>> _chipBanks = new();
    private readonly System.Collections.Generic.Dictionary<string, List<ChipRun>> _roundChipRuns = new();
    private readonly List<ChipRun> _potChipRuns = new();
    private List<ChipRun> _lastChipRuns = new();

    // A prepared wager is public table theatre, not money. It remains separate from _chipBanks and
    // _bets until TryAction accepts the normal poker action that names the exact same denominations.
    private string _preparedWagerPlayer = "";
    private int _preparedWagerTurnToken = -1;
    private int _preparedWagerRevision = -1;
    private int[] _preparedWagerDenominations = System.Array.Empty<int>();
    private int _preparedWagerCommittedActionSeq = -1;

    /// <summary>Who is still in the session, in seating order. Shrinks as players bust.</summary>
    private readonly List<string> _seatOrder = new();

    private readonly System.Collections.Generic.Dictionary<string, int[]> _reveals = new();

    /// <summary>What each shown hand actually was, so the table can say WHY it won.</summary>
    private readonly System.Collections.Generic.Dictionary<string, PokerHandRank> _showdownRanks = new();
    private readonly HashSet<string> _pendingShowdownReveals = new();

    /// <summary>Who collected what from the last hand.</summary>
    private readonly System.Collections.Generic.Dictionary<string, int> _awards = new();

    private PokerStreet _street;
    private int _buttonSeat;
    private int _actingSeat = -1;
    private int _currentBet;
    private int _minRaiseIncrement;
    private int _handNumber;
    private int _smallBlind;
    private int _bigBlind;
    private bool _handInProgress;
    private bool _awaitingShowdownReveals;
    private float _showdownRevealElapsed;
    private int _publishedShowdownCountdown = int.MinValue;
    private bool _cardCleanupActive;
    private float _scheduledCardCleanupSeconds;

    private int _actionSeq;
    private string _lastAction = "";
    private string _lastPlayer = "";
    private int _lastAmount;

    protected override void ResetSecretState()
    {
        _actionRequestLimiter.Clear();
        _preparedWagerRequestLimiter.Clear();
        _board.Clear();
        _bets.Clear();
        _chipBanks.Clear();
        _roundChipRuns.Clear();
        _potChipRuns.Clear();
        _lastChipRuns.Clear();
        ResetPreparedWagerState();
        _seatOrder.Clear();
        ClearHandResult();
        _street = PokerStreet.Preflop;
        _buttonSeat = 0;
        _actingSeat = -1;
        _currentBet = 0;
        _minRaiseIncrement = 0;
        _handNumber = 0;
        _handInProgress = false;
        _awaitingShowdownReveals = false;
        _showdownRevealElapsed = 0.0f;
        _publishedShowdownCountdown = int.MinValue;
        _pendingShowdownReveals.Clear();
        _cardCleanupActive = false;
        _scheduledCardCleanupSeconds = 0.0f;
        _lastAction = "";
        _lastPlayer = "";
        _lastAmount = 0;
        _actionSeq = 0;
    }

    protected override void ClearSecretState()
    {
        _board.Clear();
        ResetPreparedWagerState();
    }

    public override void _Process(double delta)
    {
        if (Multiplayer.IsServer() && _awaitingShowdownReveals)
            AdvanceShowdownRevealClock((float)delta);
    }

    protected override void ApplyLocalHand(int[] items) => Game?.ApplyLocalHoleCards(items);

    protected override void ApplyPublicSnapshot(Dictionary context) => Game?.ApplyPublicSnapshot(context);

    protected override void ApplyFullSnapshot(Dictionary context)
    {
        Game?.ApplyPublicSnapshot(context);
        Game?.SeatPresenter?.SnapToAuthoritativeState();
        // ApplyPublicSnapshot refreshes once before the recovery snap. The snap deliberately discards
        // every in-flight actor, including a replicated reversible wager, so reconcile once more from
        // the just-applied authoritative snapshot. Without this final pass a late/reconnected peer kept
        // the wager in PokerGame.PreparedWagers but did not draw it until some unrelated later update.
        Game?.SeatPresenter?.Refresh();
    }

    protected override Dictionary BuildSnapshot() =>
        BuildContext(_lastAction, _lastPlayer, _lastAmount, advanceTurn: false);

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
        var deal = PokerDeal.Deal(_seatOrder, DealSeed);

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
            _chipBanks[bet.PlayerId] = PokerChipStack.CreatePlayableBank(bet.Stack);
            _roundChipRuns[bet.PlayerId] = new List<ChipRun>();
        }

        foreach (var playerId in _seatOrder)
            SendHand(playerId);

        PostBlinds();

        // Posting a blind is not acting: the big blind still gets to raise their own blind when the
        // action comes back round, which is the whole reason "option" exists at a real table.
        foreach (var bet in _bets)
            bet.HasActedThisRound = false;

        _currentBet = _bets.Count == 0 ? 0 : _bets.Max(bet => bet.CommittedThisRound);
        _minRaiseIncrement = _bigBlind;

        var first = PokerSeating.FirstToActPreflop(_buttonSeat, _seatOrder.Count);
        _actingSeat = PokerSeating.FirstAbleToActFrom(_bets, first);

        if (_actingSeat < 0 || PokerBetting.CountAbleToAct(_bets) <= 1)
        {
            // Blinds alone put everyone all-in: there is nothing to decide, so run the board out.
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

    // ---------------------------------------------------------------- player requests

    public void RequestAction(int turnToken, int actionKind, int total, int[] denominations = null)
    {
        denominations ??= System.Array.Empty<int>();
        if (Multiplayer.IsServer())
            TryAction(Multiplayer.GetUniqueId(), turnToken, actionKind, total, denominations);
        else
            RpcId(1, MethodName.ActOnServer, turnToken, actionKind, total, denominations);
    }

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

        // The same pure function the client used to build the choices, re-run from scratch.
        if (!PokerBetting.IsLegal(bet, kind, total, _currentBet, _minRaiseIncrement, out var illegal))
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
                                other.HasActedThisRound = false;
                        }
                    }

                    _currentBet = bet.CommittedThisRound;
                    break;
                }
        }
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
        BeginShowdownDecision();
    }

    private void BeginShowdownDecision()
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

        if (_pendingShowdownReveals.Count <= 1)
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
            OnHandPauseOver();
            return;
        }

        if (CountWithChips() <= 1)
        {
            var finalTimer = GetTree().CreateTimer(pause);
            finalTimer.Timeout += OnHandPauseOver;
            return;
        }

        // Keep the result readable first. The authoritative next hand may only replace it after all
        // peers have received the cleanup phase and finished returning the same card nodes to deck.
        var cleanup = Mathf.Min(pause, _scheduledCardCleanupSeconds);
        var reading = Mathf.Max(0.0f, pause - cleanup);
        if (reading <= 0.0f)
        {
            BeginCardCleanup();
            return;
        }

        var readingTimer = GetTree().CreateTimer(reading);
        readingTimer.Timeout += BeginCardCleanup;
    }

    private void BeginCardCleanup()
    {
        if (!Multiplayer.IsServer() || !MatchRunning || Game == null)
            return;

        if (CountWithChips() <= 1 || _scheduledCardCleanupSeconds <= 0.0f)
        {
            OnHandPauseOver();
            return;
        }

        _cardCleanupActive = true;
        _lastAction = "card_cleanup";
        _lastPlayer = "";
        _lastAmount = 0;
        Game.CallExtendCurrentTurn(BuildContext(
            _lastAction, _lastPlayer, _lastAmount, advanceTurn: false));

        var cleanupTimer = GetTree().CreateTimer(_scheduledCardCleanupSeconds);
        cleanupTimer.Timeout += OnHandPauseOver;
    }

    private void OnHandPauseOver()
    {
        if (!Multiplayer.IsServer() || !MatchRunning || Game == null)
            return;

        // Decided here rather than before the pause, so the finished hand is on the table for the
        // same length of time whether or not it was the last one.
        if (CountWithChips() <= 1)
        {
            EndSession();
            return;
        }

        _buttonSeat = PokerSeating.Next(_buttonSeat, Mathf.Max(1, _seatOrder.Count));
        _cardCleanupActive = false;
        ClearHandResult();
        StartHand();
    }

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
        // TableGame also asks for a handoff context when a disconnected seat is RECLAIMED. Reclaiming
        // somebody who is not acting must not invalidate the real actor's stamped turn (or cancel the
        // chips they are currently preparing). A genuine owner handoff still advances the stamp, so
        // requests issued by the old connection can never be replayed by the reclaimed one.
        var outgoingOwnsTurn = Game?.IsTurnOwner(outgoingPlayerId) == true;
        return BuildContext("left", outgoingPlayerId, 0, advanceTurn: outgoingOwnsTurn);
    }
}
