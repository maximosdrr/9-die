using Godot;

/// <summary>
/// Editor marker for one analytical cushion segment. The box is never used as a Godot physics
/// collider; PoolTableGeometry reads one of its long faces and turns that face into the segment
/// used by the pool simulation.
/// </summary>
[Tool]
[GlobalClass]
public partial class PoolCushionMarker : CollisionShape3D
{
    /// <summary>
    /// Selects the opposite long face and reverses its normal. Useful for diagonal pocket facings,
    /// where choosing the face nearest the table centre can be ambiguous.
    /// </summary>
    [Export] public bool FlipNormal { get; set; }
}
