# POOL-2 — `BallPlacementManager.SetBallPlacementState` sem validação nenhuma — pior que POOL-1

**Área**: Lógica de Sinuca (Ball Placement)
**Prioridade**: 🔴 Crítico

## Problema

`Features/Games/Pool/Scripts/BallPlacementManager.cs:200-220` — RPC `AnyPeer, CallLocal=true`, zero validação de remetente. Qualquer peer pode chamar isso mirando o `NodePath` de **qualquer bola** (não só a que está em "ball in hand"), com `newAuthority`/`finalPos` arbitrários — dá pra qualquer cliente tomar posse de qualquer bola e teleportá-la a qualquer momento.

## Direção sugerida

Validar que o remetente é quem tem ball-in-hand no momento, e que o alvo é a bola correta.
