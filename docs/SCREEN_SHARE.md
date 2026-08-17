# Compartilhamento de tela

Este documento é leitura obrigatória antes de qualquer alteração que envolva captura de tela,
áudio, TV, transporte de mídia, canais de rede ou limites de pacotes. O compartilhamento funciona
em tempo real porque captura, rede e reprodução permanecem limitados e porque ENet e Steam usam
caminhos de mídia diferentes.

## Visão geral

```text
jogador próximo da TV
  -> escolhe tela ou janela
  -> servidor reserva um único sharer
  -> worker captura e codifica WebP fora da thread principal
  -> vídeo e áudio seguem para o servidor
  -> servidor valida e retransmite aos demais peers
  -> buffer curto estabiliza a reprodução na TV
```

Somente um jogador compartilha por vez. A interface local exige que o jogador esteja dentro da área
da TV. O servidor autentica o pedido pela identidade do peer conectado, registra o sharer e aceita
mídia somente desse peer. Clientes aceitam mídia somente do servidor.

## Arquivos principais

- `World/ScreenShare/Capture/ScreenCaptureWorker.cs`: captura e codificação WebP em worker;
- `World/ScreenShare/Capture/WindowsScreenCapture.cs`: captura de monitor/janela no Windows;
- `World/ScreenShare/Capture/SystemAudioCapture.cs`: áudio do sistema e fila limitada;
- `World/ScreenShare/Capture/VideoPlayoutBuffer.cs`: jitter buffer curto do receptor;
- `World/ScreenShare/TV/TvScreenShare*.cs`: sessão, captura, transporte, relay e playback;
- `World/Player/Components/UI/TvShareButton.cs`: seletor local de fonte;
- `Infrastructure/Networking/Steam/SteamNetworkProvider.cs`: relação peer ID <-> Steam ID;
- `World/ScreenShare/Tests/TvScreenShareSecurityTest.tscn`: contratos de segurança, fluxo e limites.

## Captura e reprodução

- resolução de captura: 1280 x 720;
- captura alvo: 60 FPS;
- envio de vídeo: no máximo um frame novo a cada 50 ms, aproximadamente 20 FPS;
- WebP lossy com qualidade 0,90;
- frame codificado: máximo de 512 KiB;
- receptor: máximo de 24 pacotes de vídeo por segundo;
- áudio: PCM mono, 48 kHz, com limites de chunk e de bytes por segundo;
- playout: alvo de 200 ms, atraso máximo de 600 ms e no máximo 8 frames.

O worker mantém apenas o frame codificado mais recente em `LatestFrameSlot`. Se captura ou encoding
produzir mais rápido que a rede, quadros antigos são substituídos em vez de formar uma fila. O
playout também é limitado e descarta atraso excessivo. Não substitua esses limites por filas sem
capacidade.

## Transporte ENet

ENet usa RPCs confiáveis em canais dedicados:

- canal 1: vídeo;
- canal 2: áudio.

Os pacotes WebP variam de tamanho. Eles devem permanecer `Reliable` nesses canais dedicados.
`UnreliableOrdered` já provocou starvation: um fragmento perdido invalidava quadros posteriores e a
TV virava uma sequência de slides. Ao alterar canais, confira também `network/max_channels` em
`project.godot` e lembre que outros sistemas do jogo utilizam canais próprios.

## Transporte Steam

No Steam, canais de RPC diferentes do Godot ainda passam pela mesma conexão subjacente de
`SteamMultiplayerPeer`. Colocar vídeo nessa fila pode atrasar RPCs de gameplay. Por isso a mídia
ignora `MultiplayerApi` e usa P2P bruto do GodotSteam:

- canal P2P 10: vídeo;
- canal P2P 11: áudio;
- envio: `P2P_SEND_RELIABLE_WITH_BUFFERING`;
- topologia: cliente -> host -> demais clientes, igual ao relay do ENet;
- leitura por frame: até 16 pacotes e 1 MiB por canal.

Isso é um caminho lógico separado para mídia, não uma segunda sessão/lobby Steam. Gameplay continua
no `SteamMultiplayerPeer`; vídeo e áudio usam os canais P2P brutos para não bloquear a fila de RPCs.
Sessões P2P só são aceitas para Steam IDs reconhecidos pelo provider.

O congestionamento é medido por peer com `bytes_queued_for_send`. Para vídeo, o limite é o maior
valor entre 128 KiB e três vezes o tamanho do frame atual. Esse cálculo acompanha o payload: um
limite fixo de 128 KiB descartava frames normais de 80–160 KiB e recriava o efeito de slides. Se a
versão do GodotSteam não expuser a telemetria, o jogo avisa que o controle está inativo.

## Autoridade e ciclo de vida

- o botão local abre o seletor apenas dentro da área da TV;
- o servidor não repete o overlap físico da cópia local do peer: ele autentica a conexão e aplica
  rate limit ao pedido;
- a apresentação local recebe explicitamente a autoridade do Player, inclusive nos clients;
- somente o sharer registrado pode enviar frames e áudio;
- desconexão, saída da árvore e troca de sessão encerram capture, áudio, tweens, sinais e buffers;
- envio deve parar antes que o peer ENet/Steam seja descartado.

## Problemas conhecidos já resolvidos

### Transmissão em formato de slides no ENet

Uma refatoração trocou os frames WebP variáveis para `UnreliableOrdered`. Com fragmentação e perda,
quadros completos deixavam de chegar com regularidade. A correção restaurou entrega confiável nos
canais exclusivos 1 e 2.

### Transmissão em formato de slides no Steam

Um limite fixo de fila de 128 KiB ficou menor que frames comuns depois que a cena ganhou mais
detalhes. Frames válidos eram descartados antes de entrar na rede. A correção passou a reservar uma
janela de três frames baseada no tamanho real de cada payload, mantendo a fila curta sem bloquear a
transmissão.

### Gameplay travando durante transmissão Steam

Mesmo com `TransferChannel` diferente, vídeo e gameplay compartilhavam a fila real do
`SteamMultiplayerPeer`. A correção criou os canais P2P brutos 10/11 para mídia, deixando RPCs de jogo
fora da fila de vídeo.

### Somente o host conseguia compartilhar

O servidor revalidava `GetOverlappingBodies` usando sua própria cópia física, o que rejeitava peers
legítimos. Além disso, a UI criada fora da subárvore replicada podia herdar autoridade 1. A correção
manteve o bloqueio de proximidade na UI local, autenticou no servidor pelo peer conectado e propagou
explicitamente a autoridade para a apresentação local.

### Atraso crescendo indefinidamente

Filas produtoras e receptoras podiam acumular mídia antiga. `LatestFrameSlot`, o playout limitado e
o controle de congestionamento por peer garantem que uma conexão lenta perca frames velhos em vez
de assistir ao passado cada vez mais atrasado.

## Invariantes que não devem ser quebradas

1. ENet mantém vídeo/áudio confiáveis nos canais dedicados 1/2.
2. Steam mantém mídia fora de `SteamMultiplayerPeer`, nos canais P2P brutos 10/11.
3. Filas de captura, envio e playout permanecem limitadas.
4. O limite Steam de vídeo considera o tamanho real do payload, nunca apenas um valor fixo menor que
   um frame válido.
5. Congestionamento é isolado por destinatário; um peer lento não derruba a transmissão dos demais.
6. O host continua relay e fonte de autoridade; clients também podem ser o sharer.
7. Captura e encoding permanecem fora da thread principal; objetos Godot de cena/textura/RPC só são
   tocados na thread principal.
8. Payload, frequência, remetente e sessão continuam validados antes de decodificar ou retransmitir.

## Validação obrigatória

Após uma mudança relacionada ao compartilhamento:

1. execute `dotnet build 9Die.sln --configuration Release`;
2. execute `World/ScreenShare/Tests/TvScreenShareSecurityTest.tscn` pelo runner do projeto;
3. faça smoke ENet com host compartilhando e client compartilhando;
4. faça smoke Steam com host compartilhando e client compartilhando;
5. em ambos, confirme vídeo fluido, áudio, movimento/câmera e ações de jogo durante a transmissão;
6. teste um peer lento/desconectado sem prejudicar os demais;
7. teste parar, trocar fonte, fechar a janela capturada e sair da sessão;
8. monitore avisos de payload, rate limit, telemetria Steam e crescimento de fila.

Não aprove uma mudança apenas porque funciona no host local: o caminho de client e os transportes
ENet/Steam exercitam autoridade e filas diferentes.
