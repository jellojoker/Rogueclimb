using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace PeakRoguelike;

[BepInPlugin(Id, Name, Version)]
public class Plugin : BaseUnityPlugin
{
    public const string Id = "rey.PeakRoguelike";
    public const string Name = "Rogue Climb";
    public const string Version = "0.10.7";

    internal static ManualLogSource Log = null!;

    private void Awake()
    {
        Log = Logger;
        ModConfig.Bind(Config);
        var harmony = new Harmony(Id);
        harmony.PatchAll(typeof(Plugin).Assembly);
        SunCaps.Install(harmony);
        RunState.Install();
        Net.Install();
        Hud.Install();
        ItemLevels.Install();
        Fusion.Install();
        Relics.Install();
        ToolRelics.Install();
        Blessing.Install();
        Storage.Install();
        ScoutmasterHunt.Install();
        ItemDump.Install();
        BloodMoon.Install();
        Suns.Install();
        MovementCaps.Install();
        Ambiance.Install();
        DevPanel.Install();
        Log.LogInfo($"{Name} {Version} loaded - crates, item trees, fusions, relics and blood moons.");
    }
}
