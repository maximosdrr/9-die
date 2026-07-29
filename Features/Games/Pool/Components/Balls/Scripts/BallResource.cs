using Godot;

[GlobalClass]
public partial class BallResource : Resource
{
    [Export] public string Name;
    [Export] public float Mass = 0.17f;
    [Export] public float GravityScale = 1.0f;
    [Export] public float LinearDamp = 0.2f;
    [Export] public float AngularDamp = 0.1f;
    [Export] public float Friction = 0.1f;
    [Export] public float Bounce = 0.85f;
    [Export] public bool Absorbent = false;
    [Export] public bool CanSleep = true;
    [Export] public bool ContinuosCd = true;
    [Export(PropertyHint.Range, "0.0,45.0")] public float MaxSquirtAngleDeg = 2.0f;
    [Export(PropertyHint.Range, "0.0,1.0")] public float SpinPowerFactor = 0.4f;
    [Export] public PackedScene Model;
}
