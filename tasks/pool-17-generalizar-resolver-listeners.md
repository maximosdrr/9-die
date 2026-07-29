# POOL-17 — Renomear resolver/listeners de "GoldenNine" pra genérico (já são reaproveitáveis)

**Área**: Lógica de Sinuca (GameMode)
**Prioridade**: 🟡 Simplificação (prepara o terreno pro segundo modo de sinuca — dono do projeto confirmou em 29/07/2026 que outros modos estão planejados)
**Status**: ✅ Resolvido em 29/07/2026 — `GoldenNineTurnResolver`→`PoolTurnResolver` (movido pra `Features/Games/Pool/GameModes/`), os 3 listeners movidos pra `Features/Games/Pool/GameModes/Listeners/` e renomeados (`Pool*Listener`), `GoldenNineGameMode.cs` (vazio) removido — `Pool.tscn` atualizado. `GoldenNineTurnRuler.cs` continua onde está, é o único pedaço realmente específico de Golden Nine.

**Atualização de 29/07/2026 (mesmo dia, via [pool-19](pool-19-arquitetura-cue-table-gamemode.md))**: os 3 listeners foram colapsados em métodos privados dentro do próprio `PoolTurnResolver` — deixaram de existir como arquivos/nós separados. Isso não desfaz a análise acima (eles continuam 100% genéricos/reaproveitáveis entre modos de sinuca) — só muda ONDE o código genérico mora. A reusabilidade entre modos continua garantida porque é o `PoolTurnResolver` inteiro que é reaproveitado, não os listeners individualmente.

## Problema

Investiguei se `GoldenNineTurnResolver` e os 3 listeners de evento físico (`GoldenNineScoreListener`, `GoldenNineBallFellOffListener`, `GoldenNineCueBallContactListener`) têm regra de Golden Nine misturada dentro, ou se são reaproveitáveis por um segundo modo.

Resultado: **são 100% genéricos em comportamento hoje** — só têm nome de Golden Nine porque são o único usuário. Os listeners só capturam fatos físicos (bola entrou na caçapa, bola caiu da mesa, taco encostou na bola) e gravam em campos do resolver — nenhuma regra de vitória/falta está neles. `GoldenNineTurnResolver.ApplyTurnAction` só despacha em cima do enum genérico `TurnRuler.Actions` (`CallNextTurn`, `ExtendTurn`, etc.) — toda regra de Golden Nine de verdade mora só em `GoldenNineTurnRuler.Rule` (esse sim, específico, deve continuar existindo por modo).

Também achei `GoldenNineGameMode.cs` (`Features/Games/Pool/GameModes/GoldenNine/GoldenNineGameMode.cs`) — subclasse vazia de `GameMode`, sem nada dentro, não faz nada que a classe base já não faça.

## Direção sugerida

- Mover + renomear `GoldenNineTurnResolver.cs` → `Features/Games/Pool/GameModes/PoolTurnResolver.cs` (classe `PoolTurnResolver`).
- Mover + renomear os 3 listeners pra `Features/Games/Pool/GameModes/Listeners/` (`PoolScoreListener`, `PoolBallFellOffListener`, `PoolCueBallContactListener`).
- Atualizar `Pool.tscn` (paths de `ext_resource`, nomes de nó, `node_paths`).
- Remover `GoldenNineGameMode.cs` (vazio) — o nó `GoldenNine` em `Pool.tscn` passa a usar o script `GameMode.cs` base direto.
- Quando o modo #2 existir, ele só precisa de um `TurnRuler` novo — resolver e listeners já servem sem duplicar nada.
