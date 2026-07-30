# POOL-18 — Revisão: relação Balls/Cue/Table e necessidade do GameController

**Área**: Games/Pool (Balls, Cue, Table, GameController)
**Pedido**: revisão completa pedida pelo dono do projeto em 29/07/2026, depois de confirmar que POOL-14/16/17 não quebraram nada. Duas perguntas diretas: (1) dá pra simplificar a relação entre balls/cue/tables? (2) ainda faz sentido manter o conceito de GameController?
**Status**: ✅ Levantamento respondido em 29/07/2026 + todos os itens acionáveis implementados em 29/07/2026 (ver marcações abaixo). `dotnet build` limpo a cada etapa.

## Método

Reli o cluster inteiro do zero, pós-POOL-14/16/17: `Ball.cs` + todos os Ball states + `BallResource`, `Cue.cs` + os 6 Cue states, `CueNetworkBridge`, `CueSfx`, `AimCameraPivot`, `CueAutomaticElevation`, `BallPlacementManager`, `OffTableMonitor`, `BallsMovementMonitor`, `PoolScoreMonitor`, `PoolBallRespawn`, `PoolGameTable`, `PoolController`, `PlayerGameHandler`, `ControlSwitch`, `Table.cs`, `PoolGame.cs`, `GameModeHandler.cs`, `GameMode.cs`, `TurnResolver.cs`, mais `Cue.tscn`, `PoolController.tscn`, `Pool.tscn` pra confirmar o wiring real (não só o código).

Boa parte do que é relevante pra pergunta 1 **já estava mapeado** em [pool-simplificacao.md](pool-simplificacao.md) (POOL-7 a POOL-13) — não repito aqui, só linko e priorizo. Este arquivo cobre o que é **novo** nessa passada, mais a resposta à pergunta 2 (GameController), que ainda não tinha sido revisada explicitamente.

## Resposta 1 — dá pra simplificar Balls/Cue/Table?

Sim, mas a maior parte do que vale a pena já está listada em pool-simplificacao.md.

### Já mapeado (só linko e priorizo, não duplico)

- ✅ **POOL-7 + POOL-9** — "bola parada" é calculado 3x de formas independentes (`BallIdleState`/`BallMovingState` por bola + `BallsMovementMonitor` pra mesa toda, com constantes diferentes). Era o item de maior impacto na relação Balls↔Table dos dois grupos.
  **Resolvido em 29/07/2026**: `BallIdleState.cs`/`BallMovingState.cs` (+ a `StateMachine` inteira de `Ball.tscn`, que só existia pra hospedar esses 2 estados) removidos. `Ball.cs` agora detecta o próprio movimento direto em `_PhysicsProcess` (`UpdateMovementState`, mesmo threshold/intervalo de antes: `StopSpeedThreshold=0.01f`, `StopCheckInterval=0.1f`), emitindo `StartedMoving`/`StoppedMoving` (sinal que já existia e nunca era usado — POOL-13 já tinha notado isso). `BallsMovementMonitor` parou de repollar `LinearVelocity` a cada 0.1s e passou a assinar esses sinais por bola, mantendo um contador de bolas em movimento; o `StopTolerance` (0.5s de espera após todas pararem antes de disparar `BallsStopped`) virou um `await` cancelável por token em vez de decremento de timer em `_Process`. Uma fonte só de verdade agora.
- ✅ **POOL-8** — pose do taco duplicada em 4 states + re-check de autoridade redundante em 4 states (a StateMachine já centraliza isso).
  **Resolvido em 29/07/2026**: `Cue.SnapToRestPose()` criado (`Position = new Vector3(SpinOffset.X, SpinOffset.Y, BallRadiusOffset)`); `CueIdleState`, `CueJumpState`, `CueSpinState`, `CueLockedState` chamam esse método único em vez de repetir o snippet. Os re-checks `if (!Cue.IsMultiplayerAuthority())` removidos de `CueIdleState`/`CueChargingState`/`CueJumpState`/`CueSpinState` (confirmado: `HandleInput` só é chamado por `StateMachine._UnhandledInput`, que já gateia via `CheckForMultiplayerAuthorityOnStateHandleInput`).
- 🟡 **POOL-10 / POOL-11** — raycast todo tick sem guard, em Ball e em CueAutomaticElevation.
  **CueAutomaticElevation: resolvido em 29/07/2026** — guard `IsMultiplayerAuthority()` adicionado, igual aos vizinhos (`AimCameraPivot`, Cue states). Cada `PoolController` só computa o próprio limite de ângulo, não o de todo mundo.
  **Ball: direção original revisada, NÃO apliquei o guard de autoridade** — investiguei melhor e descobri que isso quebraria funcionalidade: `CheckGroundState()` dispara `JumpLanded`, que `BallBounceAudio` escuta **localmente em cada peer** pra tocar SFX (`Ball.Strike()` só roda de fato no servidor — `CueNetworkBridge.RequestStrike` tem `CallLocal=false` — então cada cliente depende da própria posição sincronizada + raycast local pra saber quando a bola pousou e tocar o som; gatear por autoridade faria só o servidor ouvir o som de pulo). Em vez disso, apliquei a parte seguramente cortável: `CheckGroundState()` só roda enquanto a bola está no ar ou acima do `stopThreshold` de velocidade — pula o raycast redundante pras bolas paradas na mesa (a maioria, na maior parte do tempo), sem mudar quando os sinais realmente disparam.
- ✅ **POOL-12** — `AimToggleable`/`Toggleable` com NodePath quebrado, silenciosamente morto.
  **Resolvido em 29/07/2026** — ver seção "Revisão do POOL-12" abaixo (removido, não consertado).
- 🔵 **POOL-13** — grupo de achados menores; o mais ligado a "relação com a mesa" é o último bullet (NodePath relativo de 3 níveis vs. campo C# — resolvido via item B abaixo). Os outros bullets do POOL-13 continuam em aberto, fora do escopo desta revisão.

### Novo nessa passada

**A. `AimCameraPivot` acumula 4 responsabilidades num arquivo só** (`Features/Games/Pool/GameController/AimCameraPivot.cs`, 232 linhas)

Mistura, no mesmo arquivo: (1) rotação de câmera com limite dinâmico de pitch (`ApplyRotation`/`CalculateDynamicLimit`); (2) sincronização manual da posição/rotação do modelo do Player (`SyncPlayerModelRotation`, linhas 216-230, chamada em `_PhysicsProcess:103` e em `ApplyRotation:133` — escreve direto em `Player.GlobalRotation`/`Player.GlobalPosition`); (3) troca de câmera ao terminar o ball placement (`OnPlacementFinished`); (4) escrita direta de `Input.MouseMode` em `_UnhandledInput` (linhas 111-114) — mais um dono não-coordenado do mouse mode, mesma família do já mapeado [player-1-mouse-capture-sem-dono.md](player-1-mouse-capture-sem-dono.md).

**Direção sugerida**: não é urgente (não quebra nada, não é duplicação de lógica — é só 4 preocupações num arquivo). Se for mexer: separar (2) num método/local com nome que deixe claro que é "sincronizar corpo do jogador com a mira", e resolver a escrita de mouse mode junto com o player-1 (um dono único pra `Input.MouseMode` no projeto todo, não só aqui).

**Decisão em 29/07/2026 — não implementado de propósito**: revisei de novo antes de mexer e decidi não extrair nada daqui. `SyncPlayerModelRotation` já é um método privado, nomeado, de 15 linhas — separar isso numa classe/nó próprio exigiria novo `Setup()`, novo sinal ou referência cruzada, ou seja, mais indireção pra resolver uma questão só de organização, não de duplicação real (o padrão que ele usa pra mover o Player — escrita direta de `GlobalPosition`/`GlobalRotation` — é diferente do `RemoteTransform3D` usado pro próprio `PoolController`, não são a mesma coisa disfarçada). E a parte de mouse mode depende do player-1 ser resolvido primeiro (dono único do `Input.MouseMode` no projeto inteiro), que é um escopo maior que essa revisão. Forçar uma extração agora teria adicionado complexidade, não removido.

**B. `PoolTurnResolver` recebe dependências por dois caminhos diferentes pra mesma coisa** (aprofunda o POOL-13)

Em `Pool.tscn:62-67`, o nó `Resolver` recebe `OffTableMonitor` e `BallPlacementManager` via `[Export] NodePath` relativo de 3 níveis (`../../../Scripts/OffTableMonitor`). Só que `PoolTurnResolver.Setup(TableGame)` já recebe o `PoolGame` inteiro (desde POOL-16) — e `PoolGame` já expõe `OffTableMonitor`/`BallPlacementManager` como campos públicos (`PoolGame.cs:8,12`). Os dois NodePaths em `Pool.tscn` são redundantes com algo que o resolver já tem acesso direto.

**Direção sugerida**: remover os `[Export] node_paths("OffTableMonitor", "BallPlacementManager")` do nó Resolver e ler `PoolGame.OffTableMonitor`/`PoolGame.BallPlacementManager` direto dentro do resolver. Menos wiring em `.tscn`, uma fonte só de verdade.

**Resolvido em 29/07/2026**: os dois `[Export]` removidos de `PoolTurnResolver` (o de `OffTableMonitor` nem era lido em lugar nenhum — 100% morto; os listeners já acessavam `TurnResolver.PoolGame.OffTableMonitor` direto). `HandleBallReplacement` agora usa `PoolGame.BallPlacementManager` direto. `Pool.tscn` só mantém o NodePath de `TurnRuler` (referência a um nó irmão dentro do mesmo `GameMode`, não redundante com nada).

**C. `ControlSwitch` redescobre o controller ativo por `GetChildren()[0]` em vez de usar o campo já tipado**

`Features/Player/Scripts/ControlSwitch.cs:17-25` faz `GameContextSlot.GetChildren()[0] as PoolController` toda vez que `switch_control` é pressionado. Só que `PlayerGameHandler.CurrentController` (`PlayerGameHandler.cs:9`) já é exatamente essa mesma referência — tipada, sem cast, sem depender de "o controller é sempre o filho de índice 0".

**Direção sugerida**: trocar por `Player.GameHandler.CurrentController`. Remove o `[Export] GameContextSlot` de `ControlSwitch` (fica redundante com `PlayerGameHandler.ContextSlot`) e a suposição frágil de ordem de filhos.

**Resolvido em 29/07/2026**: `ControlSwitch` agora lê `Player.GameHandler.CurrentController` direto; `[Export] GameContextSlot` removido da classe e do `Player.tscn`.

## Resposta 2 — ainda faz sentido manter o GameController?

Sim — mas o motivo mudou desde a última rodada, vale deixar isso explícito.

`PoolController` não é mais uma "camada de abstração de jogo" — isso já foi removido no [POOL-14](pool-14-colapsar-playergamecontroller.md), que colapsou o `PlayerGameController` genérico (não fazia sentido manter polimorfismo pra um jogo só). Hoje `PoolController` é, na prática, **o corpo alternativo do jogador durante a tacada**: o rig de mira (Cue + AimPivot + câmera) que cada jogador assume quando é sua vez. O jogo já tem esse padrão de "dois corpos, um ativo por vez" estabelecido em outro lugar — `Player.TakeControl()`/`GiveControl()` espelham literalmente `PoolController.TakeControl()`/`GiveControl()` (`PoolController.cs:34-83`). Não é generalidade sobrando, é a mecânica do jogo (andar até a mesa, assumir o taco, jogar, devolver o corpo).

Por que existe um `PoolController` **por jogador**, não um só compartilhado pela mesa: cada instância recebe `SetMultiplayerAuthority` do peer daquele jogador (`PlayerGameHandler.cs:17`) — é assim que cada cliente só processa input/física do próprio taco. Um rig compartilhado exigiria reatribuir autoridade a cada troca de turno, o que é mais complexo (e mais frágil em rede) do que simplesmente mostrar/esconder N instâncias já prontas. Não é over-engineering, é o jeito mais simples de resolver autoridade de rede aqui.

**Conclusão**: `PoolController` já está do tamanho certo pro que faz (114 linhas, uma responsabilidade — alternar controle conforme o turno). Não recomendo remover ou fundir o conceito. As simplificações que sobram nessa área são os itens B e C acima, mais o POOL-12 já mapeado — e vale uma correção de direção nesse:

**Revisão do POOL-12**: a direção original sugeria consertar o NodePath quebrado e migrar `TakeControl`/`GiveControl` pra usar o `Toggleable`. Vendo o conjunto agora, acho que é melhor caminho inverso: **remover o nó `AimToggleable`/`Toggleable` de `PoolController.tscn` e manter só o toggle manual que já existe em `TakeControl`/`GiveControl`**. Hoje já são dois mecanismos fazendo a mesma coisa (um morto, um vivo e funcionando); "consertar" o morto significaria manter os dois — mais complexidade, não menos. `TakeControl`/`GiveControl` já deixam claro, num lugar só, tudo que liga/desliga quando o turno muda.

### Pergunta em aberto — resolvida em 29/07/2026

`GameModeHandler.Modes`/`GameModeHandler.Switch()` não eram chamados por ninguém — só `CurrentGameMode` (setado uma vez, no editor) era de fato usado. O dono do projeto confirmou: cada modo de sinuca vai ser uma **mesa/cena diferente** (não troca em runtime na mesma mesa), então `Switch()`/`Modes` eram generalidade morta de fato.

**Resolvido**: `Array<GameMode> Modes` e `void Switch(GameMode)` removidos de `Core/Table/GameMode/GameModeHandler.cs`. `Setup(TableGame)` agora chama `CurrentGameMode.TurnResolver.Setup(tableGame)` direto, sem o loop `foreach (GetChildren())` que só existia pra popular `Modes`. `dotnet build` limpo (0 erros).

## Resumo — status final (29/07/2026)

| Item | Onde | Status |
|---|---|---|
| POOL-7 + POOL-9 (unificar "bola parada") | Ball ↔ BallsMovementMonitor | ✅ Resolvido |
| POOL-8 (pose duplicada + re-checks) | Cue states | ✅ Resolvido |
| POOL-10 (raycast sem guard) | Ball | 🟡 Revisado — guard de autoridade seria regressão (SFX local por peer); aplicado corte seguro (só roda no ar/em movimento) |
| POOL-11 (raycast sem guard) | CueAutomaticElevation | ✅ Resolvido |
| POOL-12 revisado (Toggleable morto) | GameController | ✅ Resolvido — removido, não consertado |
| B (NodePath vs. campo C# no Resolver) | Table ↔ GameMode | ✅ Resolvido |
| C (ControlSwitch usa CurrentController) | GameController | ✅ Resolvido |
| A (AimCameraPivot com 4 responsabilidades) | Cue ↔ Player ↔ Câmera | 🔵 Revisado — decidido não mexer (ver justificativa acima) |
| GameModeHandler.Switch/Modes | Table ↔ GameMode | ✅ Resolvido — removido |

Nenhuma mudança tirou funcionalidade — todas verificadas com `dotnet build 9Die.csproj` (0 erros) a cada etapa. Os únicos itens sem alteração de código (POOL-10 parcial e item A) têm justificativa explícita acima, não foram esquecidos.

Achados do [pool-simplificacao.md](pool-simplificacao.md) fora do escopo desta revisão (POOL-13 grab-bag restante) continuam em aberto — ver esse arquivo pra próximos passos, se quiser continuar.
