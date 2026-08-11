using System;
using System.Globalization;
using System.Text;

/// <summary>
/// Pure validation rules for player identity data received from an untrusted peer.
/// </summary>
internal static class PlayerProfileProtocol
{
    public const int ServerPeerId = 1;
    public const int MaximumNicknameTextElements = 24;

    // Keeping preprocessing bounded avoids turning a nickname RPC into expensive Unicode work.
    private const int MaximumCandidateCodeUnits = 256;

    public static bool IsExpectedOwner(int senderPeerId, int playerPeerId) =>
        senderPeerId > 0 && senderPeerId == playerPeerId;

    public static string SanitizeNickname(string candidate, int playerPeerId)
    {
        var fallback = $"Player {Math.Max(ServerPeerId, playerPeerId)}";
        if (string.IsNullOrWhiteSpace(candidate))
            return fallback;

        var boundedCandidate = candidate;
        if (boundedCandidate.Length > MaximumCandidateCodeUnits)
        {
            var length = MaximumCandidateCodeUnits;
            if (char.IsHighSurrogate(boundedCandidate[length - 1]))
                length--;
            boundedCandidate = boundedCandidate[..length];
        }

        string normalized;
        try
        {
            normalized = boundedCandidate.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            // Malformed UTF-16 must never escape into labels or replication payloads.
            return fallback;
        }

        var result = new StringBuilder(Math.Min(normalized.Length, MaximumNicknameTextElements));
        var elements = StringInfo.GetTextElementEnumerator(normalized);
        var elementCount = 0;
        var pendingWhitespace = false;

        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (string.IsNullOrWhiteSpace(element))
            {
                pendingWhitespace = result.Length > 0;
                continue;
            }

            if (ContainsProhibitedCharacter(element))
                continue;

            if (pendingWhitespace)
            {
                // Reserve room for the next visible element; never emit a trailing space solely
                // to reach the limit.
                if (elementCount + 1 >= MaximumNicknameTextElements)
                    break;

                result.Append(' ');
                elementCount++;
                pendingWhitespace = false;
            }

            if (elementCount >= MaximumNicknameTextElements)
                break;

            result.Append(element);
            elementCount++;
        }

        var sanitized = result.ToString().Trim();
        return sanitized.Length == 0 ? fallback : sanitized;
    }

    private static bool ContainsProhibitedCharacter(string textElement)
    {
        for (var index = 0; index < textElement.Length;)
        {
            var category = char.GetUnicodeCategory(textElement, index);
            if (category is UnicodeCategory.Control
                or UnicodeCategory.Format
                or UnicodeCategory.Surrogate
                or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator)
            {
                return true;
            }

            index += char.IsSurrogatePair(textElement, index) ? 2 : 1;
        }

        return false;
    }
}
