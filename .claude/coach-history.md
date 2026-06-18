# Coach Suggestion History

**Last coach run**: 2026-06-18 — score 6/10 (first run)
**Last deep CLAUDE.md optimization**: never

## 2026-06-18 — first run, score 6/10

User accepted **all** suggestions. Implemented:

- **#1** Bridged `AGENTS.md` into context via a root `CLAUDE.md` (`@AGENTS.md`). *(committed)*
- **#2** Project permission allowlist in `.claude/settings.local.json` (dotnet build/test, git status/diff/log).
- **#4** Stop + Notification beep hooks (Windows PowerShell) in `.claude/settings.local.json`.
- **#5** PostToolUse usage-tracking hook → `~/.claude/coach-usage-log.jsonl` via `.claude/hooks/track-usage.ps1` (PowerShell, no jq/bash dependency). Verified live.
- **#6** `type=user` profile memory created + indexed.
- **#8** Scoped rule `.claude/rules/core-no-engine-types.md` (no TaleWorlds types in `*.Core`). *(committed)*
- **#9** Enabled `csharp-lsp@claude-plugins-official` in `~/.claude/settings.json` (backup at `settings.json.bak`) + installed `csharp-ls` 0.25.0 global tool. **Needs a Claude Code restart to load.**
- Added `.claude/settings.local.json` and `graphify-out/` to `.gitignore`.

Noted / not actioned:
- **#3** `moddebugkit` skill exists at both user + project level — kept both intentionally (user-level = global availability; project-level = repo-canonical, committed). Cosmetic double-listing only when working in this repo.
- **#7** rtk token optimizer — **blocked**: no Rust/`cargo` on the machine. Revisit if `cargo` is installed later.

Next-time candidates: once the usage log has data, re-run for usage-based skill/permission/friction insights.
