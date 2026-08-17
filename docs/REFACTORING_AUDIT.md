# Auditoria da refatoração

Data da auditoria: 2026-08-09.

## Baseline

- 884 arquivos rastreados;
- aproximadamente 1 GB na árvore de trabalho;
- 165 scripts C#;
- cerca de 25,4 mil linhas de produção e 7,8 mil linhas de testes;
- 14 cenas de teste originais, 804 checks, 0 falhas;
- build Debug limpa, mas Release da solução apontava indevidamente para Debug;
- nenhum README, CI ou runner único;
- preset de exportação incluía todos os recursos.

## O que já estava bem desenhado

- regras centrais de pôquer e dominó são autoritativas no servidor;
- mãos privadas não são publicadas nos snapshots gerais;
- ações usam identidade do remetente e validação de turno;
- a sinuca já separava intenção da tacada, simulação determinística e correção final;
- regras possuem uma base de testes expressiva;
- cenas usam `.uid`, o que permite mover arquivos preservando identidade.

Não houve reescrita das regras que já eram sólidas. A refatoração protegeu esses contratos com a
baseline antes de mudar estrutura.

## Resultado validado

- builds Debug e Release: 0 warnings, 0 erros;
- 17 cenas automatizadas: 947 checks, 0 falhas, 0 erros de engine e 0 leaks;
- cena principal headless: exit 0 e encerramento limpo;
- exportação Windows Release: exit 0, manifesto dinâmico presente e nenhum teste/cena órfã conhecida;
- executável exportado: smoke headless exit 0, sem erro de script/engine ou leak;
- referências estruturais: 0 paths antigos, recursos ausentes ou UIDs divergentes/duplicados.

## Problemas encontrados e destino

| Achado | Risco | Tratamento |
| --- | --- | --- |
| relay de estado rejeitava pacote reenviado pelo servidor | desync entre clientes | request e apply separados, validação e regressão |
| RPCs reliable respondiam a spam inválido sem orçamento | DoS/amplificação de CPU e banda | rate limit por peer antes da validação, excesso silencioso |
| bolas replicavam posição reliable por frame | backlog e protocolo duplicado | posição somente no spawn; tacada continua autoritativa |
| capacidade da mesa era texto, não invariante | regras/presentação divergentes | mínimo/máximo centralizados e testados |
| sucesso de conexão era emitido antes do handshake | UI e estado falsos | lifecycle explícito e teste ENet host+cliente |
| sessão aceitava 12 peers para apenas 4 spawns únicos | corpos sobrepostos e overlaps falsos | limite atual alinhado a 4; ampliar exige novos markers |
| SteamID 64-bit era convertido para `int` | identidade incorreta | peer Godot do servidor permanece 1 |
| RNG de mãos usava seed previsível de conveniência | previsibilidade | seed criptográfica, regra determinística preservada |
| avatar publicava transform sob autoridade do cliente | teleport/proximidade forjada | input validado, simulação no servidor, prediction e correção |
| nickname era publicado diretamente pelo cliente | payload não limitado/identidade de outro player | RPC validado, sanitização Unicode e spawn server-owned |
| TV aceitava peers/payloads sem limites | abuso de memória/banda e filas | autorização, limites, rate window, filas e cleanup |
| TV podia ser reservada à distância e acumular captura local | negação de serviço/memória | proximidade autoritativa, filas bounded e fallback vídeo-only |
| estado inativo de espera ainda aceitava RPC de início | reinício remoto de partida ativa | estado/partida validados antes de qualquer efeito |
| queda do servidor preservava a cena da partida | rejoin com mãos/turnos/mídia antigos | reset determinístico da raiz de composição |
| reconexão acumulava identidade e timers antigos | reclaim incorreto/leak lógico | token validado, dono único, revisão de timer e cleanup |
| `PlayerRegistry` fazia busca linear e logava misses normais | ruído e acoplamento | índice O(1), lifecycle e `TryGet...` |
| classes de apresentação >1.000 linhas | manutenção humana inviável | partials por responsabilidade, contrato serializado preservado |
| testes encerravam antes de drenar `QueueFree`/áudio | falsos leaks | teardown determinístico |
| build Release gerava DLL Debug | artefato errado | mappings corrigidos e Release validada |
| DLLs temporárias do editor rastreadas | 24 MB de lixo versionado | removidas e ignoradas |
| exportava testes/cenas órfãs | build inflada | o filtro seletivo foi testado, mas o desenvolvimento voltou deliberadamente a exportar tudo para evitar dependências ausentes |
| runner não encerrava cena travada e CI não exportava | pipeline preso/regressão só no pacote | timeout por cena, export Release e smoke do executável |

## Comparação com práticas consolidadas

Padrões oficiais de Godot, Unreal e Unity convergem em servidor autoritativo, cliente enviando input,
previsão local e correção. O núcleo das três mesas estava próximo desse padrão; movimento do avatar,
sessão e mídia eram os principais desvios.

PokerStars publica três garantias relevantes: RNG verificado, ordem do deck fixa após o shuffle e
hole cards indisponíveis durante a mão. O projeto agora usa seed imprevisível e mantém a ordem
determinística no resolver, mas um listen host ainda tem acesso técnico à memória do servidor.

A arquitetura interna de 8 Ball Pool não é publicada. O que o operador publica é a necessidade de
monitoramento e sanções contra exploits. Para uma sinuca competitiva com ranking/economia, simulação
autoritativa, telemetria e detecção de anomalias continuam necessárias mesmo quando a física normal
já é validada pelo servidor.

## Fora do escopo seguro desta entrega

Itens que exigem produto/infra, não apenas refatoração local:

- conta autenticada e vínculo entre Steam/account ID e jogador;
- servidor dedicado para partidas ranqueadas ou com economia;
- armazenamento de hand history/replay assinado e trilha antifraude;
- matchmaking por habilidade e serviço global de uma partida por jogador;
- migração da API Steam P2P legada;
- telemetria, alertas e testes sob perda/latência artificiais;
- correção no arquivo-fonte do GLB da mesa cuja normal usa UV diferente da textura base.

Esses pontos estão explicitados para não confundir “build verde” com prontidão operacional de um
jogo competitivo público.
