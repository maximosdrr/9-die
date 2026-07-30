# Infraestrutura — Simplificação e Limpeza

Itens 🟡 (simplificação/consolidação) e 🟢 (limpeza/nomenclatura) da área de infraestrutura (Autoload, Network, StateMachine, Util). Itens 🔴/🟠 dessa mesma área estão em arquivos próprios: infra-1, infra-2, [infra-3](infra-3-transporte-rede-steam-morto-localhost.md).

## 🟡 INFRA-4 — `Global` autoload como "caixa de referências" sem dono (parcial)

**Parcial, resolvido em 29/07/2026**: os dois pontos concretos de risco de NRE foram corrigidos — `Player.cs` (`TakeControl`) e `PoolController.cs` (`TakeControl`/`GiveControl`) agora usam `Global.Instance.Camera?.` igual ao que `TvShareButton.cs` já fazia.

**Ainda em aberto**: a decisão arquitetural maior (formalizar como service locator vs. parar de auto-registrar e passar por `[Export]`) não foi tomada — são dois caminhos com esforço/escopo bem diferentes (o segundo tocaria ~10 arquivos), não dá pra decidir sozinho sem validar com o dono do projeto. `Autoload/Global.cs` continua do jeito que estava.

## ✅ INFRA-5 — `AuthorityStateSynchronizer`/`PublicStateSyncronizer` são ~90% código duplicado

**Resolvido em 29/07/2026** (via infra-1) — a correção do infra-1 trocou o único consumidor de `PublicStateSyncronizer` (a `Table`) pro `AuthorityStateSynchronizer`, deixando `PublicStateSyncronizer` sem nenhum uso — removido inteiramente em vez de colapsado. A duplicação sumiu por eliminação de um dos dois, não por fusão.

~~`Core/StateMachine/Network/*.cs` — mesma estrutura (`Setup`, `OnClientConnect`, `OnLocalStateChange`, `RemoteSyncState`), únicas diferenças são o guard de autoridade. Nomes nem concordam na grafia (`Synchronizer` vs `Syncronizer`).~~

## ✅ INFRA-6 — `StateMachine` frágil: crash silencioso em `InitialState` mal digitado, ordem de sinal errada

**Resolvido em 29/07/2026**: `ChangeState` agora reordena a emissão de `StateChanged` pra **depois** de `Current` ser atualizado (quem reage ao sinal já vê o estado novo); `ChangeState` também não crasha mais com NRE se `Current` for `null` (guard `Current != null` antes de comparar `.Type`, e `Current?.Exit(...)`); rejeição por falta de autoridade agora loga via `GD.PushWarning` em vez de falhar em silêncio; `SetupInitialState` continua sem travar a inicialização (mantém o `GD.PushError` e segue), mas o crash em cadeia que isso causava no primeiro `ChangeState` não existe mais.

~~`Core/StateMachine/StateMachine.cs`: `SetupInitialState()` (91-101) dá `GD.PushError` e retorna sem setar `Current` se o `InitialState` (string livre, não enum) não bater com nenhum state filho — a próxima chamada de `ChangeState` crasha com NRE longe do erro real. `ChangeState()` (30-55) emite `StateChanged` **antes** de atualizar `Current` (linha 48 vs 52) — quem ler `Current` reagindo ao sinal vê o estado antigo. Mudança de estado rejeitada por falta de autoridade falha em silêncio (sem log).~~

## 🟡 INFRA-7 — `State`/`StateMachine` amarrados a `Node3D` mesmo pra estados não-espaciais

`Core/StateMachine/State.cs:5`, `StateMachine.cs:5,77` exigem `Node3D`. Faz sentido pra Ball/Cue/Player, mas `Core/Table/States/*` (GameStarting, GameFinished, WaitingGameStart) são puro fluxo de jogo/timer, sem semântica espacial nenhuma, e carregam o peso do framework só pra satisfazer o tipo.

**Direção**: baixa prioridade — considerar uma variante `Node`-based do framework, ou aceitar como está.

## ✅ INFRA-8 — `GameGroups` autoload morto e com bug de casing

**Resolvido em 29/07/2026** — `GameGroups.cs`/`.cs.uid` removidos (não usados em lugar nenhum) e a entrada `Groups=...` removida do `[autoload]` em `project.godot`. `Ball.cs` continua usando o literal `"Ball"` direto, que é o único caminho real em uso.

~~`Autoload/GameGroups.cs` define `Ball="ball"` mas nunca é referenciado em lugar nenhum — `Ball.cs:81` faz `AddToGroup("Ball")` (maiúsculo) direto, ignorando a constante que deveria evitar exatamente esse tipo de divergência.~~

## ✅ INFRA-9 — `SteamGlobals` inicializa Steam sempre, ignorando a própria config do projeto

**Resolvido em 29/07/2026** — `SteamGlobals._Ready()` agora lê `ProjectSettings.GetSetting("steam/initialization/initialize_on_startup", false)` e só chama `InitializeSteam()` se estiver `true`. Com o valor atual (`false`), o autoload volta a ser inerte, consistente com o transporte Steam estar dormente (ver [infra-3](infra-3-transporte-rede-steam-morto-localhost.md)).

~~`Autoload/Network/Steam/SteamGlobals.cs` chama `InitializeSteam()` incondicionalmente, ignorando `project.godot`'s `[steam] initialization/initialize_on_startup=false`.~~

## 🟢 INFRA-10 — `StatesRef` mistura domínios de todas as features num bag `Core`

**Decisão em 29/07/2026**: aceito como registry central deliberado, sem mudança de código — mover cada constante pra perto da sua feature tocaria referências espalhadas por todo o projeto pra um ganho puramente organizacional. Documentando aqui a escolha, como a própria direção original já oferecia como opção válida.

`Core/StateMachine/StatesRef.cs` tem constantes de Ball, Cue, Table E Player todas juntas — o framework genérico em `Core/` carrega conhecimento embutido de cada feature específica.

## ✅ INFRA-11 — `PlayerRegistry.HasContainer()` morto; `GetPlayerById` retorna null sem padrão consistente

**Resolvido em 29/07/2026** — `GetPlayerById` agora chama `HasContainer()` no início e retorna `null` + `GD.PushError` cedo se o container ainda não foi setado, em vez de deixar `HasContainer()` como um método morto ao lado do NPE que ele existia pra evitar.

~~`Autoload/PlayersContainer/PlayerRegistry.cs:14` nunca é chamado (existe especificamente pra evitar o NPE que `GetPlayerById` pode gerar).~~

## ✅ INFRA-12 — `LevelMultiplayerManager.PlayerScene` carregado por UID hardcoded no código

**Resolvido em 29/07/2026** — `PlayerScene` virou `[Export] public PackedScene`, wireado em `Prototype.tscn` apontando pra `Player.tscn`, consistente com `PlayersContainer`/`MultiplayerSpawner` no mesmo arquivo.

~~`Core/Level/LevelMultiplayerManager.cs:7` — `GD.Load<PackedScene>("uid://...")` enquanto `PlayersContainer`/`MultiplayerSpawner` no mesmo arquivo são `[Export]`.~~

## 🟢 INFRA-13 — Config: `physics_ticks_per_second=580` sem justificativa documentada

`project.godot:107` — quase 10x o default do Godot, decisão real de tuning mas sem comentário em lugar nenhum explicando por quê. Fácil de alguém "corrigir" de volta pro default no futuro.

**Não resolvido em 29/07/2026** — não encontrei a razão original documentada em nenhum lugar (git log/commits não explicam), e não quis inventar uma justificativa e colar como se fosse a real. Motivo técnico mais provável, pra quem for revisitar: sinuca depende de resposta de colisão precisa entre esferas pequenas e rápidas contra colisores finos (tabelas, caçapas) — um tick rate baixo aumenta o risco de tunneling/overlap perdido num impacto forte. Vale confirmar com quem tomou a decisão antes de "corrigir" de volta pro default.
