using Godot;

[GlobalClass]
public partial class PoolGameTable : StaticBody3D
{
    public PoolScoreMonitor ScoreMonitor;
    public Area3D BallOffMonitor;
    public AudioStreamPlayer3D BallPocketedAudio;

    private ulong _lastSoundTimeMsec = 0;
    private const ulong MinSoundInterval = 50;

    public override void _Ready()
    {
        ScoreMonitor = GetNode<PoolScoreMonitor>("ScoreMonitor");
        BallOffMonitor = GetNode<Area3D>("BallOffMonitor");
        BallPocketedAudio = GetNode<AudioStreamPlayer3D>("BallPocketed");

        GetNode<Node3D>("Rails").AddToGroup("Cushion");
    }

    private void OnPocketsDetectorsBallEntered(Node3D body)
    {
        if (body is not Ball ball)
            return;

        if ((bool)ball.GetMeta("in_pocket", false))
            return;

        ball.SetMeta("in_pocket", true);

        CallDeferred(MethodName.ApplyPocketPhysics, ball);
        EmitBallPocketedSound();

        var timer = GetTree().CreateTimer(1.0);
        timer.Timeout += () =>
        {
            if (IsInstanceValid(ball))
                ball.SetMeta("in_pocket", false);
        };
    }

    private void ApplyPocketPhysics(Ball ball)
    {
        if (IsInstanceValid(ball))
        {
            ball.AngularVelocity = Vector3.Zero;
            ball.LinearVelocity = new Vector3(0.1f, 0, 0.1f);
        }
    }

    private void EmitBallPocketedSound()
    {
        var currentTime = Time.GetTicksMsec();
        if (currentTime - _lastSoundTimeMsec < MinSoundInterval)
            return;

        _lastSoundTimeMsec = currentTime;
        BallPocketedAudio.Play();
    }
}
