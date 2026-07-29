# REPO-1 — Duplicidade de tecla: `interact` e `start_game` no mesmo `physical_keycode` (F)

**Área**: Config (cross-cutting)
**Prioridade**: 🟠 Bug

## Problema

`project.godot` — as ações `interact` e `start_game` estão as duas amarradas na tecla física F. Se o prompt da Table ("Waiting Start") e o prompt da TV (`interact`) puderem ficar ativos ao mesmo tempo pro mesmo jogador, apertar F dispara os dois.

## Direção sugerida

Confirmar se os contextos nunca se sobrepõem (provavelmente não, dado o layout do nível) ou consolidar numa ação só.
