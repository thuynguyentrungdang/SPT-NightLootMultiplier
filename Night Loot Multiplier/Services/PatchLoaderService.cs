using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;

namespace NightLootMultiplier.Services;

[Injectable(TypePriority = OnLoadOrder.Preload)]
public class PatchLoaderService(IEnumerable<IRuntimePatch> patches, ISptLogger<PatchLoaderService> logger) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        foreach (var patch in patches)
        {
            // One mod's broken patch must not stop every patch enumerated after it (including
            // ours) from ever being enabled - this loop has no such guard by default.
            try
            {
                patch.Enable();
            }
            catch (Exception ex)
            {
                logger.Error($"[NightLootMultiplier] Failed to enable patch {patch.GetType().FullName}: {ex.Message}");
            }
        }

        return Task.CompletedTask;
    }
}
