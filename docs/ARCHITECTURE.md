# Arquitetura

## Objetivo

A estrutura separa composição, infraestrutura, mundo compartilhado e cada jogo. A unidade de
organização é uma responsabilidade reconhecível por uma pessoa, não apenas “Scripts” ou “Core”.

```text
App
 ├── compõe World e Games
 └── usa Infrastructure para abrir uma sessão

Games ─────────┐
World ─────────┼──> Shared
Infrastructure┘
```

Dependências aceitáveis:

- `App` conhece todos os módulos porque é a raiz de composição;
- `Games` pode usar contratos de `Shared` e serviços explicitamente fornecidos;
- `World` pode usar `Shared`;
- `Infrastructure` implementa sessão/transporte e não contém regras de pôquer, dominó ou sinuca;
- `Shared` não depende de um jogo concreto.

Um jogo não deve referenciar classes internas de outro jogo. Se duas modalidades precisam do mesmo
conceito, o conceito recebe um contrato pequeno em `Shared`.

## Composição em runtime

`App/Main.tscn` instancia o nível protótipo, câmera global e menu. O nível contém o spawner de
jogadores, as mesas, o ranking e a TV. A raiz também referencia
`Shared/Resources/RuntimeResourceManifest.tres`: ele é a lista serializada dos assets que o código
carrega dinamicamente. O preset de desenvolvimento atualmente exporta todos os recursos; o
manifesto permanece como inventário explícito desses carregamentos. Os autoloads têm escopo de
processo:

| Serviço | Responsabilidade |
| --- | --- |
| `Global` | preferências locais e identidade efêmera de reconexão |
| `NetworkManager` | escolhe transporte e inicia host/cliente |
| `ReconnectionManager` | reserva temporariamente um assento após queda |
| `PlayerRegistry` | índice dos jogadores vivos da cena atual |
| `MatchRanking` | placar local da sessão |
| `SteamGlobals` | inicialização opcional do SDK Steam |

Autoload não é depósito de conveniência. Um novo singleton precisa representar estado realmente
único durante todo o processo e ter lifecycle explícito.

## Mesas

`Table` é a casca de interação e lifecycle. `TableGame` define o contrato de uma modalidade sentada;
o modelo, as colisões e os marcadores específicos pertencem à cena de cada jogo:

- `MinimumPlayers` e `MaximumPlayers` são invariantes, não apenas texto de UI;
- a máquina de estados controla espera, contagem, partida e resultado;
- `TableTurnNetworkBridge` replica apenas eventos de turno/partida;
- regras concretas ficam no módulo do jogo;
- controllers e presenters transformam estado em interação e imagem, mas não decidem regras.

Fluxo principal:

```text
WaitingGameStart
  -> valida solicitante e jogadores presentes no servidor
  -> GameStarting
  -> cria TableGame e ordem de turnos
  -> GameStarted
  -> GameFinished
  -> limpa controllers e retorna à espera
```

## Estrutura de cada jogo

Cada jogo é organizado pelos mesmos conceitos, mesmo quando nem todos exigem uma pasta própria:

```text
Games/<Jogo>/
  Rules/          tipos e regras determinísticas, sem cena/rede
  Simulation/     física determinística quando aplicável
  GameModes/      orquestração autoritativa da partida
  Runtime/        integração do jogo com TableGame
  GameController/ entrada e apresentação do jogador sentado
  Components/     cenas reutilizáveis do próprio jogo
  Tests/          regressões co-localizadas
```

Pôquer e dominó compartilham `SecretHandTurnResolver`: o servidor guarda informação privada e envia
a cada peer somente a mão correspondente. Sinuca usa uma simulação determinística separada da
representação 3D.

## Estado e serialização

O código legado ainda usa `Godot.Collections.Dictionary` em snapshots e metadados. Na fronteira de
rede, todo dicionário tem limites de profundidade, itens, strings e bytes. Para protocolos novos,
prefira DTOs explícitos e versionados, convertidos para tipos Godot apenas no adaptador RPC.

Regras para evolução:

- adicionar `protocol_version` quando o wire format mudar;
- nunca depender da ordem casual das chaves;
- validar campos ausentes e desconhecidos;
- manter snapshots idempotentes;
- separar dados públicos de dados privados.

## Apresentação

Classes de apresentação grandes são `partial` apenas quando os arquivos representam áreas coesas e
continuam formando um único componente Godot. Isso foi usado para preservar o contrato serializado
de presenters antigos sem manter arquivos de mais de mil linhas. Para código novo, prefira
colaboradores injetados e menores; `partial` é uma ponte de migração, não o padrão automático.

## Composição de cenas

Uma cena principal deve explicar o sistema pela árvore, sem exigir a leitura imediata do script:

- roots representam comportamento (`Player`, `TvScreenShare`, `PoolGame`), não um mesh usado como
  contêiner acidental;
- galhos grandes de renderização ficam em cenas `*Visual.tscn`;
- modelos de mesa, cadeiras e suas colisões ficam em `*TableFixture.tscn`;
- miniárvores repetidas viram componentes, como `Shared/Table/Components/SeatAnchor.tscn`;
- dependências fixas entre nós são campos tipados exportados no Inspector; busca por nome fica
  reservada a filhos internos estáveis ou objetos localizados dinamicamente;
- nós sem posição usam `Node`; `Node3D` existe apenas quando o transform faz parte do contrato;
- UI e apresentação exclusivas do jogador local não são filhas do avatar replicado;
- `_Process`, `_PhysicsProcess` e input ficam desligados enquanto o componente está ocioso.

O nível principal segue quatro ramos legíveis: ambiente visual, colisão estática, gameplay e
infraestrutura multiplayer. O avatar segue a mesma separação: corpo/estado replicado,
`CharacterVisual` substituível e `LocalPlayerPresentation` criada somente para a autoridade local.

Ao criar uma cena nova, o teste de carregamento deve instanciá-la e provar seus contratos exportados.
Para uma cena de rede, preserve também os NodePaths dos nós que possuem RPC.

## Compatibilidade com cenas e RPCs

Mover um arquivo e atualizar `res://` é seguro quando seu `.uid` acompanha o arquivo. Alterar a
hierarquia de nós não é equivalente: a assinatura dos RPCs Godot inclui o `NodePath`. Por isso esta
refatoração move fontes e recursos, mas preserva nomes/hierarquias de nós que participam da rede.
