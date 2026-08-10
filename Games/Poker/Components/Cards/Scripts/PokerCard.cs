using Godot;
using Poker.Rules;

/// <summary>
/// One playing card in the world.
///
/// Two quads back to back rather than a box: a card is drawn from a single sheet whose front and
/// back come from different images, and a box's one surface cannot carry both. The face quad picks
/// its card out of the atlas by UV, so all 52 share one texture and one mesh.
///
/// With no art present the face falls back to a labelled white card, so the table is playable and
/// every test still means something before the pack arrives.
/// </summary>
[GlobalClass]
public partial class PokerCard : Node3D
{
    [Export] public MeshInstance3D Face;
    [Export] public MeshInstance3D Back;
    [Export] public Label3D FaceLabel;

    /// <summary>Forces the labelled placeholder even when the atlas is present.</summary>
    [Export] public bool ForcePlaceholder;

    public int CardId { get; private set; } = Poker.Rules.CardId.None;
    public bool IsFaceDown { get; private set; }

    /// <summary>
    /// Half turn about the card's long axis, so the back comes up. Written as an explicit axis
    /// image for the same reason the domino tile's is: composing it from Euler angles mirrors the
    /// face without saying so.
    /// </summary>
    private static readonly Basis FlipOver = new(
        new Vector3(1.0f, 0.0f, 0.0f),
        new Vector3(0.0f, -1.0f, 0.0f),
        new Vector3(0.0f, 0.0f, -1.0f));

    public void Configure(int cardId, PokerLayoutSpec spec) => Configure(cardId, spec, faceDown: false);

    public void Configure(int cardId, PokerLayoutSpec spec, bool faceDown)
    {
        CardId = cardId;
        IsFaceDown = faceDown;

        Resize(spec);

        if (Back != null)
            Back.MaterialOverride = PokerCardFaces.BackMaterial();

        var hasArt = !ForcePlaceholder && PokerCardFaces.IsAvailable && Poker.Rules.CardId.IsValid(cardId);

        if (Face != null)
            Face.MaterialOverride = hasArt ? PokerCardFaces.FaceMaterial(cardId) : PlaceholderMaterial();

        if (FaceLabel != null)
        {
            FaceLabel.Visible = !hasArt && Poker.Rules.CardId.IsValid(cardId) && !faceDown;
            FaceLabel.Text = Poker.Rules.CardId.IsValid(cardId) ? Poker.Rules.CardId.Label(cardId) : "";
            FaceLabel.Modulate = SuitColor(cardId);
        }
    }

    /// <summary>Turns the card over in place, leaving whatever it was configured with intact.</summary>
    public void SetFaceDown(bool faceDown)
    {
        if (IsFaceDown == faceDown)
            return;

        IsFaceDown = faceDown;
        if (FaceLabel != null)
            FaceLabel.Visible = FaceLabel.Visible && !faceDown;
    }

    /// <summary>The transform that presents this card the right way up at a placement.</summary>
    public static Basis Orientation(bool faceDown) => faceDown ? FlipOver : Basis.Identity;

    /// <summary>
    /// Fades a card without losing its face. Goes through GeometryInstance3D.Transparency rather
    /// than a material override so a preview still reads as the card it is.
    /// </summary>
    public void SetDimmed(bool dimmed, float alpha)
    {
        var transparency = dimmed ? Mathf.Clamp(1.0f - alpha, 0.0f, 1.0f) : 0.0f;

        if (Face != null)
            Face.Transparency = transparency;
        if (Back != null)
            Back.Transparency = transparency;
    }

    private void Resize(PokerLayoutSpec spec)
    {
        var size = new Vector2(spec.CardWidth, spec.CardLength);
        var lift = Mathf.Max(spec.CardThickness, 0.0002f) * 0.5f;

        Apply(Face, size, lift, faceUp: true);
        Apply(Back, size, lift, faceUp: false);
    }

    private static void Apply(MeshInstance3D quad, Vector2 size, float lift, bool faceUp)
    {
        if (quad == null)
            return;

        // Duplicated per instance: a shared mesh would resize every card on the table at once.
        if (quad.Mesh is not QuadMesh mesh || mesh.ResourceLocalToScene == false)
        {
            mesh = new QuadMesh();
            quad.Mesh = mesh;
        }

        mesh.Size = size;

        // Lying flat with the face up: the quad's own +Z becomes the card's +Y.
        //
        // Both of these are proper ROTATIONS — determinant +1 — and that is not a detail. The
        // obvious-looking (Right, Back, Up) has determinant -1, which mirrors the quad; the geometry
        // is a symmetric rectangle so it looks identical, and the mirroring only shows up once
        // something readable rides on the face. It did: the first render came back with every rank
        // and suit reversed.
        var basis = faceUp
            ? new Basis(Vector3.Right, Vector3.Forward, Vector3.Up)
            : new Basis(Vector3.Right, Vector3.Back, Vector3.Down);

        quad.Transform = new Transform3D(basis, new Vector3(0.0f, faceUp ? lift : -lift, 0.0f));
    }

    private StandardMaterial3D PlaceholderMaterial() =>
        new()
        {
            AlbedoColor = new Color(0.97f, 0.97f, 0.95f),
            Roughness = 0.7f,
            // Unshaded so a white card stays white in the bar's dim light instead of reading grey.
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };

    private static Color SuitColor(int cardId)
    {
        if (!Poker.Rules.CardId.IsValid(cardId))
            return Colors.Black;

        var suit = Poker.Rules.CardId.SuitOf(cardId);
        var red = suit is Poker.Rules.CardId.Diamonds or Poker.Rules.CardId.Hearts;

        return red ? new Color(0.78f, 0.12f, 0.16f) : new Color(0.08f, 0.08f, 0.10f);
    }
}
