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
            registry.GetPlayerById(_game.LastPlayer)?.PlaySeatedGesture(PokerClips.ThirdPerson(gesture));

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
            UnitSize = 3.0f,
            MaxDistance = 12.0f,
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
                source.Visible = false;
        }
    }

    /// <summary>Whether the ranked five-card rows have finished reaching their comparison layout.</summary>
    public bool ShowdownPresentationSettled => _showdownPresenter?.Settled ?? false;

    /// <summary>Elapsed portion of the face-up reading beat before ranking begins.</summary>
    public float ShowdownRevealHoldElapsed => _showdownPresenter?.RevealHoldElapsed ?? 0.0f;

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
            if (batch.Phase is ChipBatchPhase.ToBet or ChipBatchPhase.Landing
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
                if (card != null && card.CardId == cardId)
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

        // Name and count together: with no panel anywhere, this is the only place a stack is
        // written down, and it has to be legible from the chair opposite.
        var suffix = _game.IsAllIn(playerId) ? " · all-in" : folded ? " · fora" : "";
        // Rules credit the award immediately, but the table count follows the physical chips so the
        // number does not jump before the pot has visibly reached its owner.
        label.Text = $"{NameOf(playerId)}\n{DisplayedStackOf(playerId)}{suffix}";
        label.Modulate = folded ? FoldedColor : isTurn ? TurnColor : IdleColor;
        label.Position = new Vector3(place.X, NameHeight, place.Y);
    }

    private void RefreshDealerButton(PokerLayoutSpec spec)
    {
        if (_game.SeatOrder.Length == 0 || _game.ButtonSeat >= _game.SeatOrder.Length)
        {
            if (_dealerButton != null)
                _dealerButton.Visible = false;

            return;
        }

        _dealerButton ??= BuildDealerButton();

        var seat = SeatNodeFor(_game.SeatOrder[_game.ButtonSeat]);
        if (seat == null)
        {
            _dealerButton.Visible = false;
            return;
        }

        var toSeat = ToLocal(seat.GlobalPosition);
        var facing = new Vector2(toSeat.X, toSeat.Z);
        if (facing.LengthSquared() < 1e-6f)
            return;

        facing = facing.Normalized();

        // Beside the seat's own things rather than in front of them, so it never covers a card.
        var across = new Vector2(-facing.Y, facing.X);
        var place = facing * (spec.SeatBetRadius + 0.02f) + across * ButtonOffset;

        _dealerButton.Position = new Vector3(place.X, 0.004f, place.Y);
        _dealerButton.Visible = true;
    }

    private MeshInstance3D BuildDealerButton()
    {
        var button = new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = ButtonRadius,
                BottomRadius = ButtonRadius,
                Height = 0.006f,
                RadialSegments = 20,
                Rings = 1,
            },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.96f, 0.95f, 0.90f),
                Roughness = 0.5f,
            },
        };

        AddChild(button);

        var label = new Label3D
        {
            Text = "D",
            Billboard = BaseMaterial3D.BillboardModeEnum.Disabled,
            PixelSize = 0.0003f,
            FontSize = 64,
            OutlineSize = 0,
            Modulate = new Color(0.15f, 0.15f, 0.18f),
            Position = new Vector3(0.0f, 0.004f, 0.0f),
            Rotation = new Vector3(-Mathf.Pi * 0.5f, 0.0f, 0.0f),
        };

        button.AddChild(label);
        return button;
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
            StackSpacing = BankColumnSpacing,
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
        var playerId = _game.Player == null ? null : (string)_game.Player.Name;
        var seat = playerId == null ? null : SeatNodeFor(playerId);

        if (seat == null)
            return PokerTableLayout.YawTowardCentre(fallbackFacing);

        var toSeat = ToLocal(seat.GlobalPosition);
        var reader = new Vector2(toSeat.X, toSeat.Z);

        return reader.LengthSquared() < 1e-6f
            ? PokerTableLayout.YawTowardCentre(fallbackFacing)
            : PokerTableLayout.YawTowardCentre(reader.Normalized());
    }

    private Node3D SeatNodeFor(string playerId)
    {
        var index = _game.TurnOrder.IndexOf(playerId);
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
                card?.QueueFree();
        });

        Drop(_stacks, seen, pile => pile.QueueFree());
        Drop(_names, seen, label => label.QueueFree());
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
