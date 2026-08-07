using Godot;
using System;

[GlobalClass]
public partial class TvShareButton : CanvasLayer
{
    private const int ThumbnailWidth = 320;
    private const int ThumbnailHeight = 180;

    private Button _stopSharingButton;
    private Control _overlay;
    private TextureRect _overlayTextureRect;
    private Control _sourcePicker;
    private GridContainer _sourceGrid;
    private bool _isSharing = false;

    public TvScreenShare TvScreen;

    public override void _Ready()
    {
        _stopSharingButton = GetNode<Button>("StopSharingButton");
        _overlay = GetNode<Control>("Overlay");
        _overlayTextureRect = GetNode<TextureRect>("Overlay/TextureRect");
        _sourcePicker = GetNode<Control>("SourcePicker");
        _sourceGrid = GetNode<GridContainer>("SourcePicker/CenterContainer/Panel/VBoxContainer/ScrollContainer/Grid");
    }

    // Godot calls _Ready() bottom-up (children before parents), so TvShareButton._Ready()
    // runs before Player._Ready() can assign TvScreen. Player calls this explicitly instead,
    // after TvScreen is set — same pattern as PlayerHud.Initialize().
    public void Initialize(TvScreenShare tvScreen)
    {
        TvScreen = tvScreen;

        if (!IsMultiplayerAuthority())
        {
            Visible = false;
            SetProcess(false);
            return;
        }

        _stopSharingButton.Pressed += () => TvScreen.RequestStopSharing();

        TvScreen.SharerChanged += OnSharerChanged;
        OnSharerChanged(TvScreen.SharerId);
    }

    public override void _ExitTree()
    {
        if (TvScreen != null)
            TvScreen.SharerChanged -= OnSharerChanged;
    }

    public override void _Process(double delta)
    {
        if (_overlay.Visible && _overlayTextureRect.Texture == null)
            RefreshOverlayTexture();

        // If the player walks away from the TV while the picker is open, close it — picking a
        // source while no longer standing at the TV would be an inconsistent state.
        if (_sourcePicker.Visible && !TvScreen.IsLocalPlayerInRange)
            CloseSourcePicker();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsMultiplayerAuthority())
            return;

        // Mouse wheel ticks are InputEventMouseButton too. When the ScrollContainer is already
        // at the end of the list, it doesn't mark the event handled, so it falls through here —
        // and HeadPivot/AimCameraPivot treat any unhandled mouse button press as "start aiming",
        // recapturing (hiding) the cursor. Swallow all mouse button events while either of our
        // panels is open so that leak can't happen.
        if ((_sourcePicker.Visible || _overlay.Visible) && @event is InputEventMouseButton)
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed("view_tv"))
        {
            if (TvScreen.SharerId != 0)
                ToggleOverlay();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("interact") && TvScreen.IsLocalPlayerInRange)
        {
            if (_isSharing)
                TvScreen.RequestStopSharing();
            else if (TvScreen.SharerId == 0)
                OpenSourcePicker();
            else
                ToggleOverlay(); // someone else is sharing on this TV — F watches, same as view_tv
            GetViewport().SetInputAsHandled();
        }
        else if (_sourcePicker.Visible && @event.IsActionPressed("ui_cancel"))
        {
            CloseSourcePicker();
            GetViewport().SetInputAsHandled();
        }
        else if (_overlay.Visible && @event.IsActionPressed("ui_cancel"))
        {
            CloseOverlay();
            GetViewport().SetInputAsHandled();
        }
    }

    private void OpenSourcePicker()
    {
        PopulateSourceGrid();
        _sourcePicker.Visible = true;
        // The game normally runs with the mouse captured/hidden (FPS-style look) — without
        // freeing it here, the player would have no visible cursor to pick a thumbnail with.
        InputFocus.Release();
        TvScreen.SetPromptSuppressed(true);
    }

    private void CloseSourcePicker()
    {
        _sourcePicker.Visible = false;
        InputFocus.Capture();
        TvScreen.SetPromptSuppressed(false);
    }

    private void PopulateSourceGrid()
    {
        foreach (var child in _sourceGrid.GetChildren())
            child.QueueFree();

        AddSourceCard("Tela inteira", CaptureThumbnail(default), default);

        var ownHwnd = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, (int)DisplayServer.MainWindowId);
        foreach (var window in WindowsScreenCapture.EnumerateCapturableWindows(ownHwnd))
            AddSourceCard(window.Title, CaptureThumbnail(window.Handle), window.Handle);
    }

    // Reuses the exact same capture functions the live share uses, just at a small resolution —
    // this is a one-shot snapshot taken when the picker opens, not a continuous loop, so it runs
    // synchronously on the main thread without needing a background worker.
    private static ImageTexture CaptureThumbnail(IntPtr windowHandle)
    {
        var rgba = new byte[ThumbnailWidth * ThumbnailHeight * 4];
        var captured = windowHandle == IntPtr.Zero
            ? WindowsScreenCapture.TryCapturePrimaryScreen(ThumbnailWidth, ThumbnailHeight, rgba)
            : WindowsScreenCapture.TryCaptureWindow(windowHandle, ThumbnailWidth, ThumbnailHeight, rgba);

        if (!captured)
            return null;

        var image = Image.CreateFromData(ThumbnailWidth, ThumbnailHeight, false, Image.Format.Rgba8, rgba);
        return ImageTexture.CreateFromImage(image);
    }

    private void AddSourceCard(string title, ImageTexture thumbnail, IntPtr handle)
    {
        var card = new Button
        {
            CustomMinimumSize = new Vector2(280, 190),
            ClipText = true,
        };
        card.Pressed += () =>
        {
            TvScreen.RequestStartSharing(handle);
            CloseSourcePicker();
        };

        // Children use MouseFilter.Ignore so a click landing on the thumbnail/label still
        // registers as a click on the card button itself, instead of being swallowed by them.
        var vbox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        card.AddChild(vbox);

        var textureRect = new TextureRect
        {
            Texture = thumbnail,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(0, 150),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        vbox.AddChild(textureRect);

        var label = new Label
        {
            Text = title,
            HorizontalAlignment = HorizontalAlignment.Center,
            ClipText = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        vbox.AddChild(label);

        _sourceGrid.AddChild(card);
    }

    private void OnSharerChanged(int sharerId)
    {
        _isSharing = sharerId == Multiplayer.GetUniqueId();
        _stopSharingButton.Visible = _isSharing;

        if (sharerId == 0)
            CloseOverlay();
        else
            _overlayTextureRect.Texture = null; // force re-bind to the new sharer's texture instance
    }

    private void ToggleOverlay()
    {
        if (_overlay.Visible)
            CloseOverlay();
        else
            OpenOverlay();
    }

    private void OpenOverlay()
    {
        _overlay.Visible = true;
        RefreshOverlayTexture();
        TvScreen.SetPromptSuppressed(true);
    }

    private void CloseOverlay()
    {
        _overlay.Visible = false;
        TvScreen.SetPromptSuppressed(false);
    }

    private void RefreshOverlayTexture()
    {
        var texture = TvScreen.Texture;
        if (texture != null)
            _overlayTextureRect.Texture = texture;
    }
}
