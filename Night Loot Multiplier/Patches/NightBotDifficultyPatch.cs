using System.Reflection;
using NightLootMultiplier.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Services.InRaid;
using SPTarkov.Server.Core.Services.Profile;

namespace NightLootMultiplier.Patches;

/// <summary>
/// Standalone night bot difficulty weighting, no dependency on any other mod (e.g. ABPS).
///
/// BotController.GetBotDifficulty is NOT a per-raid hook - the client calls it once at game
/// client start to prime its AI-settings cache for every (type, difficulty) combo, long before
/// any raid (or IsNightRaid) exists. Patching it never sees real raid state.
///
/// The actual per-spawn difficulty a bot gets comes from LocationBase.BossLocationSpawn entries
/// (BossDifficulty / BossEscortDifficulty), baked in either by vanilla map data or by ABPS's own
/// ConfigureInitialData() rewrite of the shared location table - before GenerateLocationAndLoot
/// ever clones it. So: postfix GenerateLocationAndLoot, re-roll those fields on the CLONE for
/// this raid only, night raids only. Never touches the shared template ABPS/vanilla wrote to, so
/// day raids and every other mod's own raid are completely unaffected.
///
/// Known gap: regular (non-boss) scav Wave entries carry no difficulty field at all in this data
/// model, so dynamic scav difficulty isn't covered here - only boss + PMC-as-boss-spawn entries.
/// </summary>
[Injectable]
public sealed class NightBotDifficultyPatch : AbstractPatch
{
    private static ProfileActivityService _profileActivityService = default!;
    private static WeightedRandomHelper _weightedRandomHelper = default!;
    private static ConfigService _configService = default!;
    private static ISptLogger<NightBotDifficultyPatch> _logger = default!;

    public NightBotDifficultyPatch(
        ProfileActivityService profileActivityService,
        WeightedRandomHelper weightedRandomHelper,
        ConfigService configService,
        ISptLogger<NightBotDifficultyPatch> logger
    )
    {
        _profileActivityService = profileActivityService;
        _weightedRandomHelper = weightedRandomHelper;
        _configService = configService;
        _logger = logger;
    }

    protected override MethodBase GetTargetMethod()
    {
        return typeof(LocationLifecycleService).GetMethod(nameof(LocationLifecycleService.GenerateLocationAndLoot))
            ?? throw new InvalidOperationException("Could not find LocationLifecycleService.GenerateLocationAndLoot!");
    }

    [PatchPostfix]
    public static void Postfix(MongoId sessionId, string name, bool generateLoot, LocationBase __result)
    {
        if (!generateLoot || name.Equals("hideout", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var raidConfig = _profileActivityService.GetProfileActivityRaidData(sessionId).RaidConfiguration;
        if (!(raidConfig?.IsNightRaid ?? false))
        {
            return;
        }

        // Explicit raid-wide difficulty choice overrides everything downstream anyway - don't bother.
        var dropdown = raidConfig.WavesSettings?.BotDifficulty?.ToString().ToLowerInvariant() ?? "asonline";
        if (dropdown != "asonline")
        {
            return;
        }

        if (__result.BossLocationSpawn is null || __result.BossLocationSpawn.Count == 0)
        {
            return;
        }

        foreach (var spawn in __result.BossLocationSpawn)
        {
            spawn.BossDifficulty = _weightedRandomHelper.GetWeightedValue(_configService.Config.NightBotDifficulty);
            spawn.BossEscortDifficulty = _weightedRandomHelper.GetWeightedValue(_configService.Config.NightBotDifficulty);
        }

        _logger.Info($"[NightLootMultiplier] {name}: night difficulty applied to {__result.BossLocationSpawn.Count} spawn entries");
    }
}
