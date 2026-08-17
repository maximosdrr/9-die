using System.Collections.Generic;
using Godot;
using Poker.Rules;

/// <summary>Screen-space comparison shown after every contested poker showdown.</summary>
public partial class PokerShowdownRankingOverlay : CanvasLayer
{
    public sealed class Entry
    {
        public string PlayerId = "";
        public string PlayerName = "";
        public int[] HoleCards = System.Array.Empty<int>();
        public PokerHandRank Rank = PokerHandRank.None;
        public int Place;
        public bool Won;
    }

    private static readonly Color Gold = new("d9ad58");
    private static readonly Color BrightGold = new("ffd87a");
    private static readonly Color Cream = new("fff4d7");
    private static readonly Color Muted = new("c9c0ad");
    private static readonly Color Ink = new("17100c");
    private static readonly Color RedSuit = new("b8272f");

    // The ranking is a brief hand recap, not a game-over screen. Keep the world clearly visible
    // behind it while retaining enough contrast for cards and results at every supported scale.
    private const float VeilAlpha = 0.34f;
    private const float PanelAlpha = 0.84f;
    private const float CommunityFrameAlpha = 0.64f;
    private const float WinnerRowAlpha = 0.82f;
    private const float OtherRowAlpha = 0.72f;

    private Control _root;
    private MarginContainer _safeMargin;
    private VBoxContainer _communityCards;
    private VBoxContainer _rankingRows;
    private Font _displayFont;
    private Tween _appearanceTween;
    private readonly List<string> _order = new();

    public bool VisibleOnScreen => _root?.Visible ?? false;
    public int CommunityCardCount { get; private set; }
    public int PlayerRowCount { get; private set; }
    public int WinnerRowCount { get; private set; }
    public int DisplayedCardCount => CommunityCardCount + PlayerRowCount * PokerDeal.HoleCardCount;
    public IReadOnlyList<string> PlayerOrder => _order;

    public void Configure(Font displayFont)
    {
        _displayFont = displayFont;
        EnsureBuilt();
    }

    public void Present(IReadOnlyList<int> board, IReadOnlyList<Entry> entries)
    {
        EnsureBuilt();
        ClearChildren(_communityCards);
        ClearChildren(_rankingRows);
        _order.Clear();

        var boardRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        boardRow.AddThemeConstantOverride("separation", 10);
        _communityCards.AddChild(boardRow);
        CommunityCardCount = 0;
        if (board != null)
        {
            foreach (var cardId in board)
            {
                if (!CardId.IsValid(cardId))
                    continue;
                boardRow.AddChild(BuildCard(cardId, 62.0f, 84.0f, 30));
                CommunityCardCount++;
            }
        }

        PlayerRowCount = 0;
        WinnerRowCount = 0;
        if (entries != null)
        {
            foreach (var entry in entries)
            {
                _rankingRows.AddChild(BuildPlayerRow(entry));
                _order.Add(entry.PlayerId);
                PlayerRowCount++;
                if (entry.Won)
                    WinnerRowCount++;
            }
        }

        _root.Visible = true;
        _root.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f);
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var responsiveScale = Mathf.Min(1.0f,
            Mathf.Min(viewportSize.X / 1010.0f, viewportSize.Y / 610.0f));
        _safeMargin.PivotOffset = viewportSize * 0.5f;
        _safeMargin.Scale = Vector2.One * Mathf.Max(0.62f, responsiveScale);
        _appearanceTween?.Kill();
        _appearanceTween = CreateTween();
        _appearanceTween.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _appearanceTween.TweenProperty(_root, "modulate", Colors.White, 0.30f);
    }

    public void HideImmediate()
    {
        _appearanceTween?.Kill();
        _appearanceTween = null;
        if (_root != null)
            _root.Visible = false;
        CommunityCardCount = 0;
        PlayerRowCount = 0;
        WinnerRowCount = 0;
        _order.Clear();
    }

    public void Dismiss()
    {
        if (_root == null || !_root.Visible)
            return;
        _appearanceTween?.Kill();
        _appearanceTween = CreateTween();
        _appearanceTween.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        _appearanceTween.TweenProperty(
            _root, "modulate", new Color(1.0f, 1.0f, 1.0f, 0.0f), 0.24f);
        _appearanceTween.TweenCallback(Callable.From(() => _root.Visible = false));
    }

    public override void _ExitTree()
    {
        _appearanceTween?.Kill();
        _appearanceTween = null;
    }

    private void EnsureBuilt()
    {
        if (_root != null)
            return;

        Layer = 80;
        _root = new Control
        {
            Name = "RankingRoot",
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var veil = new ColorRect
        {
            Name = "Veil",
            Color = new Color(0.035f, 0.022f, 0.016f, VeilAlpha),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _root.AddChild(veil);
        veil.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _safeMargin = new MarginContainer { Name = "SafeMargin" };
        _safeMargin.AddThemeConstantOverride("margin_left", 36);
        _safeMargin.AddThemeConstantOverride("margin_top", 26);
        _safeMargin.AddThemeConstantOverride("margin_right", 36);
        _safeMargin.AddThemeConstantOverride("margin_bottom", 26);
        _root.AddChild(_safeMargin);
        _safeMargin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var centre = new CenterContainer { Name = "Centre" };
        _safeMargin.AddChild(centre);

        var panel = new PanelContainer
        {
            Name = "RankingPanel",
            CustomMinimumSize = new Vector2(930.0f, 0.0f),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        panel.AddThemeStyleboxOverride("panel", PanelStyle(
            new Color(0.085f, 0.047f, 0.026f, PanelAlpha), Gold, 3, 18));
        centre.AddChild(panel);

        var padding = new MarginContainer();
        padding.AddThemeConstantOverride("margin_left", 30);
        padding.AddThemeConstantOverride("margin_top", 22);
        padding.AddThemeConstantOverride("margin_right", 30);
        padding.AddThemeConstantOverride("margin_bottom", 24);
        panel.AddChild(padding);

        var content = new VBoxContainer { Name = "Content" };
        content.AddThemeConstantOverride("separation", 12);
        padding.AddChild(content);
        content.AddChild(TextLabel("RESULTADO DA MÃO", 34, BrightGold,
            HorizontalAlignment.Center));
        content.AddChild(Rule());
        content.AddChild(TextLabel("CARTAS COMUNITÁRIAS", 17, Muted,
            HorizontalAlignment.Center));

        var boardFrame = new PanelContainer { Name = "CommunityFrame" };
        boardFrame.AddThemeStyleboxOverride("panel", PanelStyle(
            new Color(0.11f, 0.065f, 0.038f, CommunityFrameAlpha), Gold, 2, 10));
        content.AddChild(boardFrame);
        var boardPadding = new MarginContainer();
        boardPadding.AddThemeConstantOverride("margin_left", 14);
        boardPadding.AddThemeConstantOverride("margin_top", 10);
        boardPadding.AddThemeConstantOverride("margin_right", 14);
        boardPadding.AddThemeConstantOverride("margin_bottom", 10);
        boardFrame.AddChild(boardPadding);
        _communityCards = new VBoxContainer { Name = "CommunityCards" };
        boardPadding.AddChild(_communityCards);

        content.AddChild(BuildHeader());
        _rankingRows = new VBoxContainer { Name = "PlayerRows" };
        _rankingRows.AddThemeConstantOverride("separation", 7);
        content.AddChild(_rankingRows);
    }

    private Control BuildHeader()
    {
        var header = new HBoxContainer { Name = "Header" };
        header.AddThemeConstantOverride("separation", 12);
        header.AddChild(SizedLabel("#", 42, 15, Muted, HorizontalAlignment.Center));
        header.AddChild(SizedLabel("JOGADOR", 185, 15, Muted));
        header.AddChild(SizedLabel("CARTAS", 142, 15, Muted, HorizontalAlignment.Center));
        var hand = SizedLabel("MÃO FINAL", 0, 15, Muted);
        hand.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(hand);
        header.AddChild(SizedLabel("RESULTADO", 112, 15, Muted, HorizontalAlignment.Right));
        return header;
    }

    private Control BuildPlayerRow(Entry entry)
    {
        var row = new PanelContainer { Name = $"Player_{entry.PlayerId}" };
        row.CustomMinimumSize = new Vector2(0.0f, 92.0f);
        row.AddThemeStyleboxOverride("panel", PanelStyle(
            entry.Won ? new Color(0.25f, 0.145f, 0.050f, WinnerRowAlpha)
                : new Color(0.075f, 0.046f, 0.032f, OtherRowAlpha),
            entry.Won ? BrightGold : new Color(0.46f, 0.36f, 0.25f, 0.8f),
            entry.Won ? 4 : 1, 9));

        var padding = new MarginContainer();
        padding.AddThemeConstantOverride("margin_left", 14);
        padding.AddThemeConstantOverride("margin_top", 7);
        padding.AddThemeConstantOverride("margin_right", 14);
        padding.AddThemeConstantOverride("margin_bottom", 7);
        row.AddChild(padding);

        var columns = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        columns.AddThemeConstantOverride("separation", 12);
        padding.AddChild(columns);
        columns.AddChild(SizedLabel($"{entry.Place}º", 42, 22,
            entry.Won ? BrightGold : Cream, HorizontalAlignment.Center));
        columns.AddChild(SizedLabel(entry.PlayerName, 185, 21, Cream));

        var holeCards = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(142.0f, 0.0f),
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        holeCards.AddThemeConstantOverride("separation", 7);
        for (var index = 0; index < PokerDeal.HoleCardCount; index++)
        {
            var cardId = index < entry.HoleCards.Length ? entry.HoleCards[index] : CardId.None;
            holeCards.AddChild(BuildCard(cardId, 46.0f, 66.0f, 23));
        }
        columns.AddChild(holeCards);

        var handName = SizedLabel(entry.Rank.Describe().ToUpperInvariant(), 0, 21,
            entry.Won ? BrightGold : Cream);
        handName.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        columns.AddChild(handName);
        columns.AddChild(SizedLabel(entry.Won ? "VENCEU" : "PERDEU", 112, 20,
            entry.Won ? BrightGold : Muted, HorizontalAlignment.Right));
        return row;
    }

    private Control BuildCard(int cardId, float width, float height, int fontSize)
    {
        var card = new PanelContainer
        {
            CustomMinimumSize = new Vector2(width, height),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        card.AddThemeStyleboxOverride("panel", PanelStyle(
            new Color(0.985f, 0.975f, 0.94f, 1.0f), new Color(0.66f, 0.56f, 0.39f), 2, 6));
        var suit = CardId.IsValid(cardId) ? CardId.SuitOf(cardId) : -1;
        var label = TextLabel(CardId.Label(cardId).Replace("??", "—"), fontSize,
            suit is CardId.Diamonds or CardId.Hearts ? RedSuit : Ink,
            HorizontalAlignment.Center);
        // The display font is deliberately stylized and does not contain every suit glyph. Cards
        // use Godot's complete fallback font so ♣♦♥♠ can never turn into missing-glyph boxes.
        label.RemoveThemeFontOverride("font");
        label.VerticalAlignment = VerticalAlignment.Center;
        card.AddChild(label);
        return card;
    }

    private Label SizedLabel(string text, float width, int fontSize, Color color,
        HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        var label = TextLabel(text, fontSize, color, alignment);
        if (width > 0.0f)
            label.CustomMinimumSize = new Vector2(width, 0.0f);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        return label;
    }

    private Label TextLabel(string text, int fontSize, Color color,
        HorizontalAlignment alignment)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = alignment,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.025f, 0.012f, 0.96f));
        label.AddThemeConstantOverride("outline_size", 4);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        if (_displayFont != null)
            label.AddThemeFontOverride("font", _displayFont);
        return label;
    }

    private static HSeparator Rule()
    {
        var rule = new HSeparator { CustomMinimumSize = new Vector2(0.0f, 2.0f) };
        rule.Modulate = new Color(Gold, 0.78f);
        return rule;
    }

    private static StyleBoxFlat PanelStyle(Color background, Color border, int width, int radius) =>
        new()
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = width,
            BorderWidthTop = width,
            BorderWidthRight = width,
            BorderWidthBottom = width,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
        };

    private static void ClearChildren(Node parent)
    {
        if (parent == null)
            return;
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }
}
