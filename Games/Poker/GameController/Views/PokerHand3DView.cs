using System.Collections.Generic;
using Godot;
using Poker.Rules;

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
/// Nothing here talks to the network. It emits one intent and the controller forwards it.
/// </summary>
[GlobalClass]
public partial class PokerHand3DView : PokerHandView
{
    [Export] public Node3D HandRig;
    [Export] public Node3D CardHandPose;
    [Export] public Node3D CardSlots;
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

    [ExportGroup("Taking the cards up")]
    /// <summary>Beat before reaching down, so the deal is seen to finish before it is disturbed.</summary>
    [Export] public float PickUpDelay = 0.35f;

    /// <summary>Lifting the pair off the cloth and turning it up.</summary>
    [Export] public float PickUpLiftSeconds = 0.55f;

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
    private PokerStreet _lastStreet = PokerStreet.Preflop;
    private int _lastHand = -1;
    private bool _pickedUp;
    private bool _lookDone;
    private float _pickUpElapsed;
    private float _cardTransfer = 1.0f;
    private PokerHandVisual _cardHandVisual;
    private PokerHandVisual _chipHandVisual;
    private int _publishedWagerTurn = -1;
    private int _publishedWagerRevision;

    // ---------------------------------------------------------------- what the states drive

    public bool IsYourTurn { get; private set; }

    public IReadOnlyList<ActionOption> Options => _options;

    public bool HasAnyAction => _options.Count > 0;

    /// <summary>Legacy preset total retained for compatibility; physical chip selection is primary.</summary>
    public int RaiseTotal { get; private set; }

    /// <summary>How far the cards are turned up, 0 at rest and 1 fully peeked.</summary>
    public float PeekAmount => _peek;

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

        // A hand only exists for the peer holding it.
        if (IsMultiplayerAuthority())
            return;

        Hide();
        SetProcess(false);
    }

    public override void Setup(PokerGame game, Player player)
    {
        base.Setup(game, player);

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

    public override void _Process(double delta)
    {
        if (!IsMultiplayerAuthority() || Game?.Camera == null)
            return;

        UpdatePeek((float)delta);
        AdvanceCallHold((float)delta);
        AdvanceAutomaticWager();
        UpdateInteractionVisibility();
        AdvanceCallLabelCycle((float)delta);

        // Read in _Process rather than _PhysicsProcess so the hand does not swim a frame behind the
        // RemoteTransform3D that drives the camera.
        if (HandRig != null)
            HandRig.GlobalTransform = Game.Camera.GlobalTransform * HandPose;

        FollowImportedCardGrip();

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
            _fan.Clear();
            _fanTransferFrom.Clear();
            _lastHand = hand;
            _pickedUp = false;
            _lookDone = false;
            _pickUpElapsed = 0.0f;
            _cardTransfer = 1.0f;
            _peek = 0.0f;

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
        var street = Game?.Street ?? PokerStreet.Preflop;
        if (street == _lastStreet)
            return;

        _lastStreet = street;

        if (street != PokerStreet.Showdown || Player == null)
            return;

        // Only the players who actually had to show turn their cards up.
        if (Game.RevealedHoleCards.ContainsKey((string)Player.Name))
            PlayGesture(PokerGesture.Reveal);
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
        _fan.Clear();
        _fanTransferFrom.Clear();

        Hud?.SetPanelVisible(false);
    }

    // ---------------------------------------------------------------- acting

    /// <summary>Whether this action is on offer right now.</summary>
    public bool HasAction(PokerActionKind kind)
    {
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
    public void RequestAction(PokerActionKind kind, int total) =>
        EmitSignal(SignalName.ActionRequested, (int)kind, total);

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

            if (Game != null)
                Game.LocalPickedUpCards = true;

            AttachTransferredCards(Game?.SeatPresenter?.TakeLocalCards(CardSlots));
            // The table owns face-down placeholders until this peer picks them up. Once the same nodes
            // reach the private grip, configure their real faces before the fan starts turning toward the
            // eye; otherwise the placeholder's blank front is what the player sees for one whole hand.
            RebuildFan();
            _cardTransfer = 0.0f;
            PlayGesture(PokerGesture.PickUpCards);
        }

        var lift = Mathf.Max(PickUpLiftSeconds, 0.01f);
        var look = Mathf.Max(PickUpLookSeconds, 0.0f);
        var settle = Mathf.Max(PickUpSettleSeconds, 0.01f);

        if (_pickUpElapsed < lift)
        {
            var lifting = Smooth(_pickUpElapsed / lift);
            _cardTransfer = lifting;
            SetPeek(lifting);
            ApplyFan();
        }
        else if (_pickUpElapsed < lift + look)
            SetPeek(1.0f);
        else if (_pickUpElapsed < lift + look + settle)
            SetPeek(1.0f - Smooth((_pickUpElapsed - lift - look) / settle));
        else
            FinishPickUp();
    }

    private void FinishPickUp()
    {
        _cardTransfer = 1.0f;
        SetPeek(0.0f);
        _lookDone = true;
        UpdateInteractionVisibility();

        // The panel is redrawn by state signals, and looking at your cards is not one — without this
        // it went on telling somebody already holding them to pick them up.
        Hud?.Refresh(_options, IsYourTurn, RaiseTotal, true);
    }

    /// <summary>The voluntary look, once the opening one is done. Holding the button turns them up.</summary>
    private void UpdatePeek(float delta)
    {
        if (!_lookDone)
        {
            AdvancePickUp(delta);
            return;
        }

        // Deliberately NOT gated on the mouse being captured, unlike every action. That gate exists
        // so the click that recaptures the cursor after Escape cannot commit something; looking at
        // your own cards commits nothing.
        var wants = Input.IsActionPressed(PokerInput.Peek) ? 1.0f : 0.0f;
        var response = 1.0f - Mathf.Exp(-PeekSpeed * delta);
        var moved = Mathf.Lerp(_peek, wants, response);

        if (Mathf.Abs(moved - wants) < 0.002f)
            moved = wants;

        SetPeek(moved);
    }

    private void SetPeek(float value)
    {
        if (Mathf.IsEqualApprox(value, _peek))
            return;

        _peek = value;
        if (CardHandPose != null)
        {
            // The replaceable hand gets a small pose correction while the independently fanned cards
            // turn toward the eye. No track needs to reference the imported skeleton.
            CardHandPose.Position = new Vector3(0.0f, 0.004f * _peek, -0.012f * _peek);
            CardHandPose.Rotation = new Vector3(-0.10f * _peek, 0.0f, 0.0f);
        }
        ApplyFan();
    }

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
            if (!IsInstanceValid(_fan[i]))
                continue;
            var target = HeldCardPoseAt(i, _peek);
            _fan[i].Transform = _cardTransfer < 1.0f && i < _fanTransferFrom.Count
                ? _fanTransferFrom[i].InterpolateWith(target, PokerMotion.Smooth(_cardTransfer))
                : target;
        }
    }

    private void FollowImportedCardGrip()
    {
        if (_cardHandVisual is PlayerFirstPersonHands { CardGrip: not null } hands
            && CardSlots != null)
        {
            hands.UpdateCardGrip();
            CardSlots.GlobalTransform = hands.CardGrip.GlobalTransform;
        }
    }

    private void SetHandVisible(bool visible)
    {
        if (CardSlots != null)
            CardSlots.Visible = visible;

        if (HandRig != null)
            HandRig.Visible = visible;
    }

    /// <summary>Plays a gross rig clip and returns its real duration.</summary>
    public float PlayClip(string clipName)
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

    /// <summary>
    /// Plays the stable whole-hand motion and, when present, the custom rig's finger animation.
    /// The longest real clip controls the acting state, so no gesture is truncated by a magic timer.
    /// </summary>
    public float PlayGesture(PokerGesture gesture)
    {
        var duration = PlayClip(PokerClips.FirstPerson(gesture));
        var visual = gesture is PokerGesture.ThrowChips or PokerGesture.Knock
            ? _chipHandVisual
            : _cardHandVisual;

        return Mathf.Max(duration, visual?.Play(gesture) ?? 0.0f);
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
