# POOL-6 — `BallPlacementManager.Rpc(...)` provavelmente não alcança espectadores em partida de 3-4 jogadores

**Área**: Lógica de Sinuca (Ball Placement)
**Prioridade**: 🟠 Bug

## Problema

`BallPlacementManager.cs:45` chama `Rpc(...)` sem relay manual pros outros peers, diferente de todo outro broadcast do projeto que precisa alcançar todo mundo (`TableTurnNetworkBridge.cs:43-65`, os dois synchronizers — todos fazem loop manual em `Multiplayer.GetPeers()`).

Em partida de 3-4 jogadores, quem não é o host nem quem está posicionando a bola provavelmente não vê o estado de congelamento/reposicionamento.

## Direção sugerida

Verificar comportamento real de relay do `SceneMultiplayer` pra esse caso; se confirmado, adicionar o mesmo padrão de loop manual.
