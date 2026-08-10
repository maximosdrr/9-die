using Domino.Rules;
using Godot;

/// <summary>
/// One tile on the table. Identity is a tile id and nothing else; how that id becomes a face is
/// entirely this class's business, so the layout, the rules, the network and the hand views never
/// learn which art pack arrived.
///
/// Falls back to a labelled box whenever the pack cannot be read, so the game stays playable — and
/// debuggable — even with the art missing.
/// </summary>
[GlobalClass]
public partial class DominoTile : Node3D
{
    [Export] public MeshInstance3D Body;
    [Export] public Label3D FaceLabel;

    /// <summary>Draws the box and the written value instead of the model. Useful for debugging.</summary>
    [Export] public bool ForcePlaceholder;

    public int TileId { get; private set; } = DominoTileId.NoEnd;

    public bool IsFaceDown { get; private set; }

    /// <summary>Half turn about the tile's long axis: the face goes from +Y to -Y.</summary>
    private static readonly Transform3D FlipOver = new(
        new Basis(
            new Vector3(-1.0f, 0.0f, 0.0f),
            new Vector3(0.0f, -1.0f, 0.0f),
            new Vector3(0.0f, 0.0f, 1.0f)),
        Vector3.Zero);

    /// <summary>
    /// Gives the tile its identity. The placeholder is sized from the layout spec rather than from
    /// the scene, so the box always matches the footprint the chain reserved for it — a mismatch
    /// there would make tiles look overlapped while the layout test still passes.
    /// </summary>
    public void Configure(int tileId, LayoutSpec spec) => Configure(tileId, spec, faceDown: false);

    /// <summary>
    /// A face-down tile still carries a real id — the stock and the opponents' hands are real tiles
    /// lying face down, not blanks — but nothing that reaches a peer other than its owner is ever
    /// configured with anything but a placeholder id.
    /// </summary>
    public void Configure(int tileId, LayoutSpec spec, bool faceDown)
    {
        TileId = tileId;
        IsFaceDown = faceDown;

        if (Body == null)
            return;

        if (!ForcePlaceholder && DominoTileMeshes.For(tileId) is { } face)
        {
            Body.Mesh = face;

            var placed = DominoTileMeshes.TransformFor(tileId);
            Body.Transform = faceDown ? FlipOver * placed : placed;

            // Cleared so the pack's own atlas material shows the pips; an override here would
            // repaint all 28 tiles blank.
            Body.MaterialOverride = null;
            FaceLabel?.Hide();
            return;
        }

        ApplyPlaceholder(spec);

        if (FaceLabel == null)
            return;

        if (faceDown)
        {
            FaceLabel.Hide();
            return;
        }

        FaceLabel.Show();
        FaceLabel.Text = DominoTileId.Label(tileId);
        FaceLabel.Position = new Vector3(0.0f, spec.TileThickness * 0.5f + 0.001f, 0.0f);
    }

    /// <summary>
    /// Fades a tile the player cannot use. Goes through GeometryInstance3D.Transparency rather than
    /// a material override so the art pack's own face keeps showing through — a dimmed tile still
    /// has to be readable, it is just not selectable.
    /// </summary>
    public void SetDimmed(bool dimmed, float alpha)
    {
        if (Body == null)
            return;

        Body.Transparency = dimmed ? Mathf.Clamp(1.0f - alpha, 0.0f, 1.0f) : 0.0f;
    }

    private static readonly StandardMaterial3D PlaceholderMaterial = new()
    {
        AlbedoColor = new Color(0.925f, 0.925f, 0.898f),
        Roughness = 0.45f,
    };

    private void ApplyPlaceholder(LayoutSpec spec)
    {
        Body.Transform = Transform3D.Identity;
        Body.MaterialOverride = PlaceholderMaterial;

        // Duplicated per instance: sizing a shared BoxMesh would resize every tile at once.
        var box = (BoxMesh)(Body.Mesh as BoxMesh ?? new BoxMesh()).Duplicate();
        box.Size = new Vector3(spec.TileWidth, spec.TileThickness, spec.TileLength);
        Body.Mesh = box;
    }
}
