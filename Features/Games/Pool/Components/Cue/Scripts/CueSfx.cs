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
        StrikeSfx.VolumeDb = Mathf.Lerp(MinDb, MaxDb, finalForce);
        StrikeSfx.PitchScale = Mathf.Lerp(MinPitch, MaxPitch, finalForce);
        StrikeSfx.Play();
    }
}
