using System.Collections.Generic;
using Domino.Rules;
using Godot;

/// <summary>
/// Local-only guidance that frames the occupied stock while this player may draw.
/// </summary>
public partial class DominoHand3DView
{
    private Node3D _stockHighlight;
    private MeshInstance3D _stockHighlightGlow;
    private StandardMaterial3D _stockHighlightGlowMaterial;
    private float _stockHighlightPulseTime;

    /// <summary>
    /// Draws a thin emissive frame around the occupied stock. This remains a local-only hint:
    /// whether this player's private hand has a legal move is not information sent to opponents.
    /// </summary>
    private void SetStockHighlight(bool highlighted)
    {
        ShouldHighlightStock = highlighted;

        if (!highlighted || Game?.ChainPresenter == null || Game.BoneyardSlots.Length == 0)
        {
            _stockHighlightPulseTime = 0.0f;
            if (IsInstanceValid(_stockHighlight))
                _stockHighlight.Hide();
            return;
        }

        EnsureStockHighlight();
        if (!IsInstanceValid(_stockHighlight)
            || !TryOccupiedStockBounds(Game.BoneyardSlots, Game.StockSpec, out var bounds))
            return;

        _stockHighlight.GlobalTransform = Game.ChainPresenter.GlobalTransform;

        var padding = Mathf.Max(StockHighlightPadding, 0.0f);
        bounds = bounds.Grow(padding);

        var thickness = Mathf.Max(StockHighlightThickness, 0.002f);
        var glowWidth = Mathf.Max(StockHighlightGlowWidth, 0.0f);
        var cornerRadius = Mathf.Max(StockHighlightCornerRadius, thickness);
        var cornerSteps = Mathf.Max(StockHighlightCornerSteps, 2);
        // The frame surrounds rather than covers the tiles, so it can sit directly on the cloth.
        var y = Mathf.Max(StockHighlightLift, thickness * 0.13f);
        var centre = bounds.GetCenter();

        // One softly emissive ring is enough. The previous opaque inner frame made the guidance
        // look permanently selected even at the dim point of the pulse.
        var haloDepth = thickness + glowWidth;
        _stockHighlightGlow.Mesh = BuildRoundedFrame(
            bounds.Size + Vector2.One * haloDepth * 2.0f,
            haloDepth,
            cornerRadius + haloDepth,
            cornerSteps,
            _stockHighlightGlowMaterial);
        _stockHighlightGlow.Position = new Vector3(centre.X, y, centre.Y);

        _stockHighlight.Show();
    }

    private void UpdateStockHighlightPulse(float delta)
    {
        if (!ShouldHighlightStock || !IsInstanceValid(_stockHighlight)
            || !_stockHighlight.Visible || _stockHighlightGlowMaterial == null)
            return;

        _stockHighlightPulseTime += delta;
        var duration = Mathf.Max(StockHighlightPulseSeconds, 0.2f);
        // Cosine starts bright and eases naturally at both ends instead of blinking linearly.
        var pulse = 0.5f + 0.5f * Mathf.Cos(_stockHighlightPulseTime * Mathf.Tau / duration);
        var minGlowAlpha = Mathf.Clamp(StockHighlightGlowMinAlpha, 0.0f, 1.0f);
        var maxGlowAlpha = Mathf.Clamp(StockHighlightGlowMaxAlpha, minGlowAlpha, 1.0f);
        var glowColour = StockHighlightColor;
        glowColour.A = Mathf.Lerp(minGlowAlpha, maxGlowAlpha, pulse);
        _stockHighlightGlowMaterial.AlbedoColor = glowColour;
        _stockHighlightGlowMaterial.Emission = glowColour;
        _stockHighlightGlowMaterial.EmissionEnergyMultiplier = Mathf.Lerp(0.28f, 0.78f, pulse);
    }

    private void EnsureStockHighlight()
    {
        if (IsInstanceValid(_stockHighlight) || Game?.ChainPresenter == null)
            return;

        _stockHighlightGlowMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = StockHighlightColor with { A = StockHighlightGlowMinAlpha },
            EmissionEnabled = true,
            Emission = StockHighlightColor with { A = StockHighlightGlowMinAlpha },
            EmissionEnergyMultiplier = 0.28f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };

        // Keep this out of ChainPresenter: its children have the strict semantic of one rendered
        // domino per play, which rules and integration checks rely on.
        _stockHighlight = new Node3D { Name = "LocalStockHighlight" };
        AddChild(_stockHighlight);
        _stockHighlight.TopLevel = true;
        _stockHighlight.GlobalTransform = Game.ChainPresenter.GlobalTransform;

        _stockHighlightGlow = new MeshInstance3D
        {
            Name = "SoftGlow",
            MaterialOverride = _stockHighlightGlowMaterial,
        };
        _stockHighlight.AddChild(_stockHighlightGlow);
    }

    private static ImmediateMesh BuildRoundedFrame(
        Vector2 outerSize,
        float thickness,
        float requestedOuterRadius,
        int cornerSteps,
        Material material)
    {
        var outerHalf = outerSize * 0.5f;
        thickness = Mathf.Clamp(thickness, 0.001f, Mathf.Min(outerHalf.X, outerHalf.Y) - 0.001f);
        var innerHalf = outerHalf - Vector2.One * thickness;
        var outerRadius = Mathf.Clamp(requestedOuterRadius, thickness, Mathf.Min(outerHalf.X, outerHalf.Y));
        var innerRadius = Mathf.Max(outerRadius - thickness, 0.001f);
        cornerSteps = Mathf.Max(cornerSteps, 2);

        var outer = RoundedRectanglePerimeter(outerHalf, outerRadius, cornerSteps);
        var inner = RoundedRectanglePerimeter(innerHalf, innerRadius, cornerSteps);
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, material);

        for (var point = 0; point < outer.Count; point++)
        {
            var next = (point + 1) % outer.Count;
            var outer0 = new Vector3(outer[point].X, 0.0f, outer[point].Y);
            var outer1 = new Vector3(outer[next].X, 0.0f, outer[next].Y);
            var inner0 = new Vector3(inner[point].X, 0.0f, inner[point].Y);
            var inner1 = new Vector3(inner[next].X, 0.0f, inner[next].Y);

            AddHighlightTriangle(mesh, inner0, outer0, outer1);
            AddHighlightTriangle(mesh, inner0, outer1, inner1);
        }

        mesh.SurfaceEnd();
        return mesh;
    }

    private static List<Vector2> RoundedRectanglePerimeter(Vector2 half, float radius, int cornerSteps)
    {
        var points = new List<Vector2>((cornerSteps + 1) * 4);
        var centres = new[]
        {
            new Vector2(half.X - radius, half.Y - radius),
            new Vector2(-half.X + radius, half.Y - radius),
            new Vector2(-half.X + radius, -half.Y + radius),
            new Vector2(half.X - radius, -half.Y + radius),
        };

        for (var corner = 0; corner < centres.Length; corner++)
        {
            var startAngle = corner * Mathf.Pi * 0.5f;
            for (var step = 0; step <= cornerSteps; step++)
            {
                var angle = startAngle + step / (float)cornerSteps * Mathf.Pi * 0.5f;
                points.Add(centres[corner] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
        }

        return points;
    }

    private static void AddHighlightTriangle(ImmediateMesh mesh, Vector3 a, Vector3 b, Vector3 c)
    {
        mesh.SurfaceSetNormal(Vector3.Up);
        mesh.SurfaceAddVertex(a);
        mesh.SurfaceAddVertex(b);
        mesh.SurfaceAddVertex(c);
    }

    private static bool TryOccupiedStockBounds(
        IReadOnlyList<int> occupiedSlots,
        SlotGridSpec spec,
        out Rect2 bounds)
    {
        bounds = default;
        if (occupiedSlots == null || occupiedSlots.Count == 0)
            return false;

        var half = SlotGrid.HalfExtents(spec);
        var hasSlot = false;
        foreach (var slot in occupiedSlots)
        {
            if (slot < 0)
                continue;

            var centre = SlotGrid.SlotPosition(slot, spec);
            if (!hasSlot)
            {
                bounds = new Rect2(centre - half, half * 2.0f);
                hasSlot = true;
                continue;
            }

            bounds = bounds.Expand(centre - half).Expand(centre + half);
        }

        return hasSlot;
    }
}
