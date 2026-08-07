using Godot;

[GlobalClass]
public partial class BallBounceAudio : AudioStreamPlayer3D
{
    [ExportGroup("References")]
    [Export] public Ball Ball;

    [ExportGroup("Bounce Logic")]
    [Export] public float MaxBounceVelocity = 4.0f;
    [Export] public float MinBounceVelocity = 0.1f;
    [Export] public int MaxSimultaneousSounds = 10;

    [ExportGroup("Volume & Pitch")]
    [Export] public float MinAudibleDb = -35.0f;
    [Export] public float PitchMin = 0.8f;
    [Export] public float PitchMax = 1.1f;

    private int _activeSounds = 0;

    public override void _Ready()
    {
        if (Ball == null && GetParent() is Ball parentBall)
            Ball = parentBall;

        if (Ball != null)
            SignalUtil.ConnectGuarded(Ball, Ball.SignalName.BouncedOnCloth, new Callable(this, MethodName.OnBouncedOnCloth));
    }

    private void OnBouncedOnCloth()
    {
        if (Stream == null)
            return;

        var impactVelocityY = Mathf.Abs(Ball.LinearVelocity.Y);

        if (impactVelocityY < MinBounceVelocity)
            return;

        var rawIntensity = Mathf.Clamp(impactVelocityY / MaxBounceVelocity, 0.0f, 1.0f);
        var finalIntensity = Mathf.Pow(rawIntensity, 2);

        SpawnSoundClone(finalIntensity);
    }

    private void SpawnSoundClone(float intensity)
    {
        if (_activeSounds >= MaxSimultaneousSounds)
            return;

        _activeSounds += 1;

        var sfx = new AudioStreamPlayer3D();

        sfx.Stream = Stream;
        sfx.UnitSize = UnitSize;
        sfx.MaxDb = MaxDb;
        sfx.Bus = Bus;
        sfx.AttenuationModel = AttenuationModel;
        sfx.MaxDistance = MaxDistance;

        var targetDb = Mathf.Lerp(MinAudibleDb, VolumeDb, intensity);
        sfx.VolumeDb = targetDb;

        sfx.PitchScale = Mathf.Lerp(PitchMin, PitchMax, intensity);
        sfx.PitchScale += (float)GD.RandRange(-0.05, 0.05);

        AddChild(sfx);
        sfx.GlobalPosition = Ball.GlobalPosition;

        if (IsInstanceValid(sfx))
        {
            sfx.Play();
            sfx.Finished += () =>
            {
                _activeSounds -= 1;
                sfx.QueueFree();
            };
        }
        else
        {
            _activeSounds -= 1;
        }
    }
}
