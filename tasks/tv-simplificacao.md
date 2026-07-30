# TV — Simplificação e Limpeza

Itens 🟡 (simplificação/consolidação) e 🟢 (limpeza) da feature de TV (Core/Tv, TvShareButton). Item 🔴 dessa mesma área está em arquivo próprio: tv-1.

## 🟡 TV-2 — `Global.Instance.TvScreen` assume uma única TV no jogo inteiro

Documentado como decisão deliberada de escopo (não suportar múltiplas TVs simultâneas), mas vale registrar aqui: se o jogo crescer pra ter mais de uma TV, todo o `TvShareButton.cs` precisa parar de usar o singleton e passar a referenciar "a TV em cuja área estou agora" (natural já que `InteractionArea` já sabe disso por TV).

**Direção**: sem ação agora — só not-a-fazer quando/se surgir a necessidade de múltiplas TVs.

## ✅ TV-3 — `PoolStartGameUI` (prompt genérico) mora dentro de `Core/Table/UI/` mas é usado por uma feature não relacionada

**Resolvido em 30/07/2026** — `PoolStartGameUI.cs(.uid)`/`StartGameUI.tscn` movidos pra `Core/Util/UI/`; `Core/Table/UI/` (vazia) removida. Referências atualizadas em `Table.tscn` e `Tv.tscn` (só o `ext_resource path=`; o `uid://` e o nome da classe `[GlobalClass]` não mudam, então nenhum `.cs` precisou de alteração).

~~`Core/Tv/Tv.tscn` reaproveita `Core/Table/UI/StartGameUI.tscn` pro seu próprio prompt (decisão deliberada pra não duplicar código) — mas isso deixa `Core/Tv` dependendo de um recurso "emprestado" de dentro da pasta de outra feature, e uma mudança visual pensada só pra Table afeta a TV sem ninguém perceber.~~

## 🔵 TV-4 — Log de diagnóstico de FPS deixado permanentemente ativo

**Revisado em 29/07/2026, mantido de propósito**: o comentário que já está no código (`Core/Platform/ScreenCaptureWorker.cs:90-91`) diz explicitamente "Cheap, always-on diagnostic: makes the *actual* sustained capture+encode rate visible in the output console" — ou seja, diferente do que essa entrada supunha, não é um esquecimento de debug, é uma decisão deliberada de manter um diagnóstico permanente e barato pro pipeline de vídeo (que é complexo e historicamente teve vários bugs de framerate nesta sessão). Não removido.

~~`Core/Platform/ScreenCaptureWorker.cs:97` — `GD.Print($"[TvScreenCapture] {achievedFps:F1} fps...")` a cada 1s, adicionado pra diagnosticar o problema de framerate. Útil na hora, mas agora roda pra sempre em toda sessão de compartilhamento, poluindo o console.~~

## 🟢 TV-5 — `WindowsScreenCapture.cs` acumulou 3 responsabilidades num único static class

Captura de tela inteira, captura de janela específica (com letterbox), e enumeração de janelas — tudo numa classe só, com ~15+ declarações de P/Invoke. Coeso (tudo é GDI/Win32), mas grande. Não urgente.

**Direção**: considerar separar em `ScreenCapture`/`WindowCapture`/`WindowEnumeration` se a classe continuar crescendo.
