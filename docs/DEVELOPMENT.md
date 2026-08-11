# Desenvolvimento

## Validação local

O comando padrão é:

```powershell
./tools/run-tests.ps1
```

Antes dos testes completos, normalize e valide o estilo C#:

```powershell
dotnet format 9Die.sln
dotnet format 9Die.sln --verify-no-changes --no-restore
```

O primeiro comando é a correção mecânica; o segundo é a mesma barreira usada pelo CI. Mudanças
funcionais devem continuar pequenas e nomeadas mesmo quando o formatador não encontra diferenças.

Etapas executadas:

1. confirma versões do .NET e do Godot Mono;
2. compila a solução em Debug;
3. importa e valida recursos;
4. descobre cenas `*Test.tscn` nas raízes de código;
5. executa cada cena isoladamente;
6. encerra uma cena travada após 120 s por padrão;
7. agrega checks e reprova também por erros/leaks da engine.

Para um subconjunto:

```powershell
./tools/run-tests.ps1 -TestRoots Games/Pool,Shared
```

O limite pode ser ajustado para um teste legitimamente longo com
`-TestTimeoutSeconds <segundos>`, sem remover a proteção do runner.

Execute runners Godot sequencialmente quando compartilham a mesma árvore de trabalho. O hot reload
do GodotSteam cria uma DLL temporária de nome fixo; duas importações simultâneas podem disputar esse
arquivo e produzir uma falha de importação que não existe no jogo. Instâncias normais host/cliente
continuam sendo o procedimento correto para o smoke multiplayer.

Antes de entregar uma mudança de rede, física, cena raiz ou lifecycle, rode também:

```powershell
dotnet build 9Die.sln --configuration Release
```

e um smoke headless da cena principal. O runner já cobre importação e as regressões automatizadas;
o smoke prova a composição completa e seu encerramento.

## Estilo C#

- quatro espaços e LF, conforme `.editorconfig` e `.gitattributes`;
- uma responsabilidade nomeável por classe/arquivo;
- exports ficam próximos e documentam o contrato com a cena;
- campos privados usam `_camelCase`; tipos/métodos/propriedades usam `PascalCase`;
- evite strings de `GetNode` espalhadas: exporte referências ou resolva uma vez no lifecycle;
- não esconda falhas esperadas em `PushError`; ofereça `Try...` silencioso para ordem de spawn normal;
- cleanup desfaz sinais, timers, workers, playback e recursos nativos criados no setup;
- comentários explicam decisões/invariantes, não repetem a linha seguinte.

Namespaces não foram introduzidos nesta refatoração para não combinar reorganização física com uma
migração de API em massa. Novos módulos devem usar namespace coerente desde o início; a migração do
legado pode ser feita por domínio, com testes verdes entre os lotes.

## Criando uma modalidade

1. Crie `Games/<Nome>/Rules` com tipos determinísticos e testes sem cena.
2. Implemente um `TableGame` com limites explícitos de participantes.
3. Adicione o resolver autoritativo em `GameModes`.
4. Separe controller de entrada e presenter visual.
5. Defina quais dados são públicos, privados e exclusivos do servidor.
6. Documente RPCs: remetente permitido, validações, entrega e canal.
7. Crie testes de regra, match e carregamento de cena.
8. Só então instancie a nova mesa no nível.

## Alterando RPCs ou cenas replicadas

Checklist obrigatório:

- o método existe com a mesma assinatura em cliente e servidor;
- o nó tem o mesmo nome e `NodePath` em todos os peers;
- `RpcMode.Authority` é usado para servidor -> cliente;
- `AnyPeer` possui validação explícita de remetente;
- não há `CallLocal` duplicando uma transição do listen host;
- payloads e coleções têm limites;
- a entrega combina com a semântica do dado;
- há teste com ao menos host e cliente reais quando lifecycle/handshake muda.

## Testes de cena

Uma cena automatizada deve:

- terminar sozinha;
- imprimir exatamente um resumo `=== N passaram, M falharam ===`;
- retornar exit code diferente de zero quando `M > 0`;
- liberar nós/áudio/recursos antes de encerrar;
- não depender de Steam, desktop ou dispositivo de áudio para testar lógica;
- usar seed explícita para regras determinísticas.

## Recursos e exportação

O preset de distribuição exporta a cena principal e suas dependências. Recursos carregados em C#
por string ou UID não são descobertos automaticamente: cada novo carregamento dinâmico de produção
deve ser adicionado a `Shared/Resources/RuntimeResourceManifest.tres`. Não volte a `all_resources`:
isso inclui testes e cenas experimentais no pacote. Assets não referenciados podem continuar no
repositório enquanto estiverem em avaliação, mas não devem aumentar a build final.

O CI instala o template oficial, gera uma exportação Release real e executa a cena principal a
partir do pacote. A saída local padrão fica em `outputs/`, que não é versionada.

Para reproduzir a exportação local pela linha de comando, crie o diretório de destino antes de
chamar o Godot (a engine não cria os diretórios pais do preset):

```powershell
$releaseDirectory = Join-Path $PWD "outputs/local"
$releaseExecutable = Join-Path $releaseDirectory "9die.exe"
New-Item -Path $releaseDirectory -ItemType Directory -Force | Out-Null
& $env:GODOT_BIN --headless --path . --export-release "Windows Desktop" $releaseExecutable
```

Um export só é aprovado quando o exit code é zero **e** a saída não contém `ERROR:`. O Godot pode
retornar zero depois de uma falha interna do publish .NET e reaproveitar um artefato anterior; o CI
valida as duas condições e depois abre o executável gerado.

O App ID `480` é o Spacewar de desenvolvimento. Configure o App ID real e `steam_appid.txt` no
pipeline de distribuição; nunca publique a build comercial com 480.
