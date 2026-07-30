# 03 — Regras de falta incompletas no Golden Nine

**Prioridade**: Alta (regras de falta incorretas mudam quem ganha a partida)
**Status**: ✅ Resolvido em 29/07/2026

## Achado

Esse item também nunca teve arquivo próprio, só o título no índice. Duas lacunas reais em `GoldenNineTurnRuler.Rule`:

1. **Falta comum + 9 encaçapada = derrota instantânea.** Qualquer falta comum (taco bate na bola errada primeiro, embocar a branca) que acontecesse a acontecer de encaçapar a 9 junto contava como `EndGameFatalFoul` — derrota na hora pro autor da falta. Isso não é a regra oficial de Golden Nine/9-ball (WPA/BCA): a 9 deveria voltar pro spot e o turno passa pro adversário com bola-na-mão, igual qualquer outra falta comum. Derrota instantânea deveria ficar só pra faltas graves de verdade (bola sair voando da mesa, por exemplo — isso já existia e ficou).
2. **"Sem tacada na tabela" não existia.** Regra clássica de sinuca: uma tacada só é válida se encaçapar alguma bola OU mandar alguma bola (qualquer bola) até uma tabela depois do contato. `TurnContext`/`GoldenNineTurnRuler` não tinham NENHUM jeito de saber se uma bola tocou a tabela — a checagem simplesmente não existia, então esse tipo de falta nunca era detectado.

Levei os dois pontos pro dono do projeto via pergunta direta (não é bug óbvio, é decisão de design):

- **P1**: implementar a falta "sem tabela"? → **Sim, implementar (Recomendado)**.
- **P2**: trocar a falta comum+9 de derrota instantânea pra regra oficial (recoloca a 9, falta normal)? → **Trocar pra regra oficial (Recomendado)**.

## Resolvido

**`GoldenNineTurnRuler.Rule`**: removido o `EndGameFatalFoul` das faltas comuns (embocar a branca, bater na bola errada primeiro) — agora essas caem em `CallCueBallReplacement` como qualquer falta comum. `EndGameFatalFoul` ficou reservado só pro caso de bola saindo fisicamente da mesa (`ballsOffTable`) com a 9 entre elas — falta grave de verdade. Adicionada checagem nova: se ninguém encaçapou nada e nenhuma bola tocou tabela (`!context.AnyRailContact`), também é `CallCueBallReplacement`.

**Recolocar a 9 (respot)**: `PoolTurnResolver.RespotGoldenBallIfScored()`, chamado no case `CallCueBallReplacement` de `ApplyTurnAction`, antes de passar o turno. Usa `PoolBallRespawn.GetFootSpotGlobalPosition()` (novo helper, espelha o padrão que já existia pro head spot) pra reposicionar a bola 9 e zerar velocidade linear/angular.

**Detecção de contato com tabela**: novo sinal `Ball.TouchedRail`, emitido em `OnRigidBodyContactEntered` quando o `body_entered` da própria `RigidBody3D` (não o `Hitbox`/Area3D de bola-com-bola — são dois sinais/handlers separados) reporta contato com um nó no grupo `"Cushion"`. Isso exigiu ligar `contact_monitor=true` + `max_contacts_reported=8` no nó raiz de `Ball.tscn` (antes só tinha o Area3D `Hitbox` pra detectar bola-com-bola; contato com corpos estáticos como a tabela precisa do contact monitor da própria RigidBody3D). `PoolGameTable._Ready()` agora coloca o nó `Rails` (StaticBody3D próprio dentro de `Table1.tscn`, separado da `Table1` raiz, de `Borders` e de `Pockets` — confirmado lendo a cena) no grupo `"Cushion"`.

`PoolTurnResolver` ganhou `AnyRailContact` (bool) + `ConnectRailListeners()`/`DisconnectRailListeners()`, ligados na mesma janela de espera que já existia em `OnStrike` (entre a tacada e `BallsMovementMonitor.BallsStopped`), escutando `TouchedRail` da branca e de toda bola em `BallsInGame`. Resetado em `ResetTurnState()`. `TurnContext` ganhou o campo `AnyRailContact`, passado pro `Rule()`.

## Verificação

`dotnet build 9Die.csproj` limpo a cada passo (0 erros, 0 warnings).

**Não testei ao vivo** — e esse é o item que mais precisa disso. Diferente do resto do backlog, aqui entrou física nova (contact monitor numa `RigidBody3D`, sinal disparado por colisão com corpo estático), não só lógica de servidor. Pontos pra confirmar numa partida real:

- Bola bate na tabela → turno segue normal (sem falta) mesmo sem encaçapar nada.
- Tacada fraca que não toca nenhuma tabela e não encaçapa nada → falta, bola-na-mão pro adversário.
- Falta comum (embocar a branca, ou bater primeiro numa bola errada) encaçapando a 9 junto → 9 volta pro foot spot, falta normal (bola-na-mão), **sem** encerrar a partida.
- Bola saindo fisicamente da mesa com a 9 entre elas → ainda é derrota instantânea (comportamento inalterado).
- Checar se `max_contacts_reported=8` é suficiente — numa tacada forte com várias bolas colidindo com a tabela quase ao mesmo tempo, mais contatos que isso por bola por frame seriam descartados (improvável em sinuca real, mas vale observar).
