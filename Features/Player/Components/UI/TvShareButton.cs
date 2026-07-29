using Godot;
using System;
using System.Collections.Generic;

[GlobalClass]
public partial class TvShareButton : CanvasLayer
{
    private Button _button;
    private Label _statusLabel;
    private OptionButton _sourceOption;
    private Control _overlay;
    private TextureRect _overlayTextureRect;
    private bool _isSharing = false;
    private readonly List<IntPtr> _sourceHandles = new();

    public override void _Ready()
    {
        _button = GetNode<Button>("Panel/VBoxContainer/ShareButton");
        _statusLabel = GetNode<Label>("Panel/VBoxContainer/StatusLabel");
        _sourceOption = GetNode<OptionButton>("Panel/VBoxContainer/SourceOption");
        _overlay = GetNode<Control>("Overlay");
        _overlayTextureRect = GetNode<TextureRect>("Overlay/TextureRect");

        if (!IsMultiplayerAuthority())
        {
            Visible = false;
            SetProcess(false);
            return;
        }

        var isWindows = OS.GetName() == "Windows";
        _button.Visible = isWindows;
        _button.Pressed += OnButtonPressed;

        _sourceOption.Visible = isWindows;
        if (isWindows)
        {
            RefreshWindowSources();
            // Only refresh when the player is actually about to pick, not continuously — window
            // lists don't need to be polled every frame.
            _sourceOption.GetPopup().AboutToPopup += RefreshWindowSources;
        }

        Global.Instance.TvScreen.SharerChanged += OnSharerChanged;
        UpdateUi(Global.Instance.TvScreen.SharerId);
    }

    public override void _ExitTree()
    {
        if (Global.Instance?.TvScreen != null)
            Global.Instance.TvScreen.SharerChanged -= OnSharerChanged;
    }

    public override void _Process(double delta)
    {
        if (_overlay.Visible && _overlayTextureRect.Texture == null)
            RefreshOverlayTexture();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsMultiplayerAuthority())
            return;

        if (@event.IsActionPressed("view_tv"))
        {
            if (Global.Instance.TvScreen.SharerId != 0)
                ToggleOverlay();
            GetViewport().SetInputAsHandled();
        }
        else if (_overlay.Visible && @event.IsActionPressed("ui_cancel"))
        {
            CloseOverlay();
            GetViewport().SetInputAsHandled();
        }
    }

    private void OnButtonPressed()
    {
        if (_isSharing)
        {
            Global.Instance.TvScreen.RequestStopSharing();
            return;
        }

        var index = _sourceOption.Selected;
        var handle = index > 0 && index - 1 < _sourceHandles.Count ? _sourceHandles[index - 1] : default;
        Global.Instance.TvScreen.RequestStartSharing(handle);
    }

    private void RefreshWindowSources()
    {
        var selectedHandle = _sourceOption.Selected > 0 && _sourceOption.Selected - 1 < _sourceHandles.Count
            ? _sourceHandles[_sourceOption.Selected - 1]
            : default;

        _sourceOption.Clear();
        _sourceHandles.Clear();

        _sourceOption.AddItem("Tela inteira");

        var ownHwnd = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, (int)DisplayServer.MainWindowId);
        foreach (var window in WindowsScreenCapture.EnumerateCapturableWindows(ownHwnd))
        {
            _sourceHandles.Add(window.Handle);
            _sourceOption.AddItem(window.Title);
        }

        var restoredIndex = _sourceHandles.IndexOf(selectedHandle);
        _sourceOption.Selected = restoredIndex >= 0 ? restoredIndex + 1 : 0;
    }

    private void OnSharerChanged(int sharerId)
    {
        UpdateUi(sharerId);

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
    }

    private void CloseOverlay()
    {
        _overlay.Visible = false;
    }

    private void RefreshOverlayTexture()
    {
        var texture = Global.Instance.TvScreen.Texture;
        if (texture != null)
            _overlayTextureRect.Texture = texture;
    }

    private void UpdateUi(int sharerId)
    {
        var myId = Multiplayer.GetUniqueId();
        _isSharing = sharerId == myId;

        if (_isSharing)
        {
            _button.Text = "Parar de Compartilhar";
            _button.Disabled = false;
            _sourceOption.Disabled = true;
            _statusLabel.Text = "🔴 Compartilhando sua tela";
            _statusLabel.Visible = true;
        }
        else if (sharerId != 0)
        {
            _button.Text = "Compartilhar Tela";
            _button.Disabled = true;
            _sourceOption.Disabled = true;
            _statusLabel.Text = "Outro jogador está compartilhando (aperte V pra ver)";
            _statusLabel.Visible = true;
        }
        else
        {
            _button.Text = "Compartilhar Tela";
            _button.Disabled = false;
            _sourceOption.Disabled = false;
            _statusLabel.Visible = false;
        }
    }
}
