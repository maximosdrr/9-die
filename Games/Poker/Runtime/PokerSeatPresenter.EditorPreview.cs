using System.Collections.Generic;
using Godot;
using Poker.Rules;

/// <summary>Editor-only stand-in for the physical props owned by PokerSeatPresenter.</summary>
public partial class PokerSeatPresenter : Node3D
{
    private const string PreviewRootName = "SeatPresenterPreview_EditorOnly";

    private sealed class EditorHeldCard
    {
        public MeshInstance3D Card;
        public Node3D Seat;
        public int Index;
    }

    private sealed class EditorChipStack
    {
        public Node3D Pile;
        public Label3D Label;
        public int SeatIndex;
        public Vector2 Facing;
        public float LabelHeight;
    }

    private sealed class EditorTurnRingSegment
    {
        public MeshInstance3D Mesh;
        public Node3D Seat;
        public StandardMaterial3D Material;
    }

    private readonly List<EditorHeldCard> _editorHeldCards = new();
    private readonly List<EditorChipStack> _editorChipStacks = new();
    private readonly List<EditorTurnRingSegment> _editorTurnRingSegments = new();
    private Node3D _editorTurnRing;
    private Transform3D _editorTurnRingFrame;

    private void QueueEditorPreviewRefresh()
    {
        if (Engine.IsEditorHint() && IsInsideTree())
            CallDeferred(MethodName.RebuildEditorPreview);
    }

    private void RebuildEditorPreview()
    {
        if (!Engine.IsEditorHint() || !IsInsideTree())
            return;

        ClearEditorPreview();
        if (!ShowEditorPreview || Seats == null)
            return;

        _editorPreview = new Node3D { Name = PreviewRootName };
        AddChild(_editorPreview, forceReadableName: false, InternalMode.Back);

        var spec = EditorPreviewSpec();
        var count = Mathf.Min(EditorPreviewOccupiedSeats, Seats.GetChildCount());
        for (var seatIndex = 0; seatIndex < count; seatIndex++)
        {
            if (Seats.GetChild(seatIndex) is not Node3D seat)
                continue;

            var localSeat = ToLocal(seat.GlobalPosition);
            var facing = new Vector2(localSeat.X, localSeat.Z);
            if (facing.LengthSquared() < 1e-6f)
                continue;

            facing = facing.Normalized();
            var seatPreview = new Node3D { Name = $"Seat{seatIndex}" };
            _editorPreview.AddChild(seatPreview);

            if (EditorPreviewHeldCards)
                BuildEditorHeldCards(seatPreview, seat, spec);
            else
                BuildEditorTableCards(seatPreview, facing, spec);

            if (EditorPreviewChips)
                BuildEditorChipStack(seatPreview, facing, spec, seatIndex);

            BuildEditorSeatLabel(seatPreview, facing, seatIndex);
        }

        if (EditorPreviewTurnRing)
            BuildEditorTurnRing();

        SetProcess(EditorPreviewHeldCards || EditorPreviewChips || EditorPreviewTurnRing);
        UpdateEditorPreviewHeldCards();
        UpdateEditorPreviewChipStacks();
        UpdateEditorPreviewTurnRing();
    }

    private PokerLayoutSpec EditorPreviewSpec()
    {
        if (BoardPresenterNode == null)
            return PokerLayoutSpec.Default;

        return new PokerLayoutSpec(
            PreviewFloat(BoardPresenterNode, "CardWidth", PokerLayoutSpec.Default.CardWidth),
            PreviewFloat(BoardPresenterNode, "CardLength", PokerLayoutSpec.Default.CardLength),
            PreviewFloat(BoardPresenterNode, "CardThickness", PokerLayoutSpec.Default.CardThickness),
            PreviewFloat(BoardPresenterNode, "CardGap", PokerLayoutSpec.Default.CardGap),
            PreviewFloat(BoardPresenterNode, "BoardOffset", PokerLayoutSpec.Default.BoardOffset),
            PreviewFloat(BoardPresenterNode, "PotRadius", PokerLayoutSpec.Default.PotRadius),
            PreviewFloat(BoardPresenterNode, "SeatCardRadius", PokerLayoutSpec.Default.SeatCardRadius),
            PreviewFloat(BoardPresenterNode, "SeatBetRadius", PokerLayoutSpec.Default.SeatBetRadius),
            PreviewFloat(BoardPresenterNode, "SeatStackRadius", PokerLayoutSpec.Default.SeatStackRadius));
    }

    private static float PreviewFloat(GodotObject source, StringName property, float fallback)
    {
        if (!IsInstanceValid(source))
            return fallback;

        var value = source.Get(property);
        return value.VariantType is Variant.Type.Float or Variant.Type.Int
            ? (float)value.AsDouble()
            : fallback;
    }

    private void BuildEditorHeldCards(
        Node3D owner, Node3D seat, PokerLayoutSpec spec)
    {
        var character = seat.FindChild("CharacterPreview_EditorOnly", recursive: false,
            owned: false) as Node3D;
        var grip = character?.FindChild("CardGrip", recursive: true, owned: false) as Node3D;

        // SeatAnchorMarker creates its character preview deferred. If it has not appeared yet, use
        // the same approximate held-card location as the runtime reveal animation.
        for (var index = 0; index < PokerDeal.HoleCardCount; index++)
        {
            var card = NewEditorPreviewCard(owner, spec);
            if (card == null)
                continue;

            _editorHeldCards.Add(new EditorHeldCard
            {
                Card = card,
                Seat = seat,
                Index = index,
            });

            if (grip != null)
            {
                card.GlobalTransform = grip.GlobalTransform * OpponentHeldCardTransform(index, spec);
                continue;
            }

            var localSeat = ToLocal(seat.GlobalPosition);
            var facing = new Vector2(localSeat.X, localSeat.Z).Normalized();
            card.Transform = EstimatedHeldCardTransform(facing, index, spec);
        }
    }

    private void UpdateEditorPreviewHeldCards()
    {
        if (!Engine.IsEditorHint() || !ShowEditorPreview || !EditorPreviewHeldCards)
            return;

        var spec = EditorPreviewSpec();
        foreach (var held in _editorHeldCards)
        {
            if (!IsInstanceValid(held.Card) || !IsInstanceValid(held.Seat))
                continue;

            var character = held.Seat.FindChild("CharacterPreview_EditorOnly", recursive: false,
                owned: false) as Node3D;
            var grip = character?.FindChild("CardGrip", recursive: true, owned: false) as Node3D;
            if (grip != null)
                held.Card.GlobalTransform = grip.GlobalTransform
                                            * OpponentHeldCardTransform(held.Index, spec);
        }
    }

    private void BuildEditorTableCards(Node3D owner, Vector2 facing, PokerLayoutSpec spec)
    {
        var turned = Basis.FromEuler(new Vector3(
            0.0f, PokerTableLayout.YawTowardCentre(facing), 0.0f));
        for (var index = 0; index < PokerDeal.HoleCardCount; index++)
        {
            var card = NewEditorPreviewCard(owner, spec);
            if (card == null)
                continue;

            card.Transform = DealtCardTransform(facing, index, spec, turned);
        }
    }

    private MeshInstance3D NewEditorPreviewCard(Node3D owner, PokerLayoutSpec spec)
    {
        // Deliberately simple editor geometry. Instantiating PokerCard here would require its entire
        // runtime asset pipeline to execute as a tool script, which is exactly the sort of editor /
        // runtime type crossover that previously produced invalid casts in this scene.
        var card = new MeshInstance3D
        {
            Name = "HoleCard",
            Mesh = new BoxMesh
            {
                Size = new Vector3(
                    spec.CardWidth,
                    Mathf.Max(spec.CardThickness, 0.0012f),
                    spec.CardLength),
            },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.31f, 0.035f, 0.055f),
                Roughness = 0.62f,
                MetallicSpecular = 0.28f,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        owner.AddChild(card);
        return card;
    }

    private void BuildEditorChipStack(
        Node3D owner, Vector2 facing, PokerLayoutSpec spec, int seatIndex)
    {
        var pile = new Node3D
        {
            Name = "ChipStack",
        };
        owner.AddChild(pile);

        var stackTransform = TryAuthoredStackTransform(seatIndex, out var authored)
            ? authored
            : DefaultStackTransform(facing, spec);
        pile.Transform = stackTransform;
        var spacing = Mathf.Max(BankColumnSpacing, 0.044f);
        var heights = seatIndex switch
        {
            0 => new[] { 4, 3, 3, 2 },
            1 => new[] { 3, 3, 2, 2 },
            2 => new[] { 3, 2, 2, 1 },
            _ => new[] { 2, 2, 1, 1 },
        };
        var colours = new[]
        {
            new Color(0.93f, 0.93f, 0.90f),
            new Color(0.13f, 0.52f, 0.24f),
            new Color(0.15f, 0.34f, 0.74f),
            new Color(0.74f, 0.14f, 0.19f),
        };
        const float thickness = 0.0035f;

        for (var column = 0; column < heights.Length; column++)
        {
            for (var level = 0; level < heights[column]; level++)
            {
                var chip = new MeshInstance3D
                {
                    Name = $"Chip{column}_{level}",
                    Mesh = new CylinderMesh
                    {
                        TopRadius = 0.020f,
                        BottomRadius = 0.020f,
                        Height = thickness,
                        RadialSegments = 20,
                    },
                    MaterialOverride = new StandardMaterial3D
                    {
                        AlbedoColor = colours[column],
                        Roughness = 0.38f,
                    },
                    Position = new Vector3(
                        (column - (heights.Length - 1) * 0.5f) * spacing,
                        (level + 0.5f) * thickness,
                        0.0f),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                pile.AddChild(chip);
            }
        }

        var value = new Label3D
        {
            Name = "StackValue",
            Text = $"FICHAS {PreviewStackAmount(seatIndex)}",
            Font = ChalkFont,
            PixelSize = ChalkValuePixelSize,
            FontSize = ChalkValueFontSize,
            OutlineSize = 8,
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            Shaded = false,
            Modulate = FloatingValueLabelColor,
            Position = new Vector3(
                stackTransform.Origin.X,
                stackTransform.Origin.Y + Mathf.Max(FloatingValueLabelMinimumHeight,
                    heights[0] * thickness + FloatingValueLabelClearance),
                stackTransform.Origin.Z),
        };
        owner.AddChild(value);

        _editorChipStacks.Add(new EditorChipStack
        {
            Pile = pile,
            Label = value,
            SeatIndex = seatIndex,
            Facing = facing,
            LabelHeight = Mathf.Max(FloatingValueLabelMinimumHeight,
                heights[0] * thickness + FloatingValueLabelClearance),
        });
    }

    /// <summary>Keeps the visible preview attached to markers while they are dragged or rotated.</summary>
    private void UpdateEditorPreviewChipStacks()
    {
        if (!Engine.IsEditorHint() || !ShowEditorPreview || !EditorPreviewChips)
            return;

        var spec = EditorPreviewSpec();
        foreach (var preview in _editorChipStacks)
        {
            if (!IsInstanceValid(preview.Pile) || !IsInstanceValid(preview.Label))
                continue;

            var stackTransform = TryAuthoredStackTransform(preview.SeatIndex, out var authored)
                ? authored
                : DefaultStackTransform(preview.Facing, spec);
            preview.Pile.Transform = stackTransform;
            preview.Label.Position = stackTransform.Origin + Vector3.Up * preview.LabelHeight;
        }
    }

    private void BuildEditorTurnRing()
    {
        if (Seats == null || !IsInstanceValid(_editorPreview))
            return;

        _editorTurnRingFrame = TurnRingFrame();
        _editorTurnRing = new Node3D
        {
            Name = "TurnRingPreview",
            Transform = _editorTurnRingFrame,
        };
        _editorPreview.AddChild(_editorTurnRing);

        var count = Mathf.Min(4, Seats.GetChildCount());
        for (var seatIndex = 0; seatIndex < count; seatIndex++)
        {
            if (Seats.GetChild(seatIndex) is not Node3D seat
                || !TryTurnRingDirection(seat, _editorTurnRingFrame, out var direction))
                continue;

            var colour = seatIndex == 0
                ? ActiveTurnRingColor
                : seatIndex < EditorPreviewOccupiedSeats
                    ? OccupiedTurnRingColor
                    : EmptyTurnRingColor;
            var material = BuildTurnRingMaterial();
            material.AlbedoColor = colour;
            material.Emission = colour;
            var segment = new MeshInstance3D
            {
                Name = $"TurnRingSeat{seatIndex}",
                Mesh = BuildTurnRingSegment(direction, material),
                Position = new Vector3(0.0f, TurnRingHeight, 0.0f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            _editorTurnRing.AddChild(segment);
            _editorTurnRingSegments.Add(new EditorTurnRingSegment
            {
                Mesh = segment,
                Seat = seat,
                Material = material,
            });
        }
    }

    /// <summary>Lets the preview follow the TurnRingAnchor while its gizmo is being dragged.</summary>
    private void UpdateEditorPreviewTurnRing()
    {
        if (!Engine.IsEditorHint() || !EditorPreviewTurnRing
            || !IsInstanceValid(_editorTurnRing))
            return;

        var frame = TurnRingFrame();
        if (frame.IsEqualApprox(_editorTurnRingFrame))
            return;

        _editorTurnRingFrame = frame;
        _editorTurnRing.Transform = frame;
        foreach (var preview in _editorTurnRingSegments)
        {
            if (!IsInstanceValid(preview.Mesh) || !IsInstanceValid(preview.Seat)
                || !TryTurnRingDirection(preview.Seat, frame, out var direction))
                continue;

            // Preserve the active/occupied/empty colour and only regenerate the inexpensive editor
            // geometry when the centre marker moves.
            preview.Mesh.Mesh = BuildTurnRingSegment(direction, preview.Material);
        }
    }

    private void BuildEditorSeatLabel(Node3D owner, Vector2 facing, int seatIndex)
    {
        var place = PokerTableLayout.SeatSpot(facing,
            EditorPreviewSpec().SeatStackRadius + 0.06f);
        var label = new Label3D
        {
            Name = "PlayerName",
            Text = $"Player {seatIndex + 1}",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            PixelSize = 0.00022f,
            FontSize = 64,
            OutlineSize = 10,
            Modulate = seatIndex == 0 ? TurnColor : IdleColor,
            Position = new Vector3(place.X, NameHeight, place.Y),
        };
        owner.AddChild(label);
    }

    private static int PreviewStackAmount(int seatIndex) => seatIndex switch
    {
        0 => 500,
        1 => 420,
        2 => 335,
        _ => 260,
    };

    private void ClearEditorPreview()
    {
        _editorHeldCards.Clear();
        _editorChipStacks.Clear();
        _editorTurnRingSegments.Clear();
        _editorTurnRing = null;
        _editorTurnRingFrame = Transform3D.Identity;
        if (Engine.IsEditorHint())
            SetProcess(false);

        if (IsInstanceValid(_editorPreview))
            _editorPreview.Free();
        _editorPreview = null;

        // Also handles a script reload, where the managed field is reset but the native preview
        // child from the previous tool instance is still present in the edited scene.
        while (GetNodeOrNull<Node3D>(PreviewRootName) is { } stale)
        {
            RemoveChild(stale);
            stale.Free();
        }
    }
}
