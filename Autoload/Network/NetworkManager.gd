extends Node

var network_provider: NetworkProvider

func _ready() -> void:
	network_provider = ENetNetworkProvider.new()
	add_child(network_provider)

func create_host_session() -> void:
	network_provider.create_host(7777)

func join_session() -> void:
	network_provider.join_session(0, "127.0.0.1", 7777)

func refresh_lobby_list() -> void:
	network_provider.refresh_lobby_list()
