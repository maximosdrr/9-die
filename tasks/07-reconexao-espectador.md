# 07 — Reconexão / espectador

**Prioridade**: Baixa (robustez e polimento)
**Status**: 🟡 Investigado em 29/07/2026 — espectador já funciona, reconexão adiada (decisão do dono do projeto)

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

## Decisão

Nenhum código alterado. Espectador documentado como já resolvido por construção. Reconexão de verdade fica pendente — quando for retomada, o ponto de partida é decidir o esquema de identidade (a peça que trava tudo o resto) antes de mexer em código.

## Verificação

N/A — nenhuma mudança de código neste item.
