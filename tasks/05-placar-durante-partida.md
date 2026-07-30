# 05 — Placar durante a partida

**Prioridade**: Média (feedback e UX)
**Status**: ✅ Resolvido em 29/07/2026, como parte da Fase 2 da reforma de UI/HUD

## Decisão

O adiamento original foi revertido quando o item 05 do backlog virou o gatilho da reforma completa de UI (menu, HUD, diálogos — ver histórico do projeto). `PlayerHud.cs`/`PlayerHud.tscn` entregam exatamente o que este item pedia: indicação de vez (`TurnLabel`, destacada quando é a sua), bola-alvo atual (`TargetBallLabel`), e contagem de bolas encaçapadas por jogador (`RefreshScoreList`, alimentada por `PoolGame.BallsPocketedByPlayer` — rastreio per-player que não existia antes e foi adicionado nessa mesma fase). Tudo atualiza ao vivo via sinais já existentes (`TurnChanged`, `TurnExtended`, `HudStateUpdated`), sem polling.

## Verificação

`dotnet build` limpo. Testado ao vivo pelo dono do projeto durante a fase de bugs reportados nesta sessão (HUD confirmado funcionando após o fix do bug de ordem de `_Ready()`).
