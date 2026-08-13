using System.Collections.Generic;
using System.Linq;
using Godot;
using Poker.Rules;

/// <summary>
/// Classifies the crosshair target and renders the contextual table guide for the active decision.
/// </summary>
public partial class PokerHand3DView
{
    private InteractionZone ZoneAt(Vector2 aim)
    {
        // AimPlane already returned coordinates in the guide's own visual frame.
        var facing = Vector2.Down;
        var across = Vector2.Right;

        // One U-shaped control owns every wager. Its meaning comes from the physical state: CALL or
        // AUTO with no staged chips, APOSTAR after a custom selection, and ALL-IN while held.
        var confirmCentre = facing * ConfirmZoneCenterRadius;
        var confirmOffset = aim - confirmCentre;
        var confirmOutward = confirmOffset.Dot(facing);
        var confirmLateral = confirmOffset.Dot(across);
        var confirmRadius = Mathf.Sqrt(
            confirmOutward * confirmOutward + confirmLateral * confirmLateral);
        if (confirmOutward >= 0.0f
            && confirmRadius >= ConfirmZoneInnerRadius
            && confirmRadius <= ConfirmZoneOuterRadius)
            return InteractionZone.ConfirmBet;

        var centre = facing * InteractionZoneCenterRadius;
        var offset = aim - centre;
        var inward = -offset.Dot(facing);
        var lateral = offset.Dot(across);
        if (inward < 0.0f)
            return InteractionZone.None;

        var radius = Mathf.Sqrt(inward * inward + lateral * lateral);
        if (radius > ActionZoneRadius)
            return InteractionZone.None;

        // +across is always the seated player's left, independently of which chair they occupy.
        return lateral >= 0.0f ? InteractionZone.Check : InteractionZone.Fold;
    }

    private bool TrySeatAxes(out Vector2 facing, out Vector2 across)
    {
        facing = Vector2.Zero;
        across = Vector2.Zero;
        if (Game?.BoardPresenter == null || Player == null)
            return false;

        var playerId = (string)Player.Name;
        if (Game.SeatFor(playerId) == null)
            return false;

        // Use the player that owns this camera, not a replicated PokerGame.Player reference that
        // can still be null/stale on a client while its controller is already active.
        Game.BoardPresenter.SetReaderPlayer(playerId);
        facing = Game.BoardPresenter.ReaderFacingFor(playerId);
        if (facing.LengthSquared() < 1e-6f)
            return false;

        facing = facing.Normalized();
        across = new Vector2(-facing.Y, facing.X);
        return true;
    }

    private void SyncTableInteraction(int hand, bool isYourTurn)
    {
        if (hand != _interactionHand)
        {
            _interactionHand = hand;
            _wagerSubmitted = false;
            ResetAutomaticWager();
            ResetCallHold();
            _queuedTableGesture = PokerGesture.None;
            Game?.SeatPresenter?.CancelPreparedWager(immediate: true);
        }

        var presenter = Game?.SeatPresenter;
        if (_wagerSubmitted && presenter is { PreparedWagerSubmitted: false, PreparedWagerAmount: 0 })
            _wagerSubmitted = false;
        else if (!isYourTurn && !_wagerSubmitted
                 && presenter is { PreparedWagerSubmitted: false, PreparedWagerAmount: > 0 })
            presenter.CancelPreparedWager();

        UpdateInteractionVisibility();
    }

    private void UpdateInteractionVisibility()
    {
        // Show the controls as soon as it is this player's turn. Input remains locked until the
        // opening card-look finishes, but the player can already see where the available actions are.
        var visible = _interactionEnabled && IsYourTurn;
        SetCrosshairVisible(visible);
        SetGuideVisible(visible);
        if (visible)
        {
            RefreshInteractionHover();
            UpdateCallHoldVisual();
        }
    }

    private void SetGuideVisible(bool visible)
    {
        if (!visible)
        {
            ResetCallHold();
            SetHoveredZone(InteractionZone.None);
            _interactionGuide?.Hide();
            return;
        }

        EnsureInteractionGuide();
        _interactionGuide?.Show();
    }

    private void RefreshInteractionHover()
    {
        if (_callHoldActive)
        {
            SetHoveredZone(InteractionZone.ConfirmBet);
            return;
        }

        var zone = TryActionGuideAimPoint(out var aim) ? ZoneAt(aim) : InteractionZone.None;
        SetHoveredZone(zone);
    }

    private void SetHoveredZone(InteractionZone zone)
    {
        if (_hoveredZone == zone)
            return;

        _callHoverElapsed = 0.0f;
        _hoveredZone = zone;
        foreach (var entry in _zoneFillMaterials)
        {
            var strength = entry.Key == zone
                ? entry.Key == InteractionZone.ConfirmBet ? CallHoverOpacity : ChalkHoverOpacity
                : 0.0f;
            entry.Value.SetShaderParameter("chalk_strength", strength);
        }

        foreach (var entry in _zoneLabels)
        {
            var alpha = entry.Key == zone ? 1.0f : ChalkGuideColor.A;
            foreach (var label in entry.Value)
                label.Modulate = ChalkGuideColor with { A = alpha };
        }

        UpdateCallHoldVisual();
    }

    private void UpdateCallHoldVisual()
    {
        var progress = _callHoldActive
            ? AllInHoldVisualProgress(
                _callHoldElapsed,
                Mathf.Max(AllInVisualDelaySeconds, CallClickMaxSeconds),
                AllInHoldSeconds)
            : 0.0f;
        var showHoldIntent = _callHoldActive && progress > 0.0f;

        if (_zoneFillMaterials.TryGetValue(InteractionZone.ConfirmBet, out var material))
        {
            material.SetShaderParameter("use_fill_progress", showHoldIntent);
            material.SetShaderParameter("fill_progress", progress);
            material.SetShaderParameter("chalk_color",
                showHoldIntent ? AllInHoldColor : ChalkGuideColor);
            material.SetShaderParameter("chalk_strength", showHoldIntent
                ? Mathf.Lerp(0.18f, AllInHoldOpacity, PokerMotion.Smooth(progress))
                : _hoveredZone == InteractionZone.ConfirmBet ? CallHoverOpacity : 0.0f);
        }

        var automaticLabel = CurrentWagerButtonLabel();
        var text = showHoldIntent
            ? "SEGURE ALL-IN"
            : !_callHoldActive && _hoveredZone == InteractionZone.ConfirmBet
                ? CallLabelForHover(
                    automaticLabel, _callHoverElapsed, CallLabelCycleSeconds)
                : automaticLabel;
        var hoverOpacity = _hoveredZone == InteractionZone.ConfirmBet
            ? CallLabelOpacityForHover(
                _callHoverElapsed, CallLabelCycleSeconds, CallLabelFadeSeconds)
            : ChalkGuideColor.A;
        var colour = showHoldIntent
            ? AllInHoldColor with { A = 1.0f }
            : ChalkGuideColor with { A = hoverOpacity };
        UpdateUnifiedWagerLabel(text, colour);
    }

    private string CurrentWagerButtonLabel()
    {
        if (_automaticWagerPending)
            return _automaticWagerKind == PokerActionKind.Raise ? "AUTO" : "CALL";

        return WagerButtonLabel(
            _options, (Game?.SeatPresenter?.PreparedWagerAmount ?? 0) > 0);
    }

    private void EnsureInteractionGuide()
    {
        if (Game?.BoardPresenter == null || !TrySeatAxes(out var facing, out var across))
            return;

        var authored = Game.ExperienceAuthoring?.ActionGuideTransformFor(
            Game.BoardPresenter, facing) ?? Transform3D.Identity;
        var desiredGlobalTransform = Game.BoardPresenter.GlobalTransform * authored;

        if (IsInstanceValid(_interactionGuide)
            && _guideFacing.DistanceSquaredTo(facing) < 1e-8f)
        {
            // Markers and table dimensions are editor-owned. Keep the interaction plane attached
            // even when its geometry does not need rebuilding.
            _interactionGuide.GlobalTransform = desiredGlobalTransform;
            return;
        }

        if (!IsInstanceValid(_interactionGuide))
        {
            _interactionGuide = new Node3D { Name = "LocalPokerInteractionGuide", TopLevel = true };
            AddChild(_interactionGuide);
        }
        else
        {
            foreach (var child in _interactionGuide.GetChildren())
            {
                _interactionGuide.RemoveChild(child);
                child.QueueFree();
            }
        }

        _interactionGuide.GlobalTransform = desiredGlobalTransform;
        _guideFacing = facing;
        _hoveredZone = InteractionZone.None;
        _zoneFillMaterials.Clear();
        _zoneLabels.Clear();

        // Geometry is authored once from Seat0; ActionGuideTransformFor rotates the entire guide
        // frame to the local player's chair.
        facing = Vector2.Down;
        across = Vector2.Right;
        var yaw = PokerTableLayout.YawTowardCentre(facing);
        var labelBasis = Basis.FromEuler(new Vector3(0.0f, yaw, 0.0f));
        var actionCentre = facing * InteractionZoneCenterRadius;
        var inward = -facing;
        var confirmCentre = facing * ConfirmZoneCenterRadius;
        var confirmOutward = facing;
        const float halfCircle = Mathf.Pi * 0.5f;

        AddChalkZone(InteractionZone.Check, "CheckFill", actionCentre, inward, across,
            0.0f, ActionZoneRadius, 0.0f, halfCircle);
        AddChalkZone(InteractionZone.Fold, "FoldFill", actionCentre, inward, across,
            0.0f, ActionZoneRadius, -halfCircle, 0.0f);
        AddChalkZone(InteractionZone.ConfirmBet, "ConfirmFill", confirmCentre, confirmOutward, across,
            ConfirmZoneInnerRadius, ConfirmZoneOuterRadius, -halfCircle, halfCircle);

        var lineMaterial = NewChalkMaterial(ChalkGuideColor with { A = 0.46f }, 1.0f);
        AddArcLine("ActionOuterArc", actionCentre, inward, across, ActionZoneRadius,
            -halfCircle, halfCircle, lineMaterial);
        AddRadialLine("CentreDivider", actionCentre, inward, across, 0.0f,
            0.0f, ActionZoneRadius, lineMaterial);

        AddArcLine("ConfirmOuterArc", confirmCentre, confirmOutward, across,
            ConfirmZoneOuterRadius,
            -halfCircle, halfCircle, lineMaterial);
        AddArcLine("ConfirmInnerArc", confirmCentre, confirmOutward, across,
            ConfirmZoneInnerRadius,
            -halfCircle, halfCircle, lineMaterial);
        AddRadialLine("ConfirmLeftEdge", confirmCentre, confirmOutward, across, halfCircle,
            ConfirmZoneInnerRadius, ConfirmZoneOuterRadius, lineMaterial);
        AddRadialLine("ConfirmRightEdge", confirmCentre, confirmOutward, across, -halfCircle,
            ConfirmZoneInnerRadius, ConfirmZoneOuterRadius, lineMaterial);
        var actionLabelRadius = ActionZoneRadius * 0.56f;
        AddChalkLabel(InteractionZone.Check, "Check", "PASSAR",
            SemicirclePoint(actionCentre, inward, across, actionLabelRadius, Mathf.Pi * 0.25f), labelBasis,
            ChalkGuideFontSize);
        AddChalkLabel(InteractionZone.Fold, "Fold", "DESISTIR",
            SemicirclePoint(actionCentre, inward, across, actionLabelRadius, -Mathf.Pi * 0.25f), labelBasis,
            ChalkGuideFontSize);
        SetCurvedChalkLabel(InteractionZone.ConfirmBet, CurrentWagerButtonLabel(),
            confirmCentre, confirmOutward, across,
            (ConfirmZoneInnerRadius + ConfirmZoneOuterRadius) * 0.5f,
            Mathf.Pi * ConfirmLabelSpanPi, Mathf.RoundToInt(ChalkGuideFontSize * 0.82f));
        UpdateCallHoldVisual();
    }

    private void AddChalkZone(
        InteractionZone zone, string name, Vector2 centre, Vector2 inward, Vector2 across,
        float innerRadius, float outerRadius, float startAngle, float endAngle)
    {
        var material = NewChalkMaterial(ChalkGuideColor, 0.0f);
        // Sector UVs are table-space; only the unified wager sector enables the progress mask.
        material.SetShaderParameter("use_fill_progress", false);
        material.SetShaderParameter("fill_progress", 1.0f);
        _zoneFillMaterials[zone] = material;
        AddGuideMesh(name, BuildSectorMesh(centre, inward, across,
            innerRadius, outerRadius, startAngle, endAngle, InteractionArcSteps, material), 0.0031f);
    }

    private void AddArcLine(
        string name, Vector2 centre, Vector2 inward, Vector2 across, float radius,
        float startAngle, float endAngle, Material material)
    {
        var half = InteractionGuideThickness * 0.5f;
        AddGuideMesh(name, BuildSectorMesh(centre, inward, across,
            radius - half, radius + half, startAngle, endAngle,
            InteractionArcSteps, material), 0.0033f);
    }

    private void AddRadialLine(
        string name, Vector2 centre, Vector2 inward, Vector2 across, float angle,
        float innerRadius, float outerRadius, Material material)
    {
        var middle = Mathf.Max((innerRadius + outerRadius) * 0.5f, 0.01f);
        var halfAngle = InteractionGuideThickness / (middle * 2.0f);
        AddGuideMesh(name, BuildSectorMesh(centre, inward, across,
            innerRadius, outerRadius, angle - halfAngle, angle + halfAngle,
            1, material), 0.0033f);
    }

    private void AddGuideMesh(string name, Mesh mesh, float height)
    {
        var instance = new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            Position = Vector3.Up * height,
        };
        _interactionGuide.AddChild(instance);
    }

    private ShaderMaterial NewChalkMaterial(Color colour, float strength)
    {
        var material = new ShaderMaterial { Shader = ChalkHoverShader };
        material.SetShaderParameter("chalk_color", colour);
        material.SetShaderParameter("chalk_strength", strength);
        return material;
    }

    private void AddChalkLabel(
        InteractionZone zone, string name, string text, Vector2 position,
        Basis basis, int fontSize)
    {
        var label = new Label3D
        {
            Name = name,
            Text = text,
            Font = ChalkFont,
            PixelSize = ChalkGuidePixelSize,
            FontSize = fontSize,
            OutlineSize = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = ChalkGuideColor,
            OutlineModulate = new Color(0.23f, 0.12f, 0.07f, 0.20f),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            NoDepthTest = false,
            Transform = new Transform3D(
                basis * new Basis(Vector3.Right, -Mathf.Pi * 0.5f),
                new Vector3(position.X, 0.004f, position.Y)),
        };
        _interactionGuide.AddChild(label);
        AddZoneLabel(zone, label);
    }

    private void UpdateUnifiedWagerLabel(string text, Color colour)
    {
        if (!IsInstanceValid(_interactionGuide))
            return;

        var facing = Vector2.Down;
        var across = Vector2.Right;

        var lengthScale = Mathf.Clamp(text.Length / (float)ConfirmBetLabelText.Length, 0.55f, 1.85f);
        SetCurvedChalkLabel(InteractionZone.ConfirmBet, text,
            facing * ConfirmZoneCenterRadius, facing, across,
            (ConfirmZoneInnerRadius + ConfirmZoneOuterRadius) * 0.5f,
            Mathf.Pi * ConfirmLabelSpanPi * lengthScale,
            Mathf.RoundToInt(ChalkGuideFontSize * 0.82f));

        if (!_zoneLabels.TryGetValue(InteractionZone.ConfirmBet, out var labels))
            return;

        foreach (var label in labels)
        {
            if (label.Visible)
                label.Modulate = colour;
        }
    }

    /// <summary>
    /// Reflows reusable glyph nodes around the unified arc. CALL, AUTO, APOSTAR and SEGURE ALL-IN
    /// have different lengths, so replacing the text of one Label3D would lose the curved layout.
    /// </summary>
    private void SetCurvedChalkLabel(
        InteractionZone zone, string text, Vector2 centre, Vector2 inward, Vector2 across,
        float radius, float spanAngle, int fontSize)
    {
        if (string.IsNullOrEmpty(text))
            return;

        if (!_zoneLabels.TryGetValue(zone, out var labels))
        {
            labels = new List<Label3D>();
            _zoneLabels[zone] = labels;
        }

        var glyph = 0;
        for (var index = 0; index < text.Length; index++)
        {
            // The first glyph starts on the reader's left. The complete guide transform then
            // carries this canonical Seat0 layout to the current player's chair.
            var angle = CurvedLabelAngle(index, text.Length, spanAngle);
            if (text[index] == ' ')
                continue;

            var point = SemicirclePoint(centre, inward, across, radius, angle);
            var transform = new Transform3D(
                CurvedLabelBasis(inward, across, angle),
                new Vector3(point.X, 0.004f, point.Y));
            Label3D label;
            if (glyph >= labels.Count)
            {
                label = NewChalkLabel($"WagerGlyph{glyph}", text[index].ToString(),
                    fontSize, transform);
                _interactionGuide.AddChild(label);
                labels.Add(label);
            }
            else
            {
                label = labels[glyph];
                label.Text = text[index].ToString();
                label.FontSize = fontSize;
                label.Transform = transform;
            }

            label.Visible = true;
            glyph++;
        }

        for (var index = glyph; index < labels.Count; index++)
            labels[index].Visible = false;
    }

    private Label3D NewChalkLabel(
        string name, string text, int fontSize, Transform3D transform) => new()
        {
            Name = name,
            Text = text,
            Font = ChalkFont,
            PixelSize = ChalkGuidePixelSize,
            FontSize = fontSize,
            OutlineSize = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = ChalkGuideColor,
            OutlineModulate = new Color(0.23f, 0.12f, 0.07f, 0.20f),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            NoDepthTest = false,
            Transform = transform,
        };

    /// <summary>
    /// Generates the curved word in the same left-to-right order seen by the seated reader. Kept
    /// public for a scene check because reversing either endpoint silently spells AUTO as OTUA.
    /// </summary>
    public static float CurvedLabelAngle(int index, int textLength, float spanAngle) =>
        textLength <= 1
            ? 0.0f
            : Mathf.Lerp(-spanAngle, spanAngle, index / (float)(textLength - 1));

    /// <summary>
    /// Gives every curved glyph the same readable table orientation as the other action labels.
    /// The action-guide root supplies the per-seat rotation, so this basis remains canonical.
    /// </summary>
    public static Basis CurvedLabelBasis(Vector2 inward, Vector2 across, float angle)
    {
        var outward = inward * Mathf.Cos(angle) + across * Mathf.Sin(angle);
        var tangent = across * Mathf.Cos(angle) - inward * Mathf.Sin(angle);
        var right3 = new Vector3(tangent.X, 0.0f, tangent.Y).Normalized();
        var up3 = new Vector3(-outward.X, 0.0f, -outward.Y).Normalized();
        var normal3 = right3.Cross(up3).Normalized();
        return new Basis(right3, up3, normal3);
    }

    private void AddZoneLabel(InteractionZone zone, Label3D label)
    {
        if (!_zoneLabels.TryGetValue(zone, out var labels))
        {
            labels = new List<Label3D>();
            _zoneLabels[zone] = labels;
        }
        labels.Add(label);
    }

    private static ImmediateMesh BuildSectorMesh(
        Vector2 centre, Vector2 inward, Vector2 across, float innerRadius, float outerRadius,
        float startAngle, float endAngle, int steps, Material material)
    {
        steps = Mathf.Max(1, steps);
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, material);
        for (var step = 0; step < steps; step++)
        {
            var angle0 = Mathf.Lerp(startAngle, endAngle, step / (float)steps);
            var angle1 = Mathf.Lerp(startAngle, endAngle, (step + 1) / (float)steps);
            var inner0 = SemicirclePoint(centre, inward, across, innerRadius, angle0);
            var outer0 = SemicirclePoint(centre, inward, across, outerRadius, angle0);
            var inner1 = SemicirclePoint(centre, inward, across, innerRadius, angle1);
            var outer1 = SemicirclePoint(centre, inward, across, outerRadius, angle1);
            var innerUv0 = SectorProgressUv(step, steps, outer: false);
            var outerUv0 = SectorProgressUv(step, steps, outer: true);
            var innerUv1 = SectorProgressUv(step + 1, steps, outer: false);
            var outerUv1 = SectorProgressUv(step + 1, steps, outer: true);
            AddInteractionTriangle(mesh, inner0, outer0, outer1,
                innerUv0, outerUv0, outerUv1);
            AddInteractionTriangle(mesh, inner0, outer1, inner1,
                innerUv0, outerUv1, innerUv1);
        }
        mesh.SurfaceEnd();
        return mesh;
    }

    private static Vector2 SemicirclePoint(
        Vector2 centre, Vector2 inward, Vector2 across, float radius, float angle) =>
        centre + inward * (Mathf.Cos(angle) * radius) + across * (Mathf.Sin(angle) * radius);

    private static void AddInteractionTriangle(
        ImmediateMesh mesh, Vector2 a, Vector2 b, Vector2 c,
        Vector2 uvA, Vector2 uvB, Vector2 uvC)
    {
        AddInteractionVertex(mesh, a, uvA);
        AddInteractionVertex(mesh, b, uvB);
        AddInteractionVertex(mesh, c, uvC);
    }

    private static void AddInteractionVertex(ImmediateMesh mesh, Vector2 point, Vector2 uv)
    {
        mesh.SurfaceSetNormal(Vector3.Up);
        mesh.SurfaceSetUV(uv);
        mesh.SurfaceAddVertex(new Vector3(point.X, 0.0f, point.Y));
    }
}
