using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace PeakRoguelike;

/// <summary>
/// Everything tunable lives here (BepInEx/config/rey.PeakRoguelike.cfg).
/// Each big system has an on/off switch so a broken one can be disabled while playtesting.
/// </summary>
internal static class ModConfig
{
    // --- systems -------------------------------------------------------------------------
    public static ConfigEntry<bool> Leveling = null!;
    public static ConfigEntry<bool> ItemTrees = null!;
    public static ConfigEntry<bool> Fusion = null!;
    public static ConfigEntry<bool> Relics = null!;

    // --- fusion --------------------------------------------------------------------------
    public static ConfigEntry<Key> FusionKey = null!;
    public static ConfigEntry<Key> DevPanelKey = null!;
    public static ConfigEntry<float> SugarRushSeconds = null!;

    // --- relics --------------------------------------------------------------------------
    public static ConfigEntry<int> GlassPerTrigger = null!;
    public static ConfigEntry<float> RelicCooldown = null!, RelicVeil = null!, DaggerStackCooldown = null!;
    public static ConfigEntry<float> JetpackFuel = null!, FaerieBoost = null!, ShoreOtherItems = null!, StatueIngredientChance = null!, StatueFusionChance = null!;
    public static ConfigEntry<int> ChainLauncherUses = null!, RescueClawUses = null!, AntiGravUses = null!;
    public static ConfigEntry<bool> EqualTreeItems = null!, FusionCards = null!, CursedCopies = null!;
    public static ConfigEntry<float> SoloSprint = null!, SoloWinded = null!;
    public static ConfigEntry<float> AuraRadius = null!;

    // --- item trees ----------------------------------------------------------------------
    public static ConfigEntry<float> TreePower = null!;
    public static ConfigEntry<bool> TintItems = null!;

    // --- loot ----------------------------------------------------------------------------
    public static ConfigEntry<bool> RarityCurve = null!;

    // --- blessings / relic risks -------------------------------------------------------------
    public static ConfigEntry<bool> Blessings = null!;
    public static ConfigEntry<float> HuntSeconds = null!;
    public static ConfigEntry<bool> RelicDrops = null!;

    // --- blood moon ----------------------------------------------------------------------
    public static ConfigEntry<bool> BloodMoon = null!, BmDarkRites = null!, BmWarning = null!;
    public static ConfigEntry<bool> BmScoutmaster = null!, BmZombies = null!, BmTornado = null!, BmGlassMeteors = null!, BmMeteors = null!;
    public static ConfigEntry<int> BmZombieCount = null!;
    public static ConfigEntry<float> BmMeteorRate = null!;
    public static ConfigEntry<float> BmTint = null!, BmMoonLight = null!;
    public static ConfigEntry<float> BmMusicVolume = null!;
    public static ConfigEntry<bool> BmMusicLoop = null!;
    public static ConfigEntry<float> BmScoutmasterNear = null!, BmScoutmasterStuck = null!, BmScoutmasterStuckDistance = null!, BmScoutmasterRecover = null!;
    public static ConfigEntry<float> BmZombieSafe = null!;
    public static ConfigEntry<float> BmWaveGap = null!, BmWaveLength = null!, BmRelicAura = null!,
        BmScoutmasterSprint = null!, BmScoutmasterWinded = null!, BmTornadoGrouped = null!;
    public static ConfigEntry<float> BmScoutmasterSpeed = null!, BmTornadoApproach = null!, BmTornadoChase = null!, BmTornadoNear = null!;

    // --- suns ----------------------------------------------------------------------------
    public static ConfigEntry<bool> Suns = null!, SunBlueWear = null!, SunBlueBreaks = null!;
    public static ConfigEntry<float> SunSky = null!, SunYellowDrain = null!, SunYellowRegen = null!, SunGreenHunger = null!,
        SunRedJump = null!, GenerosityShare = null!,
        SunRedGravity = null!, SunRedSprintCut = null!, SunYellowStumble = null!, SunGreenRainEvery = null!,
        SunBlueFreezeSeconds = null!, SunSkyBlend = null!, SunWash = null!,
        SunGreenShowerSeconds = null!, SunGreenFoodLife = null!, SunFadeSeconds = null!, MaxedTreeWeight = null!, SunscreenWeight = null!,
        SunBlueHealCap = null!, SunYellowRushCap = null!, SunGreenFoodCap = null!, SunRedMoveCap = null!;
    public static ConfigEntry<bool> SafeZones = null!;
    public static ConfigEntry<float> SummitRadius = null!, SpawnRadius = null!;
    public static ConfigEntry<int> SunGreenRainCount = null!;

    // --- storage -------------------------------------------------------------------------
    public static ConfigEntry<bool> Storage = null!;
    public static ConfigEntry<int> MaxPockets = null!, BackpackPockets = null!, FannypackPockets = null!;

    // --- ambiance ------------------------------------------------------------------------
    public static ConfigEntry<float> MenuFilter = null!, MenuSky = null!, MenuLogoGlow = null!, MenuLogoTint = null!, MenuLight = null!,
        AirportSky = null!, AirportLight = null!;
    public static ConfigEntry<bool> MenuMusic = null!;

    // --- dev -----------------------------------------------------------------------------
    public static ConfigEntry<bool> DevTools = null!;
    public static ConfigEntry<Key> FlyKey = null!;
    public static ConfigEntry<float> FlySpeed = null!;

    public static void Bind(ConfigFile cfg)
    {
        BloodMoon = cfg.Bind("BloodMoon", "Enabled", true, "Binding a relic, and every nightfall while the squad holds one, raises a blood moon: one wave per relic held, each arriving WaveGap after the last.");
        BmDarkRites = cfg.Bind("BloodMoon", "DarkRites", true, "Using the Cursed Skull or the Ritual Dagger raises a blood moon at once (by day: a blood eclipse), at least tier 1.");
        BmWarning = cfg.Bind("BloodMoon", "RelicWarning", true, "The relic screen carries a red warning line.");
        BmWaveGap = cfg.Bind("BloodMoon", "WaveGap", 20f, "Seconds between blood moon waves (Scoutmaster, zombies, tornado, meteors). A moon lasts (tier-1) x gap + WaveLength.");
        BmWaveLength = cfg.Bind("BloodMoon", "WaveLength", 60f, "How long each blood moon wave lasts (s).");
        BmRelicAura = cfg.Bind("BloodMoon", "RelicAura", 24f, "The Scoutmaster and the tornado go after relic holders and anyone within this many metres of one (24 = vanilla's buddy distance). A holder with someone this close is 'grouped'.");
        BmScoutmaster = cfg.Bind("BloodMoon", "Scoutmaster", true, "1 relic: the Scoutmaster hunts relic holders.");
        BmZombies = cfg.Bind("BloodMoon", "Zombies", true, "2 relics: zombies rise above and below the squad.");
        BmZombieCount = cfg.Bind("BloodMoon", "ZombieCount", 10, "Zombies per blood moon (half at the campfire ahead - the summit on the Peak - half below the squad).");
        BmZombieSafe = cfg.Bind("BloodMoon", "ZombieSafeDistance", 50f, "No blood moon zombie rises closer than this (m) to any scout.");
        BmTornado = cfg.Bind("BloodMoon", "Tornado", true, "3 relics: a tornado that heads for relic holders (sand on Mesa, snow on Alpine).");
        BmGlassMeteors = cfg.Bind("BloodMoon", "GlassMeteors", true, "4 relics, Gloom runs (Gloom + Citadel): a shower of purple glass - a direct hit adds glass.");
        BmMeteors = cfg.Bind("BloodMoon", "Meteors", true, "4 relics, Caldera runs: a meteor shower.");
        BmMeteorRate = cfg.Bind("BloodMoon", "MeteorRate", 1f, "Meteors per second around each scout.");
        BmTint = cfg.Bind("BloodMoon", "ScreenTint", 0.16f, "Strength of the red screen filter (0 = off).");
        BmMoonLight = cfg.Bind("BloodMoon", "MoonLight", 1.1f, "Brightness of the red moonlight.");
        BmMusicVolume = cfg.Bind("BloodMoon", "MusicVolume", 0.3f, "Volume of BloodMoon.ogg/.wav/.mp3 placed next to the mod DLL (no file = no music).");
        BmMusicLoop = cfg.Bind("BloodMoon", "MusicLoop", true, "Loop the blood moon music for the whole blood moon (off = play once).");
        // New key so the old 1.25 doesn't stick around in existing configs.
        BmScoutmasterSpeed = cfg.Bind("BloodMoon", "ScoutmasterHuntSpeed", 1f, "How much faster the Scoutmaster moves while a blood moon hunt is on (1 = vanilla).");
        BmScoutmasterNear = cfg.Bind("BloodMoon", "ScoutmasterReturnDistance", 50f, "During a hunt the Scoutmaster appears about this far (m) from his prey, out of sight (vanilla: 50-70m).");
        BmScoutmasterStuck = cfg.Bind("BloodMoon", "ScoutmasterStuckSeconds", 10f, "If he hasn't moved or gained ground for this long during a hunt, he reappears out of sight near his prey (0 = off).");
        BmScoutmasterStuckDistance = cfg.Bind("BloodMoon", "ScoutmasterStuckDistance", 40f, "How far (m) from his prey he reappears after getting stuck.");
        BmScoutmasterRecover = cfg.Bind("BloodMoon", "ScoutmasterRecover", 5f, "During a hunt, after a throw or a missed lunge he stands still this many seconds, then locks onto his next target (0 = vanilla's 2s after throws only).");
        BmScoutmasterSprint = cfg.Bind("BloodMoon", "ScoutmasterSprintSeconds", 4f, "Hunting a relic holder who is alone, he sprints in bursts this long...");
        BmScoutmasterWinded = cfg.Bind("BloodMoon", "ScoutmasterWindedSeconds", 3f, "...then walks this long before sprinting again (0 = sprints like vanilla). A grouped holder only ever sees him walk.");
        BmTornadoGrouped = cfg.Bind("BloodMoon", "TornadoGroupedSpeed", 0.5f, "The tornado's speed when the relic holder nearest it is grouped (share of its normal speed).");
        BmTornadoApproach = cfg.Bind("BloodMoon", "TornadoApproachSpeed", 14f, "The tornado's speed (m/s) while it travels up from the section's starting campfire.");
        BmTornadoChase = cfg.Bind("BloodMoon", "TornadoChaseSpeed", 0.6f, "The tornado's chase speed once close, as a share of its old speed (1 = v0.6 speed).");
        BmTornadoNear = cfg.Bind("BloodMoon", "TornadoChaseDistance", 60f, "Distance (m) from the nearest scout at which the tornado slows to its chase speed.");

        Suns = cfg.Bind("Suns", "Enabled", true, "Coloured suns: each biome's days burn the colour of its statue amulet until the squad holds that relic; blood moon nights only after it.");
        SunSky = cfg.Bind("Suns", "SkyTint", 1f, "How strongly the sky and sunlight take the sun's colour (0 = vanilla sky, effects still apply).");
        SunYellowDrain = cfg.Bind("Suns", "YellowStaminaDrain", 2f, "Yellow sun: stamina use multiplier.");
        SunYellowRegen = cfg.Bind("Suns", "YellowStaminaRegen", 0.4f, "Yellow sun: stamina regeneration multiplier.");
        SunGreenFoodCap = cfg.Bind("Suns", "GreenFoodCap", 0.1f, "Green sun: food works at this fraction (hunger, stamina, buffs).");
        SunBlueHealCap = cfg.Bind("Suns", "BlueHealingCap", 0.1f, "Blue sun: healing items heal this fraction (lantern and relics never capped).");
        SunYellowRushCap = cfg.Bind("Suns", "YellowRushCap", 0.1f, "Yellow sun: infinite stamina from items lasts this fraction as long.");
        SunRedMoveCap = cfg.Bind("Suns", "RedMovementCap", 0.1f, "Red sun: glider, jetpack, rocket pack, balloons, scout cannon and anti-grav work at this fraction (ropes / pitons untouched).");
        SunSkyBlend = cfg.Bind("Suns", "SkyBlend", 0.85f, "How far the sky leans into the sun's colour (0..1).");
        SunWash = cfg.Bind("Suns", "ScreenWash", 0.08f, "Faint full-screen wash of the sun's colour (0 = off).");
        SafeZones = cfg.Bind("Suns", "SafeZones", true, "Lit campfires and the summit are safe: no sun effects, no blood moon attacks, music fades. The sky keeps its colour.");
        SpawnRadius = cfg.Bind("Suns", "SpawnSafeRadius", 30f, "Safe radius (m) around the plane crash where the run starts.");
        SummitRadius = cfg.Bind("Suns", "SummitSafeRadius", 25f, "Safe radius (m) around the summit.");
        SunGreenHunger = cfg.Bind("Suns", "GreenHunger", 3f, "Green sun: hunger rate multiplier.");
        SunYellowStumble = cfg.Bind("Suns", "YellowStumbleSeconds", 1f, "Yellow sun: running dry on the ground trips you for this long (0 = off).");
        SunGreenRainEvery = cfg.Bind("Suns", "GreenFoodRainEvery", 50f, "Green sun: seconds between food storms (wild food of every biome so far; 0 = off).");
        SunGreenRainCount = cfg.Bind("Suns", "GreenFoodPerStorm", 20, "Green sun: pieces of food that fall around each scout per storm.");
        SunGreenShowerSeconds = cfg.Bind("Suns", "GreenStormSeconds", 5f, "Green sun: how long each food storm lasts.");
        SunGreenFoodLife = cfg.Bind("Suns", "GreenFoodLifetime", 40f, "Green sun: rained food left on the ground vanishes after this many seconds (pocket it to keep it).");
        SunFadeSeconds = cfg.Bind("Suns", "FadeSeconds", 5f, "Seconds for a sun's colour to fade in or out.");
        SunRedJump = cfg.Bind("Suns", "RedJumpLaunch", 0.7f, "Red sun: jump launch-height multiplier (on top of the heavier gravity).");
        SunRedGravity = cfg.Bind("Suns", "RedGravity", 1.6f, "Red sun: gravity multiplier on scouts - lower, shorter jumps, faster falls.");
        SunRedSprintCut = cfg.Bind("Suns", "RedSprintCut", 0.6f, "Red sun: share of the sprint speed bonus taken away (0 = vanilla sprint, 1 = sprinting is no faster than walking). Walking and climbing untouched.");
        SunBlueFreezeSeconds = cfg.Bind("Suns", "BlueShoreFreezeSeconds", 120f, "Blue sun on the Shore: seconds of direct sunlight to fill an empty bar with cold (fades like heat in shade; lanterns / campfires protect; 0 = off).");
        SunBlueWear = cfg.Bind("Suns", "BlueItemWear", true, "Blue sun: limited-use items spend two uses at a time.");
        SunBlueBreaks = cfg.Bind("Suns", "BlueBreaks", true, "Blue sun: every rope frays (snaps after a climb) and every piton cracks under you.");
        GenerosityShare = cfg.Bind("Relics", "GenerosityMealShare", 0.5f, "Generosity: scouts near you get this share of the hunger your meals relieve (0 = off).");

        Storage = cfg.Bind("Storage", "Enabled", true, "Picking up a backpack or fanny pack offers to absorb it for extra pockets (lost on death). Off = vanilla packs.");
        MaxPockets = cfg.Bind("Storage", "MaxPockets", 11, new ConfigDescription("Most pockets a scout can have (vanilla 3; keys 5-9, 0, -, = reach pockets 4-11).", new AcceptableValueRange<int>(3, 11)));
        BackpackPockets = cfg.Bind("Storage", "BackpackPockets", 2, "Pockets a backpack adds.");
        FannypackPockets = cfg.Bind("Storage", "FannypackPockets", 1, "Pockets a fanny pack adds.");

        MenuMusic = cfg.Bind("Ambiance", "MenuMusic", true, "Play the blood moon song on the main menu instead of the game's menu music.");
        MenuSky = cfg.Bind("Ambiance", "MenuSky", 0.85f, "How blood red the main menu's sky is, with a red moon in it (0 = vanilla).");
        MenuFilter = cfg.Bind("Ambiance", "MenuFilter", 0f, "Red screen filter over the main menu (0 = off).");
        MenuLogoGlow = cfg.Bind("Ambiance", "MenuLogoHalo", 0f, "Experimental red halo behind the PEAK logo (0 = off; looked like a box in testing).");
        MenuLogoTint = cfg.Bind("Ambiance", "MenuLogoTint", 0.45f, "How red the PEAK logo itself is tinted (0 = vanilla colours, 1 = fully red).");
        MenuLight = cfg.Bind("Ambiance", "MenuLight", 0.5f, "Red sunlight on the main menu - reddens the clouds and the scene, not just the sky (0 = vanilla).");
        AirportSky = cfg.Bind("Ambiance", "AirportSky", 0.85f, "How blood red the sky outside the airport is, with a red moon in it (0 = vanilla).");
        AirportLight = cfg.Bind("Ambiance", "AirportLight", 0.35f, "Red sunlight at the airport - reddens the clouds and the view outside (0 = vanilla).");
        DevTools = cfg.Bind("Dev", "DevTools", false, "Host-only cheats in the F8 panel (fly, god mode, item levels, time, blood moons). Off = the panel only shows builds.");
        FlyKey = cfg.Bind("Dev", "FlyKey", Key.F9, "Toggle noclip flight (host, dev tools on).");
        FlySpeed = cfg.Bind("Dev", "FlySpeed", 14f, "Flight speed in m/s (Shift = 3x).");

        Leveling = cfg.Bind("Systems", "Leveling", true, "Crate cards level up item trees. Off = v0.1 behaviour (pick any item, no levels).");
        ItemTrees = cfg.Bind("Systems", "ItemTrees", true, "Item levels change how items behave (rope length, glider drain, ...). Off = items behave like vanilla.");
        Fusion = cfg.Bind("Systems", "Fusion", true, "Offer fusion recipes when you carry the ingredients.");
        Relics = cfg.Bind("Systems", "Relics", true, "Picking up a statue amulet offers a permanent relic instead.");

        FusionKey = cfg.Bind("Fusion", "OpenFusionKey", Key.J, "Re-open the fusion screen after choosing 'Not now'.");
        DevPanelKey = cfg.Bind("Dev", "BuildPanelKey", Key.F8, "Show which build of the mod every player in the lobby is running.");
        SugarRushSeconds = cfg.Bind("Fusion", "SugarRushShieldSeconds", 8f, "Sugar-Rush Cocktail: how long its milk shield and infinite stamina last before the healing crash (the energy drink and lollipop inside it run their own vanilla times).");
        JetpackFuel = cfg.Bind("Fusion", "JetpackFuel", 1.5f, "Fused Jetpack: fuel multiplier (its twist).");
        FaerieBoost = cfg.Bind("Fusion", "FaerieLanternBoost", 1.5f, "Fused Faerie Lantern: range, healing and burn time multiplier (its twist).");
        ChainLauncherUses = cfg.Bind("Fusion", "ChainLauncherUses", 2, "Fused Chain Launcher: uses (vanilla 1).");
        RescueClawUses = cfg.Bind("Fusion", "RescueClawUses", 6, "Fused Rescue Claw: uses (vanilla 3).");
        AntiGravUses = cfg.Bind("Fusion", "AntiGravBazookaUses", 6, "Fused Anti-Grav Bazooka: uses (vanilla 3).");
        FusionCards = cfg.Bind("Fusion", "UnlockedFusionCards", true, "Once you've made a fusion, crates that roll one of its ingredients also offer the fused item as an extra card.");
        StatueIngredientChance = cfg.Bind("Fusion", "StatueIngredientChance", 0.1f, "Blessing statues / Ancient Luggage: chance the +2 card becomes the one ingredient a maxed item of yours is missing.");
        StatueFusionChance = cfg.Bind("Fusion", "StatueFusionChance", 0.01f, "Blessing statues / Ancient Luggage: chance the +2 card becomes a random finished fusion item.");
        EqualTreeItems = cfg.Bind("Loot", "EqualTreeItems", true, "Every tree's base item is equally common in every crate (Glider and Lantern join every crate); higher tree items never roll.");
        ShoreOtherItems = cfg.Bind("Loot", "ShoreOtherItems", 0.1f, "On the Shore, crate items outside the trees are this much as likely (1 = no change).");
        CursedCopies = cfg.Bind("Relics", "CursedCopies", true, "Cursed Skull: everyone else gets cursed copies of the user's relics. Ritual Dagger: the killer copies or stacks one relic. Copies shatter on death and don't raise the blood moon.");
        DaggerStackCooldown = cfg.Bind("Relics", "DaggerStackCooldown", 0.6f, "Each dagger stack multiplies that relic's cooldown by this (0.6 = 40% shorter per stack).");
        GlassPerTrigger = cfg.Bind("Relics", "GlassPerTrigger", 3, "Petrification ('glass', 0-100) added every time a relic triggers. Half the rate of a vanilla amulet in use.");
        RelicCooldown = cfg.Bind("Relics", "TriggerCooldown", 2f, "Seconds before the same relic can fire on the same scout again (each dagger stack shortens it, down to 0.5s).");
        SunscreenWeight = cfg.Bind("Loot", "SunscreenWeight", 0.25f, "Crates outside the Mesa: Sunscreen's weight as a share of one tree item's (then scaled by the rarity curve - rarer early, less rare late).");
        MaxedTreeWeight = cfg.Bind("Loot", "MaxedTreeWeight", 0.25f, "Crates: a tree the rolling scout has maxed comes up this much as often (1 = no change).");
        AuraRadius = cfg.Bind("Relics", "GenerosityMealRadius", 12f, "Range (m) of the Generosity relic's meal sharing.");
        RelicVeil = cfg.Bind("Relics", "RelicVeilSeconds", 5f, "Under a blood moon, a relic firing hides you from the Scoutmaster, zombies and tornado for this long (meteors still fall; 0 = off).");

        SoloSprint = cfg.Bind("Solo", "ScoutmasterSprintSeconds", 5f, "Solo: length of his sprint bursts.");
        SoloWinded = cfg.Bind("Solo", "ScoutmasterWindedSeconds", 2f, "Solo: how long he walks between bursts (0 = sprints like vanilla).");

        TreePower = cfg.Bind("ItemTrees", "PowerScale", 1f, "Scales how far below vanilla low-level items start (1 = as designed, 0 = no penalty).");
        TintItems = cfg.Bind("ItemTrees", "TintItems", true, "Colour items by their level.");

        RarityCurve = cfg.Bind("Loot", "RarityCurve", true, "Early biomes roll mostly common items; rarer items get more likely as the run climbs.");

        Blessings = cfg.Bind("Systems", "Blessings", true, "Revive statues bless every scout (+2 levels or a fusion) when nobody needs reviving. Ancient Luggage blesses whoever opens it.");
        HuntSeconds = cfg.Bind("Relics", "ScoutmasterHuntSeconds", 60f, "Only with blood moons off: binding a relic sets the Scoutmaster hunting everyone for this long (0 = off).");
        RelicDrops = cfg.Bind("Relics", "DropOnDeath", true, "Dying drops your relics as amulets; whoever picks one up gets that exact relic.");
    }
}
