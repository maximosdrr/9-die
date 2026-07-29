# Infraestrutura — Simplificação e Limpeza

Itens 🟡 (simplificação/consolidação) e 🟢 (limpeza/nomenclatura) da área de infraestrutura (Autoload, Network, StateMachine, Util). Itens 🔴/🟠 dessa mesma área estão em arquivos próprios: [infra-1](infra-1-table-state-sync-sem-validacao.md), [infra-2](infra-2-disconnect-sem-guard-autoridade.md), [infra-3](infra-3-transporte-rede-steam-morto-localhost.md).

## 🟡 INFRA-4 — `Global` autoload como "caixa de referências" sem dono

`Autoload/Global.cs` só tem `Camera`/`TvScreen`, preenchidos por auto-registro de nós não relacionados (`GlobalCamera._Ready()`, `TvScreenShare._EnterTree()`). ~10 arquivos em 4 pastas diferentes leem `Global.Instance.Camera`/`.TvScreen` direto, sem checagem de nulo consistente (`TvShareButton.cs` usa `?.`; `Player.cs:44` e `PoolController.cs:60-61,71` não checam nada — risco real de NRE dependendo da ordem de `_Ready()`).

**Direção**: ou formalizar como service locator com null-safety consistente, ou parar de usar como local de auto-registro e passar as referências explicitamente (`[Export]`).

## 🟡 INFRA-5 — `AuthorityStateSynchronizer`/`PublicStateSyncronizer` são ~90% código duplicado

`Core/StateMachine/Network/*.cs` — mesma estrutura (`Setup`, `OnClientConnect`, `OnLocalStateChange`, `RemoteSyncState`), únicas diferenças são o guard de autoridade (ver [infra-1](infra-1-table-state-sync-sem-validacao.md)). Nomes nem concordam na grafia (`Synchronizer` vs `Syncronizer`).

**Direção**: colapsar em uma classe com flag `RequireAuthority`/`EnforceAuthority`, corrigindo o typo de nome.

## 🟡 INFRA-6 — `StateMachine` frágil: crash silencioso em `InitialState` mal digitado, ordem de sinal errada

`Core/StateMachine/StateMachine.cs`: `SetupInitialState()` (91-101) dá `GD.PushError` e retorna sem setar `Current` se o `InitialState` (string livre, não enum) não bater com nenhum state filho — a próxima chamada de `ChangeState` crasha com NRE longe do erro real. `ChangeState()` (30-55) emite `StateChanged` **antes** de atualizar `Current` (linha 48 vs 52) — quem ler `Current` reagindo ao sinal vê o estado antigo. Mudança de estado rejeitada por falta de autoridade falha em silêncio (sem log).

**Direção**: validar `InitialState` contra um enum/lista fechada, ou pelo menos travar a inicialização; reordenar emissão do sinal pra depois de atualizar `Current`; logar rejeições.

## 🟡 INFRA-7 — `State`/`StateMachine` amarrados a `Node3D` mesmo pra estados não-espaciais

`Core/StateMachine/State.cs:5`, `StateMachine.cs:5,77` exigem `Node3D`. Faz sentido pra Ball/Cue/Player, mas `Core/Table/States/*` (GameStarting, GameFinished, WaitingGameStart) são puro fluxo de jogo/timer, sem semântica espacial nenhuma, e carregam o peso do framework só pra satisfazer o tipo.

**Direção**: baixa prioridade — considerar uma variante `Node`-based do framework, ou aceitar como está.

## 🟢 INFRA-8 — `GameGroups` autoload morto e com bug de casing

`Autoload/GameGroups.cs` define `Ball="ball"` mas nunca é referenciado em lugar nenhum — `Ball.cs:81` faz `AddToGroup("Ball")` (maiúsculo) direto, ignorando a constante que deveria evitar exatamente esse tipo de divergência.

**Direção**: deletar `GameGroups` (não usado) ou consertar `Ball.cs` pra usar a constante e corrigir o casing.

## 🟢 INFRA-9 — `SteamGlobals` inicializa Steam sempre, ignorando a própria config do projeto

`Autoload/Network/Steam/SteamGlobals.cs` chama `InitializeSteam()` incondicionalmente, ignorando `project.godot`'s `[steam] initialization/initialize_on_startup=false`. Sem uso real já que o transporte ativo é ENet (ver [infra-3](infra-3-transporte-rede-steam-morto-localhost.md)).

**Direção**: gatear pela config real, ou remover o autoload até o transporte Steam ser retomado.

## 🟢 INFRA-10 — `StatesRef` mistura domínios de todas as features num bag `Core`

`Core/StateMachine/StatesRef.cs` tem constantes de Ball, Cue, Table E Player todas juntas — o framework genérico em `Core/` carrega conhecimento embutido de cada feature específica.

**Direção**: mover pra perto de cada feature, ou aceitar como registry central deliberado (documentar a escolha).

## 🟢 INFRA-11 — `PlayerRegistry.HasContainer()` morto; `GetPlayerById` retorna null sem padrão consistente

`Autoload/PlayersContainer/PlayerRegistry.cs:14` nunca é chamado (existe especificamente pra evitar o NPE que `GetPlayerById` pode gerar). `GetPlayerById` retorna null + `GD.PushError`, empurrando o null-check pra 4 call sites diferentes.

**Direção**: usar `HasContainer()` dentro do próprio `GetPlayerById`, ou padronizar um `TryGetPlayerById`.

## 🟢 INFRA-12 — `LevelMultiplayerManager.PlayerScene` carregado por UID hardcoded no código

`Core/Level/LevelMultiplayerManager.cs:7` — `GD.Load<PackedScene>("uid://...")` enquanto `PlayersContainer`/`MultiplayerSpawner` no mesmo arquivo são `[Export]`. Inconsistente e quebra em silêncio se o UID mudar.

**Direção**: trocar por `[Export]`.

## 🟢 INFRA-13 — Config: `physics_ticks_per_second=580` sem justificativa documentada

`project.godot:107` — quase 10x o default do Godot, decisão real de tuning mas sem comentário em lugar nenhum explicando por quê. Fácil de alguém "corrigir" de volta pro default no futuro.

**Direção**: documentar a razão onde a decisão foi tomada (ou aqui mesmo).
