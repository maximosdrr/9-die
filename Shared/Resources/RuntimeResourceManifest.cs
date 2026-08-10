using Godot;

/// <summary>
/// Explicit dependency list for resources that production code loads by path or UID.
///
/// Godot cannot discover string-based loads while exporting selected scenes. Keeping those assets
/// in one serialized resource makes the release package complete without exporting test scenes or
/// unrelated source art. Add every new production-only dynamic load to the companion .tres file.
/// </summary>
[GlobalClass]
public partial class RuntimeResourceManifest : Resource
{
    [Export]
    public Godot.Collections.Array<Resource> Resources { get; set; } = new();
}
