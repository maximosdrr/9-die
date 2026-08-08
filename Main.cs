using Godot;

public partial class Main : Node3D
{
	[Export] public GlobalCamera Camera;
	[Export] public LevelMultiplayerManager LevelManager;

	/// <summary>The level being played. Every table inside it is wired to the camera.</summary>
	[Export] public Node3D Level;

	public override void _Ready()
	{
		LevelManager.Camera = Camera;
		WireTables(Level);
	}

	/// <summary>
	/// Hands the camera to every table in the level.
	///
	/// This used to point at a single table by path, which quietly broke the moment the bar got a
	/// second one: a game that never receives the camera still runs, but every view change is
	/// null-guarded, so the player is seated and then left staring wherever they happened to be
	/// facing. Walking the tree means adding a table to a level needs no wiring at all.
	/// </summary>
	private void WireTables(Node node)
	{
		if (node == null)
			return;

		if (node is Table table)
			table.SetCamera(Camera);

		foreach (var child in node.GetChildren())
			WireTables(child);
	}
}
