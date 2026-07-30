# POOL-4 — Vencedor errado em falta fatal (Golden Nine)

**Área**: Lógica de Sinuca (Regras Golden Nine)
**Prioridade**: 🟠 Bug
**Status**: ✅ Resolvido em 29/07/2026 — o arquivo/classe mudou de nome desde que essa tarefa foi escrita (agora é `PoolTurnResolver.ApplyTurnAction`, ver [pool-17](pool-17-generalizar-resolver-listeners.md)), mas o bug continuava lá. Adicionado `GetOpponentId()` (mesma lógica de índice que `TableGame.CallNextTurn` já usa pra achar o próximo da rodada) — `EndGameFatalFoul` agora passa o oponente, não `TurnOwner`.

## Problema

`Features/Games/Pool/GameModes/GoldenNine/GoldenNineTurnResolver.cs:157-166` (`EndGameFatalFoul`) passa `PoolGame.TurnOwner.Name` (quem acabou de tacar, ou seja, quem cometeu a falta) como **vencedor** pro `CallMatchOver`.

Em 9-ball, falta fatal deveria dar a vitória pro oponente, não pra quem errou. Parece copy-paste do branch `EndGamePlayerWin` logo abaixo, que reusa `TurnOwner.Name` corretamente (lá é o vencedor de verdade).

## Direção sugerida

Verificar a regra pretendida e corrigir — provavelmente precisa do ID do oponente, não do `TurnOwner` atual.
