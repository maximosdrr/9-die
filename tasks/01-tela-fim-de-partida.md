# 01 — Tela de fim de partida

**Prioridade**: Alta (bloqueia uma partida completa)
**Status**: ✅ Resolvido em 29/07/2026

## Achado

Esse item nunca teve um arquivo próprio (só o título no índice) — ao investigar, descobri que já existia um começo: `Core/Table/States/GameFinished.cs` era um state stub **vazio** (`Type = StatesRef.GameFinished;` e nada mais), a constante `GAME_FINISHED` existia em `StatesRef.cs`, mas o state **nunca foi instanciado em `Table.tscn`** e `TableGame.ApplyMatchOver` pulava direto pra `GAME_WAITING_START`, ignorando o `GAME_FINISHED` por completo. A intenção parecia clara, só não foi terminada.

## Resolvido

- `GameFinished.cs` implementado: mostra o resultado (`PoolStartGameUI`, o mesmo Label3D reaproveitado pelos outros states) com texto diferente por motivo (`win`, `fatal_foul`, e `opponent_disconnected` do [02](02-desconexao-trava-jogo.md)); depois de um timer (5s, `EndMatchTimer`), volta sozinho pra `GAME_WAITING_START`.
- `Table.tscn`: nó `Finished` adicionado como filho do `StateMachine`, `EndMatchTimer` adicionado em `Timers/`.
- `TableGame.ApplyMatchOver`: agora transiciona pra `GAME_FINISHED` (com `winner` no metadata) em vez de pular direto pro waiting.

## Verificação

`dotnet build` limpo. Não testei visualmente (não consigo rodar o Godot) — vale conferir se o texto aparece legível na posição do `PoolStartGameUI` e se o timer de 5s parece um tempo razoável.
