using Godot;

[GlobalClass]
public partial class CueSfx : Node
{
    [Export] public AudioStreamPlayer3D StrikeSfx;
    [Export] public Cue Cue;

    [ExportGroup("Audio Config")]
    [Export] public float MinPitch = 0.8f;
    [Export] public float MaxPitch = 1.1f;

    [Export] public float MinDb = -40.0f;
    [Export] public float MaxDb = 10.0f;

    public void EmitStrikeSound(Vector3 dir, float cueBallSpeed, Vector3 hitOffset)
    {
        var maxSpeed = Cue != null && Cue.MaxCueBallSpeed > 0f ? Cue.MaxCueBallSpeed : 1f;
        var intensity = Mathf.Clamp(cueBallSpeed / maxSpeed, 0f, 1f);

        StrikeSfx.VolumeDb = Mathf.Lerp(MinDb, MaxDb, intensity);
        StrikeSfx.PitchScale = Mathf.Lerp(MinPitch, MaxPitch, intensity);
        StrikeSfx.Play();
    }
}
