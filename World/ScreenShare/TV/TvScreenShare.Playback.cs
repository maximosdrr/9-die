using System;
using Godot;

public partial class TvScreenShare
{
    private void ProcessPlayout()
    {
        if (_playoutBuffer == null || !_playoutBuffer.TryDequeueDue(out var encodedBytes))
            return;

        DisplayFrame(encodedBytes);
    }

    private void DisplayFrame(byte[] encodedBytes)
    {
        // Validate dimensions from the tiny container header before asking the decoder to
        // allocate pixel storage. A compact WebP can advertise a 16k x 16k canvas while staying
        // below the encoded byte limit, which would otherwise be an allocation denial-of-service.
        if (!IsSafeEncodedFrame(encodedBytes))
            return;

        using var image = new Image();
        var err = image.LoadWebpFromBuffer(encodedBytes);
        if (err != Error.Ok)
            return;

        if (image.GetWidth() != CaptureWidth || image.GetHeight() != CaptureHeight)
        {
            GD.PushWarning($"[TvScreenShare] Frame descartado com dimensão inválida: {image.GetWidth()}x{image.GetHeight()}.");
            return;
        }

        // Without mipmaps, viewing this texture on a small/distant 3D surface (or at a steep
        // angle) causes shimmering/aliasing since the GPU can't minify it properly.
        image.GenerateMipmaps();

        if (_texture == null)
        {
            _texture = ImageTexture.CreateFromImage(image);
            _screenMaterial.AlbedoColor = Colors.White;
            _screenMaterial.AlbedoTexture = _texture;
        }
        else
        {
            _texture.Update(image);
        }
    }

    private void ClearScreen()
    {
        // The screen material is Unshaded, so AlbedoTexture alone is enough to display the
        // captured frame at full brightness; Emission was only adding a second, unnecessary
        // brightness pass that pushed the scene's global glow/bloom into blowing out bright
        // desktop content (white backgrounds, text) into a hazy halo.
        if (_screenMaterial != null)
        {
            _screenMaterial.AlbedoColor = new Color(0.05f, 0.05f, 0.05f);
            _screenMaterial.AlbedoTexture = null;
        }

        _texture?.Dispose();
        _texture = null;
    }

    private void PlayAudioChunk(byte[] monoInt16)
    {
        if (!IsValidMediaPayload(monoInt16, isAudio: true))
            return;

        EnsureAudioPlayback();
        if (_audioPlayback == null)
            return;

        var frames = ConvertMonoInt16ToFrames(monoInt16);
        if (frames.Length == 0)
            return;

        if (_audioPlayback.CanPushBuffer(frames.Length))
            _audioPlayback.PushBuffer(frames);
    }

    private void EnsureAudioPlayback()
    {
        if (_audioPlayer == null || _audioPlayback != null)
            return;

        _audioPlayer.Play();
        _audioPlayback = _audioPlayer.GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    private void StopAudioPlayback(bool releaseStream = false)
    {
        if (_audioPlayer == null)
            return;

        _audioPlayback?.Stop();
        _audioPlayback?.ClearBuffer();
        _audioPlayer.Stop();
        if (releaseStream)
            _audioPlayer.Stream = null;
        _audioPlayback?.Dispose();
        _audioPlayback = null;
    }
}
