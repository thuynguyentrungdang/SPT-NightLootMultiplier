using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace NightLootMultiplier.Services;

/// <summary>
/// Soft integration with LotsofLoot's Marked Room / Ref Room multipliers, and with LotsofLoot's own
/// per-map LooseLootMultiplier/StaticLootMultiplier, via reflection. No compile-time reference to
/// LotsofLoot.dll — safe to run standalone if it's not installed.
///
/// LotsofLoot's own patch (LootMultipliers.Apply, run via its OnPresetUpdate pipeline, including
/// from RandomizePresetPatch which also prefixes GenerateLocationAndLoot) writes its preset's
/// LooseLootMultiplier/StaticLootMultiplier straight into the SAME shared LocationConfig dicts our
/// patch writes into. If our patch only multiplied against our own boot-time snapshot, it would
/// silently stomp LotsofLoot's per-map value (including any mid-raid preset-randomizer swap)
/// instead of multiplying on top of it — so callers must prefer LotsofLoot's live base over ours.
///
/// LotsofLoot's preset randomizer replaces the whole LotsofLootPresetConfig object on a preset
/// swap, it doesn't mutate it in place. So we never cache the live Multiplier dictionaries — we
/// re-resolve them every raid and re-snapshot "base" whenever the preset object identity changes.
/// Our own patch runs at HarmonyPriority.Low so LotsofLoot's swap (Normal priority) always happens
/// first in the same call, and we always see the post-swap object.
/// </summary>
[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.Preload + 100)]
public class LotsofLootIntegrationService(IServiceProvider serviceProvider, ISptLogger<LotsofLootIntegrationService> logger) : IOnLoad
{
    private const string ConfigServiceTypeName = "LotsofLoot.Services.ConfigService";
    private const string PresetConfigPropertyName = "LotsofLootPresetConfig";
    private const string MultiplierPropertyName = "Multiplier";
    private const string LooseLootMultiplierPropertyName = "LooseLootMultiplier";
    private const string StaticLootMultiplierPropertyName = "StaticLootMultiplier";

    public bool IsActive { get; private set; }

    private object? _configServiceInstance;
    private PropertyInfo? _presetConfigProperty;

    private object? _lastPresetConfig;
    private Dictionary<string, double>? _markedRoomLive;
    private Dictionary<string, double>? _refRoomLive;
    private IReadOnlyDictionary<string, double> _markedRoomBase = new Dictionary<string, double>();
    private IReadOnlyDictionary<string, double> _refRoomBase = new Dictionary<string, double>();
    private IReadOnlyDictionary<string, double> _looseLootBase = new Dictionary<string, double>();
    private IReadOnlyDictionary<string, double> _staticLootBase = new Dictionary<string, double>();

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
        if (!EnsureFresh())
        {
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

    /// <summary>
    /// LotsofLoot's own per-map loose loot base multiplier from its active preset, if active and
    /// the map is present. Callers should prefer this over their own base snapshot, since
    /// LotsofLoot writes this same value straight into the shared LocationConfig dict.
    /// </summary>
    public bool TryGetLooseLootMultiplier(string mapName, out double multiplier)
    {
        if (EnsureFresh() && _looseLootBase.TryGetValue(mapName, out multiplier))
        {
            return true;
        }

        multiplier = default;
        return false;
    }

    /// <summary>
    /// LotsofLoot's own per-map static loot base multiplier from its active preset, if active and
    /// the map is present. Callers should prefer this over their own base snapshot, since
    /// LotsofLoot writes this same value straight into the shared LocationConfig dict.
    /// </summary>
    public bool TryGetStaticLootMultiplier(string mapName, out double multiplier)
    {
        if (EnsureFresh() && _staticLootBase.TryGetValue(mapName, out multiplier))
        {
            return true;
        }

        multiplier = default;
        return false;
    }

    private bool EnsureFresh()
    {
        if (!IsActive)
        {
            return false;
        }

        try
        {
            var presetConfig = _presetConfigProperty!.GetValue(_configServiceInstance);
            if (presetConfig is null)
            {
                return false;
            }

            if (!ReferenceEquals(presetConfig, _lastPresetConfig))
            {
                RefreshFromPresetConfig(presetConfig);
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.Warning($"[NightLootMultiplier] LotsofLoot integration broke at runtime, disabling: {ex.Message}");
            IsActive = false;
            return false;
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

        var looseLoot = GetTopLevelDictionary(presetConfig, LooseLootMultiplierPropertyName);
        var staticLoot = GetTopLevelDictionary(presetConfig, StaticLootMultiplierPropertyName);
        _looseLootBase = looseLoot is not null ? new Dictionary<string, double>(looseLoot) : new Dictionary<string, double>();
        _staticLootBase = staticLoot is not null ? new Dictionary<string, double>(staticLoot) : new Dictionary<string, double>();

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

    private static Dictionary<string, double>? GetTopLevelDictionary(object presetConfig, string propertyName)
    {
        return presetConfig.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(presetConfig) as Dictionary<string, double>;
    }
}
