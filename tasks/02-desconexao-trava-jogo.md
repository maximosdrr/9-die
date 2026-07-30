# 02 — Jogador desconecta e trava o jogo

**Prioridade**: Alta (bloqueia uma partida completa)
**Status**: ✅ Resolvido em 29/07/2026

## Achado

Esse item também nunca teve arquivo próprio. Confirmei o problema: `LevelMultiplayerManager.OnPlayerDisconnect` só dá `QueueFree()` no nó do jogador — nada verificava se ele estava numa partida ativa. Se o jogador que desconectasse fosse o `TurnOwner` (ou estivesse no `TurnOrder`), a partida ficava travada esperando uma tacada que nunca ia acontecer, sem forfeit, sem timeout, sem nada.

## Resolvido

`TableGame.Setup` agora assina `NetworkManager.Instance.NetworkProvider.PlayerDisconnected` diretamente (é genérico, não precisou ser em `PoolGame` especificamente). `OnPlayerDisconnected`: roda só no servidor; se o ID desconectado está no `TurnOrder` da partida ativa, declara o **outro** jogador vencedor via `CallMatchOver(..., reason: "opponent_disconnected")` — reaproveita a mesma tela de fim de partida do [01](01-tela-fim-de-partida.md).

## Fora de escopo, de propósito

Não tratei reconexão (isso é o item [07](07-reconexao-espectador.md), prioridade baixa, feature bem maior). Isso aqui só evita o travamento — se o jogador cair, a partida termina em vez de travar pra sempre.

## Verificação

`dotnet build` limpo. Não testei ao vivo (precisaria derrubar a conexão de um cliente no meio de uma partida real pra confirmar).
