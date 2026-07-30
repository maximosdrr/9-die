# POOL-16 — Tipar o contexto de turno (`GenerateTurnContext`)

**Área**: Lógica de Sinuca (GameMode)
**Prioridade**: 🟡 Simplificação (importante fazer *antes* do segundo modo de sinuca existir — dono do projeto confirmou em 29/07/2026 que outros modos além de Golden Nine estão planejados)
**Status**: ✅ Resolvido em 29/07/2026 — `Core/Table/GameMode/TurnContext.cs` criado; `TurnRuler.Rule`, `GoldenNineTurnRuler.Rule` e `GoldenNineTurnResolver.GenerateTurnContext`/`OnStrike` atualizados pra usar o tipo (sem mais cast/chave string).

## Problema

`GoldenNineTurnResolver.GenerateTurnContext()` (`Features/Games/Pool/GameModes/GoldenNine/GoldenNineTurnResolver.cs:125-155`) monta um `Godot.Collections.Dictionary` solto com chaves string (`"balls_scored"`, `"first_ball_touched"`, `"balls_off_table"`, `"target_ball"`, `"current_balls_remaining"`), passado pra `TurnRuler.Rule(Dictionary context)` (`Core/Table/GameMode/TurnRuler.cs`). `GoldenNineTurnRuler.Rule` (`GoldenNineTurnRuler.cs:13-18`) desempacota tudo com cast/`.As<Ball>()`, sem ajuda nenhuma do compilador.

Hoje funciona só porque existe um `TurnRuler` consumidor. No momento que existir um segundo modo (`TurnRuler` #2), cada um vai ter que saber de cor quais chaves o outro usa ou não usa — barato de corrigir agora, caro de corrigir depois com dois consumidores dependendo do formato solto.

## Direção sugerida

- Criar `Core/Table/GameMode/TurnContext.cs` — classe simples com os mesmos campos já tipados hoje no `GoldenNineTurnResolver` (`Dictionary<int, Ball> BallsScored`, `Ball FirstBallTouched`, `Array<Ball> BallsOffTable`, `Ball TargetBall`, `Dictionary<int, Ball> CurrentBallsRemaining`).
- `TurnRuler.Rule(Dictionary context)` → `Rule(TurnContext context)`.
- `GoldenNineTurnRuler.Rule` e `GenerateTurnContext` passam a usar acesso a propriedade direto, sem cast.
