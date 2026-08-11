using Domino.Rules;
using Godot;

/// <summary>
/// Builds the translucent placement preview and its valid/invalid outline.
/// </summary>
public partial class DominoHand3DView
{
    private DominoTile _ghost;
    private readonly MeshInstance3D[] _frame = new MeshInstance3D[4];
    private StandardMaterial3D _outlineMaterial;

    /// <summary>
    /// The preview is the real tile — real face, real pips, turned exactly as it would land —
    /// made see-through, wearing a coloured border. Two separate readings for the two questions
    /// the rotation mechanic asks: which half is leading, and whether that is accepted.
    /// </summary>
    private void EnsureGhost(LayoutSpec spec, int tileId)
    {
        if (GhostSlot == null || TileScene == null)
            return;

        if (_ghost == null)
        {
            _ghost = TileScene.Instantiate<DominoTile>();
            GhostSlot.AddChild(_ghost);

            for (var i = 0; i < _frame.Length; i++)
            {
                _frame[i] = new MeshInstance3D
                {
                    Mesh = new CapsuleMesh { RadialSegments = 12, Rings = 4 },
                    MaterialOverride = _outlineMaterial,
                };

                GhostSlot.AddChild(_frame[i]);
            }
        }

        if (_ghost.TileId != tileId)
        {
            _ghost.Configure(tileId, spec);
            // Faded rather than repainted, so the pips still read through the preview.
            _ghost.SetDimmed(true, GhostOpacity);
        }

        BuildFrame(spec);
    }

    /// <summary>
    /// Four rounded bars boxing the tile in, at the tile's own height.
    ///
    /// Capsules rather than boxes: their hemispherical ends meet at the corners and close them off
    /// as rounded joins, so the whole thing reads as a soft rounded rectangle instead of a hard
    /// sharp-cornered crate, which is closer to how the rest of the game looks.
    ///
    /// A flat mat under the tile was the first attempt and looked like a coloured sticker on the
    /// cloth — worse at a shallow angle, which is exactly how a seated player sees the table. A
    /// grown-shell outline would be the usual answer and does not work here at all: the tile is
    /// see-through, so the shell's back faces show straight through it and flood the face.
    /// </summary>
    private void BuildFrame(LayoutSpec spec)
    {
        var radius = OutlineThickness * 0.5f;
        var halfWidth = spec.TileWidth * 0.5f + radius;
        var halfLength = spec.TileLength * 0.5f + radius;

        // A capsule's height is its whole length, caps included, so the mid-section is what is
        // left after them. Sized to reach the corners, where the caps overlap and round the join.
        SetBar(0, spec.TileLength + OutlineThickness, radius, Vector3.Right * -halfWidth, true);
        SetBar(1, spec.TileLength + OutlineThickness, radius, Vector3.Right * halfWidth, true);
        SetBar(2, spec.TileWidth + OutlineThickness, radius, Vector3.Back * -halfLength, false);
        SetBar(3, spec.TileWidth + OutlineThickness, radius, Vector3.Back * halfLength, false);
    }

    private void SetBar(int index, float length, float radius, Vector3 position, bool alongLength)
    {
        if (_frame[index] == null)
            return;

        var capsule = (CapsuleMesh)_frame[index].Mesh;
        capsule.Radius = radius;
        capsule.Height = Mathf.Max(length, radius * 2.0f + 0.0001f);

        // Capsules stand on Y by default; these have to lie flat along the tile's own axes.
        _frame[index].Position = position;
        _frame[index].Rotation = alongLength
            ? new Vector3(Mathf.Pi * 0.5f, 0.0f, 0.0f)
            : new Vector3(0.0f, 0.0f, Mathf.Pi * 0.5f);
    }
}

