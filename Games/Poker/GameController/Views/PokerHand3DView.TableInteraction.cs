using System.Collections.Generic;
using Godot;
using Poker.Rules;

/// <summary>Crosshair-driven, table-space poker decisions.</summary>
public partial class PokerHand3DView : PokerHandView
{
    [ExportGroup("Table interaction")]
    [Export] public AimCrosshair Crosshair;

    /// <summary>The two inner chalk sectors, moved clear of the table rim.</summary>
    [Export] public float ActionZoneNearRadius = 0.455f;
    [Export] public float ActionZoneFarRadius = 0.515f;

    /// <summary>The wager button is the outer arc embracing PASSAR and DESISTIR.</summary>
    [Export] public float ConfirmZoneFarRadius = 0.575f;
    [Export(PropertyHint.Range, "10,28,0.5")] public float ActionZoneHalfAngleDegrees = 16.0f;
    [Export(PropertyHint.Range, "8,32,1")] public int InteractionArcSteps = 20;

    [Export] public float InteractionGuideThickness = 0.0014f;
    [Export] public Font ChalkFont;
    [Export] public Shader ChalkHoverShader;
    [Export(PropertyHint.Range, "48,96,2")] public int ChalkGuideFontSize = 72;
    [Export] public float ChalkGuidePixelSize = 0.00019f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float ChalkHoverOpacity = 0.24f;
    [Export] public Color ChalkGuideColor = new(0.95f, 0.93f, 0.86f, 0.78f);

    private enum InteractionZone
    {
        None,
        ConfirmBet,
        Fold,
        Check,
    }

    private Node3D _interactionGuide;
    private bool _interactionEnabled = true;
    private bool _wagerSubmitted;
    private int _interactionHand = -1;
    private Vector2 _guideFacing;
    private InteractionZone _hoveredZone;
    private readonly Dictionary<InteractionZone, ShaderMaterial> _zoneFillMaterials = new();
    private readonly Dictionary<InteractionZone, Label3D> _zoneLabels = new();

    public bool TryAimPoint(out Vector2 tableLocal) =>
        AimPlane.TryAim(Game?.Camera, Game?.BoardPresenter, out tableLocal);

    public void SetCrosshairVisible(bool visible)
    {
        if (Crosshair != null)
            Crosshair.Visible = visible;
    }

    /// <summary>
    /// Handles one left-button press. Hover already identifies the intended chalk region, so a
    /// second confirmation click would add friction without adding useful safety.
    /// </summary>
    public PokerGesture HandleTableClick()
    {
        if (!IsYourTurn || !_interactionEnabled)
            return PokerGesture.None;

        if (!HasPickedUpCards)
        {
            ShowNotice("Aguarde — você ainda está olhando suas cartas", 1.5f);
            return PokerGesture.None;
        }

        if (!TryAimPoint(out var aim))
            return PokerGesture.None;

        var presenter = Game?.SeatPresenter;
        var playerId = Player == null ? null : (string)Player.Name;
        if (presenter == null || string.IsNullOrEmpty(playerId))
            return PokerGesture.None;

        // Physical chips take precedence if one happens to cross the projected button silhouette.
        if (presenter.TryReturnPreparedChip(playerId, aim, out _))
        {
            RefreshPhysicalHud();
            return PokerGesture.None;
        }

        if (presenter.TrySelectPreparedChip(playerId, aim, out _))
        {
            RefreshPhysicalHud();
            return PokerGesture.None;
        }

        return ZoneAt(aim) switch
        {
            InteractionZone.Fold => HandleFold(),
            InteractionZone.Check => HandleCheck(),
            InteractionZone.ConfirmBet => HandleConfirmBet(presenter, playerId),
            _ => PokerGesture.None,
        };
    }

    public override void CancelPreparedWager(bool immediate = false)
    {
        _wagerSubmitted = false;
        Game?.SeatPresenter?.CancelPreparedWager(immediate);
        RefreshPhysicalHud();
    }

    private void RefreshPhysicalHud() =>
        Hud?.Refresh(_options, IsYourTurn, RaiseTotal, HasPickedUpCards);

    private PokerGesture HandleFold()
    {
        if (!HasAction(PokerActionKind.Fold))
        {
            ShowNotice("Desistir não está disponível quando você pode passar", 1.6f);
            return PokerGesture.None;
        }

        CancelPreparedWager();
        RequestAction(PokerActionKind.Fold, TotalFor(PokerActionKind.Fold));
        return PokerGesture.Fold;
    }

    private PokerGesture HandleCheck()
    {
        if (Game?.SeatPresenter?.PreparedWagerAmount > 0)
        {
            ShowNotice("Devolva as fichas selecionadas antes de passar", 1.8f);
            return PokerGesture.None;
        }

        if (!HasAction(PokerActionKind.Check))
        {
            var playerId = Player == null ? null : (string)Player.Name;
            var call = playerId == null ? 0 : Game.AmountToCall(playerId);
            ShowNotice(call > 0
                ? $"Você precisa pagar {call} para continuar"
                : "Passar não está disponível agora", 1.8f);
            return PokerGesture.None;
        }

        RequestAction(PokerActionKind.Check, TotalFor(PokerActionKind.Check));
        return PokerGesture.Knock;
    }

    private PokerGesture HandleConfirmBet(PokerSeatPresenter presenter, string playerId)
    {
        var selected = presenter.PreparedWagerAmount;
        var committed = Game.BetOf(playerId);
        if (!PokerWagerInteraction.TryResolve(
                _options, committed, selected,
                out var kind, out var total, out var problem, out var required))
        {
            ShowWagerProblem(problem, required);
            return PokerGesture.None;
        }

        if (!presenter.SubmitPreparedWager(playerId, selected))
        {
            ShowNotice("As fichas ainda não estão prontas para serem apostadas", 1.5f);
            return PokerGesture.None;
        }

        _wagerSubmitted = true;
        RequestAction(kind, total);
        return PokerGesture.ThrowChips;
    }

    private void ShowWagerProblem(PokerWagerProblem problem, int required)
    {
        var text = problem switch
        {
            PokerWagerProblem.NoChipsSelected =>
                HasAction(PokerActionKind.Check)
                    ? "Use PASSAR para encerrar sem apostar"
                    : "Selecione fichas antes de usar CONFIRMAR APOSTA",
            PokerWagerProblem.BelowMinimum =>
                $"A aposta mínima exige {Mathf.Max(0, required)} fichas",
            PokerWagerProblem.AboveMaximum =>
                $"Você só pode colocar até {Mathf.Max(0, required)} fichas",
            _ => "Essa aposta não está disponível agora",
        };
        ShowNotice(text, 2.0f);
    }

    private InteractionZone ZoneAt(Vector2 aim)
    {
        if (!TrySeatAxes(out var facing, out var across))
            return InteractionZone.None;

        var radial = aim.Dot(facing);
        var lateral = aim.Dot(across);
        var radius = Mathf.Sqrt(radial * radial + lateral * lateral);
        var angle = Mathf.Atan2(lateral, radial);
        var halfAngle = Mathf.DegToRad(ActionZoneHalfAngleDegrees);
        if (Mathf.Abs(angle) > halfAngle)
            return InteractionZone.None;

        if (radius >= ActionZoneFarRadius && radius <= ConfirmZoneFarRadius)
            return InteractionZone.ConfirmBet;

        if (radius < ActionZoneNearRadius || radius > ActionZoneFarRadius)
            return InteractionZone.None;

        // +across is always the seated player's left, independently of which chair they occupy.
        return angle >= 0.0f ? InteractionZone.Check : InteractionZone.Fold;
    }

    private bool TrySeatAxes(out Vector2 facing, out Vector2 across)
    {
        facing = Vector2.Zero;
        across = Vector2.Zero;
        if (Game?.BoardPresenter == null || Player == null)
            return false;

        if (Game.SeatFor((string)Player.Name) == null)
            return false;

        facing = Game.BoardPresenter.ReaderFacing;
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
            Game?.SeatPresenter?.CancelPreparedWager(immediate: true);
        }

        var presenter = Game?.SeatPresenter;
        if (_wagerSubmitted && presenter is { PreparedWagerSubmitted: false, PreparedWagerAmount: 0 })
            _wagerSubmitted = false;
        else if (!isYourTurn && !_wagerSubmitted && presenter?.PreparedWagerAmount > 0)
            presenter.CancelPreparedWager();

        UpdateInteractionVisibility();
    }

    private void UpdateInteractionVisibility()
    {
        var visible = _interactionEnabled && IsYourTurn && HasPickedUpCards;
        SetCrosshairVisible(visible);
        SetGuideVisible(visible);
        if (visible)
            RefreshInteractionHover();
    }

    private void SetGuideVisible(bool visible)
    {
        if (!visible)
        {
            SetHoveredZone(InteractionZone.None);
            _interactionGuide?.Hide();
            return;
        }

        EnsureInteractionGuide();
        _interactionGuide?.Show();
    }

    private void RefreshInteractionHover()
    {
        var zone = TryAimPoint(out var aim) ? ZoneAt(aim) : InteractionZone.None;
        SetHoveredZone(zone);
    }

    private void SetHoveredZone(InteractionZone zone)
    {
        if (_hoveredZone == zone)
            return;

        _hoveredZone = zone;
        foreach (var entry in _zoneFillMaterials)
        {
            var strength = entry.Key == zone ? ChalkHoverOpacity : 0.0f;
            entry.Value.SetShaderParameter("chalk_strength", strength);
        }

        foreach (var entry in _zoneLabels)
        {
            var alpha = entry.Key == zone ? 1.0f : ChalkGuideColor.A;
            entry.Value.Modulate = ChalkGuideColor with { A = alpha };
        }
    }

    private void EnsureInteractionGuide()
    {
        if (Game?.BoardPresenter == null || !TrySeatAxes(out var facing, out var across))
            return;

        if (IsInstanceValid(_interactionGuide)
            && _guideFacing.DistanceSquaredTo(facing) < 1e-8f)
            return;

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

        _interactionGuide.GlobalTransform = Game.BoardPresenter.GlobalTransform;
        _guideFacing = facing;
        _hoveredZone = InteractionZone.None;
        _zoneFillMaterials.Clear();
        _zoneLabels.Clear();

        var yaw = PokerTableLayout.YawTowardCentre(facing);
        var labelBasis = Basis.FromEuler(new Vector3(0.0f, yaw, 0.0f));
        var halfAngle = Mathf.DegToRad(ActionZoneHalfAngleDegrees);

        AddChalkZone(InteractionZone.Check, "CheckFill", facing, across,
            ActionZoneNearRadius, ActionZoneFarRadius, 0.0f, halfAngle);
        AddChalkZone(InteractionZone.Fold, "FoldFill", facing, across,
            ActionZoneNearRadius, ActionZoneFarRadius, -halfAngle, 0.0f);
        AddChalkZone(InteractionZone.ConfirmBet, "ConfirmFill", facing, across,
            ActionZoneFarRadius, ConfirmZoneFarRadius, -halfAngle, halfAngle);

        var lineMaterial = NewChalkMaterial(ChalkGuideColor with { A = 0.46f }, 1.0f);
        AddArcLine("InnerArc", facing, across, ActionZoneNearRadius,
            -halfAngle, halfAngle, lineMaterial);
        AddArcLine("ActionArc", facing, across, ActionZoneFarRadius,
            -halfAngle, halfAngle, lineMaterial);
        AddArcLine("OuterArc", facing, across, ConfirmZoneFarRadius,
            -halfAngle, halfAngle, lineMaterial);
        AddRadialLine("LeftEdge", facing, across, halfAngle,
            ActionZoneNearRadius, ConfirmZoneFarRadius, lineMaterial);
        AddRadialLine("RightEdge", facing, across, -halfAngle,
            ActionZoneNearRadius, ConfirmZoneFarRadius, lineMaterial);
        AddRadialLine("CentreDivider", facing, across, 0.0f,
            ActionZoneNearRadius, ActionZoneFarRadius, lineMaterial);

        var actionLabelRadius = (ActionZoneNearRadius + ActionZoneFarRadius) * 0.5f;
        AddChalkLabel(InteractionZone.Check, "Check", "PASSAR",
            PolarPoint(facing, across, actionLabelRadius, halfAngle * 0.5f), labelBasis,
            ChalkGuideFontSize);
        AddChalkLabel(InteractionZone.Fold, "Fold", "DESISTIR",
            PolarPoint(facing, across, actionLabelRadius, -halfAngle * 0.5f), labelBasis,
            ChalkGuideFontSize);
        AddChalkLabel(InteractionZone.ConfirmBet, "ConfirmBet", "CONFIRMAR\nAPOSTA",
            PolarPoint(facing, across,
                (ActionZoneFarRadius + ConfirmZoneFarRadius) * 0.5f, 0.0f), labelBasis,
            Mathf.RoundToInt(ChalkGuideFontSize * 0.62f));
    }

    private void AddChalkZone(
        InteractionZone zone, string name, Vector2 facing, Vector2 across,
        float innerRadius, float outerRadius, float startAngle, float endAngle)
    {
        var material = NewChalkMaterial(ChalkGuideColor, 0.0f);
        _zoneFillMaterials[zone] = material;
        AddGuideMesh(name, BuildSectorMesh(facing, across,
            innerRadius, outerRadius, startAngle, endAngle, InteractionArcSteps, material), 0.0031f);
    }

    private void AddArcLine(
        string name, Vector2 facing, Vector2 across, float radius,
        float startAngle, float endAngle, Material material)
    {
        var half = InteractionGuideThickness * 0.5f;
        AddGuideMesh(name, BuildSectorMesh(facing, across,
            radius - half, radius + half, startAngle, endAngle,
            InteractionArcSteps, material), 0.0033f);
    }

    private void AddRadialLine(
        string name, Vector2 facing, Vector2 across, float angle,
        float innerRadius, float outerRadius, Material material)
    {
        var middle = Mathf.Max((innerRadius + outerRadius) * 0.5f, 0.01f);
        var halfAngle = InteractionGuideThickness / (middle * 2.0f);
        AddGuideMesh(name, BuildSectorMesh(facing, across,
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
        _zoneLabels[zone] = label;
    }

    private static ImmediateMesh BuildSectorMesh(
        Vector2 facing, Vector2 across, float innerRadius, float outerRadius,
        float startAngle, float endAngle, int steps, Material material)
    {
        steps = Mathf.Max(1, steps);
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, material);
        for (var step = 0; step < steps; step++)
        {
            var angle0 = Mathf.Lerp(startAngle, endAngle, step / (float)steps);
            var angle1 = Mathf.Lerp(startAngle, endAngle, (step + 1) / (float)steps);
            var inner0 = PolarPoint(facing, across, innerRadius, angle0);
            var outer0 = PolarPoint(facing, across, outerRadius, angle0);
            var inner1 = PolarPoint(facing, across, innerRadius, angle1);
            var outer1 = PolarPoint(facing, across, outerRadius, angle1);
            AddInteractionTriangle(mesh, inner0, outer0, outer1);
            AddInteractionTriangle(mesh, inner0, outer1, inner1);
        }
        mesh.SurfaceEnd();
        return mesh;
    }

    private static Vector2 PolarPoint(
        Vector2 facing, Vector2 across, float radius, float angle) =>
        facing * (Mathf.Cos(angle) * radius) + across * (Mathf.Sin(angle) * radius);

    private static void AddInteractionTriangle(
        ImmediateMesh mesh, Vector2 a, Vector2 b, Vector2 c)
    {
        AddInteractionVertex(mesh, a);
        AddInteractionVertex(mesh, b);
        AddInteractionVertex(mesh, c);
    }

    private static void AddInteractionVertex(ImmediateMesh mesh, Vector2 point)
    {
        mesh.SurfaceSetNormal(Vector3.Up);
        mesh.SurfaceSetUV(point * 8.0f);
        mesh.SurfaceAddVertex(new Vector3(point.X, 0.0f, point.Y));
    }
}
