# Night Loot Multiplier — Dev Log

Full session knowledge dump. Caveman-compressed: dense, fragments, zero facts dropped.

## Repo / build

- `D:\Git Repo\Night Loot Multiplier\` — sln `Night Loot Multiplier.slnx` → project `Night Loot Multiplier\NightLootMultiplier.csproj` (folder has space, project/assembly name doesn't).
- Scaffolded off `D:\Git Repo\SPT4-ServerMod-Template` (net9.0/4.0.5 template), corrected to net10.0/4.1.6 per live server.
- `SPTushonka.*` = NuGet id. `SPTarkov.*` = C# namespace. Not same string — don't assume match.
- Packages (all 4.1.6): `SPTushonka.Common`, `SPTushonka.DI`, `SPTushonka.Server.Core`, `SPTushonka.Reflection`, `SPTushonka.Server.Web` (added for SIC integration, kept after Blazor-page revert — `IConfigEditorConfigProvider` types live there too).
- `SPTPath=F:\SPT_4.1_Server` — Debug build auto-deploys DLL+PDB+`config/**/*.json` to `$(SPTPath)\SPT_Runtime\user\mods\NightLootMultiplier\` (`DebugCopy` target). Release → `D:\Staging Area\NightLootMultiplier\...` (`StagingForge` target).
- `ModMetadata.cs`: `record ModMetadata : IModMetadata` (no `override`, C#-4.1-shape not 4.0). GUID `com.kobethuy.nightlootmultiplier`. `SptVersion ~4.1.6`. Version 1.0.0→1.1.0 (bot difficulty feature).
- Repo pushed `github.com/thuynguyentrungdang/SPT-NightLootMultiplier`. Releases `v1.0.0`, `v1.1.0` tagged+pushed, CI-built via `.github/workflows/release.yml`.

## Feature 1 — Night/day loot multiplier

**Hook:** `LocationLifecycleService.GenerateLocationAndLoot(MongoId sessionId, string name, bool generateLoot=true)` — sole call site for raid loot gen, `server-csharp-tushonka`. Prefix patch (`AbstractPatch`/`[PatchPrefix]`).

**Night detection chain (server-verified):**
- `MatchController.ConfigureOfflineRaid` (line 92-98): `request.IsNightRaid = weatherHelper.IsNightTime(request.TimeVariant, request.Location)`, stored via `profileActivityService.GetProfileActivityRaidData(sessionId).RaidConfiguration = request` — happens *before* `GenerateLocationAndLoot` fires. No custom raid-time context needed, just read back.
- `WeatherHelper.IsNightTime(timeVariant, mapLocation)` (`Helpers/InRaid/WeatherHelper.cs:46`): `factory4_night`→always true, `factory4_day`→always false (hardcoded). Else: `GetInRaidTime()` (Tarkov-accelerated time, `(realtime*7)%24h + 3h`), `+12h` if `TimeVariant.PAST`. Night = `hour > 21 || hour < 5` (22:00–04:59). No special-case laboratory/labyrinth — confirmed via grep, both fall through normal calc, both valid `looseLootMultiplier`/`staticLootMultiplier` base entries in `location.json` (`laboratory: 2.8/1`, `labyrinth: 3/1`) — mod applies normally. Unused helper `IsHourAtNightTime` has off-by-one (`>21 || <=5`) vs `IsNightTime`'s `<5` — irrelevant, only `IsNightTime` feeds `IsNightRaid`.

**Live-verified request sequence:** `/client/raid/configuration` (sets IsNightRaid) → `/fika/raid/create` → `GenerateLocationAndLoot` fires → 2× more `/client/raid/configuration` (no-op resyncs) → `/client/match/local/start` (no 2nd gen, `Raids>1` guard). `IsNightRaid` always known before patch fires.

**Anti-compound design:** `BaseMultiplierSnapshotService` (`[Injectable(Singleton), TypePriority=Preload+100]`, `IOnLoad`) snapshots `LocationConfig.LooseLootMultiplier`/`StaticLootMultiplier` ONCE at boot into `LooseLootBase`/`StaticLootBase`. Every raid, patch computes `base * factor` fresh — never `current * factor` — repeated raids never stack.

**Patch** (`Patches/NightLootMultiplierPatch.cs`): ctor-injects `ProfileActivityService, LocationConfig, BaseMultiplierSnapshotService, ConfigService, LotsofLootIntegrationService, ISptLogger<T>` into `static` fields (Harmony prefixes must be static). `[PatchPrefix][HarmonyPriority(Priority.Low)] Prefix(MongoId sessionId, string name, bool generateLoot)`: skip if `!generateLoot || name=="hideout"`. Read `IsNightRaid`, pick `NightMultiplier`/`DayMultiplier`, write `base*factor` into `LocationConfig`'s live dicts, delegate LotsofLoot integration, log line.

## LotsofLoot soft-integration (reflection, no compile ref)

`Services/LotsofLootIntegrationService.cs`. `IsActive` gate, fail-safe (any error → log + `IsActive=false`, never throws into host).

**Discovery (`OnLoadAsync`):** `AppDomain.CurrentDomain.GetAssemblies()` find `"LotsofLoot"` by name → `GetType("LotsofLoot.Services.ConfigService")` → `serviceProvider.GetService(type)` (shared DI, not `new`) → cache `PropertyInfo` for `LotsofLootPresetConfig`.

**Found+fixed real race condition** (user asked "what if race condition at GenerateLocationAndLoot between mods"):
- LotsofLoot's OWN patch, `RandomizePresetPatch`, ALSO prefixes `GenerateLocationAndLoot` (`if RandomizePresets on → RollForRaid() → maybe ApplyPresetForRaid(newPreset)`).
- `ConfigService.ApplyPresetForRaid`: `LotsofLootPresetConfig = loadedPreset;` — **replaces whole object**, doesn't mutate in place. Cached `Dictionary<string,double>` ref from OnLoad-time orphaned instant swap happens — writes go nowhere, silently.
- Both prefixes default `Priority.Normal` → Harmony order unspecified → either way "cached ref" design breaks (their-first: swap orphans our cached ref before write; ours-first: we write correctly then they swap it away).
- **Fix:** our `Prefix` marked `[HarmonyPriority(Priority.Low)]` (Harmony runs higher priority first) → guarantees we always observe post-swap state. AND: stopped caching `LotsofLootPresetConfig` object/dicts — `TrySet(mapName, factor)` re-resolves CURRENT object every call via cached `PropertyInfo.GetValue(null)`/instance, compares `ReferenceEquals` against last-seen, re-snapshots base dicts (`MarkedRoomConfig.Multiplier`/`RefRoomConfig.Multiplier`, both reflected 2 levels deep) only when identity changed. Self-healing regardless of swap timing.
- Doc comment in file records this reasoning — read before touching class again.

## `PatchLoaderService` — patch activation + real bug fixed

`Services/PatchLoaderService.cs`, `[Injectable(TypePriority=Preload)]`, `IOnLoad`. Ctor: `IEnumerable<IRuntimePatch> patches` (DI-collects every mod's `AbstractPatch` subclasses, not just ours). `OnLoadAsync`: `foreach patch → patch.Enable()`.

**AbstractPatch mechanics learned:** `Enable()` checks `ReferenceEquals(_ownersAssembly, Assembly.GetCallingAssembly())` (captured at ctor time) — silently no-ops if mismatched, so cross-mod enabling (LotsofLoot's own `PreSPTLoad` also loops all `IRuntimePatch` and enables them) safe/idempotent by design — established ecosystem pattern, not a bug. `GetTargetMethod()` runs OUTSIDE Enable()'s try/catch though — `AmbiguousMatchException` or similar propagates raw.

**Bug found+fixed:** original loop had **no try/catch** — one mod's patch throwing during `Enable()` aborts whole `foreach`, silently preventing every patch enumerated *after* it (possibly ours) from activating. Fixed: wrapped each `patch.Enable()` in try/catch, logs `[NightLootMultiplier] Failed to enable patch {type}: {msg}` and continues. `PatchManager` (separate SPTushonka.Reflection class) exists but unused by us/LotsofLoot — both self-drive via simpler direct-loop pattern.

## Feature 2 — Night bot difficulty weighting

Goal: night raids skew PMC/boss difficulty harder, standalone (no ABPS dependency required), config-driven weights.

### ABPS research (context/compat, not a dependency)

`D:\Git Repo\botplacementsystem-csharp` = live C# rewrite (net9.0→4.1.x target, assembly name `acidphantasm-botplacementsystem`, root namespace `_botplacementsystem`). `D:\Git Repo\acidphantasm-botplacementsystem` = archived TS mod, ignore. Confirmed via commit dates (TS last 2025-06-06, C# 2026-06-13) + `.csproj` shape. Both installed live on dev server (`F:\SPT_4.1_Server\...\user\mods\acidphantasm-botplacementsystem`), config.json matches repo shape (`pmcDifficulty`/`bossDifficulty` keys, user's tuned values e.g. `pmc: 0/25/45/30`, `boss: 5/15/45/35`).

- SPT core has **no weighted-difficulty concept at all** — only `PmcConfig.UseDifficultyOverride`/`Difficulty` (single fixed value/string) + uniform `BotDifficultyHelper.ChooseRandomDifficulty()` (equal-odds, not weighted). Nearest weighted-dict precedent in core: `PmcConfig.GameVersionWeight`/`AccountTypeWeight`, consumed via `WeightedRandomHelper.GetWeightedValue<T>(Dictionary<T,double>)`.
- ABPS owns its own weighting: `Globals.ModConfig` (`[Injectable(Singleton)]`, static `Config` property type `AbpsConfig`) → `.PmcDifficulty`/`.BossDifficulty` (`Dictionary<string,double>`, public get/set, mutable in place). Consumed `Controllers/PmcSpawns.cs`/`BossSpawns.cs` via `weightedRandomHelper.GetWeightedValue(...)`, written into generated `BossLocationSpawn.BossDifficulty`/`.BossEscortDifficulty`.
- `MapSpawns.ConfigureInitialData()` — confirmed **plain sync `void`**, not async (despite `ModConfig` wrapping calls in `Task.Run` for unrelated web-UI responsiveness). Rebuilds ALL 13 hardcoded maps every call (no per-map param), wipes+rebuilds `BossLocationSpawn`/waves on SHARED persistent `Location` table entries directly — mutates `location.Base`, not a clone. Triggered server boot (`BotPlacementSystem.OnLoad`, `TypePriority=PostDBModLoader+69420`, deliberately very late) AND after every raid ends (`/client/match/local/end` → `StaticRouters.cs`). **Not** triggered at raid START.
- ABPS's 3 server Harmony patches (`AdjustWaves_Patch`, `AdjustPmcSpawns_Patch` on `RaidTimeAdjustmentService`, `ReplaceBotHostility_Patch` on `SeasonalEventService`) don't touch `GenerateLocationAndLoot` or `BotController.GetBotDifficulty` — no collision risk with our patches either way.

### Wrong approach #1 (built, shipped, debugged live): `BotController.GetBotDifficulty`

Initial design patched `BotController.GetBotDifficulty(MongoId sessionId, string type, string diffLevel, bool ignoreRaidSettings=false)` (`Controllers/BotController.cs:84`) — full prefix override (`return false`, set `__result`), gated `IsNightRaid && dropdown=="asonline" && !ignoreRaidSettings`.

**User reported** (real raid test, ABPS installed): log line never fired, difficulty unchanged, night confirmed via loot patch firing.

**Root cause** (user supplied fix insight): method fires **once at game client start** (client priming AI-settings cache for every type+difficulty combo), **not per-raid** — `raidConfig` null/stale at that point, gate always failed silently before any real raid existed. Wrong hook entirely — patching it can never see real raid state.

Along the way, also fixed `PatchLoaderService` try/catch bug above (found while diagnosing — added unconditional entry-log to patch too, later removed once hook replaced).

### Correct approach: postfix `LocationLifecycleService.GenerateLocationAndLoot`

Actual per-spawn difficulty lives on `LocationBase.BossLocationSpawn` (`List<BossLocationSpawn>`), fields `BossDifficulty`/`BossEscortDifficulty` (both `string?`) — baked in by vanilla map JSON or ABPS's `ConfigureInitialData()` rewrite, BEFORE `GenerateLocationAndLoot` clones `location.Base`. `Wave` record (regular scav waves) has **no difficulty field at all** — known gap, dynamic/non-boss scav spawns not covered.

**Design:** `Patches/NightBotDifficultyPatch.cs`, separate `AbstractPatch` also targeting `GenerateLocationAndLoot`, as `[PatchPostfix]` (runs after original returns, receives `LocationBase __result` — no `ref` needed, mutating list entries in place enough since reference type). Runs AFTER ABPS/vanilla already baked values into shared template AND after clone made — mutates only per-raid clone, shared template (and ABPS's own future raids) untouched. Gate: `!generateLoot || name=="hideout"` → skip; `!IsNightRaid` → skip (day defers entirely to ABPS/vanilla); `dropdown!="asonline"` → skip (explicit player choice wins). Then `foreach spawn in __result.BossLocationSpawn`: pick weight dict, `weightedRandomHelper.GetWeightedValue(weights)` for both `BossDifficulty` and `BossEscortDifficulty`.

No `HarmonyPriority` juggling needed — ABPS doesn't patch this method at all.

### PMC vs boss discriminator — got wrong once, user corrected

First attempt: `spawn.BossName == "pmcBot"` → PMC bucket. **Wrong.** String used ONLY on `rezervbase`/`laboratory`, per SPT core's own comment (`PostDbLoadService.cs:424`, `AdjustMinReserveRaiderSpawnChance`): *"Raiders are bosses"* — `pmcBot` = Raiders, not PMCs.

**User flagged it** ("i think pmcusec and pmcbear also used for pmcs"). Verified grepping every map's `base.json`: **`"pmcUSEC"`/`"pmcBEAR"`** real PMC-wave `BossName` values, used every map (bigmap, rezervbase, woods, laboratory, factory4_day/night, interchange, lighthouse, shoreline, tarkovstreets, sandbox_high — exhaustively confirmed). ABPS's own `Defaults/PMCs.json` same two strings. Fixed: `isPmc = BossName is "pmcUSEC" or "pmcBEAR"` (case-insensitive) → `NightPmcDifficulty`, else → `NightBossDifficulty` (`pmcBot`/Raiders correctly falls into boss bucket).

### Config shape (final)

`Models/NightLootConfig.cs`: `NightMultiplier` (1.5), `DayMultiplier` (1.0), `NightPmcDifficulty` + `NightBossDifficulty` (both `Dictionary<string,double>`, keys `easy/normal/hard/impossible`; **no `DayPmcDifficulty`/`DayBossDifficulty`** — user rejected "next-raid-lag" design for day, day fully defers, no config surface needed).

## Feature 3 — SIC config editor integration

User asked "register config to SIC (SPT's own Server Information Center) so editable from web page." Required one clarifying question first (guessed wrong: SVM) — user corrected: SIC = SPT's built-in web dashboard.

### Wrong approach #1 (built, shipped, reverted per user follow-up)

Built full custom Blazor page: `ModMetadata` implementing `IModBlazorMetadata` (`HomePage="/night-loot-multiplier"`, registers card on SIC landing page + routes mod-owned page), `Web/_imports.razor`, `Web/Pages/Settings.razor` (used SIC's own shared `@layout BaseMudBlazorLayout` — no custom layout needed, `MudBlazor` comes transitively via `SPTushonka.Server.Web`), `ConfigService.WriteConfigAsync()` (manual `FileUtil.WriteFileAsync` + `JsonUtil.Serialize`) for persistence, `[Authorize(Policy="Administrator")]` on page.

**Real Razor bug hit+fixed along way:** `@bind-Value="dict[key]"` **can't** generate valid setter for dictionary-indexer lvalue — broke Razor source-gen across whole file (56 compiler errors, malformed `.g.cs`). Fixed switching those fields (8 difficulty-weight fields) to explicit `Value=`/`ValueChanged=@((double v) => Handler(key, v))` instead of two-way `@bind-Value`. Plain-property fields (`NightMultiplier`/`DayMultiplier`, not indexers) kept simpler `@bind-Value:after` pattern fine.

**User's follow-up:** "use SIC's native interface instead of building our own page. Check how LotsofLoot does it." — LotsofLoot NOT right reference (built same kind of full custom Blazor UI, for its own elaborate multi-preset system — different/heavier mechanism, not "native" one).

### Correct approach: `IConfigEditorConfigProvider`

Found in `SPTushonka.Server.Web` (`Services/IConfigEditorConfigProvider.cs`, `Services/ConfigEditorService.cs`, `Models/Configs/ConfigEditorConfigRegistration.cs`). THE actual SIC-native generic config editor — fully reflection-driven (JSON tab + auto-generated structured "Controls" tab, diffing against on-disk "clean" values, save-as-preset, apply-to-runtime), used for every built-in SPT config already; mods opt in with **zero custom Razor**.

Contract: `[Injectable] class X : IConfigEditorConfigProvider { IEnumerable<ConfigEditorConfigRegistration> GetConfigs(); }`, DI-collected same pattern as `IRuntimePatch`/`IOnLoad` (`IEnumerable<IConfigEditorConfigProvider>` injected into `ConfigEditorService`). `ConfigEditorConfigRegistration.Create(id, displayName, runtimeConfig, filePath)` — generic, infers `RuntimeType` from `runtimeConfig`. Editor's `SaveConfigAsync` does own `File.WriteAllTextAsync` using `FilePath` when no custom `SaveToDiskAsync` callback given; `ApplyToRuntimeAsync` does own reflection-based `CopyWritableProperties` from edited-copy onto **live** `RuntimeConfig` object when no custom `ApplyToRuntimeAsync` callback given — passing actual `configService.Config` object directly means edits apply to SAME instance patches already read from, live, no extra wiring.

**Final implementation:** ripped out ALL Feature 3 wrong-approach #1 (`Web/` folder, `wwwroot/`, `IModBlazorMetadata` on `ModMetadata`, `ConfigService.WriteConfigAsync()`). `ConfigService` kept minimal: loads + exposes `Config` and new `ConfigFilePath` (absolute path, resolved via `ModHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly())` + `config/config.json` — same resolution pattern as original config-load code). New `Services/NightLootConfigEditorProvider.cs`: `[Injectable] class NightLootConfigEditorProvider(ConfigService configService) : IConfigEditorConfigProvider`, `GetConfigs()` yields one `ConfigEditorConfigRegistration.Create("night-loot-multiplier", "Night Loot Multiplier", configService.Config, configService.ConfigFilePath)`.

## Infra / release pipeline bugs found+fixed

`.github/workflows/release.yml`:
1. **Output-path double-segment bug:** csproj's `<OutputPath>bin\$(Configuration)\$(ProjectName)\$(AssemblyName)\</OutputPath>` — but `$(ProjectName)` evaluates **empty** at that point in SDK-style project evaluation order (only gets default from common targets imported LATE via `Sdk.targets`, after project body's own `PropertyGroup` already ran) — real output path only ONE name segment (`bin\Release\NightLootMultiplier\`), not two. Verified via `dotnet msbuild -getProperty:OutputPath -getProperty:TargetPath`. Workflow originally hardcoded naive two-segment guess — fixed to match reality. (Debug/Release MSBuild targets referencing `$(OutputPath)` directly self-heal regardless, read resolved value at build time not a hardcoded guess — only workflow's separately-typed path string was wrong.)
2. **Missing `SPT_Runtime` prefix:** zip staged as `stage/user/mods/...`, should be `stage/SPT_Runtime/user/mods/...` matching real SPT layout (same layout csproj's own `DebugCopy`/`StagingForge` targets deploy to). User caught this.
3. **Missing `contents: write` permission:** default `GITHUB_TOKEN` no release-create permission → `softprops/action-gh-release` 403'd (`Resource not accessible by integration`). Added `permissions: contents: write` at workflow level.

After each fix: delete+recreate version tag on corrected commit, re-push to retrigger (Actions reads workflow file version as it exists AT tagged commit).

## Process / tooling meta (not code, load-bearing for session)

- User's global `~/.claude/CLAUDE.md` mandates "Caveman Ultra" mode: `[Caveman: on]` confirmation line first, broken-grammar (drop articles/pronouns/helper-verbs), max token efficiency, every chat reply. Drifted chatty over long session — fixed adding `UserPromptSubmit` hook (`~/.claude/settings.json`) re-injecting full Caveman ruleset as fresh `additionalContext` before every message, never fades from context like once-loaded CLAUDE.md does. Also strengthened CLAUDE.md itself with explicit "grammar, not just short sentences" clause.
- Git workflow observed this session: user does merges/some commits directly in Rider outside assistant's tool calls — working tree state should always be re-checked (`git status`/`git log`) before assuming what's pending vs already pushed, don't assume assistant's own last action is latest repo state.

## Known limitations / open gaps (carried forward)

- Non-boss dynamic scav wave difficulty: not covered by Feature 2 (no per-wave difficulty field exists in `Wave` model to rewrite).
- Feature 1's LotsofLoot marked/ref-room integration: correctness depends on `HarmonyPriority.Low` continuing to out-rank whatever priority LotsofLoot's own patch uses — if LotsofLoot ever raises its own patch priority above default `Normal`, breaks again; no compile-time guard, only doc-comment warning.
- **Fixed (this session):** LotsofLoot's own `LootMultipliers.Apply` (`LotsofLoot/OnPresetUpdate/LootMultipliers.cs`, run via its `IOnPresetUpdate` pipeline incl. `RandomizePresetPatch` mid-raid preset swaps) writes preset's per-map `LooseLootMultiplier`/`StaticLootMultiplier` **directly into same shared `LocationConfig.LooseLootMultiplier`/`StaticLootMultiplier` dicts** our own patch also writes into — same shared-dict hazard as MarkedRoom/RefRoom, not yet handled. Our patch was multiplying only against own boot-time snapshot, silently stomping LotsofLoot's live per-map value (and any mid-raid randomized-preset swap) instead of multiplying on top. Fix mirrors MarkedRoom/RefRoom pattern: `LotsofLootIntegrationService` now also reflects+re-snapshots (base, per-map, re-resolved on preset-identity change same as MarkedRoom/RefRoom) preset's top-level `LooseLootMultiplier`/`StaticLootMultiplier` dicts, exposed via `TryGetLooseLootMultiplier`/`TryGetStaticLootMultiplier`. `NightLootMultiplierPatch.Prefix` now does `lotsofLoot base ?? own snapshot base` (via `||`-chained `TryGetValue`) before multiplying by night/day factor — LotsofLoot's live value wins when present, vanilla-boot snapshot fallback when LotsofLoot not installed/doesn't cover that map.
- SIC config editor integration: not yet live-verified this session (built + compiled clean, not click-tested on actual Configs page).
