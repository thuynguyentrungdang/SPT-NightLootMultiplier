using SPTarkov.DI.Annotations;
using SPTarkov.Server.Web.Models.Configs;
using SPTarkov.Server.Web.Services;

namespace NightLootMultiplier.Services;

/// <summary>
/// Registers config.json with SIC's own built-in config editor (JSON + structured Controls tab,
/// diffing, presets) instead of building a custom Blazor settings page. SIC handles load/save/
/// apply-to-runtime via reflection - RuntimeConfig is the SAME live object the patches read from,
/// so edits apply immediately, no extra wiring needed.
/// </summary>
[Injectable]
public class NightLootConfigEditorProvider(ConfigService configService) : IConfigEditorConfigProvider
{
    public IEnumerable<ConfigEditorConfigRegistration> GetConfigs()
    {
        yield return ConfigEditorConfigRegistration.Create(
            "night-loot-multiplier",
            "Night Loot Multiplier",
            configService.Config,
            configService.ConfigFilePath
        );
    }
}
