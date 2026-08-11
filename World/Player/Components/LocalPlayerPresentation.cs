using Godot;

/// <summary>
/// Owns presentation that exists only for the player controlled by this client.
///
/// The replicated Player scene intentionally contains no CanvasLayer children. Keeping local UI
/// here prevents every remote avatar from allocating HUD/input nodes and makes the authority
/// boundary visible in the scene tree.
/// </summary>
[GlobalClass]
public partial class LocalPlayerPresentation : Node
{
    [Export] public PlayerHud Hud;
    [Export] public TvShareButton TvShareButton;

    public Player Player { get; private set; }

    public void Configure(Player player, TvScreenShare tvScreen)
    {
        Player = player;

        Hud?.Initialize(player);
        TvShareButton?.Initialize(tvScreen);
    }

    public void Detach()
    {
        Hud?.Detach();
        Player = null;
    }
}
