using Godot;

public partial class Global : Node
{
    private const string ReconnectTokenPath = "user://reconnect_token.txt";

    public static Global Instance { get; private set; }

    public string LocalNickname = "";
    public string ReconnectToken { get; private set; }

    public override void _EnterTree()
    {
        Instance = this;
        ReconnectToken = LoadOrCreateReconnectToken();
    }

    // Stable identity across reconnections — the game has no login system, only
    // the peer ID the transport assigns per connection, which changes every time
    // someone reconnects. This token, persisted locally, is what lets the server
    // recognize "this new peer is the same person who just dropped".
    private static string LoadOrCreateReconnectToken()
    {
        if (FileAccess.FileExists(ReconnectTokenPath))
        {
            using var existing = FileAccess.Open(ReconnectTokenPath, FileAccess.ModeFlags.Read);
            if (existing != null)
            {
                var token = existing.GetAsText().Trim();
                if (System.Guid.TryParseExact(token, "N", out var parsed)
                    && parsed != System.Guid.Empty)
                {
                    return parsed.ToString("N");
                }
            }
        }

        var newToken = System.Guid.NewGuid().ToString("N");
        using var file = FileAccess.Open(ReconnectTokenPath, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            GD.PushWarning(
                "Reconnect token could not be persisted; this session will use an ephemeral token.");
            return newToken;
        }

        file.StoreString(newToken);
        return newToken;
    }
}
