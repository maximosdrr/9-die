# POOL-1 — `CueNetworkBridge.RequestStrike` sem validação de turno/força no servidor

**Área**: Lógica de Sinuca (Cue)
**Prioridade**: 🔴 Crítico
**Status**: ✅ Resolvido em 29/07/2026 — `RequestStrike` agora valida `Multiplayer.GetRemoteSenderId() == GetMultiplayerAuthority()` (remetente é dono desse Cue) e `senderId == PoolGame.TurnOwner` (é a vez dele) antes de aplicar. Força clampada em `[0, Cue.ForceMultiplier]`, direção normalizada (ou fallback pro forward do Cue se vier degenerada), offset de spin limitado a `Cue.SpinLimit`.

## Problema

`Features/Games/Pool/Components/Cue/Scripts/CueNetworkBridge.cs:14-20` — RPC `AnyPeer`, só checa `Multiplayer.IsServer()`. Não valida se é a vez do remetente, não valida se o remetente é dono da autoridade do Cue, não clampa força/direção no servidor (`Ball.Strike`, `Features/Games/Pool/Components/Balls/Scripts/Ball.cs:111-172`, também não clampa).

Qualquer peer pode chamar essa RPC direto com força arbitrária a qualquer momento.

## Direção sugerida

Validar turno atual + autoridade do Cue no servidor antes de aplicar; clampar força/direção server-side.
