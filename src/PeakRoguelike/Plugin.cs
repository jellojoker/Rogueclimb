using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace PeakRoguelike;

[BepInPlugin(Id, Name, Version)]
public class Plugin : BaseUnityPlugin
{
    public const string Id = "rey.PeakRoguelike";
    public const string Name = "Rogue Climb";
    public const string Version = "0.12.53";

    internal static ManualLogSource Log = null!;
    internal static string ConfigPath = "";

    private void Awake()
    {
        Log = Logger;
        ConfigPath = Config.ConfigFilePath;
        ModConfig.Bind(Config);
        var harmony = new Harmony(Id);
        // One class at a time: a hook the game no longer has costs only that class, not the whole mod.
        foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
        {
            try { harmony.CreateClassProcessor(type).Patch(); }
            catch (System.Exception e) { Log.LogError($"Couldn't hook {type.Name}: {e.Message}"); }
        }
        SunCaps.Install(harmony);
        RunState.Install();
        Net.Install();
        SupplyDrop.Install();
        DropClock.Install();
        CurioRelics.Install();
        Badges.Install();
        VoidRun.Install();
        Hud.Install();
        ItemLevels.Install();
        Fusion.Install();
        Relics.Install();
        RelicShuffle.Install();
        ToolRelics.Install();
        Blessing.Install();
        Storage.Install();
        Stacks.Install();
        StackMerge.Install();
        Medicine.Install();
        ScoutmasterHunt.Install();
        Xp.Install();
        Luck.Install();
        ItemDump.Install();
        BloodMoon.Install();
        Suns.Install();
        MovementCaps.Install();
        Ambiance.Install();
        DevPanel.Install();
        Modless.Install();
        DeathDrops.Install();
        BugReports.Install();
        Log.LogInfo($"{Name} {Version} loaded - crates, item trees, fusions, relics and blood moons.");
    }
}
