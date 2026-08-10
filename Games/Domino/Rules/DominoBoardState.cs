using System.Collections.Generic;

namespace Domino.Rules;

/// <summary>
/// The public half of a domino match: the ordered plays and the two open ends. Knows nothing about
/// hands, the boneyard or whose turn it is — those are the secret half, and keeping them apart is
/// what lets this whole type be handed to every peer.
///
/// Rebuildable from nothing but the play list, which is how a peer that connects mid-match catches
/// up and how the tile positions are derived without ever sending a transform.
/// </summary>
public sealed class DominoBoardState
{
    private readonly List<PlayRecord> _plays = new();

    public IReadOnlyList<PlayRecord> Plays => _plays;

    public int LeftEnd { get; private set; } = DominoTileId.NoEnd;

    public int RightEnd { get; private set; } = DominoTileId.NoEnd;

    public bool IsEmpty => _plays.Count == 0;

    public int Count => _plays.Count;

    public void Reset()
    {
        _plays.Clear();
        LeftEnd = DominoTileId.NoEnd;
        RightEnd = DominoTileId.NoEnd;
    }

    /// <summary>The value a tile must carry to be laid against <paramref name="end"/>.</summary>
    public int ValueAt(ChainEnd end) => end == ChainEnd.Left ? LeftEnd : RightEnd;

    /// <summary>
    /// Lays a tile down, moving the end it matched. Returns false and leaves the board untouched
    /// when the move is illegal — the server calls this last, after its own checks, so a false
    /// here means a bug rather than a bad client.
    /// </summary>
    public bool TryPlay(string playerId, int tileId, ChainEnd end)
    {
        if (!DominoTileId.IsValid(tileId))
            return false;

        if (IsEmpty)
        {
            // The opening tile has no end to match. Its low half faces the left branch and its
            // high half the right, which the layout mirrors.
            DominoTileId.Split(tileId, out var low, out var high);
            LeftEnd = low;
            RightEnd = high;
            _plays.Add(new PlayRecord(playerId, tileId, ChainEnd.Right));
            return true;
        }

        var matched = ValueAt(end);
        var outgoing = DominoTileId.OtherHalf(tileId, matched);
        if (outgoing == DominoTileId.NoEnd)
            return false;

        if (end == ChainEnd.Left)
            LeftEnd = outgoing;
        else
            RightEnd = outgoing;

        _plays.Add(new PlayRecord(playerId, tileId, end));
        return true;
    }

    /// <summary>Every tile currently face up on the table.</summary>
    public IEnumerable<int> PlayedTiles()
    {
        foreach (var play in _plays)
            yield return play.TileId;
    }

    public static DominoBoardState FromPlays(IEnumerable<PlayRecord> plays)
    {
        var board = new DominoBoardState();
        foreach (var play in plays)
            board.TryPlay(play.PlayerId, play.TileId, play.End);

        return board;
    }
}
