# Night Loot Multiplier

SPT (Single Player Tushonka) server mod. Scales loose/static loot spawn multipliers by night vs day raid, via server's built-in night-raid detection.

## Features

- **Night/day loot scaling** — reads raid's `IsNightRaid` flag (already computed server-side for bot inventory gen). Applies configurable multiplier to `LooseLootMultiplier` / `StaticLootMultiplier` for the map, before loot gen runs.
- **Soft LotsofLoot integration** — if LotsofLoot installed, also scales its Marked Room / Ref Room multipliers for current map, via runtime reflection (no assembly ref). Not installed → skipped clean, one log line, nothing breaks.
- **Night bot difficulty weighting** — standalone, no ABPS or other mod dependency. Postfixes `LocationLifecycleService.GenerateLocationAndLoot`, re-rolls `BossDifficulty`/`BossEscortDifficulty` on the per-raid location clone's boss/PMC spawn entries. Night raids only, only when pre-raid difficulty dropdown is "AsOnline" — day raids and explicit difficulty choices are completely untouched (ABPS's own weighting if installed, vanilla otherwise). Doesn't cover non-boss dynamic scav waves — that data has no per-wave difficulty field to rewrite.

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
  "NightBotDifficulty": { "easy": 0, "normal": 10, "hard": 40, "impossible": 50 }
}
```

- `NightMultiplier` — loose/static loot (+ LotsofLoot Marked/Ref room, if present) during night raid (22:00–04:59 in-raid time, or `factory4_night` always).
- `DayMultiplier` — everything else.
- `NightBotDifficulty` — weighted odds (easy/normal/hard/impossible) picked per boss/PMC spawn entry, night raids only, dropdown at "AsOnline" only.

Restart server after config edit.

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
