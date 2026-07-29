# Lógica de Sinuca — Simplificação e Limpeza

Itens 🟡 (simplificação/consolidação) e 🟢 (limpeza) da lógica de sinuca (Core/Table, Features/Games/Pool). Itens 🔴/🟠 dessa mesma área estão em arquivos próprios: [pool-1](pool-1-cue-strike-sem-validacao.md), [pool-2](pool-2-ball-placement-sem-validacao.md), [pool-3](pool-3-start-game-client-driven.md), [pool-4](pool-4-vencedor-errado-falta-fatal.md), [pool-5](pool-5-audio-tacada-sem-clamp.md), [pool-6](pool-6-ball-placement-rpc-sem-relay.md).

## 🟡 POOL-7 — `BallIdleState`/`BallMovingState` é uma state machine pra um bool só

`Features/Games/Pool/Components/Balls/Scripts/States/BallIdleState.cs`/`BallMovingState.cs` só checam `LinearVelocity.Length()` contra um threshold num timer de 0.1s. Ver também POOL-9 (a mesma checagem é feita uma TERCEIRA vez, independente, em `BallsMovementMonitor`).

**Direção**: considerar substituir por um campo `bool IsMoving` checado direto em `_PhysicsProcess`, eliminando duas classes de estado + nós de cena.

## 🟡 POOL-8 — Cue: guards de autoridade redundantes + pose duplicada em 4 arquivos

`Cue.tscn:85` já ativa `CheckForMultiplayerAuthorityOnStateHandleInput=true` (a StateMachine já bloqueia `HandleInput` pra não-autoridade), mas `CueIdleState`, `CueChargingState`, `CueJumpState`, `CueSpinState` (`.../State/*.cs`) re-checam `IsMultiplayerAuthority()` de novo, cada um. Separadamente, o snippet `pos.Z = Cue.BallRadiusOffset; pos.X = ...; pos.Y = ...; Cue.Position = pos;` aparece copiado em 4 states diferentes.

**Direção**: remover os re-checks redundantes; extrair a pose pra um método único em `Cue` (ex. `Cue.SnapToRest()`).

## 🟡 POOL-9 — Estado "bolas paradas" recalculado 3x de formas independentes

`BallsMovementMonitor._Process` (`Features/Games/Pool/Scripts/BallsMovementMonitor.cs:27-74`) recalcula "alguma bola se movendo" do zero, com constantes próprias (`StopTolerance=0.5f`) diferentes das de `BallIdleState` (`StopSpeedThreshold`) — sem fonte única de verdade; se alguém ajustar um sem o outro, o estado por-bola e o "mesa parou" (usado pra resolver faltas) podem discordar silenciosamente. `Ball.StoppedMoving` (sinal, `Ball.cs:45`) já existe e não é usado pra isso.

**Direção**: fazer `BallsMovementMonitor` assinar `Ball.StoppedMoving` de cada bola em vez de repolling.

## 🟡 POOL-10 — Ball raycasta todo tick, em todo peer, sem checar autoridade ou movimento

`Ball._PhysicsProcess` (`Features/Games/Pool/Components/Balls/Scripts/Ball.cs:185-199`) chama `CheckGroundState()` (raycast) incondicionalmente a 580Hz, em todo peer, pra todas as 10 bolas, só pra disparar SFX de pulo local.

**Direção**: gatear por `IsMultiplayerAuthority()` no mínimo, idealmente só rodar enquanto a bola está no ar/em movimento.

## 🟡 POOL-11 — `CueAutomaticElevation` raycasta todo tick sem guard de autoridade (único do grupo sem)

`Features/Games/Pool/GameController/CueAutomaticElevation.cs:11-14` — `_PhysicsProcess` sem `IsMultiplayerAuthority()`, diferente de todo script irmão no mesmo subsistema (`AimCameraPivot`, os Cue states). Roda o raycast pra todos os controllers de todos os jogadores em todo cliente.

**Direção**: adicionar o guard, igual aos vizinhos.

## 🟡 POOL-12 — `AimToggleable`/`Toggleable` com NodePath quebrado — silenciosamente inerte

`Features/Games/Pool/GameController/PoolController.tscn:32-38` aponta `Targets` pra `../../Aim`, que não existe (o nó real se chama `AimPivot`). `Toggleable.Apply` engole o `null` e não faz nada — todo o toggle de processo/visibilidade/câmera que deveria centralizar aqui é feito à mão, redundantemente, em `PoolController.TakeControl()`/`GiveControl()`.

**Direção**: corrigir o path (ou remover o nó morto) e migrar a lógica manual de `TakeControl`/`GiveControl` pra usar o `Toggleable` de verdade.

## 🟢 POOL-13 — Achados menores agrupados

- `TurnRuler.Actions.CallGoldenBallReplacement` declarado, nunca produzido nem tratado (`Core/Table/GameMode/TurnRuler.cs` vs `GoldenNineTurnResolver.ApplyTurnAction`).
- `EndGameFatalFoul` não chama `Reset()` (diferente de `EndGamePlayerWin`) — hoje inofensivo mas assimétrico.
- Estado de bola "score" detectado por dois caminhos diferentes (`PoolScoreMonitor`, conectado via `.tscn`; `GoldenNineScoreListener`, conectado via código) — difícil rastrear "o que acontece quando uma bola é pontuada" só lendo o código.
- `BallResource.Model`/`.Name` nunca lidos — `Ball.cs` usa arrays estáticos próprios pra visual, ignorando o resource.
- `PoolStartGameUI.CanBeShow` setado, nunca lido.
- `GameModeHandler.Switch`/`.Modes` nunca chamados fora da própria classe.
- `ball.GetMeta("in_pocket", ...)` (`PoolGameTable.cs`) — bag dinâmico do GDScript em vez de um `bool` tipado no `Ball`.
- Nó `CueAutomaticElevantion` (typo, falta um "n") em `PoolController.tscn:40`.
- `TableGame.CallMatchOver` só encaminha pra `ApplyMatchOver` sem fazer nada a mais — quebra a convenção `Call*`/`Apply*` que o resto do arquivo segue.
- `Features/Games/Pool/Pool.tscn` referencia `OffTableMonitor`/`BallPlacementManager` por NodePath relativo de 3 níveis, enquanto `GoldenNineBallFellOffListener` acessa os mesmos objetos via `TurnResolver.PoolGame.OffTableMonitor` (propriedade C# já existente) — duas formas de alcançar a mesma dependência, uma frágil.
- `BallBounceAudio`/`BallCollisionAudio` duplicam ~30 linhas de "clonar AudioStreamPlayer3D, tocar, liberar" cada um.
