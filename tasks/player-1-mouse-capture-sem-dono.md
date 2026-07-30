# PLAYER-1 — Captura de mouse sem dono único, mesmo padrão de bug já corrigido uma vez na TV

**Área**: Player / Input
**Prioridade**: 🟠 Bug
**Status**: 🟡 Parcialmente resolvido em 29/07/2026 — corrigido o achado concreto e seguro: `HeadPivot._UnhandledInput` agora chama `GetViewport().SetInputAsHandled()` nos dois ramos onde reage a clique/Esc (igual `TvShareButton` já fazia), então não reage mais por baixo de outra UI que devesse ter prioridade no mesmo evento.
**Não resolvido, de propósito**: a centralização completa (um `InputFocusOwner` único em vez das 6+ classes escrevendo `Input.MouseMode` direto) não foi feita. Não achei uma forma de fazer isso com segurança sem testar ao vivo — é exatamente o tipo de bug (timing de captura/liberação de mouse entre sistemas) que só aparece jogando, e essa sessão já teve um caso onde tentei consertar às cegas um problema parecido (câmera/taco) três vezes sem sucesso porque não consigo rodar o jogo. Prefiro não repetir isso aqui sem sua confirmação de que vale o risco. `Player.GiveControl()` continua sem resetar `Input.MouseMode` — investiguei e não achei um valor "certo" óbvio pra resetar pra (depende de quem assume o controle em seguida), e todo caller atual já reafirma o modo certo logo depois, então não é um bug observável hoje, só uma inconsistência.

## Problema

`Input.MouseMode` é escrito por pelo menos 6 classes independentes (`Player.cs:45`, `HeadPivot.cs:36,42,44`, `AimCameraPivot.cs`, `BallPlacementManager.cs`, `CueChargingState.cs`, `PoolController.cs`) sem nenhum árbitro central.

`HeadPivot._UnhandledInput` (`HeadPivot.cs:29-54`) nunca chama `SetInputAsHandled()`, diferente de `TvShareButton.cs` — reage a clique/Esc mesmo quando outra UI deveria ter prioridade.

`Player.GiveControl()` nunca reseta `Input.MouseMode` (só `TakeControl()` seta).

Esse é exatamente o mesmo tipo de bug já corrigido uma vez pra TV (evento de input vazando pra outro sistema que reage indevidamente).

## Direção sugerida

Centralizar a posse do mouse (ex. um `InputFocusOwner` em `Global`) em vez de cada script brigar pelo `Input.MouseMode` global.
