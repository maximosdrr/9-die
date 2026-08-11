using System.Collections.Generic;
using System.Linq;
using Godot;
using Poker.Rules;

/// <summary>Crosshair-driven, table-space poker decisions.</summary>
public partial class PokerHand3DView : PokerHandView
{
    public const string ConfirmBetLabelText = "APOSTAR";

    [ExportGroup("Table interaction")]
    [Export] public AimCrosshair Crosshair;

    /// <summary>Distance from the table centre to the flat side of the local semicircular panel.</summary>
    [Export] public float InteractionZoneCenterRadius = 0.575f;

    /// <summary>PASSAR and DESISTIR share this inner half-disc.</summary>
    [Export] public float ActionZoneRadius = 0.115f;

    /// <summary>
    /// APOSTAR sits behind the prepared chips, independently from PASSAR/DESISTIR.
    /// The centre matches the default betting line; the outward half-ring opens toward the pot.
    /// </summary>
    [Export] public float ConfirmZoneCenterRadius = 0.320f;
    [Export] public float ConfirmZoneInnerRadius = 0.070f;
    [Export] public float ConfirmZoneOuterRadius = 0.110f;
    [Export(PropertyHint.Range, "0.08,0.24,0.01")] public float ConfirmLabelSpanPi = 0.13f;
    /// <summary>Straight chalk shortcut beside the denomination bank.</summary>
    [Export] public float CallZoneLength = 0.205f;
    [Export] public float CallZoneWidth = 0.032f;
    [Export] public float CallZoneSideOffset = 0.055f;
    /// <summary>
    /// Invisible tolerance around the thin chalk plate. It preserves a subtle drawing while making
    /// the target equally reliable from the two oblique chair cameras.
    /// </summary>
    [Export] public float CallZoneHitPadding = 0.010f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float CallHoverOpacity = 0.34f;
    [Export(PropertyHint.Range, "0.15,0.6,0.01")] public float CallClickMaxSeconds = 0.35f;
    [Export(PropertyHint.Range, "1,4,0.1")] public float CallLabelCycleSeconds = 2.0f;
    [Export(PropertyHint.Range, "0.1,0.5,0.01")] public float CallLabelFadeSeconds = 0.24f;
    [Export(PropertyHint.Range, "1,4,0.1")] public float AllInHoldSeconds = 2.0f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float AllInHoldOpacity = 0.58f;
    [Export] public Color AllInHoldColor = new(0.30f, 0.88f, 0.45f, 0.86f);
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
        Call,
        ConfirmBet,
        Fold,
        Check,
    }

    private Node3D _interactionGuide;
    private bool _interactionEnabled = true;
    private bool _wagerSubmitted;
    private bool _automaticCallPending;
    private int _automaticCallTurn = -1;
    private bool _callHoldActive;
    private float _callHoldElapsed;
    private int _callHoldTurn = -1;
    private float _callHoverElapsed;
    private PokerGesture _queuedTableGesture;
    private int _interactionHand = -1;
    private Vector2 _guideFacing;
    private InteractionZone _hoveredZone;
    private readonly Dictionary<InteractionZone, ShaderMaterial> _zoneFillMaterials = new();
    private readonly Dictionary<InteractionZone, List<Label3D>> _zoneLabels = new();

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
        if (!IsYourTurn || !_interactionEnabled || _automaticCallPending || _wagerSubmitted)
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

        var aimedZone = ZoneAt(aim);
        // CALL is deliberately adjacent to the bank. Give its explicit rectangle priority over the
        // generous cylindrical chip pick radius so clicking the writing can never remove one chip.
        if (aimedZone == InteractionZone.Call)
            return BeginCallHold();

        // Physical chips take precedence if one happens to cross the projected button silhouette.
        if (presenter.TryReturnPreparedChip(playerId, aim, out _))
        {
            PublishPreparedWagerSnapshot();
            RefreshPhysicalHud();
            return PokerGesture.None;
        }

        if (presenter.TrySelectPreparedChip(playerId, aim, out _))
        {
            PublishPreparedWagerSnapshot();
            RefreshPhysicalHud();
            return PokerGesture.None;
        }

        return aimedZone switch
        {
            InteractionZone.Fold => HandleFold(),
            InteractionZone.Check => HandleCheck(),
            InteractionZone.ConfirmBet => HandleConfirmBet(presenter, playerId),
            _ => PokerGesture.None,
        };
    }

    /// <summary>
    /// A short press is committed only on release, leaving the same physical target free to become
    /// an intentional two-second all-in without ever firing both actions.
    /// </summary>
    public PokerGesture HandleTableRelease()
    {
        if (!_callHoldActive)
            return PokerGesture.None;

        var presenter = Game?.SeatPresenter;
        var playerId = Player == null ? null : (string)Player.Name;
        var valid = IsYourTurn && !_wagerSubmitted && Game?.TurnToken == _callHoldTurn
                    && presenter != null && !string.IsNullOrEmpty(playerId);
        var quickClick = IsQuickCallRelease(_callHoldElapsed, CallClickMaxSeconds);
        ResetCallHold();
        return valid && quickClick
            ? HandleAutomaticCall(presenter, playerId)
            : PokerGesture.None;
    }

    /// <summary>
    /// Classifies the release independently from the two-second all-in timer. The gap between a
    /// quick click and a completed hold deliberately performs no action, preventing an abandoned
    /// all-in from silently becoming a call.
    /// </summary>
    public static bool IsQuickCallRelease(float heldSeconds, float maxClickSeconds) =>
        heldSeconds >= 0.0f && heldSeconds <= Mathf.Max(0.0f, maxClickSeconds);

    /// <summary>Alternates one short word at a time while the crosshair remains on CALL.</summary>
    public static string CallLabelForHover(float elapsed, float cycleSeconds)
    {
        var interval = Mathf.Max(0.1f, cycleSeconds);
        var index = Mathf.FloorToInt(Mathf.Max(0.0f, elapsed) / interval);
        return index % 2 == 0 ? "CALL" : "ALL-IN";
    }

    /// <summary>Fades out before each word swap and back in afterwards, producing a soft blink.</summary>
    public static float CallLabelOpacityForHover(
        float elapsed, float cycleSeconds, float fadeSeconds)
    {
        elapsed = Mathf.Max(0.0f, elapsed);
        var interval = Mathf.Max(0.1f, cycleSeconds);
        var fade = Mathf.Clamp(fadeSeconds, 0.01f, interval * 0.45f);
        // The first word starts readable. Subsequent swaps pass through zero opacity so no frame
        // ever contains CALL and ALL-IN at the same time.
        if (elapsed < interval - fade)
            return 1.0f;

        var phase = Mathf.PosMod(elapsed, interval);
        if (phase > interval - fade)
            return 1.0f - PokerMotion.Smooth((phase - (interval - fade)) / fade);
        if (phase < fade)
            return PokerMotion.Smooth(phase / fade);
        return 1.0f;
    }

    public void AdvanceCallLabelCycle(float delta)
    {
        if (_callHoldActive || _hoveredZone != InteractionZone.Call)
            return;

        _callHoverElapsed += Mathf.Max(0.0f, delta);
        UpdateCallHoldVisual();
    }

    private PokerGesture BeginCallHold()
    {
        if (!HasAction(PokerActionKind.Call) && !TryAllIn(out _, out _))
        {
            ShowNotice("CALL e ALL-IN não estão disponíveis agora", 1.6f);
            return PokerGesture.None;
        }

        _callHoldActive = true;
        _callHoldElapsed = 0.0f;
        _callHoldTurn = Game?.TurnToken ?? -1;
        SetHoveredZone(InteractionZone.Call);
        UpdateCallHoldVisual();
        return PokerGesture.None;
    }

    public void AdvanceCallHold(float delta)
    {
        if (!_callHoldActive)
            return;

        if (!IsYourTurn || _wagerSubmitted || Game?.TurnToken != _callHoldTurn)
        {
            ResetCallHold();
            return;
        }

        // A lost release event (for example when the window loses focus) must cancel, never finish,
        // an irreversible action after the player is no longer physically holding the button.
        if (!Input.IsMouseButtonPressed(MouseButton.Left))
        {
            ResetCallHold();
            return;
        }

        _callHoldElapsed = Mathf.Min(
            _callHoldElapsed + Mathf.Max(0.0f, delta), Mathf.Max(0.1f, AllInHoldSeconds));
        UpdateCallHoldVisual();
        if (_callHoldElapsed + 1e-5f < Mathf.Max(0.1f, AllInHoldSeconds))
            return;

        if (!TryAllIn(out var kind, out var total))
        {
            ResetCallHold();
            ShowNotice("Você não tem como ir de all-in agora", 1.5f);
            return;
        }

        // The hold supersedes any tentative manual amount. The authoritative action then takes the
        // exact remaining bank and uses the established chip push on every peer.
        CancelPreparedWager(immediate: true);
        _wagerSubmitted = true;
        RequestAction(kind, total);
        _queuedTableGesture = PokerGesture.ThrowChips;
    }

    private void ResetCallHold()
    {
        _callHoldActive = false;
        _callHoldElapsed = 0.0f;
        _callHoldTurn = -1;
        UpdateCallHoldVisual();
    }

    public override void CancelPreparedWager(bool immediate = false)
    {
        var presenter = Game?.SeatPresenter;
        var publishCancellation = IsYourTurn && !_wagerSubmitted
                                  && (presenter?.PreparedWagerAmount ?? 0) > 0;
        _wagerSubmitted = false;
        _automaticCallPending = false;
        _automaticCallTurn = -1;
        ResetCallHold();
        _queuedTableGesture = PokerGesture.None;
        presenter?.CancelPreparedWager(immediate);
        if (publishCancellation)
            PublishPreparedWagerSnapshot();
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
        var prepared = Game?.SeatPresenter?.PreparedWagerAmount ?? 0;
        if (!PokerWagerInteraction.TryPrepareCheck(
                _options, prepared, out var returnSelectedChips))
        {
            var playerId = Player == null ? null : (string)Player.Name;
            var call = playerId == null ? 0 : Game.AmountToCall(playerId);
            ShowNotice(call > 0
                ? $"Você precisa pagar {call} para continuar"
                : "Passar não está disponível agora", 1.8f);
            return PokerGesture.None;
        }

        // A prepared wager is only a reversible intention. Choosing PASSAR is unambiguous, so return
        // those chips physically and let the legal check continue instead of trapping the player
        // between "complete the wager" and "return the wager" notices.
        if (returnSelectedChips)
            CancelPreparedWager();

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

    private PokerGesture HandleAutomaticCall(PokerSeatPresenter presenter, string playerId)
    {
        if (!HasAction(PokerActionKind.Call))
        {
            ShowNotice("CALL não está disponível quando você pode passar", 1.6f);
            return PokerGesture.None;
        }

        var total = TotalFor(PokerActionKind.Call);
        var required = Mathf.Max(0, total - Game.BetOf(playerId));
        if (required <= 0)
        {
            ShowNotice("Use PASSAR para continuar sem acrescentar fichas", 1.6f);
            return PokerGesture.None;
        }

        if (presenter.PreparedWagerAmount != required)
        {
            // CALL supersedes a partial manual choice. Rebuild from the stable bank in one local
            // transaction and publish only the final whole snapshot, avoiding a return/select flick.
            presenter.CancelPreparedWager(immediate: true);
            if (!presenter.TryPrepareAutomaticWager(playerId, required))
            {
                ShowNotice("Não foi possível separar as fichas exatas para o CALL", 1.8f);
                RefreshPhysicalHud();
                return PokerGesture.None;
            }
            PublishPreparedWagerSnapshot();
        }

        _automaticCallPending = true;
        _automaticCallTurn = Game.TurnToken;
        RefreshPhysicalHud();
        return PokerGesture.None;
    }

    private void AdvanceAutomaticCall()
    {
        if (!_automaticCallPending)
            return;

        var presenter = Game?.SeatPresenter;
        var playerId = Player == null ? null : (string)Player.Name;
        if (presenter == null || string.IsNullOrEmpty(playerId) || !IsYourTurn
            || Game.TurnToken != _automaticCallTurn || !HasAction(PokerActionKind.Call))
        {
            _automaticCallPending = false;
            _automaticCallTurn = -1;
            return;
        }

        var denominations = presenter.PreparedWagerDenominations;
        var publicPreview = Game.PreparedWagerOf(playerId);
        if (!presenter.PreparedWagerReady || publicPreview == null
            || publicPreview.TurnToken != _automaticCallTurn
            || !publicPreview.Denominations.SequenceEqual(denominations))
            return;

        var selected = presenter.PreparedWagerAmount;
        var total = TotalFor(PokerActionKind.Call);
        if (!presenter.SubmitPreparedWager(playerId, selected))
            return;

        _automaticCallPending = false;
        _automaticCallTurn = -1;
        _wagerSubmitted = true;
        RequestAction(PokerActionKind.Call, total);
        _queuedTableGesture = PokerGesture.ThrowChips;
    }

    public bool TryConsumeTableGesture(out PokerGesture gesture)
    {
        gesture = _queuedTableGesture;
        _queuedTableGesture = PokerGesture.None;
        return gesture != PokerGesture.None;
    }

    private void ShowWagerProblem(PokerWagerProblem problem, int required)
    {
        var text = problem switch
        {
            PokerWagerProblem.NoChipsSelected =>
                HasAction(PokerActionKind.Check)
                    ? "Use PASSAR para encerrar sem apostar"
                    : "Selecione fichas antes de usar APOSTAR",
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

        if (TryCallZoneFrame(out var callCentre, out var callAlong, out var callAcross))
        {
            var callOffset = aim - callCentre;
            if (Mathf.Abs(callOffset.Dot(callAlong))
                    <= CallZoneLength * 0.5f + CallZoneHitPadding
                && Mathf.Abs(callOffset.Dot(callAcross))
                    <= CallZoneWidth * 0.5f + CallZoneHitPadding)
                return InteractionZone.Call;
        }

        // The confirmation band is a separate U behind the prepared wager. Its curved side points
        // toward the seated player, leaving the opening toward the chips and the middle of the table.
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

        if (Game.SeatFor((string)Player.Name) == null)
            return false;

        facing = Game.BoardPresenter.ReaderFacing;
        if (facing.LengthSquared() < 1e-6f)
            return false;

        facing = facing.Normalized();
        across = new Vector2(-facing.Y, facing.X);
        return true;
    }

    private bool TryCallZoneFrame(
        out Vector2 centre, out Vector2 along, out Vector2 across)
    {
        centre = Vector2.Zero;
        along = Vector2.Zero;
        across = Vector2.Zero;
        var playerId = Player == null ? null : (string)Player.Name;
        var presenter = Game?.SeatPresenter;
        if (presenter == null || string.IsNullOrEmpty(playerId)
            || !presenter.TryBankGuideFrame(playerId, out var bankCentre, out along, out across))
            return false;

        centre = bankCentre + across * CallZoneSideOffset;
        return true;
    }

    private void SyncTableInteraction(int hand, bool isYourTurn)
    {
        if (hand != _interactionHand)
        {
            _interactionHand = hand;
            _wagerSubmitted = false;
            _automaticCallPending = false;
            _automaticCallTurn = -1;
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
        var visible = _interactionEnabled && IsYourTurn && HasPickedUpCards;
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
            SetHoveredZone(InteractionZone.Call);
            return;
        }

        var zone = TryAimPoint(out var aim) ? ZoneAt(aim) : InteractionZone.None;
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
                ? entry.Key == InteractionZone.Call ? CallHoverOpacity : ChalkHoverOpacity
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
            ? Mathf.Clamp(_callHoldElapsed / Mathf.Max(0.1f, AllInHoldSeconds), 0.0f, 1.0f)
            : 1.0f;

        if (_zoneFillMaterials.TryGetValue(InteractionZone.Call, out var material))
        {
            material.SetShaderParameter("use_fill_progress", _callHoldActive);
            material.SetShaderParameter("fill_progress", progress);
            material.SetShaderParameter("chalk_color",
                _callHoldActive ? AllInHoldColor : ChalkGuideColor);
            material.SetShaderParameter("chalk_strength", _callHoldActive
                ? Mathf.Lerp(0.18f, AllInHoldOpacity, PokerMotion.Smooth(progress))
                : _hoveredZone == InteractionZone.Call ? CallHoverOpacity : 0.0f);
        }

        if (!_zoneLabels.TryGetValue(InteractionZone.Call, out var labels))
            return;

        var text = _callHoldActive
            ? "ALL-IN"
            : _hoveredZone == InteractionZone.Call
                ? CallLabelForHover(_callHoverElapsed, CallLabelCycleSeconds)
                : "CALL";
        var hoverOpacity = _hoveredZone == InteractionZone.Call
            ? CallLabelOpacityForHover(
                _callHoverElapsed, CallLabelCycleSeconds, CallLabelFadeSeconds)
            : ChalkGuideColor.A;
        var colour = _callHoldActive
            ? AllInHoldColor with { A = 1.0f }
            : ChalkGuideColor with { A = hoverOpacity };
        foreach (var label in labels)
        {
            label.Text = text;
            label.Modulate = colour;
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
        var hasCallFrame = TryCallZoneFrame(
            out var callCentre, out var callAlong, out var callAcross);
        if (hasCallFrame)
            AddChalkRectangleZone(InteractionZone.Call, "CallFill", callCentre, callAlong, callAcross,
                CallZoneLength, CallZoneWidth);

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
        if (hasCallFrame)
            AddRectangleBorder("CallBorder", callCentre, callAlong, callAcross,
                CallZoneLength, CallZoneWidth, lineMaterial);

        var actionLabelRadius = ActionZoneRadius * 0.56f;
        AddChalkLabel(InteractionZone.Check, "Check", "PASSAR",
            SemicirclePoint(actionCentre, inward, across, actionLabelRadius, Mathf.Pi * 0.25f), labelBasis,
            ChalkGuideFontSize);
        AddChalkLabel(InteractionZone.Fold, "Fold", "DESISTIR",
            SemicirclePoint(actionCentre, inward, across, actionLabelRadius, -Mathf.Pi * 0.25f), labelBasis,
            ChalkGuideFontSize);
        AddCurvedChalkLabel(InteractionZone.ConfirmBet, ConfirmBetLabelText,
            confirmCentre, confirmOutward, across,
            (ConfirmZoneInnerRadius + ConfirmZoneOuterRadius) * 0.5f,
            Mathf.Pi * ConfirmLabelSpanPi, Mathf.RoundToInt(ChalkGuideFontSize * 0.82f),
            reverseGlyphUp: true);
        if (hasCallFrame)
            AddAlignedChalkLabel(InteractionZone.Call, "Call", "CALL", callCentre,
                callAlong, -callAcross, Mathf.RoundToInt(ChalkGuideFontSize * 0.82f));
        UpdateCallHoldVisual();
    }

    private void AddChalkZone(
        InteractionZone zone, string name, Vector2 centre, Vector2 inward, Vector2 across,
        float innerRadius, float outerRadius, float startAngle, float endAngle)
    {
        var material = NewChalkMaterial(ChalkGuideColor, 0.0f);
        // Sector UVs are table-space and must never be clipped by CALL's 0..1 progress mask.
        material.SetShaderParameter("use_fill_progress", false);
        material.SetShaderParameter("fill_progress", 1.0f);
        _zoneFillMaterials[zone] = material;
        AddGuideMesh(name, BuildSectorMesh(centre, inward, across,
            innerRadius, outerRadius, startAngle, endAngle, InteractionArcSteps, material), 0.0031f);
    }

    private void AddChalkRectangleZone(
        InteractionZone zone, string name, Vector2 centre, Vector2 along, Vector2 across,
        float length, float width)
    {
        var material = NewChalkMaterial(ChalkGuideColor, 0.0f);
        material.SetShaderParameter("use_fill_progress", false);
        material.SetShaderParameter("fill_progress", 1.0f);
        _zoneFillMaterials[zone] = material;
        AddGuideMesh(name, BuildRectangleMesh(
            centre, along, across, length, width, material), 0.0031f);
    }

    private void AddRectangleBorder(
        string name, Vector2 centre, Vector2 along, Vector2 across,
        float length, float width, Material material)
    {
        AddGuideMesh(name, BuildRectangleBorderMesh(
            centre, along, across, length, width, InteractionGuideThickness, material), 0.0033f);
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

    /// <summary>Places each glyph on the confirmation arc and turns it along the local tangent.</summary>
    private void AddCurvedChalkLabel(
        InteractionZone zone, string text, Vector2 centre, Vector2 inward, Vector2 across,
        float radius, float spanAngle, int fontSize, bool reverseGlyphUp = false)
    {
        if (string.IsNullOrEmpty(text))
            return;

        for (var index = 0; index < text.Length; index++)
        {
            var angle = text.Length <= 1
                ? 0.0f
                : Mathf.Lerp(spanAngle, -spanAngle, index / (float)(text.Length - 1));
            if (text[index] == ' ')
                continue;

            var point = SemicirclePoint(centre, inward, across, radius, angle);
            var outward = inward * Mathf.Cos(angle) + across * Mathf.Sin(angle);
            // The word is laid left-to-right while angles run from +span to -span.
            var right = inward * Mathf.Sin(angle) - across * Mathf.Cos(angle);
            var right3 = new Vector3(right.X, 0.0f, right.Y).Normalized();
            var up3 = new Vector3(outward.X, 0.0f, outward.Y).Normalized();
            if (reverseGlyphUp)
                up3 = -up3;
            var normal3 = right3.Cross(up3).Normalized();
            var label = NewChalkLabel($"ConfirmGlyph{index}", text[index].ToString(),
                fontSize, new Transform3D(
                    new Basis(right3, up3, normal3),
                    new Vector3(point.X, 0.004f, point.Y)));
            _interactionGuide.AddChild(label);
            AddZoneLabel(zone, label);
        }
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

    private void AddAlignedChalkLabel(
        InteractionZone zone, string name, string text, Vector2 position,
        Vector2 right, Vector2 up, int fontSize)
    {
        var right3 = new Vector3(right.X, 0.0f, right.Y).Normalized();
        var up3 = new Vector3(up.X, 0.0f, up.Y).Normalized();
        var normal3 = right3.Cross(up3).Normalized();
        var label = NewChalkLabel(name, text, fontSize, new Transform3D(
            new Basis(right3, up3, normal3), new Vector3(position.X, 0.004f, position.Y)));
        _interactionGuide.AddChild(label);
        AddZoneLabel(zone, label);
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
            AddInteractionTriangle(mesh, inner0, outer0, outer1);
            AddInteractionTriangle(mesh, inner0, outer1, inner1);
        }
        mesh.SurfaceEnd();
        return mesh;
    }

    private static ImmediateMesh BuildRectangleMesh(
        Vector2 centre, Vector2 along, Vector2 across, float length, float width, Material material)
    {
        var mesh = new ImmediateMesh();
        var halfLength = Mathf.Max(0.001f, length * 0.5f);
        var halfWidth = Mathf.Max(0.001f, width * 0.5f);
        var a = RectanglePoint(centre, along, across, -halfLength, -halfWidth);
        var b = RectanglePoint(centre, along, across, halfLength, -halfWidth);
        var c = RectanglePoint(centre, along, across, halfLength, halfWidth);
        var d = RectanglePoint(centre, along, across, -halfLength, halfWidth);
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, material);
        AddInteractionTriangle(mesh, a, b, c,
            new Vector2(0.0f, 0.0f), new Vector2(1.0f, 0.0f), new Vector2(1.0f, 1.0f));
        AddInteractionTriangle(mesh, a, c, d,
            new Vector2(0.0f, 0.0f), new Vector2(1.0f, 1.0f), new Vector2(0.0f, 1.0f));
        mesh.SurfaceEnd();
        return mesh;
    }

    private static ImmediateMesh BuildRectangleBorderMesh(
        Vector2 centre, Vector2 along, Vector2 across, float length, float width,
        float thickness, Material material)
    {
        var mesh = new ImmediateMesh();
        var outerLength = Mathf.Max(0.002f, length * 0.5f);
        var outerWidth = Mathf.Max(0.002f, width * 0.5f);
        var inset = Mathf.Clamp(thickness, 0.0005f, Mathf.Min(outerLength, outerWidth) * 0.45f);
        var innerLength = outerLength - inset;
        var innerWidth = outerWidth - inset;
        var outer = new[]
        {
            RectanglePoint(centre, along, across, -outerLength, -outerWidth),
            RectanglePoint(centre, along, across, outerLength, -outerWidth),
            RectanglePoint(centre, along, across, outerLength, outerWidth),
            RectanglePoint(centre, along, across, -outerLength, outerWidth),
        };
        var inner = new[]
        {
            RectanglePoint(centre, along, across, -innerLength, -innerWidth),
            RectanglePoint(centre, along, across, innerLength, -innerWidth),
            RectanglePoint(centre, along, across, innerLength, innerWidth),
            RectanglePoint(centre, along, across, -innerLength, innerWidth),
        };
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, material);
        for (var edge = 0; edge < 4; edge++)
        {
            var next = (edge + 1) % 4;
            AddInteractionTriangle(mesh, outer[edge], outer[next], inner[next]);
            AddInteractionTriangle(mesh, outer[edge], inner[next], inner[edge]);
        }
        mesh.SurfaceEnd();
        return mesh;
    }

    private static Vector2 RectanglePoint(
        Vector2 centre, Vector2 along, Vector2 across, float alongDistance, float acrossDistance) =>
        centre + along * alongDistance + across * acrossDistance;

    private static Vector2 SemicirclePoint(
        Vector2 centre, Vector2 inward, Vector2 across, float radius, float angle) =>
        centre + inward * (Mathf.Cos(angle) * radius) + across * (Mathf.Sin(angle) * radius);

    private static void AddInteractionTriangle(
        ImmediateMesh mesh, Vector2 a, Vector2 b, Vector2 c)
    {
        AddInteractionVertex(mesh, a);
        AddInteractionVertex(mesh, b);
        AddInteractionVertex(mesh, c);
    }

    private static void AddInteractionTriangle(
        ImmediateMesh mesh, Vector2 a, Vector2 b, Vector2 c,
        Vector2 uvA, Vector2 uvB, Vector2 uvC)
    {
        AddInteractionVertex(mesh, a, uvA);
        AddInteractionVertex(mesh, b, uvB);
        AddInteractionVertex(mesh, c, uvC);
    }

    private static void AddInteractionVertex(ImmediateMesh mesh, Vector2 point)
    {
        mesh.SurfaceSetNormal(Vector3.Up);
        mesh.SurfaceSetUV(point * 8.0f);
        mesh.SurfaceAddVertex(new Vector3(point.X, 0.0f, point.Y));
    }

    private static void AddInteractionVertex(ImmediateMesh mesh, Vector2 point, Vector2 uv)
    {
        mesh.SurfaceSetNormal(Vector3.Up);
        mesh.SurfaceSetUV(uv);
        mesh.SurfaceAddVertex(new Vector3(point.X, 0.0f, point.Y));
    }
}
