# POOL-4 — Vencedor errado em falta fatal (Golden Nine)

**Área**: Lógica de Sinuca (Regras Golden Nine)
**Prioridade**: 🟠 Bug

## Problema

`Features/Games/Pool/GameModes/GoldenNine/GoldenNineTurnResolver.cs:157-166` (`EndGameFatalFoul`) passa `PoolGame.TurnOwner.Name` (quem acabou de tacar, ou seja, quem cometeu a falta) como **vencedor** pro `CallMatchOver`.

Em 9-ball, falta fatal deveria dar a vitória pro oponente, não pra quem errou. Parece copy-paste do branch `EndGamePlayerWin` logo abaixo, que reusa `TurnOwner.Name` corretamente (lá é o vencedor de verdade).

## Direção sugerida

Verificar a regra pretendida e corrigir — provavelmente precisa do ID do oponente, não do `TurnOwner` atual.
