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

### Trava individual das mãos

`FirstPersonHandCameraLock` separa o movimento da câmera do movimento animado. A raiz do corpo fica
no frame estável do assento e cada braço recebe um modo independente através de
`PokerHand3DView.SetHandCameraModes(left, right)`:

- `Locked`: mantém o braço no espaço da mesa, mas preserva normalmente a animação do Blender;
- `FollowCamera`: adiciona a rotação da câmera somente ao braço escolhido;
- `IdleHoldingCardsDown` e toda a cutscene `PickCards -> olhar -> baixar` usam `Locked/Locked`;
- o olhar voluntário com botão direito usa `FollowCamera/Locked`, pois o `CardGrip` pertence à mão
  esquerda.

O retorno voluntário usa suavização para não estalar o ombro. O início de uma rodada força o lock
imediatamente, impedindo que o movimento da câmera anterior contamine `PickCards`.

`FollowCamera` limita somente o braço; a câmera permanece com todo o alcance do controlador. O
perfil inicial da mão esquerda usa a pose autorada como limite inferior (`0°` para baixo), `20°`
para cima, `18°` para a direita/cruzando o tronco e `28°` para a esquerda/afastando-se do corpo.
Esses quatro valores ficam editáveis no `FirstPersonHandCameraLock` de
`PlayerFirstPersonHands.tscn`. A aproximação usa saturação suave para não parar bruscamente.

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
