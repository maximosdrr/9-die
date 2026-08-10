using Godot;

/// <summary>How a hand of flat things is splayed in front of a seated player's camera.</summary>
public readonly struct HandFanSpec
{
    /// <summary>Angle between neighbours. Fixed per item, so a bigger hand simply fans wider.</summary>
    public readonly float StepDegrees;

    /// <summary>Distance from the pivot the items hang off, which sets how flat the fan is.</summary>
    public readonly float Radius;

    public readonly float SelectedLift;

    /// <summary>Backward lean, so the faces angle toward the player's eyes.</summary>
    public readonly float TiltDegrees;

    /// <summary>How one item stands when held — see <see cref="HandFan.LongAxisUp"/>.</summary>
    public readonly Basis Upright;

    public HandFanSpec(float stepDegrees, float radius, float selectedLift, float tiltDegrees, Basis upright)
    {
        StepDegrees = stepDegrees;
        Radius = radius;
        SelectedLift = selectedLift;
        TiltDegrees = tiltDegrees;
        Upright = upright;
    }
}

/// <summary>
/// Splays a hand like a hand of cards: everything hangs off ONE pivot below the hand at a fixed
/// radius, evenly spaced by angle, each item rolled by its own angle.
///
/// The first version placed them around an arc and swung them about the vertical, which put each
/// item at a different distance from the eye — under perspective they came out at mismatched sizes
/// and angles and looked spilled rather than held. Rolling about a shared pivot keeps every item the
/// same distance away, so the overlap is even and the fan reads as one object.
/// </summary>
public static class HandFan
{
    /// <summary>
    /// A tile or card lies face-up with its long axis on +Z. Held in a hand it stands upright with
    /// the face toward the player, so the long axis goes up and the face swings back — written as
    /// explicit axis images because composing this out of Euler angles is how it ended up edge-on.
    /// </summary>
    public static readonly Basis LongAxisUp = new(
        new Vector3(-1.0f, 0.0f, 0.0f),
        new Vector3(0.0f, 0.0f, 1.0f),
        new Vector3(0.0f, 1.0f, 0.0f));

    /// <summary>
    /// The same pose for an item whose length runs along its own −Z rather than +Z.
    ///
    /// A PokerCard is built that way: its quad puts width on +X and length on −Z so that width ×
    /// length gives the +Y face normal. Held with <see cref="LongAxisUp"/> it comes out upside down
    /// — which reads as mirrored text at a glance and cost a render to spot, hence a named basis
    /// instead of a sign buried somewhere.
    /// </summary>
    public static readonly Basis LongAxisUpFromMinusZ = new(
        new Vector3(1.0f, 0.0f, 0.0f),
        new Vector3(0.0f, 0.0f, 1.0f),
        new Vector3(0.0f, -1.0f, 0.0f));

    /// <summary>
    /// Where item <paramref name="index"/> sits, relative to the hand rig.
    ///
    /// <paramref name="centre"/> is which index the fan is currently centred on — normally
    /// <c>(count - 1) * 0.5</c>, but a hand too big for the screen slides it so the selection stays
    /// in view.
    /// </summary>
    public static Transform3D SlotTransform(int index, float centre, bool selected, HandFanSpec spec)
    {
        var angle = (index - centre) * Mathf.DegToRad(spec.StepDegrees);

        // Hanging off a pivot below: at angle zero the item sits at the hand's origin.
        var position = new Vector3(
            Mathf.Sin(angle) * spec.Radius,
            Mathf.Cos(angle) * spec.Radius - spec.Radius + (selected ? spec.SelectedLift : 0.0f),
            // Each item a hair nearer than the one before, so they always layer the same way
            // instead of fighting over which is in front.
            index * 0.0015f + (selected ? 0.012f : 0.0f));

        var lean = Basis.FromEuler(new Vector3(Mathf.DegToRad(spec.TiltDegrees), 0.0f, 0.0f));
        var roll = Basis.FromEuler(new Vector3(0.0f, 0.0f, -angle));

        return new Transform3D(lean * roll * spec.Upright, position);
    }

    /// <summary>The centre a hand rests at when every item fits on screen at once.</summary>
    public static float NaturalCentre(int count) => (count - 1) * 0.5f;
}
