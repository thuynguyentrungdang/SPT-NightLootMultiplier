using System.Reflection;
using NightLootMultiplier.Models;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;

namespace NightLootMultiplier.Services;

[Injectable(InjectionType.Singleton)]
public class ConfigService
{
    public NightLootConfig Config { get; }

    /// <summary>
    /// Absolute path to config.json - handed to the SIC config editor so it can load/save this
    /// config using its own built-in disk I/O, instead of the mod doing it itself.
    /// </summary>
    public string ConfigFilePath { get; }

    public ConfigService(ModHelper modHelper, ISptLogger<ConfigService> logger)
    {
        ConfigFilePath = Path.Combine(modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()), "config", "config.json");

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
