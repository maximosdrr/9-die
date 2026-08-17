using System;
using Godot;

public partial class SteamGlobals : Node
{
	private const string AppIdSetting = "steam/initialization/app_id";
	private const ulong SpacewarDevelopmentAppId = 480;

	public static SteamGlobals Instance { get; private set; }

	public override void _EnterTree()
	{
		Instance = this;
	}

	public override void _Ready()
	{
		var transport = ProjectSettings.GetSetting("network/transport", "enet").AsString();
		if (!transport.Equals("steam", StringComparison.OrdinalIgnoreCase))
			return;

		InitializeSteam();
	}

	private void InitializeSteam()
	{
		var appId = (ulong)(long)ProjectSettings.GetSetting(AppIdSetting, 0);
		var configurationError = ValidateConfiguration(appId, Engine.HasSingleton("Steam"));
		if (configurationError != null)
		{
			GD.PushError(configurationError);
			return;
		}

		if (appId == SpacewarDevelopmentAppId)
			GD.PushWarning("Steam App ID 480 is for local Spacewar development only; configure the production App ID before release.");

		var steam = Engine.GetSingleton("Steam");
		var response = (Godot.Collections.Dictionary)steam.Call("steamInitEx", appId, true);
		var initResultOk = (long)steam.Get("STEAM_API_INIT_RESULT_OK");

		if ((long)response["status"] != initResultOk)
		{
			GD.PushError("Steam Init Failed: " + response);
			return;
		}

		steam.Call("initRelayNetworkAccess");
		GD.Print("Steam Initialized. User ID: ", steam.Call("getSteamID"));
	}

	internal static string ValidateConfiguration(ulong appId, bool hasSteamSingleton)
	{
		if (appId == 0)
			return $"Steam transport requires a non-zero '{AppIdSetting}' project setting.";

		if (!hasSteamSingleton)
			return "Steam transport is configured, but the Steam singleton is unavailable.";

		return null;
	}
}
