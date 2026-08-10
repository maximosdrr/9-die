using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Turns physical chip arrivals into a bounded, spatial soundscape.
///
/// A large bet may be represented by several animated batches that touch the cloth on almost the
/// same frame. Playing one full-scale sample per batch clips the mix and sounds synthetic, so close
/// impacts are merged into one event. Loudness grows logarithmically with the number of chips and a
/// small voice pool puts a hard ceiling on simultaneous samples.
/// </summary>
[GlobalClass]
public partial class PokerChipSoundscape : Node3D
{
    private readonly List<AudioStreamPlayer3D> _voices = new();
    private PokerChipAnimator _animator;
    private AudioStream _stream;
    private int _voiceLimit = 3;
    private float _mergeSeconds = 0.075f;
    private float _minimumInterval = 0.09f;
    private float _singleImpactDb = -17.0f;
    private float _maximumImpactDb = -11.0f;

    private int _pendingChips;
    private int _pendingBatches;
    private Vector3 _pendingWeightedPosition;
    private float _pendingAge;
    private float _sinceLastPlayback = 10.0f;
    private int _playSequence;

    public int VoiceLimit => _voiceLimit;
    public int ActiveVoiceCount => _voices.Count(voice => voice.Playing);

    public void Configure(
        PokerChipAnimator animator, AudioStream stream, int voiceLimit = 3,
        float mergeSeconds = 0.075f, float minimumInterval = 0.09f,
        float singleImpactDb = -17.0f, float maximumImpactDb = -11.0f)
    {
        if (_animator != null)
            _animator.ChipsLanded -= QueueImpact;

        _animator = animator;
        _stream = stream;
        _voiceLimit = Mathf.Clamp(voiceLimit, 1, 3);
        _mergeSeconds = Mathf.Max(0.0f, mergeSeconds);
        _minimumInterval = Mathf.Max(0.0f, minimumInterval);
        _singleImpactDb = Mathf.Min(singleImpactDb, maximumImpactDb);
        _maximumImpactDb = Mathf.Max(singleImpactDb, maximumImpactDb);

        if (_animator != null)
            _animator.ChipsLanded += QueueImpact;

        EnsureVoicePool();
        SetProcess(_stream != null);
    }

    public override void _ExitTree()
    {
        if (_animator != null)
            _animator.ChipsLanded -= QueueImpact;
    }

    public override void _Process(double delta)
    {
        _sinceLastPlayback += (float)delta;
        if (_pendingChips <= 0)
            return;

        _pendingAge += (float)delta;
        if (_pendingAge < _mergeSeconds || _sinceLastPlayback < _minimumInterval)
            return;

        FlushImpact();
    }

    /// <summary>Safe gain curve used both by playback and integration tests.</summary>
    public float VolumeForImpact(int chipCount, int mergedBatches)
    {
        var chipWeight = Mathf.Clamp(Log2(Mathf.Max(1, chipCount)) / 4.0f,
            0.0f, 1.0f);
        var batchAccent = Mathf.Min(1.0f, Log2(Mathf.Max(1, mergedBatches)) * 0.35f);
        return Mathf.Min(_maximumImpactDb,
            Mathf.Lerp(_singleImpactDb, _maximumImpactDb, chipWeight) + batchAccent);
    }

    private void QueueImpact(Vector3 globalPosition, int chipCount)
    {
        if (_stream == null || chipCount <= 0)
            return;

        var weight = Mathf.Max(1, chipCount);
        _pendingWeightedPosition += globalPosition * weight;
        _pendingChips += weight;
        _pendingBatches++;
        if (_pendingBatches == 1)
            _pendingAge = 0.0f;
    }

    private void FlushImpact()
    {
        var player = _voices.Find(voice => !voice.Playing);
        if (player != null)
        {
            var noise = PokerChipPile.Noise(++_playSequence, 73);
            player.GlobalPosition = _pendingWeightedPosition / Mathf.Max(1, _pendingChips);
            player.VolumeDb = Mathf.Min(_maximumImpactDb,
                VolumeForImpact(_pendingChips, _pendingBatches) + noise * 0.7f);
            player.PitchScale = 0.96f + (noise + 1.0f) * 0.04f;
            player.Play();
            _sinceLastPlayback = 0.0f;
        }

        // When every voice is occupied, consume the merged impact instead of playing it late. A
        // delayed clatter after the chips have stopped is more distracting than an omitted layer.
        _pendingChips = 0;
        _pendingBatches = 0;
        _pendingWeightedPosition = Vector3.Zero;
        _pendingAge = 0.0f;
    }

    private void EnsureVoicePool()
    {
        while (_voices.Count < _voiceLimit)
        {
            var player = new AudioStreamPlayer3D
            {
                Name = $"ChipImpact{_voices.Count}",
                Stream = _stream,
                UnitSize = 2.5f,
                MaxDistance = 12.0f,
                MaxPolyphony = 1,
            };
            AddChild(player);
            _voices.Add(player);
        }

        for (var index = 0; index < _voices.Count; index++)
        {
            _voices[index].Stream = _stream;
            _voices[index].Visible = index < _voiceLimit;
        }
    }

    private static float Log2(float value) => Mathf.Log(value) * 1.442695f;
}
