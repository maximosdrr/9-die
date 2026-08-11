# 9Die

9Die é um espaço social 3D multiplayer com mesas de sinuca, pôquer e dominó. O projeto usa Godot
4.7.1 Mono e C#/.NET 8, com ENet para desenvolvimento local e GodotSteam como transporte opcional.

## Começando

Pré-requisitos:

- Godot `4.7.1` Mono;
- .NET SDK compatível com o `global.json`;
- Windows para captura de tela e áudio do sistema.

Para compilar, importar os recursos e executar toda a suíte:

```powershell
./tools/run-tests.ps1
```

Se o Godot não estiver em um local detectável:

```powershell
$env:GODOT_BIN = "C:\caminho\para\Godot_console.exe"
./tools/run-tests.ps1
```

O runner reprova por falha de compilação, recurso inválido, check reprovado, erro de engine ou
vazamento detectado no encerramento. Não existe lista de erros silenciosamente ignorados.

## Mapa do projeto

```text
App/                 composição da aplicação e interface de entrada
Games/               sinuca, pôquer e dominó; regras, runtime, apresentação e testes
Infrastructure/      transporte de rede, sessão e reconexão
Shared/              máquina de estados, mesas e utilitários reutilizáveis
World/               nível, jogador, câmera, ranking e compartilhamento de tela
Assets/              modelos, texturas, áudio e materiais importados
addons/              extensões de terceiros, atualmente GodotSteam
docs/                decisões e guias de manutenção
tools/               automação local de validação
```

As regras que não podem ser violadas estão documentadas em:

- [Arquitetura](docs/ARCHITECTURE.md)
- [Multiplayer e segurança](docs/MULTIPLAYER.md)
- [Fluxo de desenvolvimento](docs/DEVELOPMENT.md)
- [Auditoria e decisões da refatoração](docs/REFACTORING_AUDIT.md)

## Regras de ouro

1. O servidor decide resultados de jogo, turnos, cartas/pedras privadas e posições relevantes.
2. O cliente envia intenção; argumentos, remetente, turno, tamanho e frequência são validados.
3. RPCs exigem o mesmo `NodePath` em todos os peers. Não renomeie ou reordene nós de rede sem um
   teste multiplayer que prove a migração.
4. Regra determinística não depende de cena, animação ou transporte. Apresentação nunca decide o
   resultado da partida.
5. Toda correção de bug recebe uma regressão executável na suíte.
6. Uma build de distribuição parte da cena principal e suas dependências. Todo carregamento
   dinâmico de produção entra no `RuntimeResourceManifest`; testes e cenas órfãs ficam fora.
