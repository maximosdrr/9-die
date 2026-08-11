using Godot;

/// <summary>
/// Pure validation rules for the walking protocol. Keeping them outside <see cref="Player"/>
/// makes the trust boundary deterministic to test without starting a network peer or a physics
/// world.
/// </summary>
internal static class PlayerMovementProtocol
{
    public const int ServerPeerId = 1;
    public const int TransferChannel = 3;
    public const int DefaultMaxPacketsPerSecond = 75;
    public const ulong DefaultInputTimeoutMilliseconds = 250;
    public const float MaximumIntentLength = 1.001f;

    public static bool IsExpectedSender(int senderPeerId, int playerPeerId) =>
        senderPeerId > 0 && senderPeerId == playerPeerId;

    public static bool IsFinite(float value) => float.IsFinite(value);

    public static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    public static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    public static bool IsValidIntent(Vector2 intent) =>
        IsFinite(intent) && intent.LengthSquared() <= MaximumIntentLength * MaximumIntentLength;

    public static bool IsFreshSequence(int sequence, int previousSequence, bool hasPrevious) =>
        sequence > 0 && (!hasPrevious || sequence > previousSequence);

    public static bool HasTimedOut(ulong nowMilliseconds, ulong acceptedAtMilliseconds,
        ulong timeoutMilliseconds) =>
        nowMilliseconds < acceptedAtMilliseconds
        || nowMilliseconds - acceptedAtMilliseconds > timeoutMilliseconds;

    public static bool IsValidSnapshot(Vector3 position, float yaw, Vector3 velocity) =>
        IsFinite(position) && IsFinite(yaw) && IsFinite(velocity);

    /// <summary>
    /// Server snapshots correct the owning client's position, never its local camera yaw. That
    /// yaw remains an untrusted target on the server, where it is validated and rate-limited for
    /// the authoritative body seen by observers. Falling back prevents a non-finite local
    /// transform from poisoning the scene.
    /// </summary>
    public static float ResolveOwningClientYaw(float localYaw, float serverYaw) =>
        Mathf.Wrap(IsFinite(localYaw) ? localYaw : serverYaw, -Mathf.Pi, Mathf.Pi);
}

/// <summary>
/// Per-player server-side gate. It validates ownership, packet budget, monotonic sequence and
/// values before an intent is allowed to affect physics.
/// </summary>
internal sealed class PlayerMovementInputGuard
{
    private readonly int _expectedPeerId;
    private readonly int _maxPacketsPerSecond;
    private bool _windowStarted;
    private ulong _windowStartedAt;
    private int _packetsInWindow;
    private bool _hasSequence;

    public int LastSequence { get; private set; }
    public bool HasAcceptedInput => _hasSequence;
    public Vector2 LatestIntent { get; private set; }
    public float LatestYaw { get; private set; }
    public ulong LastAcceptedAt { get; private set; }

    public PlayerMovementInputGuard(int expectedPeerId, int maxPacketsPerSecond)
    {
        _expectedPeerId = expectedPeerId;
        _maxPacketsPerSecond = Mathf.Max(1, maxPacketsPerSecond);
    }

    public bool TryAccept(int senderPeerId, int sequence, Vector2 intent, float yaw,
        ulong nowMilliseconds)
    {
        if (!PlayerMovementProtocol.IsExpectedSender(senderPeerId, _expectedPeerId))
            return false;

        // Count malformed and stale packets too. Otherwise an attacker could avoid the rate
        // budget merely by repeating a sequence or sending NaN payloads.
        if (!TryConsumePacket(nowMilliseconds))
            return false;

        if (!PlayerMovementProtocol.IsFreshSequence(sequence, LastSequence, _hasSequence)
            || !PlayerMovementProtocol.IsValidIntent(intent)
            || !PlayerMovementProtocol.IsFinite(yaw))
            return false;

        LastSequence = sequence;
        _hasSequence = true;
        LatestIntent = intent.LengthSquared() > 1.0f ? intent.Normalized() : intent;
        LatestYaw = Mathf.Wrap(yaw, -Mathf.Pi, Mathf.Pi);
        LastAcceptedAt = nowMilliseconds;
        return true;
    }

    public Vector2 ActiveIntent(ulong nowMilliseconds, ulong timeoutMilliseconds) =>
        !_hasSequence || PlayerMovementProtocol.HasTimedOut(
            nowMilliseconds, LastAcceptedAt, timeoutMilliseconds)
            ? Vector2.Zero
            : LatestIntent;

    public void Stop(ulong nowMilliseconds)
    {
        LatestIntent = Vector2.Zero;
        LastAcceptedAt = nowMilliseconds;
    }

    private bool TryConsumePacket(ulong nowMilliseconds)
    {
        if (!_windowStarted || nowMilliseconds < _windowStartedAt
            || nowMilliseconds - _windowStartedAt >= 1_000)
        {
            _windowStarted = true;
            _windowStartedAt = nowMilliseconds;
            _packetsInWindow = 0;
        }

        if (_packetsInWindow >= _maxPacketsPerSecond)
            return false;

        _packetsInWindow++;
        return true;
    }
}
