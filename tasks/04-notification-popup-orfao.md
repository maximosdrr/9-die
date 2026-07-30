# 04 — NotificationPopup órfão

**Prioridade**: Média (feedback/UX — mas era código morto, não bug funcional)
**Status**: ✅ Resolvido em 29/07/2026

## Achado

`Features/Player/Components/UI/NotificationPopup.cs` (+ `.tscn`) implementa um diálogo genérico aceitar/rejeitar (`RequestDecision(string message)` assíncrono, retorna `bool`). Instanciado em `Player.tscn` sob `UI/`, existe desde o commit mais antigo do histórico (`feat: add table ui`). Busquei em todo o projeto: **nenhum código chama `RequestDecision` nem referencia o nó `NotificationPopup`** — puro código morto, nunca teve um caller. Também não tinha guard de `IsMultiplayerAuthority()`, então se alguém reativasse do jeito que estava, ficava clicável na cópia de todo mundo (rede), não só na local.

Perguntei ao dono do projeto se valia inventar um uso (revanche, confirmar saída) ou simplesmente remover. Resposta: **deletar (Recomendado)**.

## Resolvido

Removidos `NotificationPopup.cs`, `NotificationPopup.cs.uid` e `NotificationPopup.tscn` (`git rm`). Removida a instância e o `ext_resource` correspondente em `Player.tscn` (nó `UI/NotificationPopup`). `UI/TvShareButton` continua no lugar normalmente.

## Verificação

`dotnet build 9Die.csproj` limpo (0 erros, 0 warnings). Confirmado por grep que não sobrou nenhuma referência a `NotificationPopup` no projeto.
