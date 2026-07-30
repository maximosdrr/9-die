# INFRA-3 — Transporte de rede: Steam morto, `JoinSession` hardcoded pra localhost

**Área**: Infraestrutura (Network)
**Prioridade**: 🟠 Bug

## Problema

`Autoload/Network/NetworkManager.cs`: `SteamNetworkProvider` está 100% implementado (116 linhas) mas permanentemente desligado por uma linha comentada.

`JoinSession(int lobbyId = 0)` sempre conecta em `127.0.0.1:7777` — não existe caminho hoje pra um cliente entrar no IP real de um host remoto via ENet.

`NetworkProvider.MaxPlayers=4` só é consumido pelo caminho Steam morto — `ENetNetworkProvider.CreateHost` (`Autoload/Network/ENet/ENetNetworkProvider.cs:27`) chama `_enet.CreateServer(port)` sem `maxClients`, que default pra 32 no Godot — nada impede um 5º peer.

Bug adicional no mesmo arquivo: `CreateHost`/`JoinSession` (linhas 18-27, 39-48) detectam porta/endereço inválido, dão `GD.PushError`, mas **não retornam** — executam a operação mesmo assim.

## Direção sugerida

Escolher um transporte e desativar o outro de forma limpa; fazer `JoinSession` aceitar endereço real; passar `MaxPlayers` pro `CreateServer`; adicionar `return` após os erros de validação.
