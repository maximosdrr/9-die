# PLAYER-1 — Captura de mouse sem dono único, mesmo padrão de bug já corrigido uma vez na TV

**Área**: Player / Input
**Prioridade**: 🟠 Bug
**Status**: ✅ Resolvido em 30/07/2026 (dono do projeto confirmou explicitamente que valia o risco de implementar sem poder testar ao vivo)

**Resolvido em 29/07/2026**: `HeadPivot._UnhandledInput` chama `GetViewport().SetInputAsHandled()` nos dois ramos onde reage a clique/Esc, igual `TvShareButton` já fazia.

**Resolvido em 30/07/2026**: criado `Core/Util/InputFocus.cs` (`Capture()`/`Release()`/`IsCaptured`), único lugar do projeto que agora toca `Input.MouseMode` diretamente. Os 13 pontos de leitura/escrita espalhados em 6 arquivos (`Player.cs`, `HeadPivot.cs`, `TvShareButton.cs`, `BallPlacementManager.cs`, `PoolController.cs`, `AimCameraPivot.cs`, `CueChargingState.cs`) passaram a chamar `InputFocus.Capture()`/`Release()`/`IsCaptured` em vez de mexer no `Input.MouseMode` do motor direto.

**Decisão consciente sobre o escopo**: fiz essa refatoração como uma indireção 1-pra-1 — cada call site continua pedindo captura/liberação exatamente na mesma hora e pela mesma razão que pedia antes, só trocando *onde* a escrita acontece, não *quando*. Considerei ir além (um modelo de "dono" com fila/pilha que rejeita liberações de quem não é o dono atual), mas analisando os 6 arquivos não achei nenhum caso hoje onde dois sistemas competem pelo mouse ao mesmo tempo — cada par captura/mira e UI/picker já é mutuamente exclusivo por construção (`SetProcessUnhandledInput` liga/desliga cada sistema na hora certa, e o `TvShareButton` já bloqueia clique vazando pra baixo via `SetInputAsHandled`, mesma técnica do fix de 29/07). Adicionar uma trava de posse ali seria arriscar quebrar um fluxo que já funciona, sem nenhum caso concreto que ela resolvesse — exatamente o tipo de mudança que só um teste ao vivo confirmaria com segurança. `Player.GiveControl()` continua sem resetar o modo (mesma razão de antes: não achei um valor "certo" óbvio, e nenhum caller reclama disso hoje).

## Verificação

`dotnet build` limpo, grep confirma zero usos diretos de `Input.MouseMode` fora de `InputFocus.cs`. **Não testado ao vivo** — é uma indireção comportamentalmente idêntica (mesma condição, mesmo valor, em cada call site), mas ainda vale confirmar jogando: capturar/liberar o mouse ao andar (clique/Esc), abrir/fechar o picker da TV, mirar na sinuca, e reposicionar a bola branca.

## Problema

`Input.MouseMode` é escrito por pelo menos 6 classes independentes (`Player.cs:45`, `HeadPivot.cs:36,42,44`, `AimCameraPivot.cs`, `BallPlacementManager.cs`, `CueChargingState.cs`, `PoolController.cs`) sem nenhum árbitro central.

`HeadPivot._UnhandledInput` (`HeadPivot.cs:29-54`) nunca chama `SetInputAsHandled()`, diferente de `TvShareButton.cs` — reage a clique/Esc mesmo quando outra UI deveria ter prioridade.

`Player.GiveControl()` nunca reseta `Input.MouseMode` (só `TakeControl()` seta).

Esse é exatamente o mesmo tipo de bug já corrigido uma vez pra TV (evento de input vazando pra outro sistema que reage indevidamente).

## Direção sugerida

Centralizar a posse do mouse (ex. um `InputFocusOwner` em `Global`) em vez de cada script brigar pelo `Input.MouseMode` global.
