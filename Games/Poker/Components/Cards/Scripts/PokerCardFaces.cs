using Godot;
using Poker.Rules;

/// <summary>
/// Turns a card id into a patch of the art pack's atlas.
///
/// The pack is one image of 13 columns by 4 rows. That makes the mapping arithmetic rather than the
/// hand-read table the domino pack needed — with one catch that is exactly the trap PackFaces exists
/// to avoid: THE COLUMNS ARE NOT IN RANK ORDER. The contact sheet runs A,2,3,…,10,J,K,Q, with the
/// king before the queen. So the column is a lookup, written out, and the logical rank order in
/// <see cref="CardId"/> stays untouched — a differently ordered pack changes this table and nothing
/// else, and can never change who wins a hand.
///
/// Everything degrades: with no atlas present the cards render as labelled placeholders and the game
/// stays playable, the same way DominoTile does.
/// </summary>
public static class PokerCardFaces
{
    private const string AtlasPath = "res://Assets/Poker/cards.png";

    /// <summary>
    /// The back, in order of preference. The art pack ships one — it is the third texture the glb
    /// extracts — and it is the one thing about the cards the pack can actually supply today, so it
    /// is worth taking even while the 52 faces are still placeholders.
    /// </summary>
    private static readonly string[] BackPaths =
    {
        "res://Assets/Poker/card_back.png",
        "res://Assets/Poker/poker_assets_2.png",
    };

    public const int Columns = 13;
    public const int Rows = 4;

    /// <summary>
    /// Atlas column for each LOGICAL rank, 0 (Two) to 12 (Ace). Read off the pack, never assumed.
    /// </summary>
    private static readonly int[] RankColumns =
    {
        1,  // 2
		2,  // 3
		3,  // 4
		4,  // 5
		5,  // 6
		6,  // 7
		7,  // 8
		8,  // 9
		9,  // 10
		10, // J
		12, // Q  — after the king in this pack
		11, // K
		0,  // A  — first column
	};

    private static bool _loaded;
    private static Texture2D _atlas;
    private static Texture2D _back;

    public static bool IsAvailable
    {
        get
        {
            EnsureLoaded();
            return _atlas != null;
        }
    }

    /// <summary>The whole sheet, for a material that will pick its own patch by UV.</summary>
    public static Texture2D Atlas
    {
        get
        {
            EnsureLoaded();
            return _atlas;
        }
    }

    public static Texture2D Back
    {
        get
        {
            EnsureLoaded();
            return _back;
        }
    }

    /// <summary>Where this card's face sits in the sheet, as a UV offset for a 1/13 x 1/4 patch.</summary>
    public static Vector3 UvOffset(int cardId)
    {
        if (!CardId.IsValid(cardId))
            return Vector3.Zero;

        var column = RankColumns[CardId.RankOf(cardId)];
        var row = CardId.SuitOf(cardId);

        return new Vector3((float)column / Columns, (float)row / Rows, 0.0f);
    }

    public static Vector3 UvScale => new(1.0f / Columns, 1.0f / Rows, 1.0f);

    /// <summary>A material showing exactly one card's face.</summary>
    public static StandardMaterial3D FaceMaterial(int cardId)
    {
        EnsureLoaded();
        if (_atlas == null)
            return null;

        return new StandardMaterial3D
        {
            AlbedoTexture = _atlas,
            Uv1Offset = UvOffset(cardId),
            Uv1Scale = UvScale,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            Roughness = 0.62f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
    }

    public static StandardMaterial3D BackMaterial()
    {
        EnsureLoaded();

        return new StandardMaterial3D
        {
            AlbedoTexture = _back,
            AlbedoColor = _back == null ? new Color(0.35f, 0.10f, 0.14f) : Colors.White,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            Roughness = 0.62f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;

        _loaded = true;

        if (ResourceLoader.Exists(AtlasPath))
            _atlas = GD.Load<Texture2D>(AtlasPath);

        foreach (var path in BackPaths)
        {
            if (!ResourceLoader.Exists(path))
                continue;

            _back = GD.Load<Texture2D>(path);
            break;
        }
    }
}
