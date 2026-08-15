using Godot;
using Poker.Rules;

/// <summary>Editor rendering for PokerExperienceAuthoring; never instantiated during play.</summary>
public partial class PokerExperienceAuthoring : Node3D
{
    [ExportGroup("Preview content")]
    [Export] public PackedScene PreviewCardScene;
    [Export] public Font PreviewChalkFont;
    [Export] public Shader PreviewChalkShader;
    [Export(PropertyHint.Range, "0.05,0.2,0.005")] public float PreviewCardWidth = 0.076f;
    [Export(PropertyHint.Range, "0.07,0.26,0.005")] public float PreviewCardLength = 0.106f;
    [Export(PropertyHint.Range, "1.0,1.25,0.01")] public float PreviewCommunityCardScale = 1.20f;
    [Export(PropertyHint.Range, "0.001,0.03,0.001")] public float PreviewCardGap = 0.020f;

    private const string GeneratedPreviewName = "ExperiencePreview_EditorOnly";
    private Node3D _generatedPreview;

    private void RebuildExperiencePreview()
    {
        if (!Engine.IsEditorHint() || !IsInsideTree())
            return;

        _previewRebuildQueued = false;
        _previewShapeSignature = PreviewShapeSignature();
        ClearExperiencePreview();
        _generatedPreview = new Node3D { Name = GeneratedPreviewName };
        AddChild(_generatedPreview, forceReadableName: false, InternalMode.Back);

        BuildDeckPreview();
        BuildBoardPreview();
        BuildActionGuidePreview();
        BuildFirstPersonCardsPreview();
    }

    private void BuildDeckPreview()
    {
        if (DeckAnchor == null)
            return;

        var root = new Node3D { Name = "DeckPreview" };
        _generatedPreview.AddChild(root);
        root.GlobalTransform = PreviewDeckGlobalTransform();
        for (var index = 0; index < 8; index++)
        {
            var card = NewPreviewCard($"DeckCard{index}", faceDown: true);
            root.AddChild(card);
            card.Position = Vector3.Up * (index + 0.5f) * 0.0011f;
            card.Rotation = new Vector3(0.0f, (index % 2 == 0 ? 1.0f : -1.0f) * 0.012f, 0.0f);
        }
    }

    private void UpdateExperiencePreviewTransforms()
    {
        if (!Engine.IsEditorHint() || !IsInstanceValid(_generatedPreview))
            return;

        if (DeckAnchor != null
            && _generatedPreview.GetNodeOrNull<Node3D>("DeckPreview") is { } deck)
            deck.GlobalTransform = PreviewDeckGlobalTransform();
        if (CommunityCardsAnchor != null
            && _generatedPreview.GetNodeOrNull<Node3D>("CommunityCardsPreview") is { } board)
            board.GlobalTransform = PreviewCommunityCardsGlobalTransform();
        if (_generatedPreview.GetNodeOrNull<Node3D>("ActionGuidePreview") is { } actions)
        {
            if (actions.GetNodeOrNull<Node3D>("PassFoldGuidePreview") is { } passFold)
                passFold.GlobalTransform = PreviewPassFoldGuideGlobalTransform();
            if (actions.GetNodeOrNull<Node3D>("WagerGuidePreview") is { } wager)
                wager.GlobalTransform = PreviewWagerGuideGlobalTransform();
        }
    }

    private void BuildBoardPreview()
    {
        if (CommunityCardsAnchor == null)
            return;

        var root = new Node3D { Name = "CommunityCardsPreview" };
        _generatedPreview.AddChild(root);
        root.GlobalTransform = PreviewCommunityCardsGlobalTransform();
        var step = PreviewCardWidth + PreviewCardGap;
        for (var index = 0; index < PokerDeal.BoardCount; index++)
        {
            var card = NewPreviewCard($"CommunityCard{index}", faceDown: false,
                PreviewCommunityCardScale);
            root.AddChild(card);
            card.Position = new Vector3(
                (index - (PokerDeal.BoardCount - 1) * 0.5f) * step,
                0.001f,
                0.0f);
        }
    }

    private MeshInstance3D NewPreviewCard(string name, bool faceDown, float scale = 1.0f)
    {
        return new MeshInstance3D
        {
            Name = name,
            Mesh = new BoxMesh
            {
                Size = new Vector3(
                    PreviewCardWidth * scale, 0.0012f, PreviewCardLength * scale),
            },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = faceDown
                    ? new Color(0.07f, 0.08f, 0.10f)
                    : new Color(0.96f, 0.95f, 0.91f),
                Roughness = 0.65f,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private void BuildActionGuidePreview()
    {
        if (PassFoldGuideAnchor == null && WagerGuideAnchor == null && ActionGuideAnchor == null)
            return;

        var container = new Node3D { Name = "ActionGuidePreview", TopLevel = true };
        _generatedPreview.AddChild(container);
        container.GlobalTransform = Transform3D.Identity;
        var passFold = new Node3D { Name = "PassFoldGuidePreview" };
        var wager = new Node3D { Name = "WagerGuidePreview" };
        container.AddChild(passFold);
        container.AddChild(wager);
        passFold.GlobalTransform = PreviewPassFoldGuideGlobalTransform();
        wager.GlobalTransform = PreviewWagerGuideGlobalTransform();

        const float halfCircle = Mathf.Pi * 0.5f;
        var line = PreviewChalkMaterial(new Color(0.95f, 0.93f, 0.86f, 0.74f));
        AddPreviewArc(passFold, "Actions", Vector2.Zero,
            Vector2.Up, Vector2.Right, ActionZoneRadius, -halfCircle, halfCircle, line);
        AddPreviewArc(wager, "WagerOuter", Vector2.Zero,
            Vector2.Down, Vector2.Right, ConfirmZoneOuterRadius, -halfCircle, halfCircle, line);
        AddPreviewArc(wager, "WagerInner", Vector2.Zero,
            Vector2.Down, Vector2.Right, ConfirmZoneInnerRadius, -halfCircle, halfCircle, line);
        AddPreviewLine(passFold, "ActionDivider",
            Vector2.Zero,
            Vector2.Up * ActionZoneRadius, line);
        AddPreviewLine(wager, "WagerLeftEdge",
            new Vector2(-ConfirmZoneInnerRadius, 0.0f),
            new Vector2(-ConfirmZoneOuterRadius, 0.0f), line);
        AddPreviewLine(wager, "WagerRightEdge",
            new Vector2(ConfirmZoneInnerRadius, 0.0f),
            new Vector2(ConfirmZoneOuterRadius, 0.0f), line);

        AddPreviewLabel(passFold, "PASSAR", new Vector3(
            ActionZoneRadius * 0.48f, 0.004f,
            -ActionZoneRadius * 0.48f));
        AddPreviewLabel(passFold, "DESISTIR", new Vector3(
            -ActionZoneRadius * 0.48f, 0.004f,
            -ActionZoneRadius * 0.48f));
        AddPreviewCurvedLabel(wager, "APOSTAR",
            Vector2.Zero,
            (ConfirmZoneInnerRadius + ConfirmZoneOuterRadius) * 0.5f,
            Mathf.Pi * ConfirmLabelSpanPi, Mathf.RoundToInt(ChalkGuideFontSize * 0.82f));
    }

    private void BuildFirstPersonCardsPreview()
    {
        var root = new Node3D { Name = "FirstPersonCardsPreview", TopLevel = true };
        _generatedPreview.AddChild(root);
        for (var index = 0; index < PokerDeal.HoleCardCount; index++)
        {
            var card = NewPreviewCard($"HeldCard{index}", faceDown: false);
            root.AddChild(card);
            card.Transform = index == 0 ? Card0Pose : Card1Pose;
        }
    }

    private void UpdateFirstPersonCardsPreview(Transform3D gripTransform)
    {
        if (!Engine.IsEditorHint() || !IsInstanceValid(_generatedPreview))
            return;
        if (_generatedPreview.GetNodeOrNull<Node3D>("FirstPersonCardsPreview") is { } cards)
        {
            cards.Visible = ShowFirstPersonHands;
            cards.GlobalTransform = gripTransform * CardsPose;
            if (cards.GetNodeOrNull<Node3D>("HeldCard0") is { } card0)
                card0.Transform = Card0Pose;
            if (cards.GetNodeOrNull<Node3D>("HeldCard1") is { } card1)
                card1.Transform = Card1Pose;
        }
    }

    private StandardMaterial3D PreviewChalkMaterial(Color colour) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = colour,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    private Transform3D PreviewPassFoldGuideGlobalTransform()
    {
        var board = GetParent()?.GetNodeOrNull<Node3D>("BoardHolder");
        if (board == null)
            return (PassFoldGuideAnchor ?? ActionGuideAnchor)?.GlobalTransform
                   ?? Transform3D.Identity;

        return board.GlobalTransform
               * PassFoldGuideTransformFor(board, PreviewReaderFacing(board));
    }

    private Transform3D PreviewWagerGuideGlobalTransform()
    {
        var board = GetParent()?.GetNodeOrNull<Node3D>("BoardHolder");
        if (board == null)
            return (WagerGuideAnchor ?? ActionGuideAnchor)?.GlobalTransform
                   ?? Transform3D.Identity;

        return board.GlobalTransform
               * WagerGuideTransformFor(board, PreviewReaderFacing(board));
    }

    private Transform3D PreviewDeckGlobalTransform()
    {
        var board = GetParent()?.GetNodeOrNull<Node3D>("BoardHolder");
        if (board == null || DeckAnchor == null)
            return DeckAnchor?.GlobalTransform ?? Transform3D.Identity;

        var canonical = board.GlobalTransform.AffineInverse() * DeckAnchor.GlobalTransform;
        return board.GlobalTransform * PokerTableLayout.ReaderAlignedFrame(
            canonical, PreviewReaderFacing(board), Vector2.Down);
    }

    private Transform3D PreviewCommunityCardsGlobalTransform()
    {
        var board = GetParent()?.GetNodeOrNull<Node3D>("BoardHolder");
        if (board == null || CommunityCardsAnchor == null)
            return CommunityCardsAnchor?.GlobalTransform ?? Transform3D.Identity;

        var canonical = board.GlobalTransform.AffineInverse()
                        * CommunityCardsAnchor.GlobalTransform;
        return board.GlobalTransform * PokerTableLayout.ReaderAlignedFrame(
            canonical, PreviewReaderFacing(board), Vector2.Down);
    }

    private Vector2 PreviewReaderFacing(Node3D board)
    {
        if (board == null || Seats == null || PreviewSeat >= Seats.GetChildCount()
            || Seats.GetChild(PreviewSeat) is not Node3D seat)
        {
            return Vector2.Down;
        }

        var seatInBoard = board.ToLocal(seat.GlobalPosition);
        var facing = new Vector2(seatInBoard.X, seatInBoard.Z);
        return facing.LengthSquared() < 1e-6f ? Vector2.Down : facing.Normalized();
    }

    private void AddPreviewArc(
        Node3D owner, string name, Vector2 centre, Vector2 inward, Vector2 across,
        float radius, float start, float end, Material material)
    {
        var mesh = new ImmediateMesh();
        const int steps = 24;
        var halfWidth = InteractionGuideThickness * 0.5f;
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, material);
        for (var step = 0; step < steps; step++)
        {
            var angle0 = Mathf.Lerp(start, end, step / (float)steps);
            var angle1 = Mathf.Lerp(start, end, (step + 1) / (float)steps);
            var a = PreviewArcPoint(centre, inward, across, radius - halfWidth, angle0);
            var b = PreviewArcPoint(centre, inward, across, radius + halfWidth, angle0);
            var c = PreviewArcPoint(centre, inward, across, radius + halfWidth, angle1);
            var d = PreviewArcPoint(centre, inward, across, radius - halfWidth, angle1);
            PreviewTriangle(mesh, a, b, c);
            PreviewTriangle(mesh, a, c, d);
        }
        mesh.SurfaceEnd();
        var visual = new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            Position = Vector3.Up * 0.0033f,
        };
        owner.AddChild(visual);
    }

    private void AddPreviewLine(
        Node3D owner, string name, Vector2 from, Vector2 to, Material material)
    {
        var direction = to - from;
        var normal = new Vector2(-direction.Y, direction.X).Normalized()
                     * InteractionGuideThickness * 0.5f;
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, material);
        PreviewTriangle(mesh, from - normal, from + normal, to + normal);
        PreviewTriangle(mesh, from - normal, to + normal, to - normal);
        mesh.SurfaceEnd();
        var visual = new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            Position = Vector3.Up * 0.0033f,
        };
        owner.AddChild(visual);
    }

    private static Vector2 PreviewArcPoint(
        Vector2 centre, Vector2 inward, Vector2 across, float radius, float angle) =>
        centre + inward * (Mathf.Cos(angle) * radius) + across * (Mathf.Sin(angle) * radius);

    private static void PreviewTriangle(ImmediateMesh mesh, Vector2 a, Vector2 b, Vector2 c)
    {
        foreach (var point in new[] { a, b, c })
        {
            mesh.SurfaceSetNormal(Vector3.Up);
            mesh.SurfaceAddVertex(new Vector3(point.X, 0.0f, point.Y));
        }
    }

    private void AddPreviewLabel(
        Node3D owner, string text, Vector3 position, int size = 56)
    {
        var label = new Label3D
        {
            Name = text.Replace(" ", ""),
            Text = text,
            Font = PreviewChalkFont,
            PixelSize = ChalkGuidePixelSize,
            FontSize = size,
            OutlineSize = 2,
            Modulate = new Color(0.95f, 0.93f, 0.86f, 0.78f),
            Transform = new Transform3D(
                new Basis(Vector3.Up, Mathf.Pi)
                * new Basis(Vector3.Right, -Mathf.Pi * 0.5f), position),
        };
        owner.AddChild(label);
    }

    private void AddPreviewCurvedLabel(
        Node3D owner, string text, Vector2 centre, float radius, float spanAngle, int size)
    {
        var glyph = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var angle = PokerHand3DView.CurvedLabelAngle(index, text.Length, spanAngle);
            if (text[index] == ' ')
                continue;

            var point = PreviewArcPoint(centre, Vector2.Down, Vector2.Right, radius, angle);
            var label = new Label3D
            {
                Name = $"WagerGlyph{glyph}",
                Text = text[index].ToString(),
                Font = PreviewChalkFont,
                PixelSize = ChalkGuidePixelSize,
                FontSize = size,
                OutlineSize = 2,
                Modulate = new Color(0.95f, 0.93f, 0.86f, 0.78f),
                Transform = new Transform3D(
                    PokerHand3DView.CurvedLabelBasis(
                        Vector2.Down, Vector2.Right, angle),
                    new Vector3(point.X, 0.004f, point.Y)),
            };
            owner.AddChild(label);
            glyph++;
        }
    }

    private void ClearExperiencePreview()
    {
        if (IsInstanceValid(_generatedPreview))
            _generatedPreview.Free();
        _generatedPreview = null;

        while (GetNodeOrNull<Node3D>(GeneratedPreviewName) is { } stale)
        {
            RemoveChild(stale);
            stale.Free();
        }
    }
}
