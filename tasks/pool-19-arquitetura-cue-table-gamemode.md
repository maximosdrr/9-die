# POOL-19 — Análise profunda: a construção de Cue/Ball/Table/GameMode faz sentido?

**Área**: Games/Pool (Cue, GameController, Table, GameMode)
**Pedido**: seguindo o [pool-18](pool-18-revisao-balls-cue-tables-gamecontroller.md), o dono do projeto pediu uma análise mais profunda — "a maneira que o sistema do taco + bola + mesa + modo de jogo foi construído faz sentido? Teria alguma forma melhor e mais natural de construir isso?" — em 29/07/2026.
**Status**: ✅ Analisado e implementado em 29/07/2026. `dotnet build` limpo a cada etapa.

## Veredito geral

A espinha dorsal faz sentido: `Table` (slot genérico) → `TableGame`/`PoolGame` (orquestração de partida) → `GameModeHandler`/`GameMode` (regras) → `PoolController` (rig por jogador) → `Cue`/`Ball` (física) reflete separações reais — regra vs. mecanismo, fronteira de autoridade de rede, `PoolGame` como hub de contexto da partida. Não é acidental nem inchada.

Encontrei **dois pontos concretos** onde a construção não é natural, e um terceiro que era mais debate de estilo que problema real.

## 1. "Ângulo de mira" não tinha dono — resolvido

`Cue`, `AimCameraPivot` e `CueAutomaticElevation` eram 3 scripts irmãos lendo/escrevendo campos públicos uns dos outros pra decidir uma coisa só (até onde o taco pode subir e a câmera pode olhar):

1. `CueAutomaticElevation` (sensor externo, raycast) calculava `safeLimit` e escrevia direto em `Cue.MinSafeAngle`.
2. `CueJumpState.AdjustElevation` clampava `CurrentElevation` contra esse `MinSafeAngle`.
3. `Cue._Process` clampava de novo (`Mathf.Min(CurrentElevation, MinSafeAngle)`).
4. `AimCameraPivot` lia `Cue.Rotation.X` (valor já suavizado) pra derivar o próprio limite de câmera.

Nenhum dos 3 era "dono" do conceito — o valor viajava por referências públicas soltas, escrito de fora.

**Resolvido**: `CueAutomaticElevation.cs` removido. `Cue.cs` agora calcula o próprio `MinSafeAngle` (novo `_PhysicsProcess` com guard de autoridade + `UpdateSafeAngleLimit()`, usando o `CameraPivot` que o `Cue` já recebia em `Setup()` — não precisou de campo novo). O sensor físico (`RayCast3D`) continua fisicamente onde estava (filho de `AimPivot` em `PoolController.tscn`, **não** dentro de `Cue.tscn` — importante: `Cue` muda a própria rotação pra elevação, então o sensor não pode ser filho dele, senão giraria junto e o resultado ficaria circular). `Cue` agora só recebe uma referência `[Export] RayCast3D CueHandleSensor` apontando pro sensor via NodePath externo, igual como já recebe `CueSfx`/`StrokeNetworkBridge`.

O nó `Scripts/CueAutomaticElevantion` (que também tinha o typo "Elevantion" já mapeado no POOL-13) foi removido de `PoolController.tscn` — como não sobrou mais nada em `Scripts`, o nó container também saiu.

**Câmera continua acoplada a `Cue.Rotation.X`** — isso é intencional e correto (a câmera precisa saber pra onde o taco está apontando pra não "atravessar o chão" quando o taco sobe), não mudei.

## 2. `Table.GameControllerScene` era genérico sem nunca ter sido genérico — resolvido

`Table.cs` expunha `[Export] PackedScene GameControllerScene` como se pudesse ser "qualquer coisa" — mas `PlayerGameHandler.EquipGameController` já fazia cast direto pra `PoolController` (`(PoolController)controllerInstance`). A genericidade era ficção: só existe uma cena possível, e ela é sempre a mesma (`PoolController.tscn`, referenciada uma vez em `Prototype.tscn`).

Isso é diferente do `TableGameScene`, que continua genérico de verdade — tem uso real no modo `[Tool]` (preview no editor, `Table.RebuildEditorPreview()`).

**Resolvido**: `GameControllerScene` saiu de `Table.cs` e virou `[Export] PackedScene GameControllerScene` em `PoolGame.cs` — a classe que de fato consome o valor (`OnMatchStarts`). O valor agora é setado uma vez dentro do próprio `Pool.tscn` (aponta pra `PoolController.tscn`), em vez de precisar ser lembrado toda vez que alguém instancia um `Table` num nível novo. `Prototype.tscn` não precisa mais setar isso na instância de `Table` — `Pool.tscn` já "traz o próprio controller".

**Bônus encontrado no caminho**: `Table.PlayerGameControllerHandler` (`Node3D` buscado em `_Ready()`) não tinha nenhum leitor em lugar nenhum do projeto — resquício de quando o `PoolController` vivia debaixo da `Table` em vez de dentro do `PlayerGameHandler` de cada jogador (antes do POOL-14). Removido o campo e o nó `PlayerGameControllerHandler` de `Table.tscn`.

## 3. Os 3 listeners do GameMode — colapsados, a pedido do dono do projeto

`PoolCueBallContactListener`/`PoolScoreListener`/`PoolBallFellOffListener` eram 3 `Node`s inteiros só pra conectar 1 sinal físico cada num campo do `TurnResolver`. Isso não é "errado" — é consistente com o idioma que o resto do projeto usa (um nó por concern, visível na árvore, ex. `BallBounceAudio`/`BallCollisionAudio` em `Ball.tscn`) — então inicialmente registrei isso só como trade-off de estilo, sem recomendação forte. O dono do projeto concordou com a opinião de que colapsar era melhor e pediu pra fazer.

**Resolvido**: os 3 arquivos removidos. A lógica virou 3 métodos privados em `PoolTurnResolver.cs` (`OnBallTouchScoreGround`, `OnBallFellOff`, `OnCueBallContact`), conectados direto em `ConnectSignals()`/`OnStrike()`. `PoolTurnResolver.cs` foi de ~155 pra ~185 linhas — ainda bem longe de "classe gigantesca". A reusabilidade entre modos de sinuca não muda: quem é reaproveitável entre modos sempre foi o `PoolTurnResolver` inteiro (confirmado no POOL-17), não os listeners individualmente — colapsá-los não perde nada nesse sentido, só remove 3 arquivos + 3 nós + 3 `GetNode`/`Setup()` de plumbing puro.

## O que eu NÃO mudei, e por quê

- **`PoolGame` como hub central** (Cue, AimPivot, Controller, listeners todos seguram uma referência a ele) — isso é bom, é o padrão são de "objeto de contexto de partida" que a maioria dos motores de jogo tem. Não é bagunça.
- **Câmera lendo `Cue.Rotation.X`** — acoplamento intencional, não duplicação.

## Verificação

`dotnet build 9Die.csproj` limpo (0 erros) após cada um dos 3 itens. Não joguei uma partida pra confirmar em runtime — a mudança do sensor de elevação em particular (item 1) mexe em geometria de raycast, vale testar apertando o modificador de elevação perto de um obstáculo (embaixo de algo) e conferir que o taco ainda para de subir na altura certa, igual antes.
