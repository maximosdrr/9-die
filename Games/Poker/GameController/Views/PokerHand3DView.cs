using System.Collections.Generic;
using Godot;
using Poker.Rules;

public enum PokerCardAttachmentMode
{
    FollowHand,
    TableRest,
}

/// <summary>
/// The player's two cards held in front of the camera, the table-space interaction and the small
/// status HUD.
///
/// Betting is physical and reversible: chips are taken from denomination stacks, staged in front of
/// the player and pushed through the outer confirmation arc. Fold and check occupy separate chalk
/// sectors and all three respond to one deliberate hovered click. The wager arc becomes CALL,
/// AUTO or APOSTAR from the physical chip state and accepts a 1.5-second hold for all-in.
///
/// The right mouse button turns the cards up while held. At rest they lie face down, which is both
/// the real gesture and the honest one — a card
/// nobody is looking at should not be facing the room.
///
/// Gameplay still emits intents through the controller. The only direct presentation relay is the
/// raised/lowered card-pose edge, so other players can see a right-button peek on the seated body.
/// </summary>
[GlobalClass]
public partial class PokerHand3DView : PokerHandView
{
    [Export] public Node3D HandRig;
    [Export] public Node3D CardHandPose;
    [Export] public Node3D CardSlots;
    [Export] public Node3D CardDownAnchor;
    [Export] public PackedScene CardScene;
    [Export] public Node3D CardHandVisualMount;
    [Export] public Node3D ChipHandVisualMount;

    /// <summary>The corner panel. Everything the player reads in words lives there.</summary>
    [Export] public PokerHud Hud;

    /// <summary>Optional. Each state plays its clip here when a real rig is wired up.</summary>
    [Export] public AnimationPlayer AnimationPlayer;

    [ExportGroup("Hand")]
    /// <summary>
    /// Complete controller pose relative to the camera. It comes from the editable
    /// FirstPersonControllerPose marker in Poker.tscn, including position and rotation.
    /// </summary>
    [Export]
    public Transform3D HandPose = new(
        Basis.Identity, new Vector3(0.045f, -0.05f, -0.22f));
    [Export]
    public Transform3D CardsInHandPose = new(
        new Basis(Vector3.Up, Mathf.Pi), Vector3.Zero);
    /// <summary>Final resting transform of each real card, relative to CardsInHandPose.</summary>
    [Export] public Transform3D Card0InHandPose = Transform3D.Identity;
    [Export] public Transform3D Card1InHandPose = Transform3D.Identity;

    public Vector3 HandOffset
    {
        get => HandPose.Origin;
        set => HandPose = new Transform3D(HandPose.Basis, value);
    }

    [Export] public float FanStepDeg = 11.0f;
    [Export] public float FanRadius = 0.40f;
    [Export] public float SelectedLift = 0.0f;

    [ExportGroup("Peek")]
    /// <summary>
    /// Resting lean. Zero keeps the real cards upright in the exact gap authored around the red
    /// Blender placeholder; positive values lower them away from the player's eyes.
    /// </summary>
    [Export] public float RestTiltDeg = 0.0f;

    /// <summary>Lean while peeking: turned up so the faces point back at the player's own eye.</summary>
    [Export] public float PeekTiltDeg = -18.0f;

    /// <summary>How fast the cards turn over, in responses per second.</summary>
    [Export] public float PeekSpeed = 14.0f;

    [ExportGroup("Cards on table")]
    /// <summary>Short handoff between the stable felt anchor and the animated left-hand grip.</summary>
    [Export] public float CardAttachmentBlendSeconds = 0.18f;

    /// <summary>
    /// Tiny gap between visible card geometry and the physical tabletop. The visual wooden mesh is
    /// about 0.2 mm above its collider, so 0.35 mm clears both without reading as floating.
    /// </summary>
    [Export(PropertyHint.Range, "0.0001,0.002,0.00005")]
    public float CardTableClearance = 0.00035f;

    [ExportGroup("Taking the cards up")]
    /// <summary>Beat before reaching down, so the deal is seen to finish before it is disturbed.</summary>
    [Export] public float PickUpDelay = 0.35f;

    /// <summary>Lifting the pair off the cloth and turning it up.</summary>
    [Export] public float PickUpLiftSeconds = 0.55f;

    /// <summary>Normalized point in PickCards where the fingers reach the pair on the felt.</summary>
    [Export(PropertyHint.Range, "0.1,0.9,0.01")]
    public float PickUpContactFraction = 0.42f;

    /// <summary>How long the player looks at them before lowering.</summary>
    [Export] public float PickUpLookSeconds = 1.3f;

    /// <summary>Bringing them back down, face hidden, ready to play.</summary>
    [Export] public float PickUpSettleSeconds = 0.5f;

    private readonly List<PokerCard> _fan = new();
    private readonly List<Transform3D> _fanTransferFrom = new();
    private int[] _holeCards = System.Array.Empty<int>();
    private IReadOnlyList<ActionOption> _options = new List<ActionOption>();
    private List<int> _presets = new();
    private float _messageSeconds;
    private float _peek;
    private int _lastHand = -1;
    private int _showdownPlayedHand = -1;
    private bool _pickedUp;
    private bool _cardsTransferred;
    private bool _lookDone;
    private float _pickUpElapsed;
    private float _pickUpAnimationSeconds;
    private bool _openingLookPoseStarted;
    private bool _openingDownPoseStarted;
    private bool _openingTableRestStarted;
    private bool _voluntaryLookPose;
    private PokerGesture _activeTableGesture = PokerGesture.None;
    private float _activeTableGestureRemaining;
    private float _showdownPreparationElapsed;
    private float _showdownPreparationDuration;
    private bool _showdownClipStarted;
    private float _cardTransfer = 1.0f;
    private PokerHandVisual _cardHandVisual;
    private PokerHandVisual _chipHandVisual;
    private SeatedTableController _seatController;
    private FirstPersonHandCameraMode _leftHandCameraMode
        = FirstPersonHandCameraMode.Locked;
    private FirstPersonHandCameraMode _rightHandCameraMode
        = FirstPersonHandCameraMode.Locked;
    private PokerCardAttachmentMode _cardAttachmentMode
        = PokerCardAttachmentMode.FollowHand;
    private float _cardAttachmentBlend = 1.0f;
    private Transform3D _cardAttachmentBlendFrom = Transform3D.Identity;
    private bool _cardDownAnchorReady;
    private int _publishedWagerTurn = -1;
    private int _publishedWagerRevision;

    // ---------------------------------------------------------------- what the states drive

    public bool IsYourTurn { get; private set; }

    public IReadOnlyList<ActionOption> Options => _options;

    public bool TableActionsLocked => _activeTableGesture == PokerGesture.Reveal;

    public bool HasAnyAction => !TableActionsLocked && _options.Count > 0;

    /// <summary>Legacy preset total retained for compatibility; physical chip selection is primary.</summary>
    public int RaiseTotal { get; private set; }

    /// <summary>How far the cards are turned up, 0 at rest and 1 fully peeked.</summary>
    public float PeekAmount => _peek;

    public FirstPersonHandCameraMode LeftHandCameraMode => _leftHandCameraMode;
    public FirstPersonHandCameraMode RightHandCameraMode => _rightHandCameraMode;
    public PokerCardAttachmentMode CardAttachmentMode => _cardAttachmentMode;

    /// <summary>
    /// Whether the opening look is over and this player may act.
    ///
    /// Distinct from the cards merely being off the cloth: the hand is unplayable until the whole
    /// reach-lift-look-lower has run, because a hand played before its cards were seen is not a
    /// decision. It plays itself, so this is a beat rather than a chore.
    /// </summary>
    public bool HasPickedUpCards => _lookDone;

    /// <summary>
    /// Whether the hand should be listening at all. Godot delivers unhandled input to children
    /// before parents, so without this the very click that takes the cursor back after Escape would
    /// reach the hand first and be read as a peek.
    /// </summary>
    public static bool InputIsLive => InputFocus.IsCaptured;

    public override void _Ready()
    {
        // Imported hand animation and its scale-free grip update before this view moves the rig and
        // attaches the actual PokerCard nodes for the rendered frame.
        ProcessPriority = 200;
        if (CardSlots != null)
            CardSlots.TopLevel = true;
        _cardDownAnchorReady = CardDownAnchor != null
                               && !CardDownAnchor.Transform.IsEqualApprox(Transform3D.Identity);

        // A hand only exists for the peer holding it.
        if (IsMultiplayerAuthority())
            return;

        Hide();
        SetProcess(false);
    }

    public override void Setup(PokerGame game, Player player)
    {
        base.Setup(game, player);
        _seatController = GetParentOrNull<SeatedTableController>();
        player?.SetFirstPersonVisualEnabled(false);

        if (game?.BoardPresenter != null && GodotObject.IsInstanceValid(player))
        {
            game.BoardPresenter.SetReaderPlayer((string)player.Name);
            // A controller can be reclaimed after the deal has already settled. Refresh the stable
            // table visuals now as well, rather than waiting for another poker-state mutation.
            game.SeatPresenter?.Refresh();
        }

        if (HandRig != null)
            HandRig.TopLevel = true;

        InstallVisualAssets(game?.VisualAssets);

        Hud?.Setup(game, player);
    }

    public override void _ExitTree()
    {
        if (_cardHandVisual is PlayerFirstPersonHands hands)
            hands.UnbindCardSlots(CardSlots);
        SetHandCameraModes(
            FirstPersonHandCameraMode.Locked,
            FirstPersonHandCameraMode.Locked,
            immediate: true);
        if (IsMultiplayerAuthority() && GodotObject.IsInstanceValid(Player))
        {
            if (_voluntaryLookPose)
                Player?.SetPokerCardLook(false);
            Player?.SetFirstPersonVisualEnabled(true);
        }
    }

    public override void _Process(double delta)
    {
        if (!IsMultiplayerAuthority() || Game == null)
            return;

        // A public reveal can wait behind the final bet without producing another network snapshot.
        // Poll before requiring a live camera: animation/card ownership must still cross the seam
        // during camera handoff or a temporary viewport interruption.
        PlayShowdownOnce();
        if (Game.Camera == null)
            return;

        UpdatePeek((float)delta);
        AdvanceStandaloneGesture((float)delta);
        AdvanceCallHold((float)delta);
        AdvanceAutomaticWager();
        UpdateInteractionVisibility();
        AdvanceCallLabelCycle((float)delta);

        // The body and locked hands live in the chair's resting camera frame. Mouse input turns the
        // real camera independently; the per-arm modifier below selectively layers that rotation
        // only onto hands whose current policy is FollowCamera.
        var lockedView = _seatController?.StableSeatViewTransform
                         ?? Game.Camera.GlobalTransform;
        if (HandRig != null)
            HandRig.GlobalTransform = lockedView * HandPose;

        if (_cardHandVisual is PlayerFirstPersonHands hands)
        {
            hands.ConfigureHandCameraModes(
                lockedView,
                Game.Camera,
                _leftHandCameraMode,
                _rightHandCameraMode);
        }

        FollowImportedCardGrip((float)delta);

        if (_messageSeconds <= 0.0f)
            return;

        _messageSeconds -= (float)delta;
        if (_messageSeconds <= 0.0f)
        {
            Hud?.HideNotice();
        }
    }

    // ---------------------------------------------------------------- the seam

    /// <summary>
    /// Emits the entire ordered local preview. Whole snapshots plus a per-turn revision make the
    /// server channel idempotent and let every peer reconcile a missed selection without replaying
    /// an unsafe client-authored delta.
    /// </summary>
    public void PublishPreparedWagerSnapshot()
    {
        if (Game?.SeatPresenter == null || Player == null)
            return;

        if (_publishedWagerTurn != Game.TurnToken)
        {
            _publishedWagerTurn = Game.TurnToken;
            _publishedWagerRevision = 0;
        }

        _publishedWagerRevision++;
        EmitSignal(PokerHandView.SignalName.PreparedWagerChanged,
            Game.TurnToken, _publishedWagerRevision,
            Game.SeatPresenter.PreparedWagerDenominations);
    }

    public override void Refresh(int[] holeCards, IReadOnlyList<ActionOption> options, bool isYourTurn)
    {
        _holeCards = holeCards ?? System.Array.Empty<int>();
        _options = options ?? new List<ActionOption>();
        IsYourTurn = isYourTurn;

        // A new hand deals a new pair onto the cloth, so the opening look runs again.
        var hand = Game?.HandNumber ?? 0;
        if (hand != _lastHand)
        {
            SetCardAttachmentMode(PokerCardAttachmentMode.FollowHand);
            SetVoluntaryLookPose(false);
            SetHandCameraModes(
                FirstPersonHandCameraMode.Locked,
                FirstPersonHandCameraMode.Locked,
                immediate: true);
            _fan.Clear();
            _fanTransferFrom.Clear();
            _lastHand = hand;
            _pickedUp = false;
            _cardsTransferred = false;
            _lookDone = false;
            _pickUpElapsed = 0.0f;
            _pickUpAnimationSeconds = 0.0f;
            _openingLookPoseStarted = false;
            _openingDownPoseStarted = false;
            _openingTableRestStarted = false;
            _showdownPlayedHand = -1;
            _activeTableGesture = PokerGesture.None;
            _activeTableGestureRemaining = 0.0f;
            _showdownPreparationElapsed = 0.0f;
            _showdownPreparationDuration = 0.0f;
            _showdownClipStarted = false;
            _cardTransfer = 1.0f;
            _peek = 0.0f;

            PlayCardPose(PokerClips.Idle);

            if (Game != null)
                Game.LocalPickedUpCards = false;
        }

        RebuildFan();
        RebuildPresets();
        PlayShowdownOnce();
        SyncTableInteraction(hand, isYourTurn);

        Hud?.Refresh(_options, isYourTurn, RaiseTotal, _lookDone);
    }

    /// <summary>
    /// Turning the cards over at a showdown is not an action anybody takes — it is something the
    /// hand arrives at. Fired on the street CHANGING to showdown rather than on it being showdown,
    /// because Refresh runs on every context and the clip must not restart under itself.
    /// </summary>
    private void PlayShowdownOnce()
    {
        if (Game == null || Player == null || _showdownPlayedHand == Game.HandNumber)
            return;

        var playerId = (string)Player.Name;
        if (!Game.RevealedHoleCards.ContainsKey(playerId))
            return;

        // The seat presenter starts all public Showdown gestures only after the final bet and chip
        // movement have completed. Follow that same seam so the local hands cannot jump ahead.
        if (Game.SeatPresenter != null
            && !Game.SeatPresenter.HasStartedShowdownGesture(playerId))
        {
            return;
        }

        // A hand can reach showdown before the opening pickup (for example everyone all-in from
        // forced bets). Move the same physical pair into the authored FP grip now so it is already
        // in the fingers when the throw begins, instead of teleporting at the release frame.
        if (CardSlots != null)
        {
            _cardsTransferred = true;
            _cardTransfer = 1.0f;
            AttachTransferredCards(Game.SeatPresenter?.TakeLocalCards(CardSlots));
        }

        // Membership in the reveal dictionary is the authoritative edge. Manual reveals arrive
        // after the street changed, while all-in/timeout can add several players in one snapshot.
        _showdownPlayedHand = Game.HandNumber;
        var preparation = Game.SeatPresenter?.ShowdownPreparationFor(playerId) ?? 0.0f;
        BeginShowdownCutscene(preparation);
        ApplyFan();
    }

    /// <summary>
    /// Locks table interaction while leaving the seated camera free. Low cards first blend into the
    /// raised holding pose; cards already being inspected skip that lead-in. The cards, both hands
    /// and the public body use the same preparation duration supplied by PokerSeatPresenter.
    /// </summary>
    private void BeginShowdownCutscene(float preparationSeconds)
    {
        _activeTableGesture = PokerGesture.Reveal;
        _activeTableGestureRemaining = 0.0f;
        _showdownPreparationElapsed = 0.0f;
        _showdownPreparationDuration = Mathf.Max(0.0f, preparationSeconds);
        _showdownClipStarted = false;

        // Clear the private look flag without playing the low idle. Playing it here was the old
        // one-frame flick: Down and Showdown were both requested during the same process frame.
        _voluntaryLookPose = false;
        SetHandCameraModes(
            FirstPersonHandCameraMode.Locked,
            FirstPersonHandCameraMode.Locked);
        SetCardAttachmentMode(PokerCardAttachmentMode.FollowHand);
        SetGuideVisible(false);
        Hud?.SetPanelVisible(false);

        if (_showdownPreparationDuration <= 0.0f)
        {
            StartShowdownClip();
            return;
        }

        SetPeek(0.0f);
        PlayCardPose(PokerClips.LookCards);
    }

    private float StartShowdownClip()
    {
        if (_showdownClipStarted)
            return _activeTableGestureRemaining;

        _showdownClipStarted = true;
        SetPeek(1.0f);
        SetCardAttachmentMode(PokerCardAttachmentMode.FollowHand);
        SetHandCameraModes(
            FirstPersonHandCameraMode.Locked,
            FirstPersonHandCameraMode.Locked);

        var duration = PlayGrossClip(PokerClips.FirstPerson(PokerGesture.Reveal));
        duration = Mathf.Max(duration, _cardHandVisual?.Play(PokerGesture.Reveal) ?? 0.0f);
        _activeTableGestureRemaining = Mathf.Max(
            duration, PokerClips.ShowdownDurationSeconds);
        return _activeTableGestureRemaining;
    }

    public override void SetInteractive(bool interactive)
    {
        _interactionEnabled = interactive;
        UpdateInteractionVisibility();
        if (interactive)
            return;

        CancelPreparedWager(immediate: true);
        Hud?.SetPanelVisible(false);
        _peek = 0.0f;
        ApplyFan();
    }

    public override void SetTopViewActive(bool active)
    {
        // The held cards belong to the first-person seat; overhead they would float in the middle of
        // the table. The HUD stays — it is screen space either way.
        SetHandVisible(!active);
    }

    public override void ShowNotice(string text, float seconds = 2.5f)
    {
        Hud?.ShowNotice(text);
        _messageSeconds = seconds;
    }

    public override void ShowRejection(string reason) => ShowNotice(Describe(reason));

    public override void Clear()
    {
        CancelPreparedWager(immediate: true);
        SetCrosshairVisible(false);
        SetGuideVisible(false);
        Game?.SeatPresenter?.ReleaseLocalCardsFromGrip();
        FinishGesture(_activeTableGesture);
        _fan.Clear();
        _fanTransferFrom.Clear();

        Hud?.SetPanelVisible(false);
    }

    // ---------------------------------------------------------------- acting

    /// <summary>Whether this action is on offer right now.</summary>
    public bool HasAction(PokerActionKind kind)
    {
        if (TableActionsLocked)
            return false;

        foreach (var option in _options)
        {
            if (option.Kind == kind)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The street total this action would commit. A raise takes the number the player has been
    /// adjusting; everything else has exactly one legal amount.
    /// </summary>
    public int TotalFor(PokerActionKind kind)
    {
        if (TableActionsLocked)
            return 0;

        foreach (var option in _options)
        {
            if (option.Kind != kind)
                continue;

            return kind == PokerActionKind.Raise
                ? Mathf.Clamp(RaiseTotal, option.MinTotal, option.MaxTotal)
                : option.MinTotal;
        }

        return 0;
    }

    /// <summary>
    /// All-in is not its own action in the rules — it is a raise for everything, or a call when the
    /// bet already covers the stack. Resolved here so the key means the obvious thing.
    /// </summary>
    public bool TryAllIn(out PokerActionKind kind, out int total)
    {
        if (TableActionsLocked)
        {
            kind = PokerActionKind.None;
            total = 0;
            return false;
        }

        foreach (var option in _options)
        {
            if (option.Kind != PokerActionKind.Raise)
                continue;

            kind = PokerActionKind.Raise;
            total = option.MaxTotal;
            return true;
        }

        foreach (var option in _options)
        {
            if (option.Kind != PokerActionKind.Call)
                continue;

            kind = PokerActionKind.Call;
            total = option.MinTotal;
            return true;
        }

        kind = PokerActionKind.None;
        total = 0;
        return false;
    }

    /// <summary>Asks the controller to send this action. The server validates it again from scratch.</summary>
    public void RequestAction(PokerActionKind kind, int total)
    {
        if (TableActionsLocked)
            return;
        EmitSignal(SignalName.ActionRequested, (int)kind, total);
    }

    /// <summary>Steps the raise to the next legal stop: minimum, half pot, pot, all-in.</summary>
    public void StepRaise(int step)
    {
        if (_presets.Count == 0)
            return;

        var nearest = 0;
        for (var i = 1; i < _presets.Count; i++)
        {
            if (Mathf.Abs(_presets[i] - RaiseTotal) < Mathf.Abs(_presets[nearest] - RaiseTotal))
                nearest = i;
        }

        RaiseTotal = _presets[Mathf.Clamp(nearest + step, 0, _presets.Count - 1)];
        Hud?.Refresh(_options, IsYourTurn, RaiseTotal, _lookDone);
    }

    private void RebuildPresets()
    {
        _presets = new List<int>();

        if (Game == null || Player == null || !HasAction(PokerActionKind.Raise))
        {
            RaiseTotal = 0;
            return;
        }

        var playerId = (string)Player.Name;
        _presets = PokerBetting.RaisePresets(
            Game.BetStateOf(playerId), Game.CurrentBet, Game.MinRaiseIncrement, Game.PotTotal);

        if (_presets.Count == 0)
        {
            RaiseTotal = 0;
            return;
        }

        // Keep the player's chosen size across a repaint when it is still legal, so a raise being
        // re-offered on the next street does not silently snap back to the minimum.
        if (!_presets.Contains(RaiseTotal))
            RaiseTotal = _presets[0];
    }

    // ---------------------------------------------------------------- peeking

    /// <summary>
    /// Turns the cards up while the button is held. Driven by a lerp rather than an animation clip
    /// because it has to follow the hold — a clip would either finish without the player or lag
    /// behind them letting go.
    /// </summary>
    /// <summary>
    /// Taking the dealt pair off the cloth: reach, lift, look, lower.
    ///
    /// It plays ITSELF, once per hand, for every player at the table whether or not it is their
    /// turn — the way everyone at a real table looks at what they were dealt before anything else
    /// happens. Making the player perform it by hand turned the opening of every hand into a chore
    /// and, worse, made the table unplayable for anyone who did not know the gesture.
    /// </summary>
    private void AdvancePickUp(float delta)
    {
        // Never reach for a card still in the air.
        if (!_pickedUp && Game?.SeatPresenter is { LocalHandLanded: false })
            return;

        _pickUpElapsed += delta;

        if (!_pickedUp)
        {
            if (_pickUpElapsed < PickUpDelay)
                return;

            // From here the pair is in hand, so the cloth stops drawing it.
            _pickedUp = true;
            _pickUpElapsed = 0.0f;

            // The complete reach-look-lower cutscene is authored against the table. Head/camera
            // movement must not pull either arm away before it finishes.
            SetHandCameraModes(
                FirstPersonHandCameraMode.Locked,
                FirstPersonHandCameraMode.Locked,
                immediate: true);
            _pickUpAnimationSeconds = PlayGesture(PokerGesture.PickUpCards);
        }

        var lift = Mathf.Max(PickUpLiftSeconds, Mathf.Max(_pickUpAnimationSeconds, 0.01f));
        var look = Mathf.Max(PickUpLookSeconds, 0.0f);
        var settle = Mathf.Max(PickUpSettleSeconds, 0.01f);
        var contact = lift * Mathf.Clamp(PickUpContactFraction, 0.1f, 0.9f);

        if (!_cardsTransferred && _pickUpElapsed >= contact)
            TransferCardsToHand();

        if (_pickUpElapsed < lift)
        {
            var lifting = Smooth(_pickUpElapsed / lift);
            if (_cardsTransferred)
            {
                _cardTransfer = Smooth(
                    (_pickUpElapsed - contact) / Mathf.Max(lift - contact, 0.01f));
            }
            SetPeek(lifting);
            ApplyFan();
        }
        else if (_pickUpElapsed < lift + look)
        {
            if (!_openingLookPoseStarted)
            {
                _openingLookPoseStarted = true;
                PlayCardPose(PokerClips.LookCards);
            }
            SetPeek(1.0f);
        }
        else if (_pickUpElapsed < lift + look + settle)
        {
            if (!_openingDownPoseStarted)
            {
                _openingDownPoseStarted = true;
                PlayCardPose(PokerClips.Idle);
            }
            SetPeek(1.0f - Smooth((_pickUpElapsed - lift - look) / settle));
        }
        else
            FinishPickUp();
    }

    private void FinishPickUp()
    {
        if (!_cardsTransferred)
            TransferCardsToHand();
        _cardTransfer = 1.0f;
        if (!_openingDownPoseStarted)
        {
            _openingDownPoseStarted = true;
            PlayCardPose(PokerClips.Idle);
        }
        SetPeek(0.0f);
        if (!_openingTableRestStarted)
        {
            _openingTableRestStarted = true;
            SetCardAttachmentMode(PokerCardAttachmentMode.TableRest);
        }
        if (_cardAttachmentBlend < 1.0f)
            return;

        _lookDone = true;
        UpdateInteractionVisibility();

        // The panel is redrawn by state signals, and looking at your cards is not one — without this
        // it went on telling somebody already holding them to pick them up.
        Hud?.Refresh(_options, IsYourTurn, RaiseTotal, true);
    }

    private void TransferCardsToHand()
    {
        SetCardAttachmentMode(PokerCardAttachmentMode.FollowHand);
        _cardsTransferred = true;
        if (Game != null)
            Game.LocalPickedUpCards = true;

        AttachTransferredCards(Game?.SeatPresenter?.TakeLocalCards(CardSlots));
        // Configure the real faces at the exact handoff seam. Before contact the same physical nodes
        // remain face down on the cloth; afterwards they interpolate into the authored grip.
        RebuildFan();
        _cardTransfer = 0.0f;
    }

    /// <summary>The voluntary look, once the opening one is done. Holding the button turns them up.</summary>
    private void UpdatePeek(float delta)
    {
        if (_activeTableGesture != PokerGesture.None)
            return;

        var playerId = Player == null ? null : (string)Player.Name;
        var noPrivateCards = playerId != null && Game != null
            && (Game.RevealedHoleCards.ContainsKey(playerId)
                || Game.HasFolded(playerId)
                || Game.HandSettled);
        if (noPrivateCards)
        {
            // Once the physical pair left CardSlots, RMB must not raise empty arms or replicate a
            // ghost IdleSitHoldingCards pose to the other players.
            SetVoluntaryLookPose(false);
            SetPeek(0.0f);
            SetCardAttachmentMode(PokerCardAttachmentMode.TableRest);
            return;
        }

        if (!_lookDone)
        {
            AdvancePickUp(delta);
            return;
        }

        // Deliberately NOT gated on the mouse being captured, unlike every action. That gate exists
        // so the click that recaptures the cursor after Escape cannot commit something; looking at
        // your own cards commits nothing.
        var wantsLook = Input.IsActionPressed(PokerInput.Peek);
        SetVoluntaryLookPose(wantsLook);
        var wants = wantsLook ? 1.0f : 0.0f;
        var response = 1.0f - Mathf.Exp(-PeekSpeed * delta);
        var moved = Mathf.Lerp(_peek, wants, response);

        if (Mathf.Abs(moved - wants) < 0.002f)
            moved = wants;

        SetPeek(moved);
        if (!wantsLook && Mathf.IsZeroApprox(moved))
            SetCardAttachmentMode(PokerCardAttachmentMode.TableRest);
    }

    private void SetPeek(float value)
    {
        if (Mathf.IsEqualApprox(value, _peek))
            return;

        _peek = value;
        ApplyFan();
    }

    private void SetVoluntaryLookPose(bool raised)
    {
        if (_voluntaryLookPose == raised)
            return;

        _voluntaryLookPose = raised;
        if (raised)
            SetCardAttachmentMode(PokerCardAttachmentMode.FollowHand);
        // Cards are authored on the left hand. While voluntarily looking, only that arm follows
        // the camera; the supporting/right hand remains exactly on the table.
        SetHandCameraModes(
            raised
                ? FirstPersonHandCameraMode.FollowCamera
                : FirstPersonHandCameraMode.Locked,
            FirstPersonHandCameraMode.Locked);
        PlayCardPose(raised ? PokerClips.LookCards : PokerClips.Idle);
        Player?.SetPokerCardLook(raised);
    }

    /// <summary>
    /// Central policy seam for every current and future first-person table gesture. Callers choose
    /// each hand independently instead of reimplementing camera parenting at every animation site.
    /// </summary>
    public void SetHandCameraModes(
        FirstPersonHandCameraMode leftMode,
        FirstPersonHandCameraMode rightMode,
        bool immediate = false)
    {
        _leftHandCameraMode = leftMode;
        _rightHandCameraMode = rightMode;

        if (_cardHandVisual is not PlayerFirstPersonHands hands)
            return;

        if (leftMode == FirstPersonHandCameraMode.Locked
            && rightMode == FirstPersonHandCameraMode.Locked)
        {
            hands.LockBothHands(immediate);
        }

        if (GodotObject.IsInstanceValid(Game) && Game.Camera != null)
        {
            hands.ConfigureHandCameraModes(
                _seatController?.StableSeatViewTransform ?? Game.Camera.GlobalTransform,
                Game.Camera,
                leftMode,
                rightMode);
        }
    }

    private float PlayCardPose(string clip) => _cardHandVisual?.PlayClip(clip) ?? 0.0f;

    private static float Smooth(float t)
    {
        t = Mathf.Clamp(t, 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }

    /// <summary>The fan's shape right now, somewhere between lying face down and turned up.</summary>
    public HandFanSpec FanSpec => FanSpecAt(_peek);

    /// <summary>
    /// The fan's shape at an arbitrary point of the peek. Exposed so the scene test can sweep the
    /// whole gesture against the whole pitch range and prove the cards never reach the cloth.
    /// </summary>
    public HandFanSpec FanSpecAt(float peek) =>
        new(FanStepDeg, FanRadius, SelectedLift,
            Mathf.Lerp(RestTiltDeg, PeekTiltDeg, peek), HandFan.LongAxisUpFromMinusZ);

    /// <summary>
    /// Exact artist-authored card pose with only the temporary peek lean added. This keeps each
    /// card independently editable without losing the right-button look animation.
    /// </summary>
    public Transform3D HeldCardPoseAt(int index, float peek)
    {
        var authored = index == 0 ? Card0InHandPose : Card1InHandPose;
        var tiltDelta = Mathf.DegToRad(
            Mathf.Lerp(RestTiltDeg, PeekTiltDeg, Mathf.Clamp(peek, 0.0f, 1.0f))
            - RestTiltDeg);
        var leaned = new Transform3D(
            Basis.FromEuler(new Vector3(tiltDelta, 0.0f, 0.0f)) * authored.Basis,
            authored.Origin);
        return CardsInHandPose * leaned;
    }

    // ---------------------------------------------------------------- drawing

    private void RebuildFan()
    {
        if (CardSlots == null)
            return;

        // Fold/showdown reparent the same nodes back to the table before this refresh reaches the
        // hand. Drop only our references; never hide somebody else's representation.
        for (var i = _fan.Count - 1; i >= 0; i--)
        {
            if (IsInstanceValid(_fan[i]) && _fan[i].GetParent() == CardSlots)
                continue;

            _fan.RemoveAt(i);
            if (i < _fanTransferFrom.Count)
                _fanTransferFrom.RemoveAt(i);
        }

        // The pair is on the cloth until it is picked up, in the muck once it is thrown away, and
        // back on the cloth at a showdown. In all three the seat presenter owns it — keeping a copy
        // in hand as well showed the player their own hand twice, in two places, at different
        // angles, and after a fold left them still holding cards they had just given up.
        var playerId = Player == null ? null : (string)Player.Name;
        var laidDown = !_pickedUp
                       || (playerId != null
                           && Game != null
                           && (Game.RevealedHoleCards.ContainsKey(playerId) || Game.HasFolded(playerId)));

        for (var i = 0; i < _fan.Count; i++)
        {
            var card = _fan[i];
            if (!IsInstanceValid(card))
                continue;
            if (i >= _holeCards.Length || laidDown)
            {
                // SeatPresenter owns the transition out. It may happen later in this frame, so leave
                // the current physical card alone rather than substituting visibility.
                continue;
            }

            var spec = Game?.BoardPresenter?.Spec ?? PokerLayoutSpec.Default;

            if (card.CardId != _holeCards[i] || !card.Visible)
                card.Configure(_holeCards[i], spec);

            card.Visible = true;
        }

        ApplyFan();
    }

    private void AttachTransferredCards(IReadOnlyList<PokerCard> cards)
    {
        _fan.Clear();
        _fanTransferFrom.Clear();
        if (cards == null)
            return;

        // The physical deal can finish one presentation frame before the controller's next HUD
        // refresh. The game already owns this peer's private hand, so use it at the transfer seam
        // instead of showing the public face-down placeholder for a frame (or a whole idle).
        if (_holeCards.Length != PokerDeal.HoleCardCount
            && Game?.LocalHoleCards is { Length: PokerDeal.HoleCardCount } localCards)
        {
            _holeCards = localCards;
        }

        foreach (var card in cards)
        {
            if (!IsInstanceValid(card) || card.GetParent() != CardSlots)
                continue;

            card.Visible = true;
            _fan.Add(card);
            _fanTransferFrom.Add(card.Transform);
        }

        var spec = Game?.BoardPresenter?.Spec ?? PokerLayoutSpec.Default;
        for (var index = 0; index < _fan.Count && index < _holeCards.Length; index++)
            _fan[index].Configure(_holeCards[index], spec);
    }

    private void ApplyFan()
    {
        for (var i = 0; i < _fan.Count && i < _holeCards.Length; i++)
        {
            if (!IsInstanceValid(_fan[i]) || _fan[i].GetParent() != CardSlots)
                continue;
            var target = HeldCardPoseAt(i, _peek);
            _fan[i].Transform = _cardTransfer < 1.0f && i < _fanTransferFrom.Count
                ? _fanTransferFrom[i].InterpolateWith(target, PokerMotion.Smooth(_cardTransfer))
                : target;
        }
    }

    private void FollowImportedCardGrip(float delta)
    {
        if (_cardHandVisual is PlayerFirstPersonHands { CardGrip: not null } hands
            && CardSlots != null)
        {
            if (_cardAttachmentMode == PokerCardAttachmentMode.FollowHand)
            {
                if (_cardAttachmentBlend < 1.0f)
                {
                    hands.UnbindCardSlots(CardSlots);
                    AdvanceCardAttachmentBlend(
                        hands.CardGrip.GlobalTransform, (float)delta);
                    if (_cardAttachmentBlend >= 1.0f)
                        hands.BindCardSlots(CardSlots);
                }
                else
                {
                    // Bind once conceptually (the method is idempotent) instead of copying the
                    // transform only during _Process. PlayerFirstPersonHands also refreshes this
                    // follower from SkeletonUpdated, after the per-arm camera modifier has produced
                    // the rendered pose.
                    hands.BindCardSlots(CardSlots);
                }
            }
            else
            {
                hands.UnbindCardSlots(CardSlots);
                PlaceCardsAtTableRest(hands, (float)delta);
            }
        }
    }

    /// <summary>
    /// One attachment policy for the same physical pair. Pick-up and looking use the animated
    /// left-hand grip; the lowered idle uses a stable marker in the chair frame, so breathing can
    /// move over the cards without making the cards slide across the felt.
    /// </summary>
    private void SetCardAttachmentMode(PokerCardAttachmentMode mode)
    {
        if (_cardAttachmentMode == mode)
            return;

        _cardAttachmentMode = mode;
        if (_cardHandVisual is not PlayerFirstPersonHands hands || CardSlots == null)
            return;

        _cardAttachmentBlendFrom = CardSlots.GlobalTransform;
        _cardAttachmentBlend = 0.0f;
        hands.UnbindCardSlots(CardSlots);
        if (mode == PokerCardAttachmentMode.TableRest)
        {
            ApplyFan();
            PlaceCardsAtTableRest(hands, 0.0f);
        }
    }

    private void PlaceCardsAtTableRest(PlayerFirstPersonHands hands, float delta)
    {
        if (CardSlots == null || CardDownAnchor == null || hands?.CardGrip == null)
            return;

        if (!_cardDownAnchorReady)
            InitializeAutomaticCardDownAnchor(hands);

        if (!_cardDownAnchorReady)
            return;

        FlattenCardDownAnchorToCurrentSurface();

        if (_cardAttachmentBlend < 1.0f)
            AdvanceCardAttachmentBlend(CardDownAnchor.GlobalTransform, delta);
        else
            CardSlots.GlobalTransform = CardDownAnchor.GlobalTransform;

        if (_cardAttachmentBlend >= 1.0f)
            AlignRestingCardsToTable();
    }

    private void AdvanceCardAttachmentBlend(Transform3D target, float delta)
    {
        var seconds = Mathf.Max(CardAttachmentBlendSeconds, 0.0f);
        _cardAttachmentBlend = seconds <= 0.0f
            ? 1.0f
            : Mathf.Min(1.0f, _cardAttachmentBlend + Mathf.Max(delta, 0.0f) / seconds);
        CardSlots.GlobalTransform = _cardAttachmentBlendFrom.InterpolateWith(
            target, Smooth(_cardAttachmentBlend));
    }

    private void AlignRestingCardsToTable()
    {
        var board = Game?.BoardPresenter;
        if (board == null)
            return;

        var thickness = Mathf.Max(board.Spec.CardThickness, 0.0002f);
        var layerSpacing = Mathf.Max(thickness * 1.2f, 0.0008f);
        var rootAligned = false;
        for (var index = 0; index < _fan.Count; index++)
        {
            var card = _fan[index];
            // Fold, reveal and cleanup reparent these same physical nodes back to the presenter.
            // A stale fan reference must never pull a released card back onto this resting plane.
            if (!IsInstanceValid(card) || card.GetParent() != CardSlots)
                continue;

            board.TryGetTableSurface(
                card.GlobalPosition, out var surfacePoint, out var tableNormal);
            var surfaceProjection = surfacePoint.Dot(tableNormal);
            var targetBottom = surfaceProjection
                               + Mathf.Max(CardTableClearance, 0.0f)
                               + index * layerSpacing;
            var currentBottom = card.TryGetVisibleProjectionRange(
                tableNormal, out var visibleBottom, out _)
                ? visibleBottom
                : card.GlobalPosition.Dot(tableNormal) - thickness * 0.5f;
            var correction = tableNormal * (targetBottom - currentBottom);

            if (!rootAligned)
            {
                // Move the stable root with the first card. Besides keeping it tangent now, this
                // means the next look transition begins at the real tabletop instead of at the old
                // BoardHolder height.
                var anchorTransform = CardDownAnchor.GlobalTransform;
                anchorTransform.Origin += correction;
                CardDownAnchor.GlobalTransform = anchorTransform;

                var slotsTransform = CardSlots.GlobalTransform;
                slotsTransform.Origin += correction;
                CardSlots.GlobalTransform = slotsTransform;
                rootAligned = true;
            }
            else
            {
                var transform = card.GlobalTransform;
                transform.Origin += correction;
                card.GlobalTransform = transform;
            }
        }
    }

    /// <summary>
    /// Keeps the authored horizontal direction but takes pitch/roll from the live tabletop. This is
    /// evaluated while resting, so later edits to table height, scale or rotation are inherited
    /// without making the breathing hand drag the cards.
    /// </summary>
    private void FlattenCardDownAnchorToCurrentSurface()
    {
        var board = Game?.BoardPresenter;
        if (board == null || CardDownAnchor == null)
            return;

        var localCard = HeldCardPoseAt(0, 0.0f);
        var currentCard = CardDownAnchor.GlobalTransform * localCard;
        board.TryGetTableSurface(
            currentCard.Origin, out _, out var tableNormal);
        var rootBasis = FlatRootBasis(
            localCard, currentCard, tableNormal, board.GlobalBasis.Orthonormalized());
        CardDownAnchor.GlobalTransform = new Transform3D(
            rootBasis,
            currentCard.Origin - rootBasis * localCard.Origin);
    }

    /// <summary>
    /// With an untouched CardDownAnchor, derive a useful default directly below the authored hand.
    /// Card +Y is its printed face, so pointing it into the cloth leaves the back visible and makes
    /// both cards perfectly parallel to the real table plane. Moving CardDownAnchor in the scene
    /// opts into that authored position instead.
    /// </summary>
    private void InitializeAutomaticCardDownAnchor(PlayerFirstPersonHands hands)
    {
        if (CardDownAnchor == null || hands?.CardGrip == null)
            return;

        hands.UpdateCardGrip();
        var localCard = HeldCardPoseAt(0, 0.0f);
        var currentCard = hands.CardGrip.GlobalTransform * localCard;
        var board = Game?.BoardPresenter;
        var tableBasis = board?.GlobalBasis.Orthonormalized() ?? Basis.Identity;
        var tablePoint = board?.GlobalPosition ?? Vector3.Zero;
        var tableNormal = tableBasis.Y.Normalized();
        board?.TryGetTableSurface(currentCard.Origin, out tablePoint, out tableNormal);
        var rootBasis = FlatRootBasis(localCard, currentCard, tableNormal, tableBasis);

        var cardCentre = currentCard.Origin;
        if (board != null)
        {
            var height = (cardCentre - tablePoint).Dot(tableNormal);
            var cardHalfThickness = Mathf.Max(
                board.Spec.CardThickness, 0.0002f) * 0.5f;
            cardCentre += tableNormal * (
                cardHalfThickness + Mathf.Max(CardTableClearance, 0.0f) - height);
        }

        CardDownAnchor.GlobalTransform = new Transform3D(
            rootBasis,
            cardCentre - rootBasis * localCard.Origin);
        _cardDownAnchorReady = true;
    }

    private Basis FlatRootBasis(
        Transform3D localCard,
        Transform3D currentCard,
        Vector3 tableNormal,
        Basis tableBasis)
    {
        if (tableNormal.IsZeroApprox())
            tableNormal = Vector3.Up;
        tableNormal = tableNormal.Normalized();

        var longAxis = currentCard.Basis.Z;
        longAxis -= tableNormal * longAxis.Dot(tableNormal);
        if (longAxis.LengthSquared() < 1e-6f)
        {
            longAxis = HandRig?.GlobalBasis.Z ?? Vector3.Back;
            longAxis -= tableNormal * longAxis.Dot(tableNormal);
        }
        if (longAxis.LengthSquared() < 1e-6f)
            longAxis = tableBasis.Z;
        longAxis = longAxis.Normalized();

        var faceIntoTable = -tableNormal;
        var widthAxis = faceIntoTable.Cross(longAxis).Normalized();
        longAxis = widthAxis.Cross(faceIntoTable).Normalized();
        var flatCardBasis = new Basis(widthAxis, faceIntoTable, longAxis);
        return (flatCardBasis * localCard.Basis.Inverse()).Orthonormalized();
    }

    private void SetHandVisible(bool visible)
    {
        if (CardSlots != null)
            CardSlots.Visible = visible;

        if (HandRig != null)
            HandRig.Visible = visible;
    }

    /// <summary>Plays a gross rig clip and returns its real duration.</summary>
    private float PlayGrossClip(string clipName)
    {
        if (AnimationPlayer == null || string.IsNullOrWhiteSpace(clipName))
            return 0.0f;

        if (AnimationPlayer.HasAnimation(clipName))
        {
            AnimationPlayer.Play(clipName);
            return (float)(AnimationPlayer.GetAnimation(clipName)?.Length ?? 0.0);
        }

        return 0.0f;
    }

    public float PlayClip(string clipName) =>
        Mathf.Max(PlayGrossClip(clipName), PlayCardPose(clipName));

    /// <summary>
    /// Plays the stable whole-hand motion and, when present, the custom rig's finger animation.
    /// The longest real clip controls the acting state, so no gesture is truncated by a magic timer.
    /// </summary>
    public float PlayGesture(PokerGesture gesture)
    {
        if (gesture == PokerGesture.Reveal)
        {
            BeginShowdownCutscene(0.0f);
            return _activeTableGestureRemaining;
        }

        if (gesture is PokerGesture.ThrowChips or PokerGesture.Knock)
        {
            SetVoluntaryLookPose(false);
            SetHandCameraModes(
                FirstPersonHandCameraMode.Locked,
                FirstPersonHandCameraMode.Locked,
                immediate: true);
            SetPeek(0.0f);
            SetCardAttachmentMode(PokerCardAttachmentMode.TableRest);
            _activeTableGesture = gesture;
        }

        var duration = PlayGrossClip(PokerClips.FirstPerson(gesture));
        var visual = gesture is PokerGesture.ThrowChips or PokerGesture.Knock
            ? _chipHandVisual ?? _cardHandVisual
            : _cardHandVisual;

        duration = Mathf.Max(duration, visual?.Play(gesture) ?? 0.0f);
        return duration;
    }

    /// <summary>Returns an authored one-shot to the stable, table-locked low-card pose.</summary>
    public void FinishGesture(PokerGesture gesture)
    {
        if (gesture == PokerGesture.None || _activeTableGesture != gesture)
            return;

        _activeTableGesture = PokerGesture.None;
        _activeTableGestureRemaining = 0.0f;
        _showdownPreparationElapsed = 0.0f;
        _showdownPreparationDuration = 0.0f;
        _showdownClipStarted = false;
        SetHandCameraModes(
            FirstPersonHandCameraMode.Locked,
            FirstPersonHandCameraMode.Locked,
            immediate: true);
        PlayCardPose(PokerClips.Idle);
        SetPeek(0.0f);
        SetCardAttachmentMode(PokerCardAttachmentMode.TableRest);
    }

    private void AdvanceStandaloneGesture(float delta)
    {
        if (_activeTableGesture != PokerGesture.Reveal)
            return;

        if (!_showdownClipStarted)
        {
            _showdownPreparationElapsed += Mathf.Max(delta, 0.0f);
            var progress = _showdownPreparationDuration <= 0.0f
                ? 1.0f
                : Mathf.Clamp(
                    _showdownPreparationElapsed / _showdownPreparationDuration, 0.0f, 1.0f);
            SetPeek(Smooth(progress));
            if (progress >= 1.0f)
                StartShowdownClip();
            return;
        }

        _activeTableGestureRemaining -= Mathf.Max(delta, 0.0f);
        if (_activeTableGestureRemaining <= 0.0f)
            FinishGesture(PokerGesture.Reveal);
    }

    private void InstallVisualAssets(PokerVisualAssets assets)
    {
        _cardHandVisual = InstallHandVisual(
            assets?.CardHandScene, CardHandVisualMount,
            assets?.CardHandTransform ?? Transform3D.Identity);
        _chipHandVisual = InstallHandVisual(
            assets?.ChipHandScene, ChipHandVisualMount,
            assets?.ChipHandTransform ?? Transform3D.Identity);

        _cardHandVisual?.Play(PokerGesture.None);
        _chipHandVisual?.Play(PokerGesture.None);
    }

    private static PokerHandVisual InstallHandVisual(
        PackedScene scene, Node3D mount, Transform3D localTransform)
    {
        if (scene == null || mount == null || scene.Instantiate() is not Node3D visual)
            return null;

        visual.SetMultiplayerAuthority(mount.GetMultiplayerAuthority());
        mount.AddChild(visual);
        visual.Transform = localTransform;

        return visual as PokerHandVisual;
    }

    /// <summary>Server reason codes, in words the player can act on.</summary>
    private static string Describe(string reason) =>
        reason switch
        {
            "not_your_turn" => "Não é a sua vez",
            "stale_turn" => "A vez já passou",
            "match_not_running" => "A partida não está em andamento",
            "hand_not_running" => "A mão já acabou",
            "invalid_action" => "Ação inválida",
            "action_not_available" => "Essa ação não está disponível agora",
            "amount_out_of_range" => "Valor fora do permitido",
            "no_chips" => "Você não tem fichas para isso",
            "not_in_hand" => "Você não está nesta mão",
            "not_in_match" => "Você não está na partida",
            _ => "Jogada recusada",
        };
}
