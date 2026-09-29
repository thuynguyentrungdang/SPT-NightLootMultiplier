namespace NightLootMultiplier.Models;

public class NightLootConfig
{
    public double NightMultiplier { get; set; } = 1.5;
    public double DayMultiplier { get; set; } = 1.0;

    public Dictionary<string, double> NightPmcDifficulty { get; set; } =
        new() { ["easy"] = 100, ["normal"] = 0, ["hard"] = 0, ["impossible"] = 0 };

    public Dictionary<string, double> NightBossDifficulty { get; set; } =
        new() { ["easy"] = 100, ["normal"] = 0, ["hard"] = 0, ["impossible"] = 0 };
}
