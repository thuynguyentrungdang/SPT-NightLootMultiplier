using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace NightLootMultiplier.Services;

/// <summary>
/// Soft integration with LotsofLoot's Marked Room / Ref Room multipliers via reflection.
/// No compile-time reference to LotsofLoot.dll — safe to run standalone if it's not installed.
///
/// LotsofLoot's preset randomizer (RandomizePresetPatch, also prefixing GenerateLocationAndLoot)
/// replaces the whole LotsofLootPresetConfig object on a preset swap, it doesn't mutate it in
/// place. So we never cache the live Multiplier dictionaries — we re-resolve them every raid and
/// re-snapshot "base" whenever the preset object identity changes. Our own patch runs at
/// HarmonyPriority.Low so LotsofLoot's swap (Normal priority) always happens first in the same
/// call, and we always see the post-swap object.
/// </summary>
[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.Preload + 100)]
public class LotsofLootIntegrationService(IServiceProvider serviceProvider, ISptLogger<LotsofLootIntegrationService> logger) : IOnLoad
{
    private const string ConfigServiceTypeName = "LotsofLoot.Services.ConfigService";
    private const string PresetConfigPropertyName = "LotsofLootPresetConfig";
    private const string MultiplierPropertyName = "Multiplier";

    public bool IsActive { get; private set; }

    private object? _configServiceInstance;
    private PropertyInfo? _presetConfigProperty;

    private object? _lastPresetConfig;
    private Dictionary<string, double>? _markedRoomLive;
    private Dictionary<string, double>? _refRoomLive;
    private IReadOnlyDictionary<string, double> _markedRoomBase = new Dictionary<string, double>();
    private IReadOnlyDictionary<string, double> _refRoomBase = new Dictionary<string, double>();

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            ResolveConfigService();
        }
        catch (Exception ex)
        {
            logger.Warning($"[NightLootMultiplier] LotsofLoot integration failed to initialize, skipping: {ex.Message}");
            IsActive = false;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Applies base * factor to the live MarkedRoom/RefRoom multiplier dictionaries for the given
    /// map, if active. Re-resolves the current preset object every call and re-snapshots base
    /// whenever it changed since the last call (a preset swap happened this raid).
    /// </summary>
    public void TrySet(string mapName, double factor)
    {
        if (!IsActive)
        {
            return;
        }

        try
        {
            var presetConfig = _presetConfigProperty!.GetValue(_configServiceInstance);
            if (presetConfig is null)
            {
                return;
            }

            if (!ReferenceEquals(presetConfig, _lastPresetConfig))
            {
                RefreshFromPresetConfig(presetConfig);
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"[NightLootMultiplier] LotsofLoot integration broke at runtime, disabling: {ex.Message}");
            IsActive = false;
            return;
        }

        if (_markedRoomLive is not null && _markedRoomBase.TryGetValue(mapName, out var markedBase))
        {
            _markedRoomLive[mapName] = markedBase * factor;
        }

        if (_refRoomLive is not null && _refRoomBase.TryGetValue(mapName, out var refBase))
        {
            _refRoomLive[mapName] = refBase * factor;
        }
    }

    private void ResolveConfigService()
    {
        var lotsofLootAssembly = AppDomain
            .CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, "LotsofLoot", StringComparison.Ordinal));

        if (lotsofLootAssembly is null)
        {
            logger.Info("[NightLootMultiplier] LotsofLoot not detected, Marked/Ref room integration disabled");
            return;
        }

        var configServiceType = lotsofLootAssembly.GetType(ConfigServiceTypeName);
        if (configServiceType is null)
        {
            logger.Warning("[NightLootMultiplier] LotsofLoot detected but ConfigService type not found, skipping integration");
            return;
        }

        var configService = serviceProvider.GetService(configServiceType);
        if (configService is null)
        {
            logger.Warning("[NightLootMultiplier] LotsofLoot detected but ConfigService could not be resolved, skipping integration");
            return;
        }

        var presetConfigProperty = configServiceType.GetProperty(PresetConfigPropertyName, BindingFlags.Public | BindingFlags.Instance);
        if (presetConfigProperty is null)
        {
            logger.Warning("[NightLootMultiplier] LotsofLoot ConfigService.LotsofLootPresetConfig not found, skipping integration");
            return;
        }

        _configServiceInstance = configService;
        _presetConfigProperty = presetConfigProperty;
        IsActive = true;
        logger.Success("[NightLootMultiplier] LotsofLoot detected, Marked/Ref room integration active");
    }

    private void RefreshFromPresetConfig(object presetConfig)
    {
        _markedRoomLive = GetMultiplierDictionary(presetConfig, "MarkedRoomConfig");
        _refRoomLive = GetMultiplierDictionary(presetConfig, "RefRoomConfig");

        _markedRoomBase = _markedRoomLive is not null ? new Dictionary<string, double>(_markedRoomLive) : new Dictionary<string, double>();
        _refRoomBase = _refRoomLive is not null ? new Dictionary<string, double>(_refRoomLive) : new Dictionary<string, double>();

        _lastPresetConfig = presetConfig;
    }

    private static Dictionary<string, double>? GetMultiplierDictionary(object presetConfig, string roomConfigPropertyName)
    {
        var roomConfig = presetConfig.GetType().GetProperty(roomConfigPropertyName, BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(presetConfig);

        return roomConfig
                ?.GetType()
                .GetProperty(MultiplierPropertyName, BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(roomConfig) as Dictionary<string, double>;
    }
}
