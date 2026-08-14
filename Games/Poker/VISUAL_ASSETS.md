# Assets visuais do pôquer

O ponto único de configuração das cartas, fichas e mãos é `PokerVisualAssets.tres`. A lógica de
regras e rede não deve apontar diretamente para uma malha importada.

## Cartas e fichas

- `CardScene` usa `PokerCard` na raiz. As cartas medem 70 x 98 mm e mantêm profundidade normal.
- `ChipScene` usa `PokerChipVisual` na raiz. A referência é 40 mm de diâmetro por 3,5 mm de
  espessura.
- As duas cartas reais distribuídas são transferidas da mesa para o `CardGrip`; não existe cópia
  visual nem o placeholder vermelho do Blender em runtime.

## Corpo em primeira pessoa

- `PlayerFirstPerson.glb` contém braços, torso e pernas, sem cabeça, em quatro meshes separados.
- O asset possui `Idle`, `Walk`, `IdleSit`, `PickCards`, `IdleSitHoldingCards` e
  `IdleHoldingCardsDown`. Os nomes são iguais aos de terceira pessoa porque cada vista possui seu
  próprio `AnimationPlayer`.
- O jogador local instancia `FirstPersonCharacterVisual.tscn` na origem do corpo e espelha
  `Idle`/`Walk`; no dominó ele também recebe `IdleSit`. Ele usa uma camada exclusiva da câmera local.
- No pôquer, `PlayerFirstPersonHands.tscn` substitui temporariamente esse visual para usar o mesmo
  corpo sem cabeça, o marcador de cartas específico de primeira pessoa e a transformação editável
  no preview de `Poker.tscn`.
- O suporte do corpo é estável. Somente as cartas seguem o `CardGrip`; inclinar as cartas não move
  torso e pernas junto com o antigo ajuste de braços.

## Corpo em terceira pessoa

- `PlayerCharacter.glb` mantém braço esquerdo, braço direito, cabeça, calça e torso separados.
- O pôquer entra sentado com `Sit -> IdleHoldingCardsDown`.
- No início de cada rodada, o fluxo central é
  `PickCards -> IdleSitHoldingCards -> IdleHoldingCardsDown` nas duas vistas.
- Enquanto o botão direito está pressionado, a vista local e o corpo replicado usam
  `IdleSitHoldingCards`. Ao soltar, ambos voltam a `IdleHoldingCardsDown`.
- As cartas públicas dos outros jogadores chegam ao `CardGrip` quando `PickCards` alcança o ponto
  de contato. Fold e showdown devolvem as mesmas instâncias à mesa.
- O pescoço continua recebendo yaw/pitch limitados da câmera e replicados aos demais peers.

## Fonte e reexportação

- Fonte: `Desktop/3D Models/9Die_Cardroom_MotionLab/9Die_Cardroom_MotionLab.blend`.
- `tools/export-player-character.py` exporta somente os rigs, meshes e ações de produção. Mesa,
  câmeras, luzes e placeholders permanecem no Blender.
- O placeholder 3P gera o marcador 3P e `Cards_Holding_Placeholder_FP` gera o marcador FP; os dois
  offsets não são intercambiáveis.
- O script aceita `PLAYER_EXPORT_OUTPUT` para gerar arquivos em staging antes de substituir os GLBs
  de produção.
- Depois de reexportar, reimporte no editor e execute `tools/validate-new-player-assets.gd` e
  `World/Player/Tests/PlayerCharacterIntegrationTest.tscn`.
