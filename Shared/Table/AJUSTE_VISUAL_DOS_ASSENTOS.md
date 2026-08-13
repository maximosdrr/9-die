# Ajuste visual dos assentos

Abra `Games/Poker/Poker.tscn` ou `Games/Domino/Domino.tscn` e trabalhe na vista 3D.

- `TableFixture/WoodenChair_*`: move, gira e redimensiona a cadeira com os gizmos normais do Godot.
- `Seats/Seat0 ... Seat3`: define a raiz do corpo sentado. A prévia transparente mostra exatamente a escala e a animação usadas no jogo.
- `SeatView`: define a posição da câmera/olhos daquele assento.
- `StandExit`: define onde o jogador aparece quando levanta.

Os marcadores são a fonte de verdade durante o jogo e não são mais recalculados automaticamente.
Se quiser gerar novamente uma posição inicial a partir das cadeiras, selecione `Seats` e marque
`Automatic alignment (optional) > Align Now`. Depois disso, continue o ajuste manualmente.

Para esconder a pessoa transparente, selecione um `Seat` e desmarque
`Editor preview > Show Character Preview`.

Para mudar a proporção de todos os jogadores, abra
`World/Player/Components/CharacterVisual.tscn`, selecione o nó raiz `CharacterVisual` e ajuste
`Visual size > Character Scale`. As prévias dos assentos acompanham esse mesmo valor.
