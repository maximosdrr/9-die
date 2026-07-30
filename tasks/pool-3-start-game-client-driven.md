# POOL-3 — `WaitingGameStart`/`GameStarting` são client-driven onde deveriam ser server-authoritative

**Área**: Lógica de Sinuca (Table States)
**Prioridade**: 🔴 Crítico

## Problema

`Core/Table/States/WaitingGameStart.cs:39-50` — "aperte F pra começar" roda localmente em cada peer, usando a lista de jogadores computada localmente (`PlayersOnInfluencyArea`) como roster.

`GameStarting.cs:26-38` — o timer de contagem de 3s roda **independente em cada peer**, cada um chamando `ChangeState(GAME_STARTED,...)` por conta própria; só não quebra visivelmente porque `StateMachine.ChangeState` absorve mudanças redundantes pro mesmo estado.

Ligado a [infra-1](infra-1-table-state-sync-sem-validacao.md) (o sync desse state machine também não valida remetente).

## Direção sugerida

Mover o timer e a validação do roster pro servidor, sincronizar com a correção do INFRA-1.
