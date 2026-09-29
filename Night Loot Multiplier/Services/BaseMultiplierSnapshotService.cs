using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Config;

namespace NightLootMultiplier.Services;

[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.Preload + 100)]
public class BaseMultiplierSnapshotService(LocationConfig locationConfig) : IOnLoad
{
    public IReadOnlyDictionary<string, double> LooseLootBase { get; private set; } = new Dictionary<string, double>();
    public IReadOnlyDictionary<string, double> StaticLootBase { get; private set; } = new Dictionary<string, double>();

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        LooseLootBase = new Dictionary<string, double>(locationConfig.LooseLootMultiplier);
        StaticLootBase = new Dictionary<string, double>(locationConfig.StaticLootMultiplier);

        return Task.CompletedTask;
    }
}
