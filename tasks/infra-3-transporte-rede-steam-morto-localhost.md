# INFRA-3 — Transporte de rede: Steam morto, `JoinSession` hardcoded pra localhost

**Área**: Infraestrutura (Network)
**Prioridade**: 🟠 Bug
**Status**: 🟡 Parcialmente resolvido em 29/07/2026 — corrigidos os bugs concretos: `CreateHost`/`JoinSession` do `ENetNetworkProvider` agora dão `return` de verdade depois do erro de validação (antes seguiam executando mesmo assim); `CreateServer` passa `MaxPlayers` (4) como `maxClients` (antes usava o default do Godot, 32 — nada impedia um 5º peer). `NetworkManager.JoinSession` ganhou um parâmetro `hostAddress` (default `127.0.0.1`, mantendo o botão `JoinSessionLocal` funcionando igual) — a plumbing pra conectar num host remoto real agora existe.
**Não resolvido, de propósito**: não existe UI pra digitar um IP remoto (`LobbyMenu`/`Menu.tscn` só tem o botão local + lista de lobby do Steam, que está morta) — isso é trabalho de UI novo, não um bug pontual, deixei de fora pra não inventar uma tela sem ter sido pedido. Decisão Steam-vs-ENet também não foi tomada (Steam continua desligado, código morto intacto — já coberto pelo [infra-9](infra-simplificacao.md)).

## Problema

`Autoload/Network/NetworkManager.cs`: `SteamNetworkProvider` está 100% implementado (116 linhas) mas permanentemente desligado por uma linha comentada.

`JoinSession(int lobbyId = 0)` sempre conecta em `127.0.0.1:7777` — não existe caminho hoje pra um cliente entrar no IP real de um host remoto via ENet.

`NetworkProvider.MaxPlayers=4` só é consumido pelo caminho Steam morto — `ENetNetworkProvider.CreateHost` (`Autoload/Network/ENet/ENetNetworkProvider.cs:27`) chama `_enet.CreateServer(port)` sem `maxClients`, que default pra 32 no Godot — nada impede um 5º peer.

Bug adicional no mesmo arquivo: `CreateHost`/`JoinSession` (linhas 18-27, 39-48) detectam porta/endereço inválido, dão `GD.PushError`, mas **não retornam** — executam a operação mesmo assim.

## Direção sugerida

Escolher um transporte e desativar o outro de forma limpa; fazer `JoinSession` aceitar endereço real; passar `MaxPlayers` pro `CreateServer`; adicionar `return` após os erros de validação.
