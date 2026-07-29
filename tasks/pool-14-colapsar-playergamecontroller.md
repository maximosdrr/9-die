# POOL-14 — Colapsar `PlayerGameController` genérico pra dentro de `PoolController`

**Área**: Lógica de Sinuca (GameController)
**Prioridade**: 🟡 Simplificação (escopo: só sinuca, confirmado pelo dono do projeto em 29/07/2026 — sem outros minijogos planejados)
**Status**: ✅ Resolvido em 29/07/2026 — `PlayerGameController` deletado, `PoolController` agora `: Node3D` direto com `CanTakeControl` próprio, `PlayerGameHandler`/`ControlSwitch` retipados, script órfão removido de `GameContextSlot` no `Player.tscn`.

## Problema

`Core/GameController/PlayerGameController.cs` é uma base abstrata (`Setup`, `TakeControl`, `GiveControl`, `ApplyControl` — tudo virtual vazio) pensada pra "qualquer jogo pode possuir o jogador". Só existe um filho, `PoolController` (`Features/Games/Pool/GameController/PoolController.cs`), e vai continuar sendo o único pra sempre, já que só sinuca está no escopo. A indireção também já não protege nada na prática — `PoolController`/`PoolGame` já mexem direto nos campos públicos do `Player` por baixo dela.

`PlayerGameHandler.CurrentController` (`Features/Player/Scripts/PlayerGameHandler.cs:9`) e `ControlSwitch` (`Features/Player/Scripts/ControlSwitch.cs`) tipam tudo como `PlayerGameController`, exigindo cast pra baixo em vários lugares.

`Player.tscn`'s nó `GameContextSlot` (linha ~4130) tem o script `PlayerGameController.cs` anexado direto nele — vestigial, só usado como container.

## Direção sugerida

- Deletar `Core/GameController/PlayerGameController.cs` (pasta fica vazia, remover também).
- `PoolController.cs`: parar de herdar de `PlayerGameController`, virar `: Node3D` direto, absorver o campo `CanTakeControl`.
- `PlayerGameHandler.cs`/`ControlSwitch.cs`: retipar todas as referências de `PlayerGameController` pra `PoolController`.
- `Player.tscn`: remover o script de `GameContextSlot` (vira um `Node3D` puro, sem script).
