# POOL-2 — `BallPlacementManager.SetBallPlacementState` sem validação nenhuma — pior que POOL-1

**Área**: Lógica de Sinuca (Ball Placement)
**Prioridade**: 🔴 Crítico
**Status**: ✅ Resolvido em 29/07/2026 — restruturado pro padrão cliente-pede/servidor-valida-e-rebroadcasta (igual `CueNetworkBridge`/`TvScreenShare`). `SetBallPlacementState` virou `RpcMode.Authority` (só o servidor pode chamar — Godot rejeita a chamada de qualquer outro peer antes até de chegar no método). Novo par `RequestPlacementState`/`RequestSetBallPlacementState`: cliente pede, servidor confere contra `_authorizedPlacerId`/`_authorizedBall` (setados por `PoolTurnResolver.OnTurnStart`, que agora autoriza no servidor de forma desacoplada de "é a minha vez", justamente pra isso funcionar pro jogador certo mesmo quando o servidor não é ele) antes de rebroadcastar.

## Problema

`Features/Games/Pool/Scripts/BallPlacementManager.cs:200-220` — RPC `AnyPeer, CallLocal=true`, zero validação de remetente. Qualquer peer pode chamar isso mirando o `NodePath` de **qualquer bola** (não só a que está em "ball in hand"), com `newAuthority`/`finalPos` arbitrários — dá pra qualquer cliente tomar posse de qualquer bola e teleportá-la a qualquer momento.

## Direção sugerida

Validar que o remetente é quem tem ball-in-hand no momento, e que o alvo é a bola correta.
