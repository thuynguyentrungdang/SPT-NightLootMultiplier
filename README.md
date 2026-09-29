# Night Loot Multiplier

SPT (Single Player Tushonka) server mod. Scales loose/static loot spawn multipliers by night vs day raid, via server's built-in night-raid detection.

## Features

- **Night/day loot scaling** — reads raid's `IsNightRaid` flag (already computed server-side for bot inventory gen). Applies configurable multiplier to `LooseLootMultiplier` / `StaticLootMultiplier` for the map, before loot gen runs.
- **Soft LotsofLoot integration** — if LotsofLoot installed, also scales its Marked Room / Ref Room multipliers for current map, via runtime reflection (no assembly ref). Not installed → skipped clean, one log line, nothing breaks.
- **Night bot difficulty weighting** — standalone, no ABPS or other mod dependency. Postfixes `LocationLifecycleService.GenerateLocationAndLoot`, re-rolls `BossDifficulty`/`BossEscortDifficulty` on the per-raid location clone's boss/PMC spawn entries — independent PMC vs boss weights, split by `BossName` ("pmcUSEC"/"pmcBEAR" = PMC, everything else = boss — including "pmcBot", which is actually Raiders). Night raids only, only when pre-raid difficulty dropdown is "AsOnline" — day raids and explicit difficulty choices are completely untouched (ABPS's own weighting if installed, vanilla otherwise). Doesn't cover non-boss dynamic scav waves — that data has no per-wave difficulty field to rewrite.
- **SIC config editor integration** — registers `config.json` with SIC's own built-in config editor via `IConfigEditorConfigProvider` (no custom page). Shows up alongside every other SPT/mod config on SIC's Configs screen — JSON + structured Controls tab, diffing, presets, all for free. `RuntimeConfig` is the same live object the patches read from, so edits apply immediately.

## Installation

1. Get built mod folder (`NightLootMultiplier/` — DLL, `config/config.json`): release zip, or your own build (see Build guide).
2. Copy `NightLootMultiplier` folder into SPT server's `user/mods/`:
   ```
   <SPT server root>/SPT_Runtime/user/mods/NightLootMultiplier/
   ```
3. Start server. Log line `[NightLootMultiplier]` confirms LotsofLoot integration active or skipped.

Requires SPT/Tushonka **~4.1.6**.

## Usage

Edit `user/mods/NightLootMultiplier/config/config.json`:

```json
{
  "NightMultiplier": 1.5,
  "DayMultiplier": 1.0,
  "NightPmcDifficulty": { "easy": 100, "normal": 0, "hard": 0, "impossible": 0 },
  "NightBossDifficulty": { "easy": 100, "normal": 0, "hard": 0, "impossible": 0 }
}
```

- `NightMultiplier` — loose/static loot (+ LotsofLoot Marked/Ref room, if present) during night raid (22:00–04:59 in-raid time, or `factory4_night` always).
- `DayMultiplier` — everything else.
- `NightPmcDifficulty` — weighted odds (easy/normal/hard/impossible) for PMC-as-wave spawn entries, night raids only, dropdown at "AsOnline" only.
- `NightBossDifficulty` — same, for actual boss spawn entries.

Restart server after config edit — or skip the file entirely and use SIC's Configs page (find "Night Loot Multiplier" in the config list), which writes changes live.

## Build guide

Requires: .NET 10 SDK, local SPT/Tushonka 4.1.6 server install to deploy against.

1. Clone repo, open `Night Loot Multiplier.slnx` in Rider/VS.
2. In `Night Loot Multiplier/NightLootMultiplier.csproj`, set `<SPTPath>` to local SPT server install path (folder containing `SPT_Runtime`).
3. Build:
   ```
   dotnet build "Night Loot Multiplier/NightLootMultiplier.csproj"
   ```
   - **Debug** → auto-copies DLL+PDB+`config/config.json` into `$(SPTPath)/SPT_Runtime/user/mods/NightLootMultiplier/`. Fast iteration.
   - **Release** (`-c Release`) → copies into `$(StagingDir)`. Packaging.
4. Test: launch SPT server (Rider config `SPTarkov.Server`, or `SPT.Server.exe` direct). Raid at night hour (~23:00) vs day hour. Confirm `[NightLootMultiplier]` log shows expected `isNight`/`factor`, loose/static spawn counts differ.
