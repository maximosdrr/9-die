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
    /// <summary>Hover strength shared by CALL, AUTO, APOSTAR and the all-in hold.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float CallHoverOpacity = 0.34f;
    [Export(PropertyHint.Range, "0.15,0.6,0.01")] public float CallClickMaxSeconds = 0.35f;
    [Export(PropertyHint.Range, "1,4,0.1")] public float CallLabelCycleSeconds = 2.0f;
    [Export(PropertyHint.Range, "0.1,0.5,0.01")] public float CallLabelFadeSeconds = 0.24f;
    [Export(PropertyHint.Range, "0.5,2,0.1")] public float AllInHoldSeconds = 1.5f;
    [Export(PropertyHint.Range, "0.1,0.6,0.01")] public float AllInVisualDelaySeconds = 0.35f;
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
        ConfirmBet,
        Fold,
        Check,
    }

    private Node3D _interactionGuide;
    private bool _interactionEnabled = true;
    private bool _wagerSubmitted;
    private bool _automaticWagerPending;
    private int _automaticWagerTurn = -1;
    private PokerActionKind _automaticWagerKind = PokerActionKind.None;
    private int _automaticWagerTotal;
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

    /// <summary>
    /// Aim on the actual plane that draws PASSAR/DESISTIR/APOSTAR. The guide can sit above the
    /// board plane, so reusing the board hit creates a perspective offset after table edits.
    /// </summary>
    private bool TryActionGuideAimPoint(out Vector2 guideLocal)
    {
        guideLocal = Vector2.Zero;
        EnsureInteractionGuide();
        return IsInstanceValid(_interactionGuide)
               && AimPlane.TryAim(Game?.Camera, _interactionGuide, out guideLocal);
    }

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
        if (!IsYourTurn || !_interactionEnabled || _automaticWagerPending || _wagerSubmitted)
            return PokerGesture.None;

        if (!HasPickedUpCards)
        {
            ShowNotice("Aguarde — você ainda está olhando suas cartas", 1.5f);
            return PokerGesture.None;
        }

        if (!AimPlane.TryCentreRay(Game?.Camera, out var rayOrigin, out var rayDirection))
            return PokerGesture.None;

        var presenter = Game?.SeatPresenter;
        var playerId = Player == null ? null : (string)Player.Name;
        if (presenter == null || string.IsNullOrEmpty(playerId))
            return PokerGesture.None;

        var aimedZone = TryActionGuideAimPoint(out var guideAim)
            ? ZoneAt(guideAim)
            : InteractionZone.None;

        // Physical chips take precedence even when their edited layout crosses a projected button.
        // In particular, staged chips sit close to the wager arc and must always remain reversible.
        if (presenter.TryReturnPreparedChipAtRay(
                playerId, rayOrigin, rayDirection, out _))
        {
            PublishPreparedWagerSnapshot();
            RefreshPhysicalHud();
            return PokerGesture.None;
        }

        if (presenter.TrySelectPreparedChipAtRay(
                playerId, rayOrigin, rayDirection, out _))
        {
            PublishPreparedWagerSnapshot();
            RefreshPhysicalHud();
            return PokerGesture.None;
        }

        // The unified wager arc owns press/release so a short click and an all-in hold can never
        // fire together. It is considered only after both kinds of physical chip target.
        if (aimedZone == InteractionZone.ConfirmBet)
            return BeginCallHold();

        return aimedZone switch
        {
            InteractionZone.Fold => HandleFold(),
            InteractionZone.Check => HandleCheck(),
            _ => PokerGesture.None,
        };
    }

    /// <summary>
    /// A short press is committed only on release, leaving the same physical target free to become
    /// an intentional 1.5-second all-in without ever firing both actions.
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
        if (!valid || !quickClick)
            return PokerGesture.None;

        return presenter.PreparedWagerAmount > 0
            ? HandleConfirmBet(presenter, playerId)
            : HandleAutomaticWager(presenter, playerId);
    }

    /// <summary>
    /// Classifies the release independently from the 1.5-second all-in timer. The gap between a
    /// quick click and a completed hold deliberately performs no action, preventing an abandoned
    /// all-in from silently becoming a call.
    /// </summary>
    public static bool IsQuickCallRelease(float heldSeconds, float maxClickSeconds) =>
        heldSeconds >= 0.0f && heldSeconds <= Mathf.Max(0.0f, maxClickSeconds);

    /// <summary>Alternates the available shortcut with the explicit hold instruction.</summary>
    public static string CallLabelForHover(
        string automaticLabel, float elapsed, float cycleSeconds)
    {
        var interval = Mathf.Max(0.1f, cycleSeconds);
        var index = Mathf.FloorToInt(Mathf.Max(0.0f, elapsed) / interval);
        return index % 2 == 0 ? automaticLabel : "SEGURE ALL-IN";
    }

    public static string CallLabelForHover(float elapsed, float cycleSeconds) =>
        CallLabelForHover("CALL", elapsed, cycleSeconds);

    /// <summary>
    /// CALL has priority when chips are owed. With nothing to call, the same physical shortcut
    /// becomes AUTO and chooses the minimum legal raise rather than disappearing from the table.
    /// </summary>
    public static bool TryAutomaticWagerOption(
        IReadOnlyList<ActionOption> options, out PokerActionKind kind, out int total)
    {
        if (options != null)
        {
            foreach (var option in options)
            {
                if (option.Kind != PokerActionKind.Call)
                    continue;

                kind = PokerActionKind.Call;
                total = option.MinTotal;
                return true;
            }

            foreach (var option in options)
            {
                if (option.Kind != PokerActionKind.Raise)
                    continue;

                kind = PokerActionKind.Raise;
                total = option.MinTotal;
                return true;
            }
        }

        kind = PokerActionKind.None;
        total = 0;
        return false;
    }

    public static string AutomaticWagerLabel(IReadOnlyList<ActionOption> options) =>
        TryAutomaticWagerOption(options, out var kind, out _)
            && kind == PokerActionKind.Raise ? "AUTO" : "CALL";

    public static string WagerButtonLabel(
        IReadOnlyList<ActionOption> options, bool hasPreparedChips) =>
        hasPreparedChips ? ConfirmBetLabelText : AutomaticWagerLabel(options);

    /// <summary>Fades out before each word swap and back in afterwards, producing a soft blink.</summary>
    public static float CallLabelOpacityForHover(
        float elapsed, float cycleSeconds, float fadeSeconds)
    {
        elapsed = Mathf.Max(0.0f, elapsed);
        var interval = Mathf.Max(0.1f, cycleSeconds);
        var fade = Mathf.Clamp(fadeSeconds, 0.01f, interval * 0.45f);
        // The first word starts readable. Subsequent swaps pass through zero opacity so no frame
        // ever contains the automatic action and SEGURE ALL-IN at the same time.
        if (elapsed < interval - fade)
            return 1.0f;

        var phase = Mathf.PosMod(elapsed, interval);
        if (phase > interval - fade)
            return 1.0f - PokerMotion.Smooth((phase - (interval - fade)) / fade);
        if (phase < fade)
            return PokerMotion.Smooth(phase / fade);
        return 1.0f;
    }

    /// <summary>
    /// A quick click lives entirely inside the dead zone and therefore never flashes the green
    /// all-in progress. After that threshold, the remaining hold time maps cleanly from zero to one.
    /// </summary>
    public static float AllInHoldVisualProgress(
        float heldSeconds, float visualDelaySeconds, float holdSeconds)
    {
        var end = Mathf.Max(0.1f, holdSeconds);
        var start = Mathf.Clamp(visualDelaySeconds, 0.0f, end - 0.01f);
        if (heldSeconds <= start)
            return 0.0f;

        var fillDuration = end - start;
        var remaining = Mathf.Max(0.0f, end - heldSeconds);
        return 1.0f - Mathf.Clamp(remaining / fillDuration, 0.0f, 1.0f);
    }

    /// <summary>
    /// Progress UVs are local to the arc, never table coordinates. The same 0..1 range is therefore
    /// produced for every chair and peer, independently of where or how the local guide is rotated.
    /// </summary>
    public static Vector2 SectorProgressUv(int step, int steps, bool outer)
    {
        var safeSteps = Mathf.Max(1, steps);
        return new Vector2(
            Mathf.Clamp(step / (float)safeSteps, 0.0f, 1.0f), outer ? 1.0f : 0.0f);
    }

    public void AdvanceCallLabelCycle(float delta)
    {
        if (_callHoldActive || _hoveredZone != InteractionZone.ConfirmBet)
            return;

        _callHoverElapsed += Mathf.Max(0.0f, delta);
        UpdateCallHoldVisual();
    }

    private PokerGesture BeginCallHold()
    {
        var prepared = Game?.SeatPresenter?.PreparedWagerAmount ?? 0;
        if (prepared <= 0
            && !TryAutomaticWagerOption(_options, out _, out _)
            && !TryAllIn(out _, out _))
        {
            ShowNotice("A aposta automática e o ALL-IN não estão disponíveis agora", 1.6f);
            return PokerGesture.None;
        }

        _callHoldActive = true;
        _callHoldElapsed = 0.0f;
        _callHoldTurn = Game?.TurnToken ?? -1;
        SetHoveredZone(InteractionZone.ConfirmBet);
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
        ResetAutomaticWager();
        ResetCallHold();
        _queuedTableGesture = PokerGesture.None;
        presenter?.CancelPreparedWager(immediate);
        if (publishCancellation)
            PublishPreparedWagerSnapshot();
        RefreshPhysicalHud();
    }

    private void RefreshPhysicalHud()
    {
        Hud?.Refresh(_options, IsYourTurn, RaiseTotal, HasPickedUpCards);
        UpdateCallHoldVisual();
    }

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

    private PokerGesture HandleAutomaticWager(PokerSeatPresenter presenter, string playerId)
    {
        if (!TryAutomaticWagerOption(_options, out var kind, out var total))
        {
            ShowNotice("A aposta automática não está disponível agora", 1.6f);
            return PokerGesture.None;
        }

        var required = Mathf.Max(0, total - Game.BetOf(playerId));
        if (required <= 0)
        {
            ShowNotice("Use PASSAR para continuar sem acrescentar fichas", 1.6f);
            return PokerGesture.None;
        }

        if (presenter.PreparedWagerAmount != required)
        {
            // The shortcut supersedes a partial manual choice. Rebuild from the stable bank in one
            // local transaction and publish only the final snapshot, avoiding a return/select flick.
            presenter.CancelPreparedWager(immediate: true);
            if (!presenter.TryPrepareAutomaticWager(playerId, required))
            {
                var label = kind == PokerActionKind.Call ? "CALL" : "AUTO";
                ShowNotice($"Não foi possível separar as fichas exatas para o {label}", 1.8f);
                RefreshPhysicalHud();
                return PokerGesture.None;
            }
            PublishPreparedWagerSnapshot();
        }

        _automaticWagerPending = true;
        _automaticWagerTurn = Game.TurnToken;
        _automaticWagerKind = kind;
        _automaticWagerTotal = total;
        RefreshPhysicalHud();
        return PokerGesture.None;
    }

    private void AdvanceAutomaticWager()
    {
        if (!_automaticWagerPending)
            return;

        var presenter = Game?.SeatPresenter;
        var playerId = Player == null ? null : (string)Player.Name;
        if (presenter == null || string.IsNullOrEmpty(playerId) || !IsYourTurn
            || Game.TurnToken != _automaticWagerTurn
            || !OptionAllows(_automaticWagerKind, _automaticWagerTotal))
        {
            ResetAutomaticWager();
            return;
        }

        var denominations = presenter.PreparedWagerDenominations;
        var publicPreview = Game.PreparedWagerOf(playerId);
        if (!presenter.PreparedWagerReady || publicPreview == null
            || publicPreview.TurnToken != _automaticWagerTurn
            || !publicPreview.Denominations.SequenceEqual(denominations))
            return;

        var selected = presenter.PreparedWagerAmount;
        if (!presenter.SubmitPreparedWager(playerId, selected))
            return;

        var kind = _automaticWagerKind;
        var total = _automaticWagerTotal;
        ResetAutomaticWager();
        _wagerSubmitted = true;
        RequestAction(kind, total);
        _queuedTableGesture = PokerGesture.ThrowChips;
    }

    private bool OptionAllows(PokerActionKind kind, int total) =>
        _options.Any(option => option.Kind == kind && option.Allows(total));

    private void ResetAutomaticWager()
    {
        _automaticWagerPending = false;
        _automaticWagerTurn = -1;
        _automaticWagerKind = PokerActionKind.None;
        _automaticWagerTotal = 0;
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
}
