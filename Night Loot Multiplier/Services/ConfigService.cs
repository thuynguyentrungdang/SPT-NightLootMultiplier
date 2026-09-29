using NightLootMultiplier.Models;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;

namespace NightLootMultiplier.Services;

[Injectable(InjectionType.Singleton)]
public class ConfigService
{
    public NightLootConfig Config { get; }

    public ConfigService(ModHelper modHelper, ISptLogger<ConfigService> logger)
    {
        try
        {
            Config = modHelper.GetJsonDataFromModFile<NightLootConfig>("config", "config.json");
        }
        catch (Exception ex)
        {
            logger.Error("[NightLootMultiplier] Failed to load config.json, using defaults", ex);
            Config = new NightLootConfig();
        }
    }
}
