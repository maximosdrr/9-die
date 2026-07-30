# POOL-6 — `BallPlacementManager.Rpc(...)` provavelmente não alcança espectadores em partida de 3-4 jogadores

**Área**: Lógica de Sinuca (Ball Placement)
**Prioridade**: 🟠 Bug
**Status**: 🔵 Investigado em 29/07/2026 — não encontrei evidência de que seja um bug real. `Rpc()` sem loop manual (broadcast implícito) é o mesmo padrão que `AuthorityStateSynchronizer.OnLocalStateChange` já usa corretamente pra propagar mudança de estado autoritativa da própria Table (`Rpc(MethodName.RemoteSyncState, ...)`, sem loop). Os loops manuais no projeto (`RemoteSyncState`, `TableTurnNetworkBridge`) só existem pra **excluir o remetente original** ao re-repassar algo que já chegou de outro peer — não porque `Rpc()` falhe em alcançar todo mundo. Como o [pool-2](pool-2-ball-placement-sem-validacao.md) já reestruturou esse fluxo pra ser sempre originado no servidor (nunca "repassando" o pedido de um cliente pros outros), o caso de uso que precisaria do loop manual (excluir o remetente) não se aplica aqui. Não mexi — não achei justificativa pra adicionar complexidade sem um bug confirmado.

## Problema

`BallPlacementManager.cs:45` chama `Rpc(...)` sem relay manual pros outros peers, diferente de todo outro broadcast do projeto que precisa alcançar todo mundo (`TableTurnNetworkBridge.cs:43-65`, os dois synchronizers — todos fazem loop manual em `Multiplayer.GetPeers()`).

Em partida de 3-4 jogadores, quem não é o host nem quem está posicionando a bola provavelmente não vê o estado de congelamento/reposicionamento.

## Direção sugerida

Verificar comportamento real de relay do `SceneMultiplayer` pra esse caso; se confirmado, adicionar o mesmo padrão de loop manual.
