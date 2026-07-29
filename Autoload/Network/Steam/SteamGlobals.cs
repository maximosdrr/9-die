using Godot;

public partial class SteamGlobals : Node
{
    public static SteamGlobals Instance { get; private set; }

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        InitializeSteam();
    }

    private void InitializeSteam()
    {
        var steam = Engine.GetSingleton("Steam");
        var response = (Godot.Collections.Dictionary)steam.Call("steamInitEx", 480, true);
        var initResultOk = (long)steam.Get("STEAM_API_INIT_RESULT_OK");

        if ((long)response["status"] != initResultOk)
        {
            GD.PushError("Steam Init Failed: " + response);
            return;
        }

        steam.Call("initRelayNetworkAccess");
        GD.Print("Steam Initialized. User ID: ", steam.Call("getSteamID"));
    }
}
