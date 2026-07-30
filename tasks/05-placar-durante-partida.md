# 05 — Placar durante a partida

**Prioridade**: Média (feedback e UX)
**Status**: 🟡 Adiado em 29/07/2026 (decisão do dono do projeto)

## Achado

Não existe nenhum HUD/placar durante uma partida de sinuca. O jogador não tem nenhuma indicação em tela de quem é a vez (fora o prompt pontual de replacement da bola) nem de quantas bolas cada um já encaçapou. `PoolGame`/`PoolTurnResolver` já têm toda a informação necessária internamente (`TurnOwner`, `BallsScored` por jogador não é rastreado per-player hoje, só globalmente em `PoolTurnResolver.BallsScored`) — falta só a camada de UI.

Perguntei ao dono do projeto qual escopo mínimo fazia sentido agora (vez + bolas encaçapadas / versão completa com bola-alvo / adiar). Resposta: **adiar**.

## Decisão

Não implementado por enquanto. Item fica pendente no backlog — quando for retomado, meu ponto de partida seria: HUD simples (`CanvasLayer` em `Player.tscn`, no mesmo estilo do que já existe pra TV) mostrando de quem é a vez e contagem de bolas por jogador, alimentado por sinais que já existem em `PoolGame`/`TableGame` (`TurnChanged`) — precisaria adicionar rastreio de "bolas encaçapadas por jogador" já que hoje `BallsScored` só sabe quais bolas foram encaçapadas na rodada atual, não por quem historicamente.

## Verificação

N/A — nada foi implementado, por escolha explícita.
