# 06 — Lobby sem lista de jogadores

**Prioridade**: Média (feedback e UX)
**Status**: 🔵 Investigado em 29/07/2026, não implementado (decisão do dono do projeto)

## Achado

`LobbyMenu.cs` mostra uma lista de **salas** disponíveis pra entrar (`OnLobbyListReceived`, populando `_lobbyListContainer` com botões de sala). Depois que o jogador entra numa sessão (`OnLobbySessionJoined`/`OnLobbyCreated`), o menu só faz `Visible = false` e o jogador cai direto no mundo 3D — não existe, na tela do `LobbyMenu`, nenhuma lista ao vivo de quem mais está conectado na sessão antes de entrar no mundo.

Dentro do mundo 3D, porém, `Core/Table/States/WaitingGameStart.cs` já cobre uma necessidade parecida na mesa: mostra "Waiting Start (Press F) / Players X/4" atualizado via `BodyEntered`/`BodyExited` na área de influência da mesa.

Perguntei ao dono do projeto se valia adicionar uma lista ao vivo na tela 2D do `LobbyMenu` (via `PlayerConnected`/`PlayerDisconnected`, mostrando IDs de peer já que não existe sistema de nome de jogador) ou se o que já existe em `WaitingGameStart` é suficiente. Resposta: **não vale a pena agora**.

## Decisão

Não implementado. `WaitingGameStart` (contagem de jogadores na mesa) considerado suficiente por enquanto.

## Verificação

N/A — nada foi implementado, por escolha explícita.
