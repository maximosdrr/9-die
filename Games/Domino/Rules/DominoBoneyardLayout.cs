using Godot;

namespace Domino.Rules;

/// <summary>
/// How the face-down stock is laid out, so the player can pick from it without it leaking.
///
/// The mechanism is <see cref="SlotGrid"/>, shared with anything else on a table that is addressed
/// by place rather than by contents. All that lives here is the double-six tuning: how wide the
/// rows are and where on the cloth they sit.
/// </summary>
public static class DominoBoneyardLayout
{
    /// <summary>Sat beyond the playing area, which reaches z = 0.32, and inside the 0.60 m table.</summary>
    public static SlotGridSpec Default =>
        new(0.072f, 0.036f, 0.004f, 7, new Vector2(0.0f, 0.38f));
}
