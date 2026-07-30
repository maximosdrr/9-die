# Player / UI / Câmera — Simplificação e Limpeza

Itens 🟡 (simplificação/consolidação) e 🟢 (limpeza) da área de player/UI/câmera. Itens 🟠 dessa mesma área estão em arquivos próprios: [player-1](player-1-mouse-capture-sem-dono.md), player-2.

## ✅ PLAYER-3 — `ControlSwitch` rederiva o controller ativo em vez de usar `PlayerGameHandler.CurrentController`

**Resolvido em 29/07/2026** (via pool-18, item C): `ControlSwitch` agora lê `Player.GameHandler.CurrentController` direto, sem `GetChildren()[0]`; `[Export] GameContextSlot` removido.

**Parte final resolvida em 29/07/2026**: `ControlSwitch._UnhandledInput` trocou `Input.IsActionJustPressed("switch_control")` (global) por `@event.IsActionPressed("switch_control")` (igual a `HeadPivot`/`AimCameraPivot`), e passou a chamar `GetViewport().SetInputAsHandled()` quando consome o evento.

## ✅ PLAYER-4 — `Player.cs` é uma casca fina — todo mundo mexe direto nos campos públicos

**Resolvido em 29/07/2026, escopo confirmado com o dono do projeto antes de mexer** (era o item de maior risco/esforço de toda a passada de simplificação — perguntei explicitamente antes de tocar no fluxo de troca de controle jogador↔mesa, resposta: fazer agora).

`PoolController.TakeControl()`/`GiveControl()` faziam `Player.PlayerModel.Hide()` + `Player.StateMachine.ChangeState(StatesRef.PlayerStrike, ...)` (e o inverso) direto nos campos públicos do `Player`, por cima de `TakeControl()`/`GiveControl()` do próprio `Player`. Isso foi extraído pra dois métodos novos, de verdade, no `Player`: `EnterGameControllerMode()`/`ExitGameControllerMode()` — `PoolController` agora só chama esses dois métodos, sem saber que por trás disso existe um `PlayerModel` ou uma `StateMachine`. Confirmado por grep: não sobrou nenhum acesso externo a `Player.PlayerModel`/`Player.StateMachine` no projeto.

**Escopo que ficou de fora, deliberadamente**: os acessos a `Player.GameHandler.X` (em `PoolGame.cs` e `ControlSwitch.cs`) não foram tocados — diferente de `PlayerModel`/`StateMachine`, `GameHandler` já é um componente (`PlayerGameHandler`) com API própria e razoável (`EquipGameController`, `UnequipCurrentController`, `CurrentController`); envolver isso em mais métodos de passagem no `Player` só adicionaria uma camada de indireção sem reduzir acoplamento de verdade. `Player.TakeControl()`/`GiveControl()` chamados de fora (`PoolGame.cs`, `ControlSwitch.cs`) também não mudaram — já eram métodos de verdade, não campos.

~~`Player.cs` expõe `StateMachine`, `PlayerModel`, `HeadPivot`, `GameHandler` como campos públicos mutáveis. `PoolController.cs:57-58,79-80` e `PoolGame.cs:60-68` mexem direto neles, passando por cima de `TakeControl()`/`GiveControl()`.~~

## Verificação

`dotnet build 9Die.csproj` limpo a cada passo. **Não testei ao vivo** — assim como o item 03, esse refactor toca um fluxo já funcionando (troca de controle jogador↔mesa ao começar/terminar a tacada) sem mudar o comportamento esperado, mas vale confirmar numa partida real: taco aparece e o modelo do jogador esconde ao tomar controle da mesa; ao devolver o controle (fim de tacada, fim de partida, `switch_control`), o modelo do jogador volta a aparecer e o `StateMachine` volta pro estado idle normalmente.

## ✅ PLAYER-5 — Câmera: cena não usada, método de transição que não transiciona, referência morta

**Resolvido em 29/07/2026**: `Features/Camera/GlobalCamera.tscn` removida (confirmado sem nenhum caller — `Main.tscn` monta o `Camera3D` na mão de verdade). `GlobalCamera.TransitionTo` perdeu o parâmetro `duration` (nenhum dos 4 call sites passava valor — era sempre o default `0`; implementar tween de verdade seria uma feature nova, não simplificação, então optei por remover a promessa falsa em vez de construir a feature). `HeadPivot.CameraMount` passou a ser lido de verdade: `Player.cs` agora faz `RemoteFps = HeadPivot.CameraMount` em vez do `GetNode<RemoteTransform3D>("FirstPerson/HeadPivot/RemoteFPS")` hardcoded — o export já estava corretamente wireado na cena (`CameraMount = NodePath("RemoteFPS")`), só não era usado.

~~`Features/Camera/GlobalCamera.tscn` nunca é instanciado em lugar nenhum (`Main.tscn` monta o `Camera3D` na mão e anexa o script direto). `GlobalCamera.TransitionTo(RemoteTransform3D, float duration=0)` (`GlobalCamera.cs:32`) nunca usa `duration`. `HeadPivot.CameraMount` (`[Export]`, `HeadPivot.cs:6`) nunca é lido.~~

## 🟢 PLAYER-6 — Exports/campos mortos agrupados

- ~~`PlayerToggleable` configurado na cena mas `Enable()`/`Disable()` nunca chamados~~ — **removido em 29/07/2026**: nó `PlayerToggleable` tirado de `Player.tscn`, campo `Player.PlayerToggleable` removido, e `Core/ToggleNode/Toggleable.cs` deletado inteiro (ficou sem nenhum outro uso no projeto depois que o [POOL-12](pool-simplificacao.md) já tinha removido o `AimToggleable` equivalente do lado da mesa). `TakeControl`/`GiveControl` continuam sendo o único mecanismo de fato.
- ~~`Player.Id` escrito, nunca lido~~ — **resolvido em 29/07/2026**: `PlayerGameHandler.EquipGameController` agora usa `Player.Id` em vez de `int.Parse((string)Player.Name)`, removendo o risco de quebrar se o nome do nó vier sufixado.
- ~~`NotificationPopup` órfão~~ — resolvido, ver 04.
- ~~`LobbyMenu.InitialLevel` nunca referenciado no código.~~ — **removido em 29/07/2026**.
- ~~`Player.Gravity`/`Player.Speed` são campos públicos simples, não `[Export]`~~ — **resolvido em 29/07/2026**, viraram `[Export]`.
- ~~`LobbyMenu`: `_hostButton`/`_joinSessionLocal` ficam `Disabled=true` após clique e nunca são reabilitados se a operação falhar~~ — **resolvido em 29/07/2026**: `OnError` agora reabilita os dois botões.
- ~~`GameContextSlot` tem o script base `PlayerGameController` anexado direto no nó~~ — já resolvido no POOL-14 (script removido, virou `Node3D` puro).
