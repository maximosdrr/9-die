# POOL-3 — `WaitingGameStart`/`GameStarting` são client-driven onde deveriam ser server-authoritative

**Área**: Lógica de Sinuca (Table States)
**Prioridade**: 🔴 Crítico
**Status**: ✅ Resolvido em 29/07/2026, junto com [infra-1](infra-1-table-state-sync-sem-validacao.md) — depois que a Table passou a usar `AuthorityStateSynchronizer` (que só aceita `ChangeState` de quem tem autoridade), `WaitingGameStart` não podia mais chamar `ChangeState` direto no cliente (seria ignorado). Virou pedido: `RequestStartGame()` manda `RpcId(1,...)` pro servidor se não for o servidor; o servidor recalcula o roster ele mesmo via `TableInfluence.GetOverlappingBodies()` (não confia na lista que o cliente mandaria) e confere que quem pediu está fisicamente ali antes de mudar de estado. `GameStarting.cs` não precisou mudar — o timer de cada cliente virou só cosmético (o `ChangeState` pro `GAME_STARTED` que ele dispara é automaticamente ignorado por não ter autoridade); só o timer do servidor efetivamente transiciona e propaga.

## Problema

`Core/Table/States/WaitingGameStart.cs:39-50` — "aperte F pra começar" roda localmente em cada peer, usando a lista de jogadores computada localmente (`PlayersOnInfluencyArea`) como roster.

`GameStarting.cs:26-38` — o timer de contagem de 3s roda **independente em cada peer**, cada um chamando `ChangeState(GAME_STARTED,...)` por conta própria; só não quebra visivelmente porque `StateMachine.ChangeState` absorve mudanças redundantes pro mesmo estado.

Ligado a [infra-1](infra-1-table-state-sync-sem-validacao.md) (o sync desse state machine também não valida remetente).

## Direção sugerida

Mover o timer e a validação do roster pro servidor, sincronizar com a correção do INFRA-1.
