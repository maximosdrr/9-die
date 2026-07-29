# Config e Higiene de Repositório — Limpeza

Itens 🟢 (limpeza) de config/repositório, cross-cutting. Item 🟠 dessa mesma área está em arquivo próprio: [repo-1](repo-1-tecla-f-duplicada.md).

## 🟢 REPO-2 — Arquivos `.tmp` de autosave do editor commitados no git

Confirmado via `git ls-files`: `Features/Player/Player.tscn38481106168.tmp`, `Levels/PrototypeLevel/Prototype.tscn*.tmp` (6 variantes), `Features/Games/Pool/GameController/PoolController.tscn3018064757.tmp`, `Features/Games/Pool/PoolGame.tscn2654146847.tmp`. Lixo de salvamento atômico do Godot, sem função nenhuma. `.gitignore` só cobre `*.tmp_proj`, não esse padrão.

**Direção**: já existe uma sugestão separada pra isso (chip enviado); remover do git e adicionar `*.tscn*.tmp` ao `.gitignore`.

## 🟢 REPO-3 — Sem guard de plataforma no nível de projeto pra código Windows-only

`9Die.csproj` mira `net8.0` puro (não `net8.0-windows`), e nada em `Core/Platform`/`Core/Tv` é condicionado por `OperatingSystem.IsWindows()` além dos poucos pontos já cobertos (ver [tv-1](tv-1-regressao-gate-windows.md) pra onde isso realmente falta hoje).

**Direção**: baixa prioridade a menos que exportar pra macOS/Linux vire meta real.

## 🟢 REPO-4 — `steam_appid.txt` ainda com o AppID de teste público da Valve (480)

Consistente com Steam nunca ter sido ativado de verdade (ver [infra-3](infra-3-transporte-rede-steam-morto-localhost.md)/`infra-simplificacao.md#infra-9`) — só sinaliza que é scaffolding, não integração viva.
