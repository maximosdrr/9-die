extends Node

func _ready() -> void:
	_initialize_steam()

func _initialize_steam() -> void:
	var response = Steam.steamInitEx(480, true)
	if response["status"] != Steam.SteamAPIInitResult.STEAM_API_INIT_RESULT_OK:
		push_error("Steam Init Failed: " + str(response))
		return
		
	Steam.initRelayNetworkAccess()
	print("Steam Initialized. User ID: ", Steam.getSteamID())
