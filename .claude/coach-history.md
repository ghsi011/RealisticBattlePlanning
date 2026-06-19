# Coach Suggestion History

**Last coach run**: 2026-06-20 — score 8/10 (=)
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

## 2026-06-19 — run #2 (delta, post block-1; score 8/10 =)

Usage-log-driven (the log now has data). Applied to `.claude/settings.local.json`
+ `.claude/hooks/track-usage.ps1` (machine-local, gitignored):
- **Usage hook missed the PowerShell tool** — on Windows that's the dominant path
  (dotnet build/test, dev-relaunch, file-channel driving), so the log was blind to
  it. Added `PowerShell` to the PostToolUse matcher and the script's tool list +
  detail handling. Verified it now logs PowerShell commands.
- **git workflow not in permissions** — full-auto mode commits/pushes constantly;
  added `Bash(git add/commit/push:*)` to project-local permissions to smooth the loop.

Still pending (carry-over, needs the user): `csharp-lsp` requires a Claude Code
restart to load; after that, create `.claude/rules/tooling/lsp-fallbacks.md`.

## 2026-06-20 — run #3 (during the overnight map redesign; score 8/10 =)

Focused doc-freshness pass (the setup itself is still solid at 8/10). The biggest
gap was a **stale load-bearing doc**: `AGENTS.md`'s "Fast UI dev loop" still told
future sessions to drive the planner with computer-use, but this session proved the
in-game keyboard unreliable over automation and built a file-driven loop instead.
Applied:
- Rewrote the "Fast UI dev loop" section + the `tools/` inventory to document the
  keyboard-free loop: the `planner.cmd` sentinel (PlanningModeView poll) and the
  wrappers `map-iterate.ps1` (deploy-ui → brushes hot-reload → reopen → shot →
  PNG, no relaunch for XML/brush), `respawn.ps1` (relaunch+battle for C# changes),
  `crop-zoom.ps1`, plus the `click`/`rightclick` test verbs. *(committed)*

Not actioned (still solid / out of scope mid-task): full skills/hooks/permissions
re-audit — no new gaps since run #2; `csharp-lsp` restart still pending the user.
