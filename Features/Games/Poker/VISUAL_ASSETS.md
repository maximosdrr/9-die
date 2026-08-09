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

- `CardHandScene` e `ChipHandScene` são encaixadas em raízes de movimento estáveis e independentes.
- Use `CardHandTransform` e `ChipHandTransform` para alinhar posição, rotação e escala sem editar as
  animações existentes.
- Uma cena 3D comum já acompanha os gestos completos. Para animações de dedos, coloque
  `PokerHandVisual` na raiz, conecte seu `AnimationPlayer` e informe os nomes dos clipes opcionais.
- Os placeholders somem automaticamente quando uma cena de mão é configurada.

## Personagem em terceira pessoa

- No `Player`, conecte o `AnimationPlayer` do novo personagem em `SeatedGestureAnimator`.
- Os nomes esperados atualmente estão em `PokerClips`: `SitPickUpCards`, `SitThrowChips`,
  `SitKnock`, `SitFold` e `SitReveal`.
- A ausência desses clipes é aceita: o corpo permanece no idle até as animações serem produzidas.
