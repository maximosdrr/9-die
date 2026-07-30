# Backlog — 9Die (Sinuca)

Levantamento do que falta para uma partida de sinuca ser jogável do início ao fim, feito em 27/07/2026 logo após a conversão do projeto de GDScript para C#. A arquitetura central (física, tacada, turnos, sincronização de rede) já está implementada e portada — o que falta aqui é o que fica em volta dela.

Os itens 100% concluídos (01,02,03,04,05,06,09 + todos os 🔴/🟠 críticos originais + as simplificações validadas de escopo fixo + a revisão de arquitetura) foram removidos daqui em 29-30/07/2026 a pedido do dono do projeto — a correção já está no código e no histórico do git, o arquivo de tarefa não tinha mais função. Só ficam registrados os itens que ainda têm alguma ponta em aberto.

## Prioridade Baixa (robustez e polimento)

- [08 - Ativar Steam como provider de rede](08-ativar-steam-provider.md) — ativado e testado até onde dá sozinho (hospedar já funciona); falta confirmar join com uma segunda identidade Steam

---

# Arquitetura e Qualidade de Código

Levantamento separado, feito em 29/07/2026, a pedido do dono do projeto ("tudo muito bagunçado, conceitos, abstrações, decisões que foram tomadas"). Cobre a árvore inteira do projeto (`Autoload/`, `Core/`, `Features/`, config).

**Escopo confirmado em 29/07/2026**: só sinuca (sem outros minijogos), mas com múltiplos modos de sinuca planejados além de Golden Nine.

Todos os itens 🔴/🟠 (infra-1, infra-2, infra-3, player-1, player-2, pool-1 a pool-6, tv-1) estão resolvidos — ver histórico do git. Os itens 🟡/🟢 de simplificação continuam agrupados por área (abaixo) porque cada arquivo mistura itens resolvidos com itens fechados sem ação, e alguns ainda genuinamente em aberto.

## 🟡🟢 Simplificação e limpeza (agrupados por área)

Cada arquivo mistura itens já resolvidos com itens fechados sem ação (decisão deliberada de não fazer) e alguns ainda genuinamente em aberto — por isso continuam inteiros, não dá pra apagar sem perder contexto.

Restam só itens de baixo valor ou deliberadamente adiados (precisam de teste ao vivo de áudio, ou esforço/risco não justificado agora) — nada acionável sem nova decisão do dono do projeto:

- [Infraestrutura — simplificação e limpeza](infra-simplificacao.md) (Autoload, Network, StateMachine, Util)
- [Lógica de sinuca — simplificação e limpeza](pool-simplificacao.md) (Core/Table, Features/Games/Pool)
- [Player / UI / Câmera — simplificação e limpeza](player-simplificacao.md)
- [TV — simplificação e limpeza](tv-simplificacao.md) (Core/Tv, TvShareButton)
- [Config e higiene de repositório — limpeza](repo-limpeza.md) (cross-cutting)
