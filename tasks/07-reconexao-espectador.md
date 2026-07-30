# 07 — Reconexão / espectador

**Prioridade**: Baixa (robustez e polimento)
**Status**: ✅ Resolvido em 30/07/2026 — reconexão implementada com token persistente + 60s de graça

## Achado — espectador já funciona sem nenhuma mudança

`LevelMultiplayerManager.InitializePlayerNode` spawna um `Player` pra qualquer peer que conecta, independente do estado da mesa (`WaitingGameStart`/`GameStarting`/`GameStarted`) — o spawn é disparado só pelo sinal `PlayerConnected`, sem checar nada de `TableGame`. E do lado da mesa, só quem está no `TurnOrder` (definido em `WaitingGameStart.TryStartGame` a partir de quem estava fisicamente na área de influência quando a partida começou) recebe `GameHandler.EquipGameController(...)` em `PoolGame.OnMatchStarts`.

Resultado prático: um 3º ou 4º jogador que conecta durante uma partida em andamento já cai no nível, anda livremente, pode se aproximar da mesa e ver a partida acontecendo (e até assistir pela TV, já que `Tv.tscn` também não faz nenhuma checagem de estado de partida) — sem receber controle da mesa, porque nunca entra no `TurnOrder`. `MaxPlayers=4` (`NetworkProvider.cs`) já permite isso hoje mesmo sendo sinuca um jogo de 2.

Ou seja: **suporte a espectador, no sentido de "assistir uma partida em andamento sem participar dela", já existe e não precisou de nenhuma linha de código nova.**

## Achado — reconexão é uma feature bem maior, não uma correção pontual

Hoje, se o cliente de um jogador cai no meio de uma partida ativa, `TableGame.OnPlayerDisconnected` (item 02) decide o forfeit **na hora**, sem nenhuma janela de espera. Reverter isso pra dar chance de reconexão de verdade exige três peças que não existem hoje:

1. **Tempo de graça** antes de declarar forfeit (fácil — um timer).
2. **Identidade estável entre reconexões** — o jogo não tem sistema de login/conta, só o peer ID que o ENet atribui a cada conexão, e isso muda toda vez que alguém reconecta. Sem alguma forma de dizer "esse novo peer é a mesma pessoa que caiu", não dá pra saber quem deveria retomar o slot.
3. **Re-atribuir autoridade de rede** do `PoolController`/`TurnOrder`/`Table.PlayersOnMatch` (hoje todos indexados pelo peer ID antigo) pro peer ID novo, depois que a identidade for resolvida.

Perguntei ao dono do projeto como proceder, com três opções (documentar só o espectador e adiar reconexão / implementar reconexão com heurística simples de peer ID / só aumentar o tempo de graça sem reatribuir autoridade). Resposta: **documentar só o espectador, adiar reconexão de verdade** — é item de prioridade baixa no backlog, e a peça que falta (identidade estável sem sistema de login) é uma decisão de arquitetura que merece uma conversa própria, não uma resposta rápida dentro dessa varredura.

## Implementado em 30/07/2026

Decisões do dono do projeto: identidade via **token de reconexão persistente** (não apelido, não SteamID) e **60 segundos** de graça.

- **`Global.cs`** — gera um GUID na primeira execução e persiste em `user://reconnect_token.txt`; carregado no boot, sobrevive a reconexões (`ReconnectToken`).
- **`Autoload/ReconnectionManager.cs`** (novo autoload) — ao conectar, todo cliente manda seu token pro servidor via RPC (`RegisterToken`). Guarda `peerId → token` enquanto conectado. `BeginGracePeriod(token, oldPlayerId, table)` registra uma pendência e arma um timer de 60s; se o timer expirar sem reclaim, aplica o forfeit de sempre (`RemovePlayerFromMatch`, motivo `"opponent_disconnected"` — texto inalterado). Se outro peer conectar com o mesmo token antes disso, cancela a pendência e chama `table.ReclaimSlot(...)`.
- **`TableGame.cs`** — `OnPlayerDisconnected` não força mais desistência na hora: se o peer tinha token registrado, abre o período de graça em vez de remover. Novo `ReclaimSlot`/`ApplyPlayerReclaimed` (mesmo padrão de `RemovePlayerFromMatch`/`ApplyPlayerRemoved`: troca a entrada no `TurnOrder`/`Table.PlayersOnMatch` do peer ID antigo pro novo, sincronizado em todo peer via sinal `PlayerReclaimed` + RPC).
- **`TableTurnNetworkBridge.cs`** — relay novo (`RpcSyncPlayerReclaimed`) espelhando o padrão já usado pra `PlayerRemovedFromMatch`.
- **`PoolGame.cs`** — reage ao sinal `PlayerReclaimed`: reequipa o `PoolController` no jogador reconectado (mesmo fluxo de `OnMatchStarts`) e, se a partida estava esperando a vez dele quando caiu, retoma o turno na hora (`ApplyControl`) em vez de esperar o próximo `TurnChanged`.

O nó `Player` antigo continua sendo destruído normalmente ao desconectar (`LevelMultiplayerManager`, comportamento inalterado) — o jogador que reconecta ganha um nó novo (spawn normal, incluindo posição de spawn do zero, não a posição de onde caiu) que é "adotado" pelo slot antigo assim que o token bate.

**Fora do escopo desta rodada, de propósito**: se caiu durante o próprio turno, a partida simplesmente espera (sem opção de pular a vez e retomar depois) — comportamento simples o suficiente pra um jogo de 2 jogadores, evita construir lógica de "pular e resumir turno" sem necessidade concreta. Reconexão do host (peer 1) não é tratada — a sessão inteira depende dele, é um problema bem maior, fora do pedido original.

## Verificação

`dotnet build`/`dotnet build 9Die.sln` limpos. **Não testado ao vivo** — é a feature mais sensível a timing desta sessão (RPC de handshake, timer de 60s, corrida entre spawn normal e reclaim, referência a nó potencialmente liberado em `TurnOwner`). Roteiro sugerido pra testar: (1) jogador em partida ativa fecha o cliente, reabre e reconecta dentro de 60s — deve reassumir o mesmo lugar no `TurnOrder`, com o taco de volta; (2) mesmo cenário mas era a vez dele quando caiu — deve retomar o turno direto, sem precisar de outra ação; (3) deixa passar dos 60s sem reconectar — deve dar forfeit exatamente como hoje; (4) confirma que o HUD/quadro de ranking mostram o jogador certo depois do reclaim.
