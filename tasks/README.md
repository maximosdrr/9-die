# Backlog — 9Die (Sinuca)

Levantamento do que falta para uma partida de sinuca ser jogável do início ao fim, feito em 27/07/2026 logo após a conversão do projeto de GDScript para C#. A arquitetura central (física, tacada, turnos, sincronização de rede) já está implementada e portada — o que falta aqui é o que fica em volta dela.

## Prioridade Alta (bloqueia uma partida completa)

- [01 - Tela de fim de partida](01-tela-fim-de-partida.md)
- [02 - Jogador desconecta e trava o jogo](02-desconexao-trava-jogo.md)
- [03 - Regras de falta incompletas no Golden Nine](03-regras-falta-golden-nine.md)

## Prioridade Média (feedback e UX)

- [04 - NotificationPopup órfão](04-notification-popup-orfao.md)
- [05 - Placar durante a partida](05-placar-durante-partida.md)
- [06 - Lobby sem lista de jogadores](06-lobby-lista-jogadores.md)

## Prioridade Baixa (robustez e polimento)

- [07 - Reconexão / espectador](07-reconexao-espectador.md)
- [08 - Ativar Steam como provider de rede](08-ativar-steam-provider.md)
- [09 - Export presets incompletos](09-export-presets.md)

## Concluído

- ~~Addon `godotsteam_server` quebrado (DLL desatualizada, não usado pelo projeto)~~ — removido em 27/07/2026.
