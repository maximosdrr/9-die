using System.Collections.Generic;
using Godot;
using Poker.Rules;

/// <summary>
/// Owns the revealed-hands reading beat. The old implementation duplicated the best five cards and
/// rearranged those copies on the cloth; this version leaves every physical card in place and shows
/// the shared result as a screen-space ranking instead.
/// </summary>
[GlobalClass]
public partial class PokerShowdownPresenter : Node3D
{
    private readonly List<string> _displayOrder = new();
    private PokerGame _game;
    private PokerBoardPresenter _board;
    private PokerPresentationProfile _profile;
    private System.Func<string, string> _nameOf;
    private PokerShowdownRankingOverlay _overlay;
    private int _hand = -1;
    private float _readingElapsed;

    public bool Active { get; private set; }
    public bool Settled { get; private set; }
    public bool ReadyForPayout { get; private set; }
    public float RevealHoldElapsed { get; private set; }
    public IReadOnlyList<string> DisplayOrder => _displayOrder;
    public int DisplayedCardCount => _overlay?.DisplayedCardCount ?? 0;
    public int DisplayedPlayerCount => _overlay?.PlayerRowCount ?? 0;
    public int DisplayedWinnerCount => _overlay?.WinnerRowCount ?? 0;
    public int DisplayedCommunityCardCount => _overlay?.CommunityCardCount ?? 0;
    public bool OverlayVisible => _overlay?.VisibleOnScreen ?? false;
    public int CleanupCardCount => 0;
    public int VisibleCardCount => 0;

    public void Configure(PokerGame game, PokerBoardPresenter board,
        PokerPresentationProfile profile, System.Func<string, string> nameOf,
        Font displayFont = null)
    {
        _game = game;
        _board = board;
        _profile = profile ?? new PokerPresentationProfile();
        _nameOf = nameOf;
        _overlay = GetNodeOrNull<PokerShowdownRankingOverlay>("RankingOverlay");
        if (_overlay == null)
        {
            _overlay = new PokerShowdownRankingOverlay { Name = "RankingOverlay" };
            AddChild(_overlay);
        }
        _overlay.Configure(displayFont);
    }

    public void Reset(bool authoritativeSettled = false, int hand = -1)
    {
        Active = false;
        Settled = authoritativeSettled;
        ReadyForPayout = authoritativeSettled;
        _hand = authoritativeSettled ? hand : -1;
        _readingElapsed = 0.0f;
        RevealHoldElapsed = 0.0f;
        _displayOrder.Clear();
        _overlay?.HideImmediate();
    }

    public bool Advance(float delta, bool blocked)
    {
        if (!Active)
            return TryStart(delta, blocked);
        if (ReadyForPayout)
            return false;

        _readingElapsed += Mathf.Max(0.0f, delta);
        ReadyForPayout = _readingElapsed >= Mathf.Max(
            0.0f, _profile.RankedHandsReadingSeconds);
        if (ReadyForPayout)
            _overlay?.Dismiss();
        return true;
    }

    public void BeginCardCleanup(
        float returnSeconds, float stagger, float shuffleSeconds, int startSlot = 0)
    {
        _overlay?.HideImmediate();
        Active = false;
    }

    private bool TryStart(float delta, bool blocked)
    {
        if (_game == null || _board == null || _game.HandNumber <= 0 || _hand == _game.HandNumber
            || !_game.HandSettled || _game.RevealedHoleCards.Count == 0 || !_board.Settled || blocked)
        {
            RevealHoldElapsed = 0.0f;
            return false;
        }

        // The blocked contract already waits for every authored reveal and physical card flight.
        // Showing on the first unblocked frame makes the ranking truly immediate after showdown.
        RevealHoldElapsed += Mathf.Max(0.0f, delta);
        var ranked = new List<(string PlayerId, PokerHandRank Rank, int[] HoleCards)>();
        foreach (var entry in _game.RevealedHoleCards)
        {
            var rank = PokerHandEvaluator.Evaluate(entry.Value, _game.Board);
            if (rank != PokerHandRank.None && entry.Value?.Length == PokerDeal.HoleCardCount)
                ranked.Add((entry.Key, rank, (int[])entry.Value.Clone()));
        }
        ranked.Sort((left, right) =>
        {
            var strength = right.Rank.CompareTo(left.Rank);
            return strength != 0 ? strength : System.Array.IndexOf(_game.SeatOrder, left.PlayerId)
                .CompareTo(System.Array.IndexOf(_game.SeatOrder, right.PlayerId));
        });
        if (ranked.Count == 0)
            return false;

        var entries = new List<PokerShowdownRankingOverlay.Entry>(ranked.Count);
        _displayOrder.Clear();
        var place = 0;
        var previous = PokerHandRank.None;
        for (var index = 0; index < ranked.Count; index++)
        {
            var result = ranked[index];
            if (index == 0 || result.Rank != previous)
                place = index + 1;
            previous = result.Rank;
            _displayOrder.Add(result.PlayerId);
            entries.Add(new PokerShowdownRankingOverlay.Entry
            {
                PlayerId = result.PlayerId,
                PlayerName = _nameOf?.Invoke(result.PlayerId) ?? result.PlayerId,
                HoleCards = result.HoleCards,
                Rank = result.Rank,
                Place = place,
                Won = _game.Winners.ContainsKey(result.PlayerId),
            });
        }

        _hand = _game.HandNumber;
        Active = true;
        Settled = true;
        ReadyForPayout = _profile.RankedHandsReadingSeconds <= 0.0f;
        _readingElapsed = 0.0f;
        _overlay.Present(_game.Board, entries);
        if (ReadyForPayout)
            _overlay.Dismiss();
        return true;
    }
}
