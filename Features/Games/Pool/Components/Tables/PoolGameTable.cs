using Godot;

[GlobalClass]
public partial class PoolGameTable : StaticBody3D
{
	public Area3D ScoreMonitor;
	public Area3D BallOffMonitor;
	public AudioStreamPlayer3D BallPocketedAudio;

	/// <summary>
	/// This table's own ball-physics geometry, read from its scene. Every table carries its own,
	/// so swapping table scenes swaps the cushion line and pockets with it.
	/// </summary>
	[Export] public PoolTableGeometry Geometry;

	private ulong _lastSoundTimeMsec = 0;
	private const ulong MinSoundInterval = 50;

	public override void _Ready()
	{
		ScoreMonitor = GetNode<Area3D>("ScoreMonitor");
		BallOffMonitor = GetNode<Area3D>("BallOffMonitor");
		BallPocketedAudio = GetNode<AudioStreamPlayer3D>("BallPocketed");
	}

	/// <summary>
	/// Called by PoolSimulationRunner when the simulation reports a pot. Balls are no longer
	/// physics bodies, so the pocket Area3D triggers that used to drive this never fire — the
	/// simulated event timeline is the source of truth now.
	/// </summary>
	public void EmitBallPocketedSound()
	{
		var currentTime = Time.GetTicksMsec();
		if (currentTime - _lastSoundTimeMsec < MinSoundInterval)
			return;

		_lastSoundTimeMsec = currentTime;
		BallPocketedAudio.Play();
	}
}
