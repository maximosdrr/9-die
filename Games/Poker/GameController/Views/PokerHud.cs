using System.Collections.Generic;
using Godot;
using Poker.Rules;

/// <summary>
/// Screen-space feedback that cannot live naturally on the table: persistent control hints and the
/// brief showdown announcement. Pot, stack, turn and betting information are physical table
/// elements, so this layer deliberately has no corner status panel.
/// </summary>
[GlobalClass]
public partial class PokerHud : CanvasLayer
{
    [Export] public Control Root;
    [Export] public Label HintsLabel;
    [Export] public Label NoticeLabel;

    [ExportGroup("Showdown announcement")]
    [Export] public Control ShowdownAnnouncement;
    [Export] public Label ShowdownTitle;
    [Export] public Label ShowdownPrompt;
    [Export] public AudioStreamPlayer ShowdownBell;

    private PokerGame _game;
    private Tween _showdownTween;
    private bool _showdownWasActive;
    private int _announcedShowdownHand = -1;

    public void ShowNotice(string text)
    {
        if (NoticeLabel == null)
            return;

        NoticeLabel.Text = text ?? "";
        NoticeLabel.Visible = !string.IsNullOrWhiteSpace(text);
    }

    public void HideNotice()
    {
        if (NoticeLabel != null)
            NoticeLabel.Visible = false;
    }

    public override void _Ready()
    {
        SetPanelVisible(false);

        if (HintsLabel != null)
        {
            HintsLabel.Text = "Segure o botão direito para olhar suas cartas"
                              + " · use o arco para CALL/AUTO/APOSTAR"
                              + " ou segure para ALL-IN"
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

    public void Setup(PokerGame game, Player player) => _game = game;

    /// <summary>Controls this lightweight overlay without reintroducing a status panel.</summary>
    public void SetPanelVisible(bool visible)
    {
        if (Root != null)
            Root.Visible = visible;
        if (!visible)
            HideNotice();
    }

    public void Refresh(
        IReadOnlyList<ActionOption> options, bool isYourTurn, int raiseTotal, bool pickedUpCards)
    {
        if (_game == null)
            return;

        var inMatch = _game.IsMatchActive;
        SetPanelVisible(inMatch);
        if (!inMatch)
        {
            ResetShowdownAnnouncement();
            return;
        }

        UpdateShowdownAnnouncement(_game.ShowdownWaiting);
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
}
