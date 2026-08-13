using System.Collections.Generic;
using Godot;
using Poker.Rules;
using ChipBatchPhase = PokerChipAnimator.Phase;

/// <summary>
/// Presents action gestures, showdown diagnostics, seat labels and the dealer marker, and owns
/// the shared seat-relative geometry helpers used by those visuals.
/// </summary>
public partial class PokerSeatPresenter : Node3D
{
    public Label3D PotValueLabel => _potValueLabel;

    public Label3D StackValueLabelOf(string playerId) =>
        !string.IsNullOrEmpty(playerId) && _stackValueLabels.TryGetValue(playerId, out var label)
            ? label
            : null;

    /// <summary>
    /// Replays what somebody just did, on their own seated body.
    ///
    /// Derived from the turn context every peer already holds — the action code and who made it are
    /// both in there — so a gesture costs NO message at all and every peer arrives at the same table
    /// on its own. The turn stamp is the change key, because it moves exactly once per action.
    ///
    /// The body clips are not authored yet, so today this resolves to the seated idle everywhere.
    /// The call site is the point: when they exist, they appear here and nowhere else.
    /// </summary>
    private void PlayActionGesture()
    {
        if (_game.ActionSeq == _lastGestureToken)
            return;

        _lastGestureToken = _game.ActionSeq;

        var gesture = PokerClips.ForAction(_game.LastAction);
        if (gesture == PokerGesture.None || string.IsNullOrEmpty(_game.LastPlayer))
            return;

        if (PlayerRegistry.Instance is { } registry && registry.HasContainer())
            registry.GetPlayerById(_game.LastPlayer)?.PlaySeatedGesture(
                PokerClips.ThirdPerson(gesture), BoardPresenter.GlobalPosition);

        if (gesture == PokerGesture.Knock)
            PlayKnock(_game.LastPlayer);
    }

    /// <summary>A knuckle on the table. Heard by everyone, positioned at the seat that made it.</summary>
    private void PlayKnock(string playerId)
    {
        if (KnockSound == null)
            return;

        _knock ??= NewKnockPlayer();

        var seat = SeatNodeFor(playerId);
        if (seat != null)
            _knock.GlobalPosition = seat.GlobalPosition with { Y = GlobalPosition.Y };

        _knock.Play();
    }

    private AudioStreamPlayer3D NewKnockPlayer()
    {
        var player = new AudioStreamPlayer3D
        {
            Stream = KnockSound,
            VolumeDb = KnockVolumeDb,
            UnitSize = 3.0f,
            MaxDistance = 12.0f,
            MaxPolyphony = 1,
        };

        AddChild(player);
        return player;
    }

    private void HideShowdownSources(IReadOnlyList<string> players)
    {
        for (var index = 0; index < PokerDeal.BoardCount; index++)
        {
            var source = BoardPresenter.BoardCardNodeAt(index);
            if (source != null)
                source.Visible = false;
        }
        foreach (var playerId in players)
        {
            if (!_holeCards.TryGetValue(playerId, out var hand))
                continue;
            foreach (var source in hand.Cards)
            {
                if (IsInstanceValid(source))
                    source.Visible = false;
            }
        }
    }

    /// <summary>Whether the ranked five-card rows have finished reaching their comparison layout.</summary>
    public bool ShowdownPresentationSettled => _showdownPresenter?.Settled ?? false;

    /// <summary>Elapsed portion of the face-up reading beat before ranking begins.</summary>
    public float ShowdownRevealHoldElapsed => _showdownPresenter?.RevealHoldElapsed ?? 0.0f;

    /// <summary>Exposed pairs currently travelling from a local or estimated remote hand pose.</summary>
    public int RevealedHandsInMotion
    {
        get
        {
            var count = 0;
            foreach (var hand in _holeCards.Values)
            {
                if (hand.Revealed && hand.Returning)
                    count++;
            }
            return count;
        }
    }

    public int RemoteRevealedHandsInMotion
    {
        get
        {
            var localId = _game?.Player == null ? null : (string)_game.Player.Name;
            var count = 0;
            foreach (var entry in _holeCards)
            {
                if (entry.Key != localId && entry.Value.Revealed && entry.Value.Returning)
                    count++;
            }
            return count;
        }
    }

    /// <summary>Players in the same best-to-worst order currently shown on the cloth.</summary>
    public IReadOnlyList<string> ShowdownDisplayOrder =>
        _showdownPresenter?.DisplayOrder ?? System.Array.Empty<string>();

    public int ShowdownDisplayedCardCount => _showdownPresenter?.DisplayedCardCount ?? 0;

    public string DebugShowdownState()
    {
        var returning = 0;
        var phases = new Dictionary<ChipBatchPhase, int>();
        foreach (var hand in _holeCards.Values)
        {
            if (hand.Returning)
                returning++;
        }
        foreach (var batch in _chipAnimator.Batches)
            phases[batch.Phase] = phases.GetValueOrDefault(batch.Phase) + 1;
        var moving = new List<string>();
        foreach (var batch in _chipAnimator.Batches)
        {
            if (batch.Phase is ChipBatchPhase.ToBet or ChipBatchPhase.PushingBet
                or ChipBatchPhase.Landing
                or ChipBatchPhase.ToPot or ChipBatchPhase.Organizing or ChipBatchPhase.ToDealer)
                moving.Add($"{batch.Phase}:{batch.Progress:F2}/{batch.Delay:F2}");
        }

        return $"active={_showdownPresenter?.Active} settled={_showdownPresenter?.Settled} "
            + $"hand={_game?.HandNumber} result={_game?.HandSettled} "
            + $"reveals={_game?.RevealedHoleCards.Count} board={BoardPresenter?.Settled} "
            + $"pending={_pendingChipActions.Count} collect={_collecting}/{_organizing}/{_collectionRequested} "
            + $"returning={returning} visible={_visibleStreet} requested={_requestedStreet} "
            + $"phases={string.Join(",", phases)} moving={string.Join(",", moving)}";
    }

    private Transform3D ShowdownSourceTransform(string playerId, int cardId)
    {
        if (_holeCards.TryGetValue(playerId, out var hand))
        {
            foreach (var card in hand.Cards)
            {
                if (IsInstanceValid(card) && card.CardId == cardId)
                    return GlobalTransform.AffineInverse() * card.GlobalTransform;
            }
        }

        var boardIndex = System.Array.IndexOf(_game.Board, cardId);
        var boardCard = BoardPresenter.BoardCardNodeAt(boardIndex);
        if (boardCard != null)
            return GlobalTransform.AffineInverse() * boardCard.GlobalTransform;

        return new Transform3D(PokerCard.Orientation(false), BoardPresenter.DeckPosition);
    }

    private void RefreshName(string playerId, Vector2 facing)
    {
        // Your own plate would hang in the middle of your own view, telling you your own name. You
        // know who you are; what you need to read is everyone else.
        if (_game.Player != null && playerId == (string)_game.Player.Name)
        {
            if (_names.TryGetValue(playerId, out var own))
                own.Hide();

            return;
        }

        if (!_names.TryGetValue(playerId, out var label))
        {
            label = new Label3D
            {
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = false,
                // Small: these sit barely a metre from a seated eye, and the first render came out
                // with a name taller than the table.
                PixelSize = 0.00022f,
                FontSize = 64,
                OutlineSize = 10,
            };

            AddChild(label);
            _names[playerId] = label;
        }

        label.Show();

        var isTurn = _game.IsMatchActive && _game.IsTurnOwner(playerId);
        var folded = _game.HasFolded(playerId);
        var place = PokerTableLayout.SeatSpot(facing, BoardPresenter.Spec.SeatStackRadius + 0.06f);

        var suffix = _game.IsAllIn(playerId) ? " · all-in" : folded ? " · fora" : "";
        label.Text = $"{NameOf(playerId)}{suffix}";
        label.Modulate = folded ? FoldedColor : isTurn ? TurnColor : IdleColor;
        label.Position = new Vector3(place.X, NameHeight, place.Y);
    }

    /// <summary>
    /// Writes the pot value directly on the felt. It uses the local reader frame so the inscription
    /// is upright from every chair without turning into a camera-facing HUD element.
    /// </summary>
    private void RefreshPotValue()
    {
        if (BoardPresenter == null)
            return;

        _potValueLabel ??= BuildFlatPotLabel();
        _potValueLabel.Visible = _game?.IsMatchActive == true;
        if (!_potValueLabel.Visible)
            return;

        var facing = BoardPresenter.ReaderFacing.Normalized();
        var boardPlace = BoardPresenter.PotPosition
                         + new Vector3(facing.X, 0.0f, facing.Y) * PotValueLabelOffset;
        var place = ToLocal(BoardPresenter.ToGlobal(boardPlace));
        _potValueLabel.Text = $"POTE {_game.PotTotal}";
        var boardBasis = ReaderTableLabelBasis(facing);
        var localBasis = GlobalTransform.Basis.Inverse()
                         * BoardPresenter.GlobalTransform.Basis * boardBasis;
        _potValueLabel.Transform = new Transform3D(
            localBasis, new Vector3(place.X, 0.0042f, place.Z));
    }

    public static Basis ReaderTableLabelBasis(Vector2 facing) =>
        Basis.FromEuler(new Vector3(
            0.0f, PokerTableLayout.YawTowardCentre(facing.Normalized()), 0.0f))
        * new Basis(Vector3.Right, -Mathf.Pi * 0.5f);

    /// <summary>
    /// Floats a physical count above every denomination bank. It stays attached to that player's
    /// chips, while the billboard makes it readable from whichever peer is currently looking.
    /// </summary>
    private void RefreshStackValue(string playerId, Vector2 facing, PokerLayoutSpec spec)
    {
        if (!_stackValueLabels.TryGetValue(playerId, out var label))
        {
            label = BuildTableChalkLabel($"StackValue{playerId}");
            _stackValueLabels[playerId] = label;
        }

        label.Visible = _game?.IsMatchActive == true;
        if (!label.Visible)
            return;

        var place = StackPlace(facing, spec);
        label.Text = $"FICHAS {VisibleStackValue(playerId)}";
        _stacks.TryGetValue(playerId, out var pile);
        SetFloatingLabelAnchor(label,
            new Vector3(place.X, FloatingLabelHeight(pile), place.Y));
    }

    private int VisibleStackValue(string playerId)
    {
        var prepared = playerId == _preparedPlayerId ? PreparedWagerAmount : 0;
        foreach (var wager in _replicatedPrepared.Values)
        {
            if (wager.PlayerId == playerId)
                prepared += wager.Amount;
        }

        // The authoritative stack already excludes a confirmed action. Tentative actors are the
        // only chips that still need subtracting so the writing agrees with the physical bank.
        return Mathf.Max(0, DisplayedStackOf(playerId) - prepared);
    }

    private Label3D BuildTableChalkLabel(string name)
    {
        var label = new Label3D
        {
            Name = name,
            Font = ChalkFont,
            PixelSize = ChalkValuePixelSize,
            FontSize = ChalkValueFontSize,
            OutlineSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            // Only yaw follows the camera. Keeping world-up prevents the label from copying the
            // camera pitch and looking like a flat HUD element pasted over the table.
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            // Warm room lighting tinted the supposedly white value beige. The face remains
            // unshaded white; its outline, world perspective and cast shadow keep the 3D presence.
            Shaded = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            Modulate = FloatingValueLabelColor,
            OutlineModulate = new Color(0.10f, 0.055f, 0.025f, 0.90f),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            NoDepthTest = false,
        };
        AddChild(label);
        return label;
    }

    private Label3D BuildFlatPotLabel()
    {
        var label = BuildTableChalkLabel("PotValue");
        label.Billboard = BaseMaterial3D.BillboardModeEnum.Disabled;
        label.Shaded = false;
        label.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        label.Modulate = ChalkValueColor;
        label.OutlineSize = 2;
        label.OutlineModulate = new Color(0.23f, 0.12f, 0.07f, 0.22f);
        return label;
    }

    private float FloatingLabelHeight(PokerChipPile pile) => Mathf.Max(
        FloatingValueLabelMinimumHeight,
        (pile?.TopHeight ?? 0.0f) + FloatingValueLabelClearance);

    private void SetFloatingLabelAnchor(Label3D label, Vector3 anchor)
    {
        if (label == null)
            return;

        _floatingValueLabelAnchors[label] = anchor;
        label.Position = anchor + Vector3.Up * CurrentFloatingLabelOffset();
    }

    private void AdvanceFloatingValueLabels(float delta)
    {
        _floatingValueLabelTime += Mathf.Max(0.0f, delta);
        foreach (var entry in _floatingValueLabelAnchors)
        {
            var label = entry.Key;
            if (!GodotObject.IsInstanceValid(label) || !label.Visible)
                continue;

            label.Position = entry.Value + Vector3.Up * CurrentFloatingLabelOffset();
        }
    }

    private float CurrentFloatingLabelOffset() => FloatingValueBobOffset(
        _floatingValueLabelTime,
        0.0f,
        FloatingValueLabelBobDistance,
        FloatingValueLabelBobSeconds);

    /// <summary>A soft rest-to-down-to-rest loop shared by every floating value label.</summary>
    public static float FloatingValueBobOffset(
        float elapsed, float phase, float distance, float seconds)
    {
        var duration = Mathf.Max(seconds, 0.01f);
        var progress = Mathf.PosMod(elapsed / duration + phase, 1.0f);
        return -Mathf.Abs(distance) * 0.5f * (1.0f - Mathf.Cos(Mathf.Tau * progress));
    }

    private void RefreshDealerLabel()
    {
        if (_game.SeatOrder.Length == 0 || _game.ButtonSeat < 0
            || _game.ButtonSeat >= _game.SeatOrder.Length)
        {
            _dealerLabel?.Hide();
            return;
        }

        var dealerId = _game.SeatOrder[_game.ButtonSeat];
        var player = PlayerRegistry.Instance?.GetPlayerById(dealerId);
        var anchor = player?.HeadPivot as Node3D ?? SeatNodeFor(dealerId);
        if (anchor == null)
        {
            _dealerLabel?.Hide();
            return;
        }

        _dealerLabel ??= BuildDealerLabel();
        _dealerLabel.Text = "DEALER";
        _dealerLabel.GlobalPosition = anchor.GlobalPosition + Vector3.Up * DealerLabelHeightAboveHead;
        _dealerLabel.Show();
    }

    private Label3D BuildDealerLabel()
    {
        var label = new Label3D
        {
            Name = "DealerHeadLabel",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            PixelSize = 0.00024f,
            FontSize = 56,
            OutlineSize = 10,
            Modulate = new Color(1.0f, 0.82f, 0.30f),
            NoDepthTest = false,
        };
        AddChild(label);
        return label;
    }

    private PokerChipPile PileFor(
        Dictionary<string, PokerChipPile> into, string playerId, float scatter, bool settles)
    {
        if (into.TryGetValue(playerId, out var pile))
            return pile;

        pile = new PokerChipPile
        {
            ChipScene = _game?.ChipScene,
            Scatter = scatter,
            StableRunColumns = true,
            StackSpacing = Mathf.Max(BankColumnSpacing, 0.044f),
            Spread = 0.0f,
            SettleSeconds = settles ? 0.24f : 0.0f,
            SettleHeight = 0.018f,
        };

        AddChild(pile);
        into[playerId] = pile;

        return pile;
    }

    /// <summary>
    /// Which way up a card on the cloth is drawn, so it reads from THIS peer's chair. Falls back to
    /// the card's own seat when the local player has none — a spectator has nowhere to read from.
    /// </summary>
    private float ReaderYaw(Vector2 fallbackFacing)
    {
        if (BoardPresenter == null)
            return PokerTableLayout.YawTowardCentre(fallbackFacing);

        var boardFacing = BoardPresenter.ReaderFacing;
        var worldFacing = BoardPresenter.GlobalTransform.Basis
                          * new Vector3(boardFacing.X, 0.0f, boardFacing.Y);
        var localFacing = GlobalTransform.Basis.Inverse() * worldFacing;
        var reader = new Vector2(localFacing.X, localFacing.Z);

        return reader.LengthSquared() < 1e-6f
            ? PokerTableLayout.YawTowardCentre(fallbackFacing)
            : PokerTableLayout.YawTowardCentre(reader.Normalized());
    }

    private Node3D SeatNodeFor(string playerId)
    {
        var index = _game.SeatIndexFor(playerId);
        if (index < 0 || index >= Seats.GetChildCount())
            return null;

        return Seats.GetChild(index) as Node3D;
    }

    /// <summary>Frees what belongs to somebody who is no longer at the table.</summary>
    private void DropStale(HashSet<string> seen)
    {
        Drop(_holeCards, seen, hand =>
        {
            foreach (var card in hand.Cards)
            {
                if (IsInstanceValid(card))
                    card.QueueFree();
            }
        });

        Drop(_stacks, seen, pile => pile.QueueFree());
        Drop(_names, seen, label => label.QueueFree());
        Drop(_stackValueLabels, seen, label =>
        {
            _floatingValueLabelAnchors.Remove(label);
            label.QueueFree();
        });
    }

    private static void Drop<T>(Dictionary<string, T> from, HashSet<string> seen, System.Action<T> free)
    {
        var stale = new List<string>();
        foreach (var entry in from)
        {
            if (!seen.Contains(entry.Key))
                stale.Add(entry.Key);
        }

        foreach (var playerId in stale)
        {
            free(from[playerId]);
            from.Remove(playerId);
        }
    }

    private static string NameOf(string playerId)
    {
        var player = PlayerRegistry.Instance?.GetPlayerById(playerId);

        return player != null && !string.IsNullOrWhiteSpace(player.Nickname)
            ? player.Nickname
            : $"Jogador {playerId}";
    }
}
