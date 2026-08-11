using System;
using System.Collections.Generic;
using Godot;
using Poker.Rules;

/// <summary>
/// Bridges the logical 0..51 card ids to the individual meshes contained in the imported deck.
///
/// The GLB is a contact sheet: its 52 meshes have their positions baked into their vertices. This
/// library keeps those source bounds with each mesh so PokerCard can recenter and resize it without
/// changing any dealing, networking or animation code.
/// </summary>
public static class PokerCardAssetMeshes
{
    public const string AssetPath = "res://Assets/Poker/poker_cards_assets.glb";

    private static readonly Mesh[] Meshes = new Mesh[CardId.Count];
    private static readonly Aabb[] Bounds = new Aabb[CardId.Count];
    private static readonly HashSet<ulong> PreparedMaterials = new();

    private static readonly Dictionary<string, int> UnnamedClubCards = new()
    {
        ["Card.047"] = CardId.From(6, CardId.Clubs), // 8
        ["Card.048"] = CardId.From(7, CardId.Clubs), // 9
        ["Card.049"] = CardId.From(CardId.Ten, CardId.Clubs),
        ["Card.050"] = CardId.From(CardId.Jack, CardId.Clubs),
        ["Card.051"] = CardId.From(CardId.Queen, CardId.Clubs),
        ["Card.052"] = CardId.From(CardId.King, CardId.Clubs),
    };

    private static bool _loadAttempted;
    private static int _loadedCount;

    public static bool IsAvailable
    {
        get
        {
            EnsureLoaded();
            return _loadedCount == CardId.Count;
        }
    }

    public static bool TryGet(int cardId, out Mesh mesh, out Aabb bounds)
    {
        EnsureLoaded();

        if (!CardId.IsValid(cardId) || Meshes[cardId] == null)
        {
            mesh = null;
            bounds = default;
            return false;
        }

        mesh = Meshes[cardId];
        bounds = Bounds[cardId];
        return true;
    }

    private static void EnsureLoaded()
    {
        if (_loadAttempted)
            return;

        _loadAttempted = true;

        var scene = GD.Load<PackedScene>(AssetPath);
        var root = scene?.Instantiate();
        if (root == null)
            return;

        try
        {
            ReadMeshes(root);
        }
        finally
        {
            root.Free();
        }
    }

    private static void ReadMeshes(Node node)
    {
        if (node is MeshInstance3D { Mesh: not null } meshInstance
            && TryCardId(meshInstance.Name.ToString(), out var cardId)
            && Meshes[cardId] == null)
        {
            Meshes[cardId] = meshInstance.Mesh;
            Bounds[cardId] = meshInstance.Mesh.GetAabb();
            PrepareMaterials(meshInstance.Mesh);
            _loadedCount++;
        }

        foreach (var child in node.GetChildren())
            ReadMeshes(child);
    }

    /// <summary>
    /// Cards are almost always observed at a grazing angle. Trilinear filtering alone picks a very
    /// small mip level there and smears the corner rank; anisotropic filtering preserves that detail
    /// while retaining mipmaps, so distant cards stay sharp without shimmering.
    /// </summary>
    private static void PrepareMaterials(Mesh mesh)
    {
        for (var surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            if (mesh.SurfaceGetMaterial(surface) is not BaseMaterial3D material
                || !PreparedMaterials.Add(material.GetInstanceId()))
                continue;

            material.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
        }
    }

    private static bool TryCardId(string nodeName, out int cardId)
    {
        // Godot's glTF importer normalizes the Blender names Card.047..052 to Card_047..052.
        // Accept both forms so changing the import naming version cannot silently lose six cards.
        if (UnnamedClubCards.TryGetValue(nodeName.Replace('_', '.'), out cardId))
            return true;

        var separator = nodeName.IndexOf('_');
        if (separator <= 0 || separator >= nodeName.Length - 1)
        {
            cardId = CardId.None;
            return false;
        }

        if (!TrySuit(nodeName[..separator], out var suit)
            || !TryRank(nodeName[(separator + 1)..], out var rank))
        {
            cardId = CardId.None;
            return false;
        }

        cardId = CardId.From(rank, suit);
        return true;
    }

    private static bool TrySuit(string value, out int suit)
    {
        suit = value switch
        {
            "Club" => CardId.Clubs,
            "Diamond" => CardId.Diamonds,
            "Heart" => CardId.Hearts,
            "Spade" => CardId.Spades,
            _ => CardId.None,
        };

        return suit != CardId.None;
    }

    private static bool TryRank(string value, out int rank)
    {
        rank = value switch
        {
            "Jack" => CardId.Jack,
            "Queen" => CardId.Queen,
            "King" => CardId.King,
            "Ace" => CardId.Ace,
            _ => CardId.None,
        };

        if (rank != CardId.None)
            return true;

        if (!int.TryParse(value, out var number) || number < 2 || number > 10)
            return false;

        rank = number - 2;
        return true;
    }
}
