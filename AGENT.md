# AGENT.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Achievement Tracker is a Playnite **GenericPlugin** extension for .NET Framework 4.6.2 that scans games for achievements from Steam and multiple Steam emulator sources, displaying progress in the Playnite UI.

## Build & Development

```bash
make build      # dotnet build src/AchievementTracker.csproj
make clean      # clean build artifacts
make rebuild    # clean + build
```

Deploy by copying `AchievementTracker.dll` + dependencies to Playnite's extensions directory.

## Architecture

```
AchievementTrackerPlugin (GenericPlugin, entry point)
 ├─ GetGameViewControl() → AchievementTrackerControl (embedded game detail view)
 ├─ OnGameSelected()     → updates control with selected game
 ├─ GetGameMenuItems()   → "View Achievements" window + "Debug Info" window
 └─ GetSidebarItems()    → Library-wide progress sidebar

AchievementScanner (core service)
 └─ ScanForAchievements(game)
      1. Detect Steam AppId (GameId, steam_appid.txt, links, INI files)
      2. Fetch achievements from Steam Community (XML or HTML fallback)
      3. Collect local unlock status into dictionary
      4. Merge local unlocks into Steam master list
      5. Sort and return

Local unlock sources: CODEX, RUNE, FLT, Goldberg/GSE, EMPRESS,
  OnlineFix, RLD!, SKIDROW, CreamAPI, SmartSteamEmu, Steam userdata cache
```

See `.spec/` for detailed pipeline and format specifications.

## Key Conventions

- C# 6 syntax only (no ValueTuple, no C# 7+ features) — targets .NET 4.6.2
- Long operations run on background threads; UI updates via `Dispatcher.Invoke()`
- `AchievementTrackerControl` uses plain `UserControl` (not `PluginUserControl`) to avoid Playnite SDK auto-collapse
- Scanner uses online-first strategy: Steam provides the master list, local files only provide `IsUnlocked` / `UnlockTime`
- Achievement matching: ID → Name → Normalized Name → Snake-case bridge → Normalized Description

## File Layout

| Path | Purpose |
|------|---------|
| `src/AchievementTrackerPlugin.cs` | Plugin entry point |
| `src/AchievementTracker.csproj` | .NET 4.6.2 project |
| `src/Models/{Achievement,ScanDebugInfo}.cs` | Data models |
| `src/Services/AchievementScanner.cs` | Core scanning + merge logic |
| `src/UI/AchievementTrackerControl.xaml(.cs)` | Main game detail view |
| `src/UI/AchievementsWindow.xaml(.cs)` | Standalone achievement window |
| `src/UI/SidebarView.xaml(.cs)` | Library progress sidebar |
| `src/UI/DebugWindow.xaml(.cs)` | Diagnostics window |

## Progress Report Format

APPEND to progress.txt (never replace, always append):
```
## [Date/Time] - [Story ID]
- What was implemented
- Files changed
- **Learnings for future iterations:**
  - Patterns discovered (e.g., "this codebase uses X for Y")
  - Gotchas encountered (e.g., "don't forget to update Z when changing W")
  - Useful context (e.g., "the evaluation panel is in component X")
---
```

The learnings section is critical - it helps future iterations avoid repeating mistakes and understand the codebase better.

## Consolidate Patterns

If you discover a **reusable pattern** that future iterations should know, add it to the `## Codebase Patterns` section at the TOP of progress.txt (create it if it doesn't exist). This section should consolidate the most important learnings:

```
## Codebase Patterns
- Example: Use `sql<number>` template for aggregations
- Example: Always use `IF NOT EXISTS` for migrations
- Example: Export types from actions.ts for UI components
```

## Important

- Work on ONE story per iteration
- Read the Codebase Patterns section in progress.txt before starting
