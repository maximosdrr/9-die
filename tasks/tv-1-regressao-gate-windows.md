# TV-1 — Regressão real: gate de `OS.GetName() == "Windows"` foi perdido no refactor pra proximidade

**Área**: TV (Core/Tv, TvShareButton)
**Prioridade**: 🔴 Crítico
**Status**: ✅ Resolvido em 29/07/2026 — `TvScreenShare.IsAvailable` (static, `OS.GetName() == "Windows"`) adicionado; `ConnectSignals()` (que liga o `InteractionArea`) só roda se `IsAvailable`. Fora do Windows, `IsLocalPlayerInRange` nunca vira `true`, o que já cascateia corretamente pro `TvShareButton` (que já gateava nisso) — o prompt nunca aparece e o picker nunca abre, sem precisar duplicar o check lá. Ver visualizar já continua funcionando fora do Windows (decodificação de WebP é cross-platform, só a *captura* é Windows-only).

## Problema

`Features/Player/Components/UI/TvShareButton.cs` e `Core/Tv/TvScreenShare.cs` **não checam mais `OS.GetName() == "Windows"` em lugar nenhum**. Na versão antiga (botão fixo), `_button.Visible = OS.GetName() == "Windows"` escondia a funcionalidade inteira fora do Windows. Isso se perdeu quando o gatilho virou proximidade+F.

Hoje, um jogador em Mac/Linux perto da TV vê o prompt normalmente, aperta F, e:

- `TvShareButton.PopulateSourceGrid()` chama `WindowsScreenCapture.TryCapturePrimaryScreen`/`EnumerateCapturableWindows` — P/Invoke puro em `user32.dll`/`gdi32.dll`/`dwmapi.dll`, sem try/catch em volta — `DllNotFoundException` não tratada.
- Se de alguma forma isso passasse, `TvScreenShare.StartLocalCapture()` também instancia `SystemAudioCapture` (NAudio/WASAPI), também Windows-only, também sem guard.

## Direção sugerida

Reintroduzir o gate — mais simples é `TvScreenShare._Ready()` só popular `InteractionArea`/mostrar prompt se `OS.GetName() == "Windows"` (ou expor um `IsAvailable` que `TvShareButton` checa antes de abrir o picker).
