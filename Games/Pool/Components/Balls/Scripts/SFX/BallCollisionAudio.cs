using System.Threading.Tasks;
using Godot;

[GlobalClass]
public partial class BallCollisionAudio : AudioStreamPlayer3D
{
    [ExportGroup("References")]
    [Export] public Ball Ball;

    [ExportGroup("Collision Logic")]
    [Export] public float MaxImpactSpeed = 8.0f;
    [Export] public int MaxSimultaneousSounds = 5;
    [Export] public float MaxSpawnDelay = 0.02f;
    [Export] public float MinCollisionSpeed = 0.05f;

    [ExportGroup("Volume & Pitch")]
    [Export] public float MinAudibleDb = -35.0f;
    [Export] public float PitchMin = 0.9f;
    [Export] public float PitchMax = 1.1f;

    private int _activeSounds = 0;

    public override void _Ready()
    {
        if (Ball == null && GetParent() is Ball parentBall)
            Ball = parentBall;

        if (Ball != null)
            SignalUtil.ConnectGuarded(Ball, Ball.SignalName.BallContacted, new Callable(this, MethodName.OnBallContacted));
    }

    private void OnBallContacted(Ball otherBall)
    {
        if (Stream == null)
            return;

        if (Ball.GetInstanceId() < otherBall.GetInstanceId())
            return;

        var relativeVelocity = Ball.LinearVelocity - otherBall.LinearVelocity;
        var impactSpeed = relativeVelocity.Length();

        if (impactSpeed < MinCollisionSpeed)
            return;

        var intensity = Mathf.Clamp(impactSpeed / MaxImpactSpeed, 0.0f, 1.0f);
        intensity *= intensity;

        _ = SpawnSoundClone(intensity);
    }

    private async Task SpawnSoundClone(float intensity)
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

        var dynamicPitch = Mathf.Lerp(PitchMin, PitchMax, intensity);
        dynamicPitch += (float)GD.RandRange(-0.02, 0.02);

        sfx.PitchScale = dynamicPitch;

        AddChild(sfx);
        sfx.GlobalPosition = Ball.GlobalPosition;

        var delay = (float)GD.RandRange(0.0, MaxSpawnDelay);
        if (delay > 0.0f)
            await ToSignal(GetTree().CreateTimer(delay), SceneTreeTimer.SignalName.Timeout);

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
