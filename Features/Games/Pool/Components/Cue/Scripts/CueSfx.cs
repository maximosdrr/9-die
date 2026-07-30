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

    public void EmitStrikeSound(Vector3 dir, float finalForce, Vector3 hitOffset)
    {
        var maxForce = Cue != null && Cue.ForceMultiplier > 0f ? Cue.ForceMultiplier : 1f;
        var normalizedForce = Mathf.Clamp(finalForce / maxForce, 0f, 1f);

        StrikeSfx.VolumeDb = Mathf.Lerp(MinDb, MaxDb, normalizedForce);
        StrikeSfx.PitchScale = Mathf.Lerp(MinPitch, MaxPitch, normalizedForce);
        StrikeSfx.Play();
    }
}
