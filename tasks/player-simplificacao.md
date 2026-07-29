# Player / UI / Câmera — Simplificação e Limpeza

Itens 🟡 (simplificação/consolidação) e 🟢 (limpeza) da área de player/UI/câmera. Itens 🟠 dessa mesma área estão em arquivos próprios: [player-1](player-1-mouse-capture-sem-dono.md), [player-2](player-2-game-handler-owner-vs-player.md).

## 🟡 PLAYER-3 — `ControlSwitch` rederiva o controller ativo em vez de usar `PlayerGameHandler.CurrentController` (parcialmente resolvido)

**Resolvido em 29/07/2026** (via [pool-18](pool-18-revisao-balls-cue-tables-gamecontroller.md), item C): `ControlSwitch` agora lê `Player.GameHandler.CurrentController` direto, sem `GetChildren()[0]`; `[Export] GameContextSlot` removido.

**Ainda em aberto**: `ControlSwitch._UnhandledInput` continua lendo `Input.IsActionJustPressed("switch_control")` global em vez de checar o `@event` recebido (diferente de `HeadPivot`/`AimCameraPivot`), rodando o corpo inteiro em todo input não tratado. Não fazia parte do escopo do pool-18.

**Direção restante**: trocar pra `@event.IsActionPressed(...)`.

## 🟡 PLAYER-4 — `Player.cs` é uma casca fina — todo mundo mexe direto nos campos públicos

`Player.cs` expõe `StateMachine`, `PlayerModel`, `HeadPivot`, `GameHandler` como campos públicos mutáveis. `PoolController.cs:57-58,79-80` e `PoolGame.cs:60-68` mexem direto neles (`Player.PlayerModel.Hide()`, `Player.StateMachine.ChangeState(...)`), passando por cima de `TakeControl()`/`GiveControl()`. Maior esforço da lista, maior ganho — é o "god object com campos públicos" clássico.

**Direção**: dar métodos de verdade pro `Player` (`PlayStrike()`, `EnterPoolMode()`) em vez de deixar quem chama mexer nos internals.

## 🟡 PLAYER-5 — Câmera: cena não usada, método de transição que não transiciona, referência morta

`Features/Camera/GlobalCamera.tscn` nunca é instanciado em lugar nenhum (`Main.tscn` monta o `Camera3D` na mão e anexa o script direto). `GlobalCamera.TransitionTo(RemoteTransform3D, float duration=0)` (`GlobalCamera.cs:32`) nunca usa `duration` — é uma troca instantânea disfarçada de transição suave. `HeadPivot.CameraMount` (`[Export]`, `HeadPivot.cs:6`) nunca é lido — `Player.cs:24` acessa o mesmo nó via `GetNode<RemoteTransform3D>("FirstPerson/HeadPivot/RemoteFPS")` hardcoded, duplicando a referência de forma mais frágil.

**Direção**: deletar a `.tscn` morta ou passar a instanciá-la de verdade; implementar o tween ou renomear o método; remover `CameraMount` morto ou trocar o `GetNode` hardcoded por ele.

## 🟢 PLAYER-6 — Exports/campos mortos agrupados

- `PlayerToggleable` configurado na cena (`Player.tscn:4136-4141`) mas `Enable()`/`Disable()` nunca chamados — `TakeControl`/`GiveControl` fazem toggle manual paralelo e sobreposto.
- `Player.Id` (`Player.cs:8`) escrito, nunca lido — `PlayerGameHandler.cs:17` faz `int.Parse((string)Player.Name)` pro mesmo dado, de um jeito que quebra se `Player.Name` não for um inteiro puro (ex. Godot sufixando nome duplicado).
- `NotificationPopup` órfão (já listado em `tasks/README.md` #04) — e sem guard de `IsMultiplayerAuthority()`, então se for reativado como está, fica clicável na cópia de todo mundo.
- `LobbyMenu.InitialLevel` (`[Export]`, `LobbyMenu.cs:7`) nunca referenciado no código.
- `Player.Gravity`/`Player.Speed` são campos públicos simples, não `[Export]`, inconsistente com o resto dos tunáveis do projeto.
- `LobbyMenu`: `_hostButton`/`_joinSessionLocal` ficam `Disabled=true` após clique e nunca são reabilitados se a operação falhar — sem feedback, sem retry.
- ~~`GameContextSlot` tem o script base `PlayerGameController` anexado direto no nó~~ — já resolvido no [POOL-14](pool-14-colapsar-playergamecontroller.md) (script removido, virou `Node3D` puro).
