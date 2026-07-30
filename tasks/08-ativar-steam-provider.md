# 08 — Ativar Steam como provider de rede

**Prioridade**: Baixa (robustez e polimento)
**Status**: 🔵 Investigado em 29/07/2026, não ativado (fora do alcance desta sessão)

## Achado

`SteamNetworkProvider.cs` está 100% implementado (lobby, host, join, lista de lobbies) mas desligado — `NetworkManager` usa `ENetNetworkProvider` (ver [infra-3](infra-3-transporte-rede-steam-morto-localhost.md)). Os binários do addon `godotsteam` pra Windows existem de verdade em `addons/godotsteam/win64/` (`libgodotsteam.windows.template_debug.x86_64.dll`, `..._release...`, `steam_api64.dll`) — não é um addon quebrado, só não usado (o arquivo deletado no início desta sessão, `~libgodotsteam.windows.template_debug.x86_64.dll` com til, era um arquivo de lock/backup solto, não a DLL real).

## Por que não ativei

Ativar Steam de verdade não é só trocar uma linha no `NetworkManager` — precisa de:
1. Um **App ID real** do Steamworks (hoje `steam_appid.txt` tem `480`, o App ID de teste público da Valve, ver [repo-4](repo-limpeza.md)).
2. Cadastro do jogo no painel do Steamworks (fora do meu alcance — é uma conta/processo do dono do projeto, não código).
3. **Teste ao vivo com contas Steam reais** pra confirmar que lobby/host/join funcionam de ponta a ponta — não dá pra validar isso sem rodar dois clientes com Steam de verdade, o que não é possível nesta sessão.

Ligar o switch sem poder testar contra uma sessão Steam real seria pior que não mexer — um bug de wiring passaria despercebido até alguém tentar usar de verdade.

## Decisão

Nenhum código alterado. Quando/se isso for retomado, o primeiro passo é o dono do projeto conseguir um App ID de teste no Steamworks — sem isso não tem como validar nada.

## Verificação

N/A — nenhuma mudança de código neste item.
