# Backlog — 9Die (Sinuca)

Levantamento do que falta para uma partida de sinuca ser jogável do início ao fim, feito em 27/07/2026 logo após a conversão do projeto de GDScript para C#. A arquitetura central (física, tacada, turnos, sincronização de rede) já está implementada e portada — o que falta aqui é o que fica em volta dela.

Os itens 100% concluídos (01,02,03,04,06,09 + todos os 🔴/🟠 críticos originais + as simplificações validadas de escopo fixo + a revisão de arquitetura) foram removidos daqui em 29/07/2026 a pedido do dono do projeto — a correção já está no código e no histórico do git, o arquivo de tarefa não tinha mais função. Só ficam registrados os itens que ainda têm alguma ponta em aberto.

## Prioridade Média (feedback e UX)

- [ ] [05 - Placar durante a partida](05-placar-durante-partida.md) — adiado, ver arquivo

## Prioridade Baixa (robustez e polimento)

- [07 - Reconexão / espectador](07-reconexao-espectador.md) — espectador já funciona; reconexão de verdade adiada
- [08 - Ativar Steam como provider de rede](08-ativar-steam-provider.md) — bloqueado por App ID real do Steamworks

---

# Arquitetura e Qualidade de Código

Levantamento separado, feito em 29/07/2026, a pedido do dono do projeto ("tudo muito bagunçado, conceitos, abstrações, decisões que foram tomadas"). Cobre a árvore inteira do projeto (`Autoload/`, `Core/`, `Features/`, config).

**Escopo confirmado em 29/07/2026**: só sinuca (sem outros minijogos), mas com múltiplos modos de sinuca planejados além de Golden Nine.

Itens totalmente resolvidos foram removidos (mesma limpeza do backlog acima). Ficam só os que ainda têm alguma parte em aberto:

- [infra-3 - Transporte de rede: Steam morto, localhost hardcoded](infra-3-transporte-rede-steam-morto-localhost.md) — parcial: bugs concretos corrigidos, falta UI de IP remoto e decisão Steam-vs-ENet
- [player-1 - Captura de mouse sem dono único](player-1-mouse-capture-sem-dono.md) — parcial: fix seguro aplicado, centralização completa fica pra quando puder ser testada ao vivo

## 🟡🟢 Simplificação e limpeza (agrupados por área)

Cada arquivo mistura itens já resolvidos com itens fechados sem ação (decisão deliberada de não fazer) e alguns ainda genuinamente em aberto — por isso continuam inteiros, não dá pra apagar sem perder contexto.

- [Infraestrutura — simplificação e limpeza](infra-simplificacao.md) (Autoload, Network, StateMachine, Util)
- [Lógica de sinuca — simplificação e limpeza](pool-simplificacao.md) (Core/Table, Features/Games/Pool)
- [Player / UI / Câmera — simplificação e limpeza](player-simplificacao.md)
- [TV — simplificação e limpeza](tv-simplificacao.md) (Core/Tv, TvShareButton)
- [Config e higiene de repositório — limpeza](repo-limpeza.md) (cross-cutting)
