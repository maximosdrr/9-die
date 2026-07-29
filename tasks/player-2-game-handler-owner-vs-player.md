# PLAYER-2 — `PlayerGameHandler.EquipGameController` mistura `Player` (campo) com `(Player)Owner` (implícito)

**Área**: Player
**Prioridade**: 🟠 Bug

## Problema

`Features/Player/Scripts/PlayerGameHandler.cs:17` usa o campo `Player`; linha 27 usa `(Player)Owner` pro mesmo propósito. Funciona hoje só porque os dois coincidem no layout atual da cena — frágil se a cena for reorganizada, real risco de crash (`InvalidCastException`/null) se `Owner` não for o `Player` esperado.

## Direção sugerida

Usar `Player` consistentemente, remover o cast via `Owner`.
