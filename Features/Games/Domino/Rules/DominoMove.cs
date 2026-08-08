namespace Domino.Rules;

/// <summary>Which open end of the chain a tile is laid against.</summary>
public enum ChainEnd
{
	Left = 0,
	Right = 1,
}

/// <summary>
/// One tile reaching the table. Together with the ones before it this is the ENTIRE public state
/// of the board: the ends, every tile's orientation and every tile's position on the cloth are
/// derived from the ordered list of plays, so positions never travel over the network.
/// </summary>
public readonly struct PlayRecord
{
	public readonly string PlayerId;
	public readonly int TileId;

	/// <summary>Ignored for the opening tile, which has no end to choose from.</summary>
	public readonly ChainEnd End;

	public PlayRecord(string playerId, int tileId, ChainEnd end)
	{
		PlayerId = playerId;
		TileId = tileId;
		End = end;
	}
}

/// <summary>
/// A move the player could legally make right now. A tile that fits both ends yields two options,
/// because they put it in different places on the table even when the ends are the same value.
/// </summary>
public readonly struct MoveOption
{
	public readonly int TileId;
	public readonly ChainEnd End;

	public MoveOption(int tileId, ChainEnd end)
	{
		TileId = tileId;
		End = end;
	}
}
