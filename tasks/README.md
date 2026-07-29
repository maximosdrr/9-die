# Backlog — 9Die (Sinuca)

Levantamento do que falta para uma partida de sinuca ser jogável do início ao fim, feito em 27/07/2026 logo após a conversão do projeto de GDScript para C#. A arquitetura central (física, tacada, turnos, sincronização de rede) já está implementada e portada — o que falta aqui é o que fica em volta dela.

## Prioridade Alta (bloqueia uma partida completa)

- [01 - Tela de fim de partida](01-tela-fim-de-partida.md)
- [02 - Jogador desconecta e trava o jogo](02-desconexao-trava-jogo.md)
- [03 - Regras de falta incompletas no Golden Nine](03-regras-falta-golden-nine.md)

## Prioridade Média (feedback e UX)

- [04 - NotificationPopup órfão](04-notification-popup-orfao.md)
- [05 - Placar durante a partida](05-placar-durante-partida.md)
- [06 - Lobby sem lista de jogadores](06-lobby-lista-jogadores.md)

## Prioridade Baixa (robustez e polimento)

- [07 - Reconexão / espectador](07-reconexao-espectador.md)
- [08 - Ativar Steam como provider de rede](08-ativar-steam-provider.md)
- [09 - Export presets incompletos](09-export-presets.md)

## Concluído

- ~~Addon `godotsteam_server` quebrado (DLL desatualizada, não usado pelo projeto)~~ — removido em 27/07/2026.

---

# Arquitetura e Qualidade de Código

Levantamento separado, feito em 29/07/2026, a pedido do dono do projeto ("tudo muito bagunçado, conceitos, abstrações, decisões que foram tomadas"). Cobre a árvore inteira do projeto (`Autoload/`, `Core/`, `Features/`, config). Diferente do backlog acima — isso aqui não é sobre features faltando, é sobre qualidade/arquitetura do que já existe. Cada arquivo tem contexto suficiente pra atacar sozinho (ex.: "resolve o pool-1").

**Escopo confirmado em 29/07/2026**: só sinuca (sem outros minijogos), mas com múltiplos modos de sinuca planejados além de Golden Nine. Isso já influenciou algumas decisões abaixo — ver seção "Simplificações validadas pro escopo fixo".

`- [ ]` = pendente, `- [x]` = resolvido (data no próprio arquivo).

## 🔴 Críticos (segurança de rede, crash real)

- [ ] [infra-1 - Table state sync sem validação de remetente](infra-1-table-state-sync-sem-validacao.md)
- [ ] [pool-1 - Cue strike sem validação de turno/força](pool-1-cue-strike-sem-validacao.md)
- [ ] [pool-2 - Ball placement sem validação nenhuma](pool-2-ball-placement-sem-validacao.md)
- [ ] [pool-3 - Início de partida client-driven](pool-3-start-game-client-driven.md)
- [ ] [tv-1 - Regressão: gate de Windows perdido no refactor de proximidade](tv-1-regressao-gate-windows.md)

## 🟠 Bugs de lógica

- [ ] [infra-2 - Disconnect sem guard de autoridade](infra-2-disconnect-sem-guard-autoridade.md)
- [ ] [infra-3 - Transporte de rede: Steam morto, localhost hardcoded](infra-3-transporte-rede-steam-morto-localhost.md)
- [ ] [pool-4 - Vencedor errado em falta fatal (Golden Nine)](pool-4-vencedor-errado-falta-fatal.md)
- [ ] [pool-5 - Áudio de tacada sem clamp (volume/pitch)](pool-5-audio-tacada-sem-clamp.md)
- [ ] [pool-6 - Ball placement RPC provavelmente sem relay pra espectadores](pool-6-ball-placement-rpc-sem-relay.md)
- [ ] [player-1 - Captura de mouse sem dono único](player-1-mouse-capture-sem-dono.md)
- [ ] [player-2 - PlayerGameHandler: Player vs (Player)Owner](player-2-game-handler-owner-vs-player.md)
- [ ] [repo-1 - Tecla F duplicada (interact vs start_game)](repo-1-tecla-f-duplicada.md)

## 🟡 Simplificações validadas pro escopo fixo (só sinuca, múltiplos modos)

Achados numa segunda passada depois de confirmar o escopo com o dono do projeto — distintos do resto porque dependem dessa decisão pra fazer sentido (ver discussão nos arquivos).

- [x] [pool-14 - Colapsar PlayerGameController genérico pra dentro de PoolController](pool-14-colapsar-playergamecontroller.md)
- [x] [pool-16 - Tipar o contexto de turno (TurnContext)](pool-16-tipar-contexto-turno.md)
- [x] [pool-17 - Renomear resolver/listeners de GoldenNine pra genérico](pool-17-generalizar-resolver-listeners.md)

## 🔵 Revisão de arquitetura (perguntas do dono do projeto)

- [x] [pool-18 - Revisão: relação Balls/Cue/Table e necessidade do GameController](pool-18-revisao-balls-cue-tables-gamecontroller.md)
- [x] [pool-19 - Análise profunda: a construção de Cue/Ball/Table/GameMode faz sentido?](pool-19-arquitetura-cue-table-gamemode.md)

## 🟡🟢 Simplificação e limpeza (agrupados por área)

- [Infraestrutura — simplificação e limpeza](infra-simplificacao.md) (Autoload, Network, StateMachine, Util)
- [Lógica de sinuca — simplificação e limpeza](pool-simplificacao.md) (Core/Table, Features/Games/Pool)
- [Player / UI / Câmera — simplificação e limpeza](player-simplificacao.md)
- [TV — simplificação e limpeza](tv-simplificacao.md) (Core/Tv, TvShareButton)
- [Config e higiene de repositório — limpeza](repo-limpeza.md) (cross-cutting)
