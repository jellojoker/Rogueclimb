using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace PeakRoguelike;

[BepInPlugin(Id, Name, Version)]
public class Plugin : BaseUnityPlugin
{
    public const string Id = "rey.PeakRoguelike";
    public const string Name = "Rogue Climb";
    public const string Version = "1.0.0";
    /// <summary>Compat branch builds; empty on a release.</summary>
    public const string Build = "";

    internal static ManualLogSource Log = null!;
    internal static string ConfigPath = "";
    private static int failed;

    private void Awake()
    {
        Log = Logger;
        ConfigPath = Config.ConfigFilePath;
        ModConfig.Bind(Config);
        var harmony = new Harmony(Id);
        // One class at a time: a hook the game no longer has costs only that class, not the whole mod.
        int hooked = 0, classes = 0;
        foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
        {
            var proc = harmony.CreateClassProcessor(type);
            try { if (proc.Patch()?.Count > 0) { hooked++; classes++; } }
            catch (System.Exception e) { classes++; failed++; Log.LogError($"Couldn't hook {type.Name}: {e.Message}"); }
        }
        // Same for every system: one that fails to start leaves the rest running.
        Start("SunCaps", () => SunCaps.Install(harmony));
        Start("RunState", RunState.Install);
        Start("Net", Net.Install);
        Start("SupplyDrop", SupplyDrop.Install);
        Start("DropClock", DropClock.Install);
        Start("CurioRelics", CurioRelics.Install);
        Start("Badges", Badges.Install);
        Start("VoidRun", VoidRun.Install);
        Start("Hud", Hud.Install);
        Start("ItemLevels", ItemLevels.Install);
        Start("Fusion", Fusion.Install);
        Start("Relics", Relics.Install);
        Start("RelicShuffle", RelicShuffle.Install);
        Start("Tickets", Tickets.Install);
        Start("ToolRelics", ToolRelics.Install);
        Start("Blessing", Blessing.Install);
        Start("Storage", Storage.Install);
        Start("Stacks", Stacks.Install);
        Start("StackMerge", StackMerge.Install);
        Start("Medicine", Medicine.Install);
        Start("ScoutmasterHunt", ScoutmasterHunt.Install);
        Start("Xp", Xp.Install);
        Start("Luck", Luck.Install);
        Start("ItemDump", ItemDump.Install);
        Start("BloodMoon", BloodMoon.Install);
        Start("Suns", Suns.Install);
        Start("MovementCaps", MovementCaps.Install);
        Start("Ambiance", Ambiance.Install);
        Start("DevPanel", DevPanel.Install);
        Start("Modless", Modless.Install);
        Start("DeathDrops", DeathDrops.Install);
        Start("BugReports", BugReports.Install);
        Start("RunReport", RunReport.Install);
        string build = Build.Length > 0 ? $"-{Build}" : "";
        Log.LogInfo($"{Name} {Version}{build} loaded - {hooked}/{classes} hook classes, {failed} failed.");
    }

    private static void Start(string name, System.Action install)
    {
        try { install(); }
        catch (System.Exception e) { failed++; Log.LogError($"Couldn't start {name}: {e}"); }
    }
}
