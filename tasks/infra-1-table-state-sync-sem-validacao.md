# INFRA-1 — `PublicStateSyncronizer` aceita mudança de estado de qualquer peer sem validar remetente

**Área**: Infraestrutura (StateMachine/Network)
**Prioridade**: 🔴 Crítico

## Problema

`Core/StateMachine/Network/PublicStateSyncronizer.cs:37-56` (`RemoteSyncState`) nunca checa `Multiplayer.GetRemoteSenderId()` contra nada — aplica e rebroadcasta qualquer `(type, metadata)` que chegar. É o synchronizer usado pela `StateMachine` da `Table` (`Core/Table/Table.tscn:38`), que controla o ciclo `GAME_WAITING_START → GAME_STARTING → GAME_STARTED → GAME_FINISHED`.

Comparar com `AuthorityStateSynchronizer.RemoteSyncState` (`AuthorityStateSynchronizer.cs:40-66`), que rejeita remetente diferente do `GetMultiplayerAuthority()` — e que `Features/Player/Player.tscn:4164-4185` já usa corretamente.

Qualquer cliente pode hoje forjar `type="GAME_STARTED"` com uma lista de jogadores arbitrária e o host aceita.

Ver também [pool-3](pool-3-start-game-client-driven.md) (mesmo problema raiz, consequência direta neste mesmo fluxo).

## Direção sugerida

Decidir deliberadamente se o sync da Table deveria validar remetente (trocar pra `AuthorityStateSynchronizer` ou adicionar validação equivalente).
