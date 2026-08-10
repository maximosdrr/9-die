using System.Collections.Generic;
using Domino.Rules;
using Godot;

/// <summary>
/// Builds and animates the local player's fanned hand, including carousel navigation.
/// </summary>
public partial class DominoHand3DView
{
    private readonly List<DominoTile> _fan = new();
    private float _fanCarouselCenter;
    private float _fanCarouselTarget;
    private int _carouselHandSize;
    private bool _fanCarouselInitialized;

    private void RebuildFan()
    {
        if (TileSlots == null || TileScene == null)
            return;

        EnsureFanCarouselRange();

        while (_fan.Count > _hand.Length)
        {
            var last = _fan[^1];
            _fan.RemoveAt(_fan.Count - 1);
            last.QueueFree();
        }

        var spec = Game?.ChainPresenter?.Spec ?? LayoutSpec.Default;

        for (var i = 0; i < _hand.Length; i++)
        {
            if (i == _fan.Count)
            {
                var tile = TileScene.Instantiate<DominoTile>();
                TileSlots.AddChild(tile);
                _fan.Add(tile);
            }

            var node = _fan[i];
            if (node.TileId != _hand[i])
                node.Configure(_hand[i], spec);

            node.Transform = FanTransform(i, _hand.Length);
        }
    }

    /// <summary>The fan's shape, as the shared splay maths wants it.</summary>
    private HandFanSpec FanSpec =>
        new(FanStepDeg, FanRadius, SelectedLift, TileTiltDeg, HandFan.LongAxisUp);

    /// <summary>
    /// Where a held tile sits. The splay itself is <see cref="HandFan"/>; what belongs to dominoes
    /// is only which tile is selected and how far the carousel has slid.
    /// </summary>
    private Transform3D FanTransform(int index, int count)
    {
        var centre = _fanCarouselInitialized ? _fanCarouselCenter : HandFan.NaturalCentre(count);
        return HandFan.SlotTransform(index, centre, index == SelectedIndex, FanSpec);
    }

    /// <summary>
    /// Only the lift marks the selection. Fading the rest broke the illusion of holding a real hand
    /// of tiles, and nothing here touches the preview: while the player is still choosing there is
    /// deliberately no preview at all, so where a tile fits is something they find out by trying it.
    /// </summary>
    private void ApplyFanHighlight()
    {
        for (var i = 0; i < _fan.Count && i < _hand.Length; i++)
            _fan[i].Transform = FanTransform(i, _hand.Length);
    }

    /// <summary>
    /// Keeps the selected tile inside a configurable central window. The centre only advances one
    /// slot at a time near either edge, so browsing a large hand reads as a carousel rather than the
    /// whole fan snapping to every selection.
    /// </summary>
    private void UpdateFanCarouselTarget()
    {
        EnsureFanCarouselRange();
        if (_hand.Length == 0 || SelectedIndex < 0)
            return;

        var visible = Mathf.Clamp(CarouselVisibleTiles, 1, _hand.Length);
        if (_hand.Length <= visible)
        {
            _fanCarouselTarget = (_hand.Length - 1) * 0.5f;
            return;
        }

        var halfWindow = (visible - 1) * 0.5f;
        if (SelectedIndex < _fanCarouselTarget - halfWindow)
            _fanCarouselTarget = SelectedIndex + halfWindow;
        else if (SelectedIndex > _fanCarouselTarget + halfWindow)
            _fanCarouselTarget = SelectedIndex - halfWindow;

        _fanCarouselTarget = Mathf.Clamp(
            _fanCarouselTarget,
            halfWindow,
            _hand.Length - 1 - halfWindow);
    }

    private void EnsureFanCarouselRange()
    {
        var normalCentre = _hand.Length > 0 ? (_hand.Length - 1) * 0.5f : 0.0f;
        if (!_fanCarouselInitialized || _carouselHandSize == 0)
        {
            _fanCarouselCenter = normalCentre;
            _fanCarouselTarget = normalCentre;
            _fanCarouselInitialized = true;
        }

        _carouselHandSize = _hand.Length;
        if (_hand.Length == 0)
        {
            _fanCarouselCenter = 0.0f;
            _fanCarouselTarget = 0.0f;
            return;
        }

        var visible = Mathf.Clamp(CarouselVisibleTiles, 1, _hand.Length);
        var halfWindow = (visible - 1) * 0.5f;
        var minCentre = _hand.Length <= visible ? normalCentre : halfWindow;
        var maxCentre = _hand.Length <= visible ? normalCentre : _hand.Length - 1 - halfWindow;

        _fanCarouselCenter = Mathf.Clamp(_fanCarouselCenter, minCentre, maxCentre);
        _fanCarouselTarget = Mathf.Clamp(_fanCarouselTarget, minCentre, maxCentre);
    }

    private void UpdateFanCarousel(float delta)
    {
        if (!_fanCarouselInitialized || Mathf.IsEqualApprox(_fanCarouselCenter, _fanCarouselTarget))
            return;

        var response = 1.0f - Mathf.Exp(-Mathf.Max(CarouselSlideSpeed, 1.0f) * delta);
        _fanCarouselCenter = Mathf.Lerp(_fanCarouselCenter, _fanCarouselTarget, response);
        if (Mathf.Abs(_fanCarouselCenter - _fanCarouselTarget) < 0.001f)
            _fanCarouselCenter = _fanCarouselTarget;

        ApplyFanHighlight();
    }
}

