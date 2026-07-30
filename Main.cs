using Godot;

public partial class Main : Node3D
{
	[Export] public GlobalCamera Camera;
	[Export] public LevelMultiplayerManager LevelManager;
	[Export] public Table Table;

	public override void _Ready()
	{
		LevelManager.Camera = Camera;
		Table.SetCamera(Camera);
	}
}
