# Troca de assets visuais do pôquer

O ponto único de configuração é `PokerVisualAssets.tres`. A lógica de regras, rede, layout e
movimento não deve apontar diretamente para malhas importadas.

## Cartas

- A cena configurada em `CardScene` deve ter `PokerCard` na raiz.
- Conecte os nós visuais de frente e verso nos campos `Face` e `Back`.
- O tamanho de referência na mesa é 70 x 98 mm. Ajuste a malha dentro da própria cena, sem alterar
  a escala dos nós de movimento.

## Fichas

- A cena configurada em `ChipScene` deve ter `PokerChipVisual` na raiz.
- Atribua a parte que recebe a cor da denominação em `TintTarget`.
- O volume de referência é 40 mm de diâmetro por 3,5 mm de espessura. Normalize a malha dentro da
  cena da ficha; a pilha cuida apenas da posição de cada unidade.

## Mãos em primeira pessoa

- `CardHandScene` usa `PlayerFirstPersonHands.tscn`, exportado do mesmo rig e da mesma ação
  `IdleSitHoldingCards` que o corpo. Somente os dois braços são renderizados pela câmera local.
- O `CardGrip` acompanha a pose animada da mão e recebe os próprios nós `PokerCard` que foram
  distribuídos; não existe uma segunda cópia visual nem o leque vermelho do Blender.
- Os materiais dos braços ignoram profundidade e não escrevem no depth buffer, evitando atravessar
  visualmente a mesa quando a câmera olha para baixo. As cartas continuam com profundidade normal.
- Use `CardHandTransform` e `ChipHandTransform` somente para ajustes finos sem editar as animações.
- Uma cena 3D comum já acompanha os gestos completos. Para animações de dedos, coloque
  `PokerHandVisual` na raiz, conecte seu `AnimationPlayer` e informe os nomes dos clipes opcionais.
- Os placeholders somem automaticamente quando uma cena de mão é configurada.

## Personagem em terceira pessoa

- `CharacterVisual.tscn` encapsula o GLB de produção, mantém braço esquerdo, braço direito, cabeça,
  calça e torso como cinco malhas separadas e aplica escala de jogo `0.72`.
- O fluxo do poker é `Sit` -> `SitHoldingCards` -> `IdleSitHoldingCards`; dominó usa
  `Sit` -> `IdleSit`. `Idle` e `Walk` continuam sendo as ações de locomoção.
- O `CardGrip` em terceira pessoa recebe as duas cartas públicas viradas para baixo e segue a mão
  até fold/reveal. No próprio cliente, as cartas privadas ficam exclusivamente no grip 1P.
- O pescoço `CC_Base_NeckTwist02` recebe yaw/pitch limitados da câmera; o dono envia essa orientação
  a 20 Hz e os demais peers a aplicam de forma suavizada.
- Os nomes esperados atualmente estão em `PokerClips`: `SitPickUpCards`, `SitThrowChips`,
  `SitKnock`, `SitFold` e `SitReveal`.
- A ausência desses cinco gestos adicionais é aceita: após qualquer ação, o corpo retorna para
  `IdleSitHoldingCards`.

## Fonte e reexportação

- A fonte atual é `TestCharacter_Rigged.blend`, fora do projeto, na pasta `Desktop/3D Models`.
- `tools/export-player-character.py` é a allow-list reprodutível de exportação. Ela nunca leva para
  o jogo câmeras, luzes, meshes-fonte ou `Cards_Holding_Placeholder`.
- O script gera `Assets/Characters/Player/PlayerCharacter.glb` e `PlayerFirstPerson.glb`; depois de
  reexportar, abra o editor para reimportar e execute `PlayerCharacterIntegrationTest.tscn`.
