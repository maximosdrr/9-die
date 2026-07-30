# PLAYER-1 — Captura de mouse sem dono único, mesmo padrão de bug já corrigido uma vez na TV

**Área**: Player / Input
**Prioridade**: 🟠 Bug

## Problema

`Input.MouseMode` é escrito por pelo menos 6 classes independentes (`Player.cs:45`, `HeadPivot.cs:36,42,44`, `AimCameraPivot.cs`, `BallPlacementManager.cs`, `CueChargingState.cs`, `PoolController.cs`) sem nenhum árbitro central.

`HeadPivot._UnhandledInput` (`HeadPivot.cs:29-54`) nunca chama `SetInputAsHandled()`, diferente de `TvShareButton.cs` — reage a clique/Esc mesmo quando outra UI deveria ter prioridade.

`Player.GiveControl()` nunca reseta `Input.MouseMode` (só `TakeControl()` seta).

Esse é exatamente o mesmo tipo de bug já corrigido uma vez pra TV (evento de input vazando pra outro sistema que reage indevidamente).

## Direção sugerida

Centralizar a posse do mouse (ex. um `InputFocusOwner` em `Global`) em vez de cada script brigar pelo `Input.MouseMode` global.
