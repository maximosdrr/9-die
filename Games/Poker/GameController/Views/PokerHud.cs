using System.Collections.Generic;
using Godot;
using Poker.Rules;

/// <summary>
/// The corner panel: whose turn it is, what this player may do, and the key for each.
///
/// It decides nothing. The legal action list is built by <see cref="PokerBetting.LegalActions"/> in
/// the controller and handed here already filtered, so a row can only appear for something the
/// server would accept. The rule is the one the pool HUD established: a row is visible if and only
/// if it is your turn AND the action is on the list — recomputed from scratch on every refresh,
/// never toggled incrementally.
///
/// Everything is <c>MouseFilter.Ignore</c>. The mouse is captured for the whole match, and a Control
/// that swallowed a click would break the recapture that <see cref="SeatedTableController"/> does
/// after Escape.
/// </summary>
[GlobalClass]
public partial class PokerHud : CanvasLayer
{
    [Export] public Control Root;
    [Export] public Label TurnLabel;
    [Export] public Label StakesLabel;
    [Export] public Label PreparedWagerLabel;
    [Export] public VBoxContainer ActionList;

    /// <summary>How the last hand ended, and why. Takes over from the actions between hands.</summary>
    [Export] public Label ResultLabel;

    [Export] public Label HintsLabel;

    [ExportGroup("Showdown announcement")]
    [Export] public Control ShowdownAnnouncement;
    [Export] public Label ShowdownTitle;
    [Export] public Label ShowdownPrompt;
    [Export] public AudioStreamPlayer ShowdownBell;

    /// <summary>Rows are built once and reused; rebuilding them per refresh would flicker.</summary>
    private readonly Dictionary<PokerActionKind, ActionRow> _rows = new();
    private ActionRow _showdownRow;

    private PokerGame _game;
    private Player _player;
    private Tween _showdownTween;
    private bool _showdownWasActive;
    private int _announcedShowdownHand = -1;

    private sealed class ActionRow
    {
        public HBoxContainer Box;
        public Label Text;
        public Label Key;
    }

    /// <summary>Order top to bottom, cheapest decision first and the irreversible one last.</summary>
    // Check, call, raise and fold now live on the physical table. Only the all-in shortcut and the
    // showdown reveal remain screen-space actions.
    private static readonly (PokerActionKind Kind, string Key)[] Layout =
        System.Array.Empty<(PokerActionKind, string)>();

    public override void _Ready()
    {
        BuildRows();
        SetPanelVisible(false);

        if (HintsLabel != null)
        {
            HintsLabel.Text = "Segure o botão direito para olhar suas cartas"
                              + " · clique nas fichas e depois em CONFIRMAR APOSTA"
                              + "\nMire e clique em PASSAR/DESISTIR"
                              + " · T vista de cima · E levantar · Q (segurar) sair";
        }
    }

    public override void _ExitTree()
    {
        _showdownTween?.Kill();
        _showdownTween = null;
        if (ShowdownBell != null)
        {
            ShowdownBell.Stop();
            // MP3 playback owns a native decoder. Releasing the stream explicitly keeps short-lived
            // table/test scenes from retaining that decoder until the engine itself shuts down.
            ShowdownBell.Stream = null;
        }
    }

    public void Setup(PokerGame game, Player player)
    {
        _game = game;
        _player = player;
    }

    public void SetPanelVisible(bool visible)
    {
        if (Root != null)
            Root.Visible = visible;
    }

    /// <summary>
    /// Redraws from the legal action list. <paramref name="raiseTotal"/> is the size the player has
    /// been adjusting with A/D — shown on the raise row, because with no confirmation step it is the
    /// only place they can read what R is about to cost them.
    /// </summary>
    public void Refresh(
        IReadOnlyList<ActionOption> options, bool isYourTurn, int raiseTotal, bool pickedUpCards)
    {
        if (_game == null || _player == null)
            return;

        var playerId = (string)_player.Name;
        var inMatch = _game.IsMatchActive;
        var showdownWaiting = _game.ShowdownWaiting;

        SetPanelVisible(inMatch);
        if (!inMatch)
        {
            ResetShowdownAnnouncement();
            return;
        }

        UpdateShowdownAnnouncement(showdownWaiting);

        // Nothing may be decided until the opening look has run. It plays itself, so the panel
        // reports what is happening rather than instructing — and it lists no action, because one
        // offered now would only be refused.
        var waitingForPickUp = !showdownWaiting && _game.HandNumber > 0
            && !pickedUpCards && !_game.HandSettled;

        if (TurnLabel != null)
        {
            TurnLabel.Text = showdownWaiting
                ? "SHOWDOWN"
                : waitingForPickUp
                ? "OLHANDO AS CARTAS…"
                : isYourTurn ? "SUA VEZ" : "Aguardando…";

            TurnLabel.ThemeTypeVariation = isYourTurn || waitingForPickUp || _game.LocalMustReveal
                ? "YourTurnLabel"
                : "HudLabelSmall";
        }

        if (waitingForPickUp || showdownWaiting)
            isYourTurn = false;

        if (StakesLabel != null)
        {
            var prepared = _game.SeatPresenter?.PreparedWagerAmount ?? 0;
            StakesLabel.Text = $"Pote {_game.PotTotal}   ·   Suas fichas {Mathf.Max(0, _game.StackOf(playerId) - prepared)}"
                               + $"\n{StreetName(_game.Street)}   ·   blinds {_game.ActiveSmallBlind}/{_game.ActiveBigBlind}";

            if (PreparedWagerLabel != null)
            {
                PreparedWagerLabel.Visible = prepared > 0;
                PreparedWagerLabel.Text = prepared > 0 ? $"Aposta selecionada: {prepared}" : "";
            }
        }

        // Between hands the actions are gone and what matters is what just happened, so the panel
        // says that instead of sitting empty.
        var result = showdownWaiting ? DescribeShowdownPrompt() : DescribeResult();
        if (ResultLabel != null)
        {
            ResultLabel.Visible = result.Length > 0;
            ResultLabel.Text = result;
        }

        var allInTotal = AllInTotal(options);

        foreach (var entry in Layout)
        {
            if (!_rows.TryGetValue(entry.Kind, out var row))
                continue;

            var option = Find(options, entry.Kind);
            var offered = isYourTurn && option.HasValue;

            // The all-in row is the raise row's twin: it only earns its own line when raising all
            // the way is a different decision from the raise being shown.
            row.Box.Visible = offered;
            if (!offered)
                continue;

            row.Text.Text = Describe(entry.Kind, option.Value, playerId, raiseTotal);
            row.Key.Text = entry.Key;
        }

        if (_rows.TryGetValue(PokerActionKind.None, out var allInRow))
        {
            var separate = isYourTurn && allInTotal > 0;
            allInRow.Box.Visible = separate;
            if (separate)
            {
                allInRow.Text.Text = $"All-in {allInTotal}";
                allInRow.Key.Text = PokerInput.AllInKey;
            }
        }

        if (_showdownRow != null)
        {
            _showdownRow.Box.Visible = _game.LocalMustReveal;
            if (_showdownRow.Box.Visible)
            {
                _showdownRow.Text.Text = "Mostrar cartas";
                _showdownRow.Key.Text = PokerInput.ShowdownRevealKey;
            }
        }
    }

    private void UpdateShowdownAnnouncement(bool showdownWaiting)
    {
        if (!showdownWaiting)
        {
            _showdownWasActive = false;
            return;
        }

        if (_showdownWasActive && _announcedShowdownHand == _game.HandNumber)
            return;

        _showdownWasActive = true;
        _announcedShowdownHand = _game.HandNumber;

        if (ShowdownAnnouncement == null)
            return;

        if (ShowdownTitle != null)
            ShowdownTitle.Text = "SHOWDOWN";
        if (ShowdownPrompt != null)
        {
            ShowdownPrompt.Text = _game.LocalMustReveal
                ? $"MOSTRE SUAS CARTAS   [ {PokerInput.ShowdownRevealKey} ]"
                : "AS CARTAS VÃO À MESA";
        }

        _showdownTween?.Kill();
        ShowdownAnnouncement.Visible = true;
        ShowdownAnnouncement.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f);
        ShowdownAnnouncement.Scale = new Vector2(0.88f, 0.88f);

        _showdownTween = CreateTween();
        _showdownTween.SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        _showdownTween.TweenProperty(
            ShowdownAnnouncement, "modulate", Colors.White, 0.28f);
        _showdownTween.Parallel().TweenProperty(
            ShowdownAnnouncement, "scale", Vector2.One, 0.36f);
        _showdownTween.SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        _showdownTween.TweenInterval(2.25f);
        _showdownTween.TweenProperty(
            ShowdownAnnouncement, "modulate", new Color(1.0f, 1.0f, 1.0f, 0.0f), 0.65f);
        _showdownTween.TweenCallback(Callable.From(HideShowdownAnnouncement));

        // Dedicated/headless peers have no listener and must not allocate a streaming decoder.
        // Every real player's graphical peer still hears the bell once for this hand.
        if (ShowdownBell != null && DisplayServer.GetName() != "headless")
            ShowdownBell.Play();
    }

    private void HideShowdownAnnouncement()
    {
        if (ShowdownAnnouncement != null)
            ShowdownAnnouncement.Visible = false;
    }

    private void ResetShowdownAnnouncement()
    {
        _showdownWasActive = false;
        _announcedShowdownHand = -1;
        _showdownTween?.Kill();
        _showdownTween = null;
        HideShowdownAnnouncement();
    }

    // ---------------------------------------------------------------- building

    private void BuildRows()
    {
        if (ActionList == null)
            return;

        foreach (var entry in Layout)
            _rows[entry.Kind] = AddRow();

        // All-in has no PokerActionKind of its own — it is a raise for everything — so it is keyed
        // under None, which is the enum's "no decision" slot and cannot collide with a real action.
        _rows[PokerActionKind.None] = AddRow();
        _showdownRow = AddRow();
    }

    private ActionRow AddRow()
    {
        var box = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        box.AddThemeConstantOverride("separation", 10);

        var text = new Label
        {
            ThemeTypeVariation = "HudLabel",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var key = new Label
        {
            ThemeTypeVariation = "HudLabelSmall",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(26.0f, 0.0f),
        };

        box.AddChild(text);
        box.AddChild(key);
        ActionList.AddChild(box);

        return new ActionRow { Box = box, Text = text, Key = key };
    }

    // ---------------------------------------------------------------- the result

    /// <summary>
    /// Who won the hand that just finished, and with what.
    ///
    /// The category comes from the server alongside the shown cards, so the table can name the hand
    /// without re-evaluating anything — and without being able to disagree with the payout.
    /// </summary>
    private string DescribeResult()
    {
        if (_game.Winners.Count == 0)
            return "";

        var parts = new List<string>();

        foreach (var entry in _game.Winners)
        {
            var name = NameOf(entry.Key);
            var text = $"{name} leva {entry.Value}";

            if (_game.ShowdownCategories.TryGetValue(entry.Key, out var category))
                text += $" com {new PokerHandRank(category).Describe().ToLowerInvariant()}";

            parts.Add(text);
        }

        var headline = _game.Winners.Count > 1
            ? "Pote dividido\n" + string.Join("\n", parts)
            : parts[0];

        // Everyone else who had to show, so a beaten hand is visible rather than just a loss.
        foreach (var entry in _game.ShowdownCategories)
        {
            if (_game.Winners.ContainsKey(entry.Key))
                continue;

            headline += $"\n{NameOf(entry.Key)}: {new PokerHandRank(entry.Value).Describe().ToLowerInvariant()}";
        }

        return headline;
    }

    private string DescribeShowdownPrompt()
    {
        var countdown = _game.ShowdownCountdown;
        if (_game.LocalMustReveal)
        {
            return countdown < 0
                ? "Mostre suas cartas quando estiver pronto"
                : $"Mostre suas cartas\nRevelação automática em {countdown}";
        }

        return countdown < 0
            ? "Aguardando os jogadores mostrarem as cartas"
            : $"Aguardando as cartas\nRevelação automática em {countdown}";
    }

    private static string NameOf(string playerId)
    {
        var player = PlayerRegistry.Instance?.GetPlayerById(playerId);

        return player != null && !string.IsNullOrWhiteSpace(player.Nickname)
            ? player.Nickname
            : $"Jogador {playerId}";
    }

    // ---------------------------------------------------------------- wording

    private string Describe(PokerActionKind kind, ActionOption option, string playerId, int raiseTotal) =>
        kind switch
        {
            PokerActionKind.Fold => "Desistir",
            PokerActionKind.Check => "Passar",
            PokerActionKind.Call => $"Pagar {_game.AmountToCall(playerId)}",
            PokerActionKind.Raise => $"◂ Aumentar para {Mathf.Clamp(raiseTotal, option.MinTotal, option.MaxTotal)} ▸",
            _ => "",
        };

    private static int AllInTotal(IReadOnlyList<ActionOption> options)
    {
        var raise = Find(options, PokerActionKind.Raise);
        if (raise.HasValue)
            return raise.Value.MaxTotal;

        var call = Find(options, PokerActionKind.Call);
        return call?.MaxTotal ?? 0;
    }

    private static ActionOption? Find(IReadOnlyList<ActionOption> options, PokerActionKind kind)
    {
        if (options == null)
            return null;

        foreach (var option in options)
        {
            if (option.Kind == kind)
                return option;
        }

        return null;
    }

    private static string StreetName(PokerStreet street) =>
        street switch
        {
            PokerStreet.Preflop => "Pré-flop",
            PokerStreet.Flop => "Flop",
            PokerStreet.Turn => "Turn",
            PokerStreet.River => "River",
            _ => "Showdown",
        };
}
