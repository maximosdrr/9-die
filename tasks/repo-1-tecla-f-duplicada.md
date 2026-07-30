# REPO-1 — Duplicidade de tecla: `interact` e `start_game` no mesmo `physical_keycode` (F)

**Área**: Config (cross-cutting)
**Prioridade**: 🟠 Bug
**Status**: ✅ Resolvido em 29/07/2026 — não removi a duplicidade de tecla (F continua fazendo sentido pras duas ações, é a UX pretendida), resolvi a causa real: `WaitingGameStart` lia a tecla via polling (`Input.IsActionJustPressed` dentro de `Process`), que ignora `SetInputAsHandled()` completamente — mesmo se a TV (`TvShareButton`, que já marca o evento como tratado) tivesse prioridade no mesmo frame, a Table reagiria de qualquer jeito. Convertido pra `HandleInput(InputEvent)` (o mecanismo de evento que o resto do projeto já usa), chamando `SetInputAsHandled()` depois de reagir. Agora só um dos dois sistemas reage por frame — qual dos dois depende da ordem de processamento da árvore, não ficou determinístico "TV sempre ganha", mas o duplo-disparo (o bug real) não acontece mais.

## Problema

`project.godot` — as ações `interact` e `start_game` estão as duas amarradas na tecla física F. Se o prompt da Table ("Waiting Start") e o prompt da TV (`interact`) puderem ficar ativos ao mesmo tempo pro mesmo jogador, apertar F dispara os dois.

## Direção sugerida

Confirmar se os contextos nunca se sobrepõem (provavelmente não, dado o layout do nível) ou consolidar numa ação só.
