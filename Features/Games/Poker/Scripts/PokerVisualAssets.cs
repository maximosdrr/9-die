using Godot;

/// <summary>
/// The single place where replaceable poker art is selected.
///
/// Game rules and motion operate on stable roots; these scenes are only the visible content placed
/// below those roots. Replacing a card, chip or hand therefore does not change network state,
/// animation tracks or table layout code.
/// </summary>
[GlobalClass]
public partial class PokerVisualAssets : Resource
{
	[ExportGroup("Table pieces")]
	[Export] public PackedScene CardScene;
	[Export] public PackedScene ChipScene;

	[ExportGroup("First-person hands")]
	[Export] public PackedScene CardHandScene;
	[Export] public Transform3D CardHandTransform = Transform3D.Identity;
	[Export] public PackedScene ChipHandScene;
	[Export] public Transform3D ChipHandTransform = Transform3D.Identity;
}
