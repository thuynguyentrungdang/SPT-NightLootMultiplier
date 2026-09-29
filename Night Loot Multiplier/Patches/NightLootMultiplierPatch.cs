using System.Reflection;
using HarmonyLib;
using NightLootMultiplier.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Services.InRaid;
using SPTarkov.Server.Core.Services.Profile;

namespace NightLootMultiplier.Patches;

[Injectable]
public sealed class NightLootMultiplierPatch : AbstractPatch
{
    private static ProfileActivityService _profileActivityService = default!;
    private static LocationConfig _locationConfig = default!;
    private static BaseMultiplierSnapshotService _baseSnapshot = default!;
    private static ConfigService _configService = default!;
    private static LotsofLootIntegrationService _lotsofLootIntegration = default!;
    private static ISptLogger<NightLootMultiplierPatch> _logger = default!;

    public NightLootMultiplierPatch(
        ProfileActivityService profileActivityService,
        LocationConfig locationConfig,
        BaseMultiplierSnapshotService baseSnapshot,
        ConfigService configService,
        LotsofLootIntegrationService lotsofLootIntegration,
        ISptLogger<NightLootMultiplierPatch> logger
    )
    {
        _profileActivityService = profileActivityService;
        _locationConfig = locationConfig;
        _baseSnapshot = baseSnapshot;
        _configService = configService;
        _lotsofLootIntegration = lotsofLootIntegration;
        _logger = logger;
    }

    protected override MethodBase GetTargetMethod()
    {
        return typeof(LocationLifecycleService).GetMethod(nameof(LocationLifecycleService.GenerateLocationAndLoot))
            ?? throw new InvalidOperationException("Could not find LocationLifecycleService.GenerateLocationAndLoot!");
    }

    [PatchPrefix]
    [HarmonyPriority(Priority.Low)]
    public static void Prefix(MongoId sessionId, string name, bool generateLoot)
    {
        if (!generateLoot || name.Equals("hideout", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var raidConfig = _profileActivityService.GetProfileActivityRaidData(sessionId)?.RaidConfiguration;
        var isNight = raidConfig?.IsNightRaid ?? false;
        var factor = isNight ? _configService.Config.NightMultiplier : _configService.Config.DayMultiplier;

        if (_baseSnapshot.LooseLootBase.TryGetValue(name, out var looseBase))
        {
            _locationConfig.LooseLootMultiplier[name] = looseBase * factor;
        }

        if (_baseSnapshot.StaticLootBase.TryGetValue(name, out var staticBase))
        {
            _locationConfig.StaticLootMultiplier[name] = staticBase * factor;
        }

        _lotsofLootIntegration.TrySet(name, factor);

        _logger.Info($"[NightLootMultiplier] {name}: isNight={isNight} factor={factor}");
    }
}
