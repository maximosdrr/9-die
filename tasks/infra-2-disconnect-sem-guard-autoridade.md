# INFRA-2 — `LevelMultiplayerManager.OnPlayerDisconnect` sem guard de autoridade

**Área**: Infraestrutura (Level)
**Prioridade**: 🟠 Bug
**Status**: ✅ Resolvido em 29/07/2026 — `if (!IsMultiplayerAuthority()) return;` adicionado no início, espelhando `OnPlayerConnect`.

## Problema

`Core/Level/LevelMultiplayerManager.cs:34-43`. `OnPlayerConnect` (linha 29) checa `IsMultiplayerAuthority()` antes de spawnar; `OnPlayerDisconnect` não checa nada, e roda em `NetworkProvider.PlayerDisconnected` (sinal que TODO peer recebe, não só o host) — cada cliente também chama `player.QueueFree()` na sua cópia replicada, correndo contra o despawn nativo do `MultiplayerSpawner`.

Possível causa de bugs intermitentes de "node not found"/dessincronia.

## Direção sugerida

Espelhar o guard de `OnPlayerConnect`.
