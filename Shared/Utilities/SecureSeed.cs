using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

/// <summary>
/// Creates unpredictable seeds for server-owned hidden information. The deterministic game rules
/// still consume a plain <see cref="ulong"/> so tests and replays remain reproducible; only the
/// production source of that seed is cryptographically secure.
/// </summary>
public static class SecureSeed
{
    public static ulong Create()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        RandomNumberGenerator.Fill(bytes);
        return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }
}
