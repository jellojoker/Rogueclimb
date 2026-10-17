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
    public static ConfigEntry<float> MedicineRadius = null!, HolyShieldSeconds = null!, PassOutKitCooldown = null!,
        AntidoteCleanse = null!, AntidoteWeight = null!, BookOfBonesWeight = null!;
    public static ConfigEntry<bool> EqualTreeItems = null!, FusionCards = null!, CursedCopies = null!;
    public static ConfigEntry<float> AuraRadius = null!;

    // --- item trees ----------------------------------------------------------------------
    public static ConfigEntry<float> TreePower = null!, BalloonAirControl = null!, BalloonPopJump = null!;
    public static ConfigEntry<string> BalloonFlightSeconds = null!;
    public static ConfigEntry<bool> TintItems = null!;

    // --- loot ----------------------------------------------------------------------------
    public static ConfigEntry<bool> RarityCurve = null!;

    // --- blessings / relic risks -------------------------------------------------------------
    public static ConfigEntry<bool> Blessings = null!;
    public static ConfigEntry<float> HuntSeconds = null!;
    public static ConfigEntry<bool> RelicDrops = null!;
    internal static ConfigEntry<StatueOrder> StatueOrder = null!;

    // --- blood moon ----------------------------------------------------------------------
    public static ConfigEntry<bool> BloodMoon = null!, BmDarkRites = null!, BmWarning = null!;
    public static ConfigEntry<bool> BmScoutmaster = null!, BmZombies = null!, BmTornado = null!, BmGlassMeteors = null!, BmMeteors = null!;
    public static ConfigEntry<float> BmMeteorRate = null!;
    public static ConfigEntry<float> BmMoonLight = null!;
    public static ConfigEntry<float> BmMusicVolume = null!;
    public static ConfigEntry<bool> BmMusicLoop = null!;
    public static ConfigEntry<float> BmScoutmasterSpawnMin = null!, BmScoutmasterSpawnMax = null!, BmScoutmasterSpawnHeight = null!,
        BmScoutmasterStuck = null!, BmScoutmasterRecover = null!;
    public static ConfigEntry<float> BmZombieNear = null!;
    public static ConfigEntry<int> BmZombiesPerScout = null!, BmZombiesPerCarrier = null!, BmGrabsTier1 = null!, BmGrabsTier2 = null!;
    public static ConfigEntry<float> BmWaveGap = null!, BmWaveLength = null!, BmRelicAura = null!, BmTornadoGrouped = null!;
    public static ConfigEntry<float> BmScoutmasterSpeed = null!, BmTornadoApproach = null!, BmTornadoChase = null!, BmTornadoNear = null!;

    // --- suns ----------------------------------------------------------------------------
    public static ConfigEntry<bool> Suns = null!, SunFlareTint = null!, SunBlueFreeze = null!, SunBlueWear = null!, SunBlueBreaks = null!;
    public static ConfigEntry<float> SunSky = null!, SunYellowDrain = null!, SunGreenHunger = null!,
        SunRedJump = null!, GenerosityShare = null!,
        SunRedGravity = null!, SunRedSprintCut = null!, SunRedLungeCut = null!, SunYellowStumble = null!, SunGreenRainEvery = null!,
        SunSkyBlend = null!, SunLight = null!, SunGreenBite = null!,
        SunGreenShowerSeconds = null!, SunGreenFoodLife = null!, SunFadeSeconds = null!, MaxedTreeWeight = null!, SunscreenWeight = null!,
        SunBlueHealCap = null!, SunYellowRushCap = null!, SunGreenFoodCap = null!, SunRedMoveCap = null!;
    public static ConfigEntry<bool> SafeZones = null!, SunRedHeavyShoreOnly = null!;
    public static ConfigEntry<float> SummitRadius = null!, SpawnRadius = null!;
    public static ConfigEntry<int> SunGreenRainCount = null!;

    // --- storage -------------------------------------------------------------------------
    public static ConfigEntry<bool> Storage = null!, Stacking = null!, StackByLevel = null!, StackCrateOnly = null!, SunGreenNoStack = null!;
    public static ConfigEntry<int> SunGreenShoreWeight = null!;
    public static ConfigEntry<int> MaxPockets = null!, BackpackPockets = null!, FannypackPockets = null!;

    // --- ambiance ------------------------------------------------------------------------
    public static ConfigEntry<float> MenuFilter = null!, MenuSky = null!, MenuLogoGlow = null!, MenuLogoTint = null!, MenuLight = null!,
        AirportSky = null!, AirportLight = null!;
    public static ConfigEntry<bool> MenuMusic = null!;

    // --- dev -----------------------------------------------------------------------------
    public static ConfigEntry<bool> DevTools = null!;
    public static ConfigEntry<bool> DevShareRelics = null!, DevMoonEveryNight = null!, DevMoonOnPickup = null!;
    public static ConfigEntry<float> DevShareWait = null!;
    public static ConfigEntry<float> BmScoutmasterGrabGrace = null!, BmScoutmasterStare = null!, BmScoutmasterSprintDistance = null!, BmGrabPause = null!, BmGrabDelay = null!;
    public static ConfigEntry<float> BmScoutmasterFarDistance = null!, BmScoutmasterFarTime = null!, BmScoutmasterUnseen = null!, BmScoutmasterThrowForce = null!,
        BmHunterAppearMin = null!, BmHunterAppearMax = null!, BmNoticeWait = null!, BmHunterGrace = null!, BmHunterRecover = null!,
        BmWatchMin = null!, BmWatchMax = null!, BmStareLunge = null!, BmStareAngle = null!, BmStareStatic = null!, BmLungeTime = null!, SunYellowDrowsy = null!, SunBlueDamage = null!;
    public static ConfigEntry<bool> Xp = null!;
    public static ConfigEntry<int> XpPerCrate = null!, XpHealingUse = null!, XpFoodUse = null!, XpItemUse = null!, XpCampfire = null!;
    public static ConfigEntry<float> XpCrateLevels = null!, XpItemLevels = null!, XpMinClimb = null!;
    public static ConfigEntry<float> XpBiomeMinutes = null!, XpClimbLevels = null!, XpFastClearLevels = null!, XpBarScale = null!, XpBarX = null!, XpBarY = null!, XpKilnClimb = null!,
        XpLandingXp = null!, XpLandingSpacing = null!, XpLandingCap = null!, XpLandingFloor = null!;
    public static ConfigEntry<int> XpLevelCards = null!;
    public static ConfigEntry<float> LuckPullEarly = null!, LuckPullLate = null!, LuckStatueIngredientLate = null!, LuckStatueFusionLate = null!, LuckFusionChance = null!;
    public static ConfigEntry<int> LuckLateFrom = null!;
    public static ConfigEntry<bool> LuckGuarantee = null!;
    public static ConfigEntry<Key> FlyKey = null!;
    public static ConfigEntry<float> FlySpeed = null!;

    public static void Bind(ConfigFile cfg)
    {
        BloodMoon = cfg.Bind("BloodMoon", "Enabled", true, "Binding a relic, and every nightfall while the squad holds one, raises a blood moon: one wave per relic held, all rising together and lasting the whole moon.");
        BmDarkRites = cfg.Bind("BloodMoon", "DarkRites", true, "Using the Cursed Skull or the Ritual Dagger raises a blood moon at once (by day: a blood eclipse), at least tier 1.");
        BmWarning = cfg.Bind("BloodMoon", "RelicWarning", true, "The relic screen carries a red warning line.");
        BmWaveGap = cfg.Bind("BloodMoon", "WaveGap", 20f, "Seconds each tier above 1 adds to a blood moon. A moon lasts (tier-1) x WaveGap + WaveLength; every wave runs the whole moon.");
        BmWaveLength = cfg.Bind("BloodMoon", "WaveLength", 60f, "How long a tier 1 blood moon lasts (s).");
        BmRelicAura = cfg.Bind("BloodMoon", "RelicAura", 24f, "The Scoutmaster and the tornado go after relic holders and anyone within this many metres of one (24 = vanilla's buddy distance). A holder with someone this close is 'grouped' (the tornado slows).");
        BmScoutmaster = cfg.Bind("BloodMoon", "Scoutmaster", true, "1 relic: the Scoutmaster hunts relic holders.");
        BmZombies = cfg.Bind("BloodMoon", "Zombies", true, "2 relics: zombies rise beside every scout carrying a relic, the Cursed Skull or the Ritual Dagger.");
        BmZombieNear = cfg.Bind("BloodMoon", "ZombieDistance", 4f, "How far (m, +-25%) from its scout each blood moon zombie rises.");
        BmZombiesPerScout = cfg.Bind("BloodMoon", "ZombiesPerScout", 1, "How many zombies rise beside every scout in the open, from tier 1 (spread evenly around them).");
        BmZombiesPerCarrier = cfg.Bind("BloodMoon", "ZombiesPerCarrier", 1, "From tier 2: this many more beside each scout carrying a relic, the Cursed Skull or the Ritual Dagger.");
        BmGrabsTier1 = cfg.Bind("BloodMoon", "GrabsPerScoutTier1", 1, "Tier 1: grabs per scout per moon before the Scoutmaster only watches them.");
        BmGrabsTier2 = cfg.Bind("BloodMoon", "GrabsPerScoutTier2", 2, "Tier 2: the same (from tier 3 he only watches).");
        BmTornado = cfg.Bind("BloodMoon", "Tornado", true, "3 relics: a tornado that heads for relic holders (sand on Mesa, snow on Alpine).");
        BmGlassMeteors = cfg.Bind("BloodMoon", "GlassMeteors", true, "4 relics, Gloom runs (Gloom + Citadel): a shower of purple glass - a direct hit adds glass.");
        BmMeteors = cfg.Bind("BloodMoon", "Meteors", true, "4 relics, Caldera runs: a meteor shower.");
        BmMeteorRate = cfg.Bind("BloodMoon", "MeteorRate", 1f, "Meteors per second around each scout.");
        BmMoonLight = cfg.Bind("BloodMoon", "MoonLight", 1.6f, "Brightness of the red moonlight (a blood moon night is lit like a red-sun day; never darker than the vanilla moon).");
        BmMusicVolume = cfg.Bind("BloodMoon", "MusicVolume", 0.3f, "Volume of BloodMoon.ogg/.wav/.mp3 placed next to the mod DLL (no file = no music).");
        BmMusicLoop = cfg.Bind("BloodMoon", "MusicLoop", true, "Loop the blood moon music for the whole blood moon (off = play once).");
        // New key so the old 1.25 doesn't stick around in existing configs.
        BmScoutmasterSpeed = cfg.Bind("BloodMoon", "ScoutmasterHuntSpeed", 1f, "How much faster the Scoutmaster moves while a blood moon hunt is on (1 = vanilla).");
        BmScoutmasterSpawnMin = cfg.Bind("BloodMoon", "ScoutmasterSpawnMin", 80f, "During a hunt the Scoutmaster appears out of every scout's sight at least this far (m) from his prey (vanilla's own teleport: 80-112m)...");
        BmScoutmasterSpawnMax = cfg.Bind("BloodMoon", "ScoutmasterSpawnMax", 112f, "...and at most this far (m).");
        BmScoutmasterSpawnHeight = cfg.Bind("BloodMoon", "ScoutmasterSpawnHeight", 24f, "...within this many metres above or below his prey (vanilla: 24m).");
        BmScoutmasterStuck = cfg.Bind("BloodMoon", "ScoutmasterStuckRespawn", 3f, "During a hunt: sprinting for his prey (out of their sight) without moving for this long, he appears again out of sight (0 = never).");
        // New key (was ScoutmasterGrabGrace 2) so the old value doesn't stick around.
        BmScoutmasterGrabGrace = cfg.Bind("BloodMoon", "ScoutmasterSightGrace", 5f, "During a hunt, for this many seconds after he gets his prey in sight he can't grab - a sprint right up to someone is a scare, not a catch. He never grabs a scout a tornado is carrying.");
        BmScoutmasterStare = cfg.Bind("BloodMoon", "ScoutmasterStare", 2f, "Catching sight of his prey after a sprint, he stops and stares this many seconds before walking on.");
        BmScoutmasterSprintDistance = cfg.Bind("BloodMoon", "ScoutmasterSprintDistance", 20f, "Hunting, unwatched: he sprints while his prey is further than this (m); closer he walks. Anyone looking at him freezes him at any range.");
        BmGrabPause = cfg.Bind("BloodMoon", "GrabPause", 1f, "Hunting: seconds he stands right on his prey before he may grab...");
        BmGrabDelay = cfg.Bind("BloodMoon", "GrabDelay", 1f, "...and he grabs only once nobody has looked at him for this long (turn to run and that's your head start).");
        BmScoutmasterFarDistance = cfg.Bind("BloodMoon", "ScoutmasterFarDistance", 60f, "During a hunt: still further than this (m) from his prey after ScoutmasterFarTime, he appears again out of sight...");
        BmScoutmasterFarTime = cfg.Bind("BloodMoon", "ScoutmasterFarTime", 20f, "...after this many seconds (0 = never).");
        BmScoutmasterUnseen = cfg.Bind("BloodMoon", "ScoutmasterUnseen", 4f, "During a hunt: out of his prey's line of sight this long, he appears somewhere in the open again - picked while nobody is looking at it (0 = never).");
        BmScoutmasterThrowForce = cfg.Bind("BloodMoon", "ScoutmasterThrowForce", 0.6f, "How far his throws carry under a blood moon (share of vanilla's force; damage is untouched).");
        BmHunterAppearMin = cfg.Bind("BloodMoon", "HunterAppearMin", 40f, "Tier 1 (he hunts): he appears in the open at least this far (m) from his prey...");
        BmHunterAppearMax = cfg.Bind("BloodMoon", "HunterAppearMax", 60f, "...and at most this far.");
        BmNoticeWait = cfg.Bind("BloodMoon", "HunterNoticeWait", 6f, "Tier 1: after appearing he stands until his prey sees him (then stares ScoutmasterStare seconds) or this many seconds pass, then comes.");
        BmHunterGrace = cfg.Bind("BloodMoon", "HunterSightGrace", 3f, "Tier 1: seconds after he gets his prey in sight before he can grab.");
        BmHunterRecover = cfg.Bind("BloodMoon", "HunterRecover", 3f, "Tier 1: seconds he rests after a throw or a miss.");
        BmWatchMin = cfg.Bind("BloodMoon", "WatchMin", 25f, "Tier 2+ (he watches): he stands in the open at least this far (m) from who he watches...");
        BmWatchMax = cfg.Bind("BloodMoon", "WatchMax", 40f, "...and at most this far.");
        BmStareLunge = cfg.Bind("BloodMoon", "StareLunge", 5f, "Tier 2: look straight at him this many seconds (without looking away) and he charges from where he stands - once per scout per moon.");
        BmStareAngle = cfg.Bind("BloodMoon", "StareAngle", 12f, "Tier 2: how close to the middle of your view (degrees) he must be to count as staring.");
        BmStareStatic = cfg.Bind("BloodMoon", "StareStatic", 0.9f, "Tier 2: how strong his static gets as your stare runs out (0 = none).");
        BmLungeTime = cfg.Bind("BloodMoon", "LungeTime", 8f, "Tier 2: how long a charge lasts before he gives up (it still counts as his one try).");
        Xp = cfg.Bind("Xp", "Enabled", true, "A gold XP bar: per biome, climbing (new height only), crates and using up crate items each fill up to 2 levels; reaching camp adds the fast-clear bonus and 100. Each level opens a crate card pick once you stand on flat ground.");
        XpBiomeMinutes = cfg.Bind("Xp", "BiomeMinutes", 20f, "The fast-clear bonus drains to nothing over this many minutes (starts when you leave camp).");
        XpClimbLevels = cfg.Bind("Xp", "ClimbLevels", 2.5f, "Levels a whole biome's climb is worth (bottom to campfire).");
        XpLevelCards = cfg.Bind("Xp", "LevelCards", 4, "Cards on a level-up: items you carry and trees you found before this run (those come with a copy).");
        XpLandingXp = cfg.Bind("Xp", "LandingXp", 125f, "Caldera / Gloom (flat biomes): XP for landing on new ground instead of climbing...");
        XpLandingSpacing = cfg.Bind("Xp", "LandingSpacing", 15f, "...at least this many metres from every spot you've landed on in that biome...");
        XpLandingCap = cfg.Bind("Xp", "LandingLevels", 3f, "...up to this many levels...");
        XpLandingFloor = cfg.Bind("Xp", "LandingFloor", 2f, "...and reaching the campfire tops it up to at least this many.");
        XpFastClearLevels = cfg.Bind("Xp", "FastClearLevels", 1f, "Levels the fast-clear bonus is worth with the full time left.");
        XpMinClimb = cfg.Bind("Xp", "MinClimb", 50f, "A biome whose campfire is less than this many metres above its start pays its climb on arrival instead.");
        XpCampfire = cfg.Bind("Xp", "CampfireXp", 100, "XP for reaching each campfire (and the summit), on top of the fast-clear bonus.");
        XpPerCrate = cfg.Bind("Xp", "PerCrate", 100, "XP for each crate you claim from...");
        XpCrateLevels = cfg.Bind("Xp", "CrateLevels", 1f, "...up to this many levels per biome.");
        XpHealingUse = cfg.Bind("Xp", "HealingUse", 200, "XP for using up a healing item that came from a card pick (crate, level-up, statue)...");
        XpFoodUse = cfg.Bind("Xp", "FoodUse", 50, "...a food (wild food never counts)...");
        XpItemUse = cfg.Bind("Xp", "ItemUse", 100, "...anything else...");
        XpItemLevels = cfg.Bind("Xp", "ItemLevels", 1.5f, "...up to this many levels per biome.");
        XpKilnClimb = cfg.Bind("Xp", "KilnClimbMultiplier", 1.5f, "The Kiln is a short climb: its climb XP is worth this much more.");
        LuckPullEarly = cfg.Bind("Luck", "IngredientPullEarly", 1.5f, "Crates: while you hold a maxed item one ingredient short of a fusion, that ingredient rolls this much more often (before LateFromSegment).");
        LuckPullLate = cfg.Bind("Luck", "IngredientPullLate", 3f, "...and this much more often from LateFromSegment on.");
        LuckLateFrom = cfg.Bind("Luck", "LateFromSegment", 3, "Map segment where the late luck starts (0 Shore, 1 Tropics/Roots, 2 Alpine/Mesa, 3 Caldera/Gloom, 4 Kiln) - once the squad is past the third biome's campfire.");
        LuckStatueIngredientLate = cfg.Bind("Luck", "StatueIngredientLate", 0.3f, "Late luck: statue / Ancient Luggage chance of the missing ingredient (early = [Fusion] StatueIngredientChance).");
        LuckStatueFusionLate = cfg.Bind("Luck", "StatueFusionLate", 0.05f, "Late luck: statue / Ancient Luggage chance of a finished fusion (early = [Fusion] StatueFusionChance).");
        LuckFusionChance = cfg.Bind("Luck", "LuckyFusionChance", 0.1f, "Reaching the second biome's campfire: chance per SQUAD that a scout's next level-up offers a finished fusion (each scout rolls this divided by the squad size).");
        LuckGuarantee = cfg.Bind("Luck", "SquadGuarantee", true, "Reaching the campfire before the Kiln (Caldera / Gloom) with no fusion made by anyone: one scout (the closest to a fusion) is offered a guaranteed one.");
        XpBarScale = cfg.Bind("Xp", "BarScale", 0.75f, "Size of the XP bar (a copy of the stamina bar).");
        XpBarX = cfg.Bind("Xp", "BarOffsetX", 0f, "Nudge the XP bar right (+) / left (-), in UI pixels.");
        XpBarY = cfg.Bind("Xp", "BarOffsetY", 0f, "Nudge the XP bar up (+) / down (-), in UI pixels.");
        BmScoutmasterRecover = cfg.Bind("BloodMoon", "ScoutmasterRecover", 5f, "During a hunt, after a throw or a missed lunge he stands still this many seconds, then locks onto his next target (0 = vanilla's 2s after throws only).");
        BmTornadoGrouped = cfg.Bind("BloodMoon", "TornadoGroupedSpeed", 0.5f, "The tornado's speed when the relic holder nearest it is grouped (share of its normal speed).");
        BmTornadoApproach = cfg.Bind("BloodMoon", "TornadoApproachSpeed", 14f, "The tornado's speed (m/s) while it travels up from the section's starting campfire.");
        BmTornadoChase = cfg.Bind("BloodMoon", "TornadoChaseSpeed", 0.6f, "The tornado's chase speed once close, as a share of its old speed (1 = v0.6 speed).");
        BmTornadoNear = cfg.Bind("BloodMoon", "TornadoChaseDistance", 60f, "Distance (m) from the nearest scout at which the tornado slows to its chase speed.");

        Suns = cfg.Bind("Suns", "Enabled", true, "Coloured suns: each biome's days burn the colour of its statue amulet until the squad holds that relic; blood moon nights only after it.");
        SunSky = cfg.Bind("Suns", "SkyTint", 1f, "How strongly the sky and sunlight take the sun's colour (0 = vanilla sky, effects still apply).");
        SunYellowDrain = cfg.Bind("Suns", "YellowStaminaDrain", 1.4f, "Yellow sun: stamina use multiplier, on top of the ascent's own (Ascent 6+ is x1.4, so x1.96 there).");
        SunGreenFoodCap = cfg.Bind("Suns", "GreenFoodCap", 0.1f, "Unused since 0.12.7.");
        SunBlueHealCap = cfg.Bind("Suns", "BlueHealingCap", 0.1f, "Blue sun: healing items heal this fraction (lantern and relics never capped).");
        SunBlueDamage = cfg.Bind("Suns", "BlueDamage", 1.5f, "Blue sun: every hit and every tick of harm (injury and falls, poison, cold, heat, spores, drowsiness, curse) lands this many times harder.");
        SunYellowRushCap = cfg.Bind("Suns", "YellowRushCap", 0.1f, "Yellow sun: infinite stamina from items lasts this fraction as long.");
        SunRedMoveCap = cfg.Bind("Suns", "RedMovementCap", 0.1f, "Red sun: glider, jetpack, rocket pack, balloons, scout cannon and anti-grav work at this fraction (ropes / pitons untouched).");
        SunSkyBlend = cfg.Bind("Suns", "SkyBlend", 0.8f, "How far the sky leans into the sun's colour (0..1).");
        SunFlareTint = cfg.Bind("Suns", "FlareTint", true, "Tint the game's sun glare (lens flare) to the sky's colour - coloured suns, and blood red on the menu and at the airport.");
        SunLight = cfg.Bind("Suns", "SunlightColour", 1f, "How strongly the sunlight itself takes the sun's colour (0 = the game's near-white light, 1 = the full colour, up to 1.5 = deeper than the sun's colour). Real light, not a filter: shadows stay shadows. Tune live in F8 > World.");
        SunGreenBite = cfg.Bind("Suns", "GreenFoodBite", 0.05f, "Unused since 0.12.7.");
        SafeZones = cfg.Bind("Suns", "SafeZones", true, "Lit campfires and the summit are safe: no sun effects, no blood moon attacks, music fades. The sky keeps its colour.");
        SpawnRadius = cfg.Bind("Suns", "SpawnSafeRadius", 30f, "Safe radius (m) around the plane crash where the run starts.");
        SummitRadius = cfg.Bind("Suns", "SummitSafeRadius", 25f, "Safe radius (m) around the summit.");
        SunGreenHunger = cfg.Bind("Suns", "GreenHunger", 1.6f, "Unused since 0.12.7 (no sun changes hunger).");
        SunGreenRainEvery = cfg.Bind("Suns", "GreenFoodRainEvery", 120f, "Unused since 0.12.7.");
        SunGreenRainCount = cfg.Bind("Suns", "GreenFoodPerStorm", 20, "Unused since 0.12.7.");
        SunGreenShowerSeconds = cfg.Bind("Suns", "GreenStormSeconds", 5f, "Green sun: how long each food storm lasts.");
        SunGreenFoodLife = cfg.Bind("Suns", "GreenFoodLifetime", 20f, "Green sun: rained food left on the ground vanishes after this many seconds (pocket it to keep it).");
        SunGreenNoStack = cfg.Bind("Suns", "GreenNoStacking", true, "Green sun: nothing new joins a stack (existing stacks stay and can be used); singles merge back at sunset or when the relic is taken.");
        SunGreenShoreWeight = cfg.Bind("Suns", "GreenShoreWeight", 1, "Green sun on the Shore: extra weight per item (on top of the ascent's own +1 from Ascent 3).");
        SunFadeSeconds = cfg.Bind("Suns", "FadeSeconds", 5f, "Seconds for a sun's colour to fade in or out.");
        SunRedJump = cfg.Bind("Suns", "RedJumpLaunch", 0.7f, "Red sun: jump launch-height multiplier (on top of the heavier gravity).");
        SunRedGravity = cfg.Bind("Suns", "RedGravity", 1.6f, "Red sun: gravity multiplier on scouts - lower, shorter jumps, faster falls.");
        SunRedSprintCut = cfg.Bind("Suns", "RedSprintCut", 0.6f, "Red sun: share of the sprint speed bonus taken away (0 = vanilla sprint, 1 = sprinting is no faster than walking). Walking untouched.");
        SunRedLungeCut = cfg.Bind("Suns", "RedLungeCut", 0.5f, "Red sun: share of the climbing lunge's distance taken away (0 = vanilla, 1 = no lunge). Stamina cost unchanged.");
        SunRedHeavyShoreOnly = cfg.Bind("Suns", "RedHeavyShoreOnly", true, "Red sun: heavier gravity and low jumps only on the Shore (false = every biome). Sprint, lunge and item caps apply everywhere.");
        SunYellowStumble = cfg.Bind("Suns", "YellowStumbleSeconds", 0f, "Yellow sun on the Shore: running dry on the ground trips you for this long (0 = off; replaced by YellowDrowsy).");
        SunYellowDrowsy = cfg.Bind("Suns", "YellowDrowsy", 0.05f, "Yellow sun on the Shore: each try to sprint, climb or jump with no stamina adds this much Drowsy (at most every half second; 0 = off).");
        SunBlueFreeze = cfg.Bind("Suns", "BlueShoreFreeze", true, "Unused since 0.12.7.");
        SunBlueWear = cfg.Bind("Suns", "BlueItemWear", true, "Green sun: limited-use items spend two uses at a time (blue until 0.12.7).");
        SunBlueBreaks = cfg.Bind("Suns", "BlueBreaks", true, "Green sun: every rope frays (snaps after a climb) and every piton cracks under you (blue until 0.12.7).");
        GenerosityShare = cfg.Bind("Relics", "GenerosityMealShare", 0.5f, "Generosity: scouts near you get this share of the hunger your meals relieve (0 = off).");

        Storage = cfg.Bind("Storage", "Enabled", true, "Picking up a backpack or fanny pack offers to absorb it for extra pockets (lost on death). Off = vanilla packs.");
        MaxPockets = cfg.Bind("Storage", "MaxPockets", 6, new ConfigDescription("Most pockets a scout can have (vanilla 3 + one backpack 2 + one fanny pack 1 = 6; keys 5-9, 0, -, = reach pockets 4-11).", new AcceptableValueRange<int>(3, 11)));
        BackpackPockets = cfg.Bind("Storage", "BackpackPockets", 2, "Pockets a backpack adds.");
        FannypackPockets = cfg.Bind("Storage", "FannypackPockets", 1, "Pockets a fanny pack adds.");
        Stacking = cfg.Bind("Storage", "Stacking", true, "The same item picked up again joins its pocket (up to 3): food, pitons, bandages and drinks count up; spools, lanterns and multi-use items bring their next copy when one runs out; gliders glide longer. A dropped stack spills into a pile. Fusions never stack.");
        StackByLevel = cfg.Bind("Storage", "StackByLevel", false, "A tree item stacks only as high as its level, but always to at least 2 (level 1-2 = 2, level 3 = 3). Off = everything stacks to 3.");
        StackCrateOnly = cfg.Bind("Storage", "CrateItemsOnly", true, "Only items that can come out of a crate or luggage (and tree items, evolutions, earned items) stack. Wild food - berries, coconuts, mushrooms - never does.");

        MenuMusic = cfg.Bind("Ambiance", "MenuMusic", true, "Play the blood moon song on the main menu instead of the game's menu music.");
        MenuSky = cfg.Bind("Ambiance", "MenuSky", 1f, "How blood red the main menu's sky is, with a red moon in it (0 = vanilla).");
        MenuFilter = cfg.Bind("Ambiance", "MenuFilter", 0f, "Red screen filter over the main menu (0 = off).");
        MenuLogoGlow = cfg.Bind("Ambiance", "MenuLogoHalo", 0f, "Experimental red halo behind the PEAK logo (0 = off; looked like a box in testing).");
        MenuLogoTint = cfg.Bind("Ambiance", "MenuLogoTint", 0.45f, "How red the PEAK logo itself is tinted (0 = vanilla colours, 1 = fully red).");
        MenuLight = cfg.Bind("Ambiance", "MenuLight", 0.5f, "Red sunlight on the main menu - reddens the clouds and the scene, not just the sky (0 = vanilla).");
        AirportSky = cfg.Bind("Ambiance", "AirportSky", 1f, "How blood red the sky outside the airport is, with a red moon in it (0 = vanilla).");
        AirportLight = cfg.Bind("Ambiance", "AirportLight", 0.35f, "Red sunlight at the airport - reddens the clouds and the view outside (0 = vanilla).");
        DevTools = cfg.Bind("Dev", "DevTools", false, "Host-only cheats in the F8 panel (fly, god mode, item levels, time, blood moons). Off = the panel only shows builds.");
        DevShareRelics = cfg.Bind("Dev", "SharedRelicPickups", false, "Testing (host): when a scout binds a relic from an amulet, every other scout gets that colour's relic choice too. Shared relics don't raise the moon's tier, and the pickup's moon waits until everyone has chosen.");
        DevShareWait = cfg.Bind("Dev", "SharedRelicWait", 60f, "Testing (host): longest the pickup's moon waits for everyone to choose their shared relic (s).");
        DevMoonEveryNight = cfg.Bind("Dev", "BloodMoonEveryNight", true, "Host: once the squad holds a relic, every nightfall raises a blood moon (skipped the night after an afternoon or evening pickup moon; a morning pickup still gets its night).");
        DevMoonOnPickup = cfg.Bind("Dev", "BloodMoonOnPickup", true, "Host: binding a fresh relic raises a blood moon at once (by day a blood eclipse). Off = pickups raise nothing.");
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
        MedicineRadius = cfg.Bind("Medicine", "AreaRadius", 10f, "Metres: a thrown Cure-All that breaks, and Pandora's Lunchbox, reach every scout this close.");
        HolyShieldSeconds = cfg.Bind("Medicine", "RadiantShieldSeconds", 15f, "Radiant Health Pack (holy level 6) and the Cure-All: seconds of milk invincibility.");
        PassOutKitCooldown = cfg.Bind("Medicine", "PassOutPackCooldown", 60f, "With a Radiant Health Pack scout in the squad, a scout who passes out gets one dropped beside them - at most once per this many seconds each (0 = off).");
        AntidoteCleanse = cfg.Bind("Medicine", "AntidoteCleanse", 0.15f, "Antidote: share of injury, cold, heat and drowsiness each sip removes (poison stays vanilla 35%).");
        AntidoteWeight = cfg.Bind("Medicine", "AntidoteCrateWeight", 0.5f, "Antidote: weight in every biome's crates, as a share of a Bandage's.");
        BookOfBonesWeight = cfg.Bind("Medicine", "BookOfBonesWeight", 0.5f, "The Book of Bones: weight in cursed luggage and coffins, as a share of its vanilla weight (= the Skull's and Dagger's).");
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

        TreePower = cfg.Bind("ItemTrees", "PowerScale", 1f, "Scales how far below vanilla low-level items start (1 = as designed, 0 = no penalty).");
        BalloonFlightSeconds = cfg.Bind("ItemTrees", "BalloonFlightSeconds", "0, 2, 4, 6, 8, 12", "Seconds of flight per balloon level: three single-balloon levels, then three bunch levels (0 = vanilla floating). The longest rises exactly one max-length vanilla rope; every flight climbs at that same speed.");
        BalloonAirControl = cfg.Bind("ItemTrees", "BalloonAirControl", 2f, "Steering multiplier while a balloon flies you.");
        BalloonPopJump = cfg.Bind("ItemTrees", "BalloonPopJump", 6f, "Upward speed (units/s) of popping a balloon by jumping in the air (0 = off).");
        TintItems = cfg.Bind("ItemTrees", "TintItems", true, "Colour items by their level.");

        RarityCurve = cfg.Bind("Loot", "RarityCurve", true, "Early biomes roll mostly common items; rarer items get more likely as the run climbs.");

        Blessings = cfg.Bind("Systems", "Blessings", true, "Revive statues bless every scout (+2 levels or a fusion) when nobody needs reviving. Ancient Luggage blesses whoever opens it.");
        HuntSeconds = cfg.Bind("Relics", "ScoutmasterHuntSeconds", 60f, "Only with blood moons off: binding a relic sets the Scoutmaster hunting everyone for this long (0 = off).");
        RelicDrops = cfg.Bind("Relics", "DropOnDeath", true, "Dying drops your relics as amulets; whoever picks one up gets that exact relic.");
        StatueOrder = cfg.Bind("Relics", "StatueOrder", PeakRoguelike.StatueOrder.Off, "Which amulet sits on each biome's statue (the suns follow). Off = the game's own order; Daily = shuffled by the day's map, the same for everyone that day; EveryRun = a new order every run. The host's setting counts - also on the airport ticket and in F8.");

        // One-time: bring existing configs up to this release's defaults (BepInEx keeps old values).
        var defaults = cfg.Bind("Internal", "DefaultsVersion", 0, "Which release's defaults this file has been brought up to. Leave it alone.");
        if (defaults.Value < 1)
        {
            DevMoonEveryNight.Value = true;
            DevMoonOnPickup.Value = true;
            SunLight.Value = 1f;
            SunSkyBlend.Value = 0.8f;
            defaults.Value = 1;
            Plugin.Log.LogInfo("Config: blood moons every night and on pickup, sunlight colour 100%, sky colour 80% (new defaults).");
        }
        if (defaults.Value < 2)
        {
            SunGreenRainEvery.Value = 120f;
            SunGreenFoodLife.Value = 20f;
            defaults.Value = 2;
            Plugin.Log.LogInfo("Config: green sun food storms every 120s, rained food gone after 20s (new defaults).");
        }
        if (defaults.Value < 3)
        {
            XpCrateLevels.Value = 1f;
            XpItemLevels.Value = 1.5f;
            StackByLevel.Value = false;
            defaults.Value = 3;
            Plugin.Log.LogInfo("Config: crate XP up to 1 level a biome, item XP up to 1.5, everything stacks to 3 (new defaults).");
        }
        if (defaults.Value < 4)
        {
            XpClimbLevels.Value = 2.5f;
            XpBiomeMinutes.Value = 20f;
            BmZombiesPerScout.Value = 1;
            BmMoonLight.Value = 1.6f;
            defaults.Value = 4;
            Plugin.Log.LogInfo("Config: climb 2.5 levels a biome, fast clear over 20 min, 1 zombie per scout, brighter blood moonlight (new defaults).");
        }
        if (defaults.Value < 5)
        {
            BmScoutmasterSprintDistance.Value = 20f;
            defaults.Value = 5;
            Plugin.Log.LogInfo("Config: the Scoutmaster walks the last 20m (new default).");
        }
    }
}
