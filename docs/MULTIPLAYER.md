# Multiplayer e segurança

## Modelo de confiança

Todo dado vindo de um cliente é não confiável. O servidor é a fonte de verdade para:

- participantes e capacidade de uma mesa;
- ordem, token e resultado de turnos;
- baralho, pedras, mãos privadas, potes e pagamentos;
- intenção, execução e resultado final de uma tacada;
- movimento usado para validar proximidade/interação;
- nickname exibido aos demais jogadores;
- identidade do peer que pode compartilhar mídia.

O cliente é responsável por entrada, previsão visual e apresentação. Ele não escolhe o resultado.
Esse limite segue a recomendação oficial da Godot de manter estados importantes no servidor e
validar argumentos e frequência de RPCs.

## Protocolos atuais

### Pôquer e dominó

1. O servidor cria uma seed imprevisível e inicializa a regra determinística.
2. O resolver autoritativo guarda a mão de cada jogador.
3. Cada ação inclui identidade inferida do remetente e token/estado do turno.
4. O servidor valida e aplica a ação.
5. Snapshots públicos vão para todos; a mão privada vai somente ao dono.

Ordem de turno e assento físico são estados diferentes. A ordem é compactada quando alguém sai;
o slot da cadeira permanece estável até a próxima partida. O setup de um peer tardio replica esse
mapa, inclusive lacunas e identidades históricas ainda usadas por fichas/cartas da mão corrente.
Uma reconexão troca apenas a identidade que ocupa o mesmo slot. Assim, remover o jogador do meio
não desloca avatares, marcadores, controles ou a saída das cadeiras seguintes.

O host de uma partida listen-server ainda executa o processo autoritativo e, tecnicamente, pode
inspecionar a memória de todas as mãos. Isso é aceitável para protótipo/social casual, mas não para
ranking com valor econômico. Esse cenário exige servidor dedicado, autenticação e log de auditoria.

### Sinuca

1. O cliente envia intenção da tacada, não posições arbitrárias das bolas.
2. O servidor valida turno e parâmetros.
3. A simulação determinística produz a sequência e o resultado oficial.
4. Clientes reproduzem a tacada e recebem correção/final autoritativo.

A posição da bola é enviada no spawn, mas não é mais replicada de forma `reliable on change` a cada
frame. Tráfego descartável não bloqueia eventos confiáveis de gameplay.

### Movimento do jogador

O jogador local prevê movimento para manter resposta imediata. A intenção é enviada ao servidor,
que valida, limita velocidade e rotação, simula colisões e publica snapshots. A posição local é
reconciliada com o erro do snapshot mais novo; erros antigos nunca são somados e um snapshot já
alinhado cancela a correção pendente.

A rotação visual da câmera do dono não é reescrita por snapshots. Ela responde diretamente ao mouse,
enquanto o servidor continua autoritativo sobre o corpo e observadores interpolam a rotação recebida.
Isso é seguro enquanto a intenção de movimento for enviada em coordenadas de mundo e o collider for
simétrico. Se orientação passar a decidir tiro, hitbox ou outra regra, separe explicitamente yaw do
corpo autoritativo e yaw visual da câmera.

`RemoteTransform3D` de câmera nasce sem alvo e com posição, rotação e escala desligadas. A transição
local de câmera é a única operação que pode ativá-lo. O mouse capturado usa deslocamento físico de
tela (`ScreenRelative`, com fallback validado) para manter a mesma sensibilidade entre resoluções. A
posição que entra nos overlaps das mesas é sempre a cópia simulada pelo servidor.

### Perfil do jogador

O dono envia somente uma proposta de nickname por RPC confiável. O servidor confere se o remetente
é o dono daquele `Player`, aceita uma única submissão, normaliza Unicode, remove caracteres de
controle e limita o resultado a 24 caracteres visíveis. A propriedade nasce no spawn para late
joiners, mas nunca é publicada pelo `MultiplayerSynchronizer` do cliente.

### Máquina de estados

Mudanças de uma máquina pertencente a um cliente usam dois RPCs distintos:

```text
cliente dono -> request validada no servidor -> apply assinada pelo servidor -> demais clientes
```

O nó de transporte continua server-owned; a máquina preserva o dono que pode solicitar a mudança.
Metadados têm limites de tipo, quantidade, profundidade e 16 KiB por mensagem.

### Orçamento de ações

RPCs confiáveis de gameplay consomem um orçamento por peer antes de interpretar o payload. O
excesso é descartado sem log e sem RPC de rejeição, evitando amplificação de CPU/banda. Os limites
atuais são 8 ações/s no pôquer, 10 pedidos/s compartilhados no dominó, e na sinuca 2 snapshots/s,
6 pedidos de push-out/s, 4 tacadas/s e 8 etapas de posicionamento/s. Mudança de estado aceita 12/s;
início de partida e desistência aceitam 2/s; sentar/levantar e controle da TV aceitam 4/s. As tabelas
do limitador também têm capacidade rígida, portanto peers desconhecidos não causam crescimento
ilimitado.

### Compartilhamento de tela

Arquitetura, invariantes, regressões já resolvidas e matriz de validação estão documentadas em
[`SCREEN_SHARE.md`](SCREEN_SHARE.md). A leitura desse documento é obrigatória antes de modificar
captura, TV, áudio ou transporte de mídia.

- somente um jogador dentro da área da TV pode reservar o compartilhamento;
- somente o sharer registrado pode enviar ao servidor;
- clientes aceitam mídia somente do servidor;
- frames e áudio têm limites de payload e frequência;
- captura local mantém apenas o frame de vídeo mais recente e no máximo 0,5 s de áudio;
- a drenagem Steam possui orçamento rígido de pacotes e bytes por canal a cada frame;
- no ENet, mídia usa canais separados e entrega descartável; no Steam P2P legado, o envio é
  controlado antes de entrar na fila;
- peers Steam desconhecidos não têm a sessão P2P aceita;
- perda da sessão interrompe captura imediatamente; captura, playback, textura e sinais também são
  liberados em `_ExitTree`;
- falha do dispositivo de áudio degrada para compartilhamento somente de vídeo.
- imagens WebP são inspecionadas antes da descompressão; dimensões, variante, animação e tamanho
  precisam respeitar o contrato de frame antes de o codec alocar a imagem;
- chamadas nativas de captura usam apenas DLLs de sistema, validam resultados e liberam handles e
  imagens também nos caminhos de falha.

O transporte P2P bruto usado pelo plugin deve migrar da API Steam legada para
`ISteamNetworkingMessages` ou `ISteamNetworkingSockets` antes de uma distribuição pública.

## Sessão

O ciclo explícito é:

```text
Offline -> StartingHost -> Hosting
Offline -> Connecting -> Connected
qualquer início -> Failed
Connected -> Offline (servidor caiu/encerramento)
```

Mudanças incompatíveis de RPC incrementam `network/protocol_version`; lobbies filtram a versão antes
do handshake. O payload de assentos estáveis introduzido nesta auditoria usa a versão 2.

O menu só anuncia sucesso depois de `ConnectedToServer`. ENet e Steam verificam o retorno de criação
do peer. Lobbies Steam publicam e filtram `game_id` e `protocol_version`, evitando misturar builds
incompatíveis. Tentativas de host/conexão possuem timeout com revisão de tentativa: callbacks
atrasados não ressuscitam uma sessão já encerrada e uma queda durante o handshake libera a UI. Ao
perder o servidor, o cliente reconstrói a cena principal antes de permitir outra conexão; mãos,
turnos, players, TV e apresentações da sessão anterior não sobrevivem ao rejoin.

O token local de reconexão é aleatório e evita colisões acidentais, mas não substitui uma conta
autenticada. Para produção, use a autenticação do provedor/plataforma, rotacione credenciais e associe
um jogador a no máximo uma partida ativa.

O registro de token tem orçamento e capacidade rígidos. Durante uma reconexão rápida, o servidor
reserva a identidade enquanto remove o peer antigo; cada mesa só equipa o novo controller quando o
novo `Player` já existe, a partida segue ativa e o jogador continua na ordem da mesa. O pedido
pendente é cancelado em reset, fim de partida e saída da árvore.

## Entrega e canais

| Conteúdo | Entrega | Motivo |
| --- | --- | --- |
| ação/turno/snapshot final | reliable | não pode desaparecer |
| input e snapshot de movimento | unreliable ordered | o estado novo substitui o velho |
| preview de posicionamento da bola | unreliable ordered, canal 4 | não disputa fila com frames da TV |
| frame/áudio da TV sobre ENet | unreliable ordered, canais próprios | mídia atrasada não tem valor |
| frame/áudio da TV sobre Steam P2P legado | congestion gate antes do envio | reduz crescimento da fila e atraso acumulado |

Não coloque payloads de tamanhos muito diferentes no mesmo canal `unreliable ordered`; a própria
Godot alerta que isso pode aumentar descartes.

## Referências usadas na auditoria

- [Godot: high-level multiplayer](https://docs.godotengine.org/en/4.7/tutorials/networking/high_level_multiplayer.html)
- [Godot: export para servidor dedicado](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_dedicated_servers.html)
- [Valve: Steam Networking](https://partner.steamgames.com/doc/features/multiplayer/networking)
- [Valve: Matchmaking & Lobbies](https://partner.steamgames.com/doc/features/multiplayer/matchmaking)
- [Unreal: movimento com previsão e correção](https://dev.epicgames.com/documentation/en-us/unreal-engine/understanding-networked-movement-in-the-character-movement-component-for-unreal-engine)
- [Unity: soluções server-authoritative](https://docs.unity.com/en-us/multiplayer/netcode/netcode)
- [PokerStars: integridade, shuffle fixo e mãos privadas](https://www.pokerstars.com/help/articles/integrity-info/)
- [UK Gambling Commission: requisitos de RNG](https://www.gamblingcommission.gov.uk/standards/remote-gambling-and-software-technical-standards/rts-7-generation-of-random-outcomes)
- [Miniclip: fair play e combate a exploits](https://support.miniclip.com/hc/en-us/articles/115001105368--Suspensions-bans-clubs-and-exploit-related-issues)

As implementações internas de PokerStars e 8 Ball Pool não são públicas. A comparação usa garantias
publicadas pelos operadores e padrões documentados por engines/plataformas, sem presumir detalhes
proprietários.
