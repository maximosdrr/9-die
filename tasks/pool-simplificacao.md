# Lógica de Sinuca — Simplificação e Limpeza

Itens 🟡 (simplificação/consolidação) e 🟢 (limpeza) da lógica de sinuca (Core/Table, Features/Games/Pool). Itens 🔴/🟠 dessa mesma área estão em arquivos próprios: [pool-1](pool-1-cue-strike-sem-validacao.md), [pool-2](pool-2-ball-placement-sem-validacao.md), [pool-3](pool-3-start-game-client-driven.md), [pool-4](pool-4-vencedor-errado-falta-fatal.md), [pool-5](pool-5-audio-tacada-sem-clamp.md), [pool-6](pool-6-ball-placement-rpc-sem-relay.md).

## ✅ POOL-7 — `BallIdleState`/`BallMovingState` é uma state machine pra um bool só

**Resolvido em 29/07/2026** (via [pool-18](pool-18-revisao-balls-cue-tables-gamecontroller.md)) — `BallIdleState.cs`/`BallMovingState.cs` e a `StateMachine` de `Ball.tscn` removidos; `Ball.cs` detecta o próprio movimento direto em `_PhysicsProcess` e emite `StartedMoving`/`StoppedMoving`.

~~`Features/Games/Pool/Components/Balls/Scripts/States/BallIdleState.cs`/`BallMovingState.cs` só checam `LinearVelocity.Length()` contra um threshold num timer de 0.1s. Ver também POOL-9 (a mesma checagem é feita uma TERCEIRA vez, independente, em `BallsMovementMonitor`).~~

## ✅ POOL-8 — Cue: guards de autoridade redundantes + pose duplicada em 4 arquivos

**Resolvido em 29/07/2026** (via [pool-18](pool-18-revisao-balls-cue-tables-gamecontroller.md)) — re-checks removidos; pose extraída pra `Cue.SnapToRestPose()`.

~~`Cue.tscn:85` já ativa `CheckForMultiplayerAuthorityOnStateHandleInput=true` (a StateMachine já bloqueia `HandleInput` pra não-autoridade), mas `CueIdleState`, `CueChargingState`, `CueJumpState`, `CueSpinState` (`.../State/*.cs`) re-checam `IsMultiplayerAuthority()` de novo, cada um. Separadamente, o snippet `pos.Z = Cue.BallRadiusOffset; pos.X = ...; pos.Y = ...; Cue.Position = pos;` aparece copiado em 4 states diferentes.~~

## ✅ POOL-9 — Estado "bolas paradas" recalculado 3x de formas independentes

**Resolvido em 29/07/2026** (via [pool-18](pool-18-revisao-balls-cue-tables-gamecontroller.md)) — `BallsMovementMonitor` agora assina `Ball.StartedMoving`/`StoppedMoving` por bola em vez de repollar `LinearVelocity` em `_Process`.

~~`BallsMovementMonitor._Process` (...) recalcula "alguma bola se movendo" do zero, com constantes próprias (`StopTolerance=0.5f`) diferentes das de `BallIdleState` (`StopSpeedThreshold`) — sem fonte única de verdade.~~

## 🟡 POOL-10 — Ball raycasta todo tick sem checar movimento (revisado — sem guard de autoridade)

**Revisado em 29/07/2026** (via [pool-18](pool-18-revisao-balls-cue-tables-gamecontroller.md)): a direção original sugeria gatear por `IsMultiplayerAuthority()`, mas isso quebraria o SFX de pulo — `Ball.Strike()` só roda de fato no servidor (`CueNetworkBridge.RequestStrike` tem `CallLocal=false`), então cada cliente depende do próprio raycast local (usando a posição sincronizada da bola) pra saber quando ela pousou e tocar o som via `BallBounceAudio`. Gatear por autoridade faria só o servidor ouvir o som. Em vez disso, **aplicado o corte seguro**: `CheckGroundState()` só roda enquanto a bola está no ar ou acima do `stopThreshold` de velocidade — pula o raycast redundante pras bolas já paradas na mesa.

`Ball._PhysicsProcess` (`Features/Games/Pool/Components/Balls/Scripts/Ball.cs`) chamava `CheckGroundState()` (raycast) incondicionalmente a 580Hz, em todo peer, pra todas as 10 bolas, só pra disparar SFX de pulo local — mesmo já paradas.

## ✅ POOL-11 — `CueAutomaticElevation` raycasta todo tick sem guard de autoridade (único do grupo sem)

**Resolvido em 29/07/2026** (via [pool-18](pool-18-revisao-balls-cue-tables-gamecontroller.md)) — guard `IsMultiplayerAuthority()` adicionado, igual aos vizinhos.

~~`Features/Games/Pool/GameController/CueAutomaticElevation.cs:11-14` — `_PhysicsProcess` sem `IsMultiplayerAuthority()`, diferente de todo script irmão no mesmo subsistema (`AimCameraPivot`, os Cue states).~~

## ✅ POOL-12 — `AimToggleable`/`Toggleable` com NodePath quebrado — silenciosamente inerte

**Resolvido em 29/07/2026** (via [pool-18](pool-18-revisao-balls-cue-tables-gamecontroller.md)) — direção revisada: em vez de consertar o NodePath e migrar `TakeControl`/`GiveControl` pro `Toggleable`, o nó `AimToggleable` foi **removido** de `PoolController.tscn` (já eram dois mecanismos fazendo a mesma coisa; `TakeControl`/`GiveControl` já centralizavam isso num lugar só, de forma legível).

~~`Features/Games/Pool/GameController/PoolController.tscn:32-38` aponta `Targets` pra `../../Aim`, que não existe (o nó real se chama `AimPivot`). `Toggleable.Apply` engole o `null` e não faz nada.~~

## 🟢 POOL-13 — Achados menores agrupados

- `TurnRuler.Actions.CallGoldenBallReplacement` declarado, nunca produzido nem tratado (`Core/Table/GameMode/TurnRuler.cs` vs `GoldenNineTurnResolver.ApplyTurnAction`).
- `EndGameFatalFoul` não chama `Reset()` (diferente de `EndGamePlayerWin`) — hoje inofensivo mas assimétrico.
- Estado de bola "score" detectado por dois caminhos diferentes (`PoolScoreMonitor`, conectado via `.tscn`; `GoldenNineScoreListener`, conectado via código) — difícil rastrear "o que acontece quando uma bola é pontuada" só lendo o código.
- `BallResource.Model`/`.Name` nunca lidos — `Ball.cs` usa arrays estáticos próprios pra visual, ignorando o resource.
- `PoolStartGameUI.CanBeShow` setado, nunca lido.
- ~~`GameModeHandler.Switch`/`.Modes` nunca chamados fora da própria classe.~~ — removidos em 29/07/2026, ver [pool-18](pool-18-revisao-balls-cue-tables-gamecontroller.md).
- `ball.GetMeta("in_pocket", ...)` (`PoolGameTable.cs`) — bag dinâmico do GDScript em vez de um `bool` tipado no `Ball`.
- Nó `CueAutomaticElevantion` (typo, falta um "n") em `PoolController.tscn:40`.
- `TableGame.CallMatchOver` só encaminha pra `ApplyMatchOver` sem fazer nada a mais — quebra a convenção `Call*`/`Apply*` que o resto do arquivo segue.
- ~~`Features/Games/Pool/Pool.tscn` referencia `OffTableMonitor`/`BallPlacementManager` por NodePath relativo de 3 níveis, enquanto `PoolBallFellOffListener` acessa os mesmos objetos via `TurnResolver.PoolGame.OffTableMonitor`~~ — resolvido em 29/07/2026 via [pool-18](pool-18-revisao-balls-cue-tables-gamecontroller.md): os `[Export] NodePath` removidos de `PoolTurnResolver`, agora só lê `PoolGame.OffTableMonitor`/`PoolGame.BallPlacementManager`.
- `BallBounceAudio`/`BallCollisionAudio` duplicam ~30 linhas de "clonar AudioStreamPlayer3D, tocar, liberar" cada um.
