# POOL-5 — `CueSfx.EmitStrikeSound` trata força bruta como peso de interpolação normalizado

**Área**: Lógica de Sinuca (Cue SFX)
**Prioridade**: 🟠 Bug
**Status**: ✅ Resolvido em 29/07/2026 — normalização feita dentro do próprio `CueSfx.EmitStrikeSound` (que já tinha uma referência a `Cue`): `finalForce / Cue.ForceMultiplier`, clampado em `[0,1]` antes dos dois `Lerp`. Ponto único de verdade — `Cue.ExecuteStrike` continua passando a força bruta como sempre, sem precisar saber sobre normalização.

## Problema

`Features/Games/Pool/Components/Cue/Scripts/Cue.cs:64-83` calcula `force` na faixa `0..28` (`ForceMultiplier=28` em `Cue.tscn:61`) e passa direto pra `CueSfx.EmitStrikeSound` (`CueSfx.cs:16-21`), que faz `Mathf.Lerp(MinDb, MaxDb, finalForce)` — `Lerp` não clampa o terceiro argumento, então qualquer tacada além de muito leve produz volume/pitch bem fora da faixa pretendida.

Bug audível real (não é só teórico).

## Direção sugerida

Normalizar `force` por `ForceMultiplier` antes do lerp, ou clampar dentro do `CueSfx`.
