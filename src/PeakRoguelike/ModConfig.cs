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
    public static ConfigEntry<Key> FusionKey = null!, StackKey = null!, RelicKey = null!;
    public static ConfigEntry<Key> DevPanelKey = null!;
    /// <summary>"Skip" on a saved-for-later card asks "are you sure?" first (each player can turn it off).</summary>
    public static ConfigEntry<bool> ConfirmSkip = null!;
    /// <summary>When a statue amulet's relic cards open: when it's read (default) or the moment it's picked up (before 0.12.68).</summary>
    public static ConfigEntry<AmuletCards> AmuletCardsWhen = null!;
    /// <summary>Any tree item that reaches your pockets puts its tree at level 1 (level-ups offer it for the rest of the run).</summary>
    public static ConfigEntry<bool> XpPickupsCount = null!;
    public static ConfigEntry<float> SugarRushSeconds = null!;

    // --- bug reports ---------------------------------------------------------------------
    public static ConfigEntry<Key> BugReportKey = null!;
    public static ConfigEntry<bool> BugReportOnError = null!;
    public static ConfigEntry<float> BugReportLogMinutes = null!;

    // --- relics --------------------------------------------------------------------------
    public static ConfigEntry<int> GlassPerTrigger = null!;
    public static ConfigEntry<float> RelicCooldown = null!, RelicVeil = null!, DaggerStackCooldown = null!, GlideLandSeconds = null!, BadgeGlassScale = null!;
    public static ConfigEntry<float> JetpackFuel = null!, FaerieBoost = null!, ShoreOtherItems = null!, StatueIngredientChance = null!, StatueJackpotChance = null!, AncientJackpotChance = null!, JackpotDarkShare = null!, ReviveDarkChance = null!;
    public static ConfigEntry<int> ChainLauncherUses = null!, RescueClawUses = null!, AntiGravUses = null!;
    public static ConfigEntry<float> MedicineRadius = null!, HolyShieldSeconds = null!, PassOutKitCooldown = null!,
        AntidoteWeight = null!, BookOfBonesWeight = null!;
    public static ConfigEntry<int> AntidotePoints = null!, CookieGlass = null!;
    public static ConfigEntry<float> CookieInjury = null!;
    public static ConfigEntry<bool> EqualTreeItems = null!, FusionCards = null!, CursedCopies = null!;
    public static ConfigEntry<float> AuraRadius = null!;

    // --- item trees ----------------------------------------------------------------------
    public static ConfigEntry<float> TreePower = null!, BalloonAirControl = null!, BalloonPopJump = null!;
    public static ConfigEntry<float> AirlineHeal = null!, GranolaHeal = null!;
    public static ConfigEntry<int> AirlineGlass = null!, GranolaGlass = null!, TopKitGlass = null!;
    public static ConfigEntry<float> TopKitHunger = null!;
    public static ConfigEntry<string> BalloonFlightSeconds = null!;
    public static ConfigEntry<float> BalloonRiseSpeed = null!;
    public static ConfigEntry<bool> TintItems = null!;

    // --- loot ----------------------------------------------------------------------------
    public static ConfigEntry<bool> RarityCurve = null!;

    // --- blessings / relic risks -------------------------------------------------------------
    public static ConfigEntry<bool> Blessings = null!;
    public static ConfigEntry<bool> FlareReroll = null!, FlareSupplyDrop = null!, TimedDrops = null!, EmptyHandedPicks = null!, FindCards = null!;
    public static ConfigEntry<float> FlareAncientChance = null!, DropClusterMetres = null!, DropTrappedChance = null!;
    public static ConfigEntry<string> DropSolo = null!, DropDuo = null!, DropSmall = null!, DropBig = null!;
    public static ConfigEntry<int> DropClusterScouts = null!;
    public static ConfigEntry<bool> CurioRelics = null!, CurioWakeNotice = null!, FrisbeeHitsScouts = null!;
    public static ConfigEntry<float> CurioPingSeconds = null!, CurioCrateBoost = null!, FrisbeeForce = null!, BingBongMinSeconds = null!, BingBongMaxSeconds = null!;
    public static ConfigEntry<float> BingBongHideMin = null!, BingBongHideMax = null!, BingBongDarkSeconds = null!, BingBongBlinkRange = null!, BingBongGiftMaxChance = null!;
    public static ConfigEntry<Key> FlashlightKey = null!;
    public static ConfigEntry<float> HuntSeconds = null!;
    public static ConfigEntry<bool> RelicDrops = null!;
    internal static ConfigEntry<StatueOrder> StatueOrder = null!;

    // --- blood moon ----------------------------------------------------------------------
    public static ConfigEntry<bool> BloodMoon = null!, BmDarkRites = null!, BmWarning = null!, BmRelay = null!;
    public static ConfigEntry<float> BmRelaySeconds = null!, BmRelayTornadoBack = null!, BmZombieAhead = null!, BmZombieLeash = null!;
    public static ConfigEntry<bool> BmScoutmaster = null!, BmZombies = null!, BmTornado = null!, BmGlassMeteors = null!, BmMeteors = null!;
    public static ConfigEntry<float> BmMeteorRate = null!, BmMeteorSceneryBase = null!, BmMeteorSceneryPerScout = null!, BmMeteorCoverAngle = null!;
    public static ConfigEntry<float> BmMoonLight = null!, BmNightDarkness = null!, BmEclipseDarkness = null!, BmMeteorHitGrace = null!;
    public static ConfigEntry<float> BmMusicVolume = null!;
    public static ConfigEntry<bool> BmMusicLoop = null!, BmRelayTornadoFromBase = null!;
    public static ConfigEntry<float> BmScoutmasterSpawnMin = null!, BmScoutmasterSpawnMax = null!, BmScoutmasterSpawnHeight = null!,
        BmScoutmasterStuck = null!, BmScoutmasterRecover = null!;
    public static ConfigEntry<float> BmZombieNear = null!;
    public static ConfigEntry<int> BmZombiesPerScout = null!, BmZombiesPerCarrier = null!, BmZombiesSoloCarrier = null!, BmGrabsTier1 = null!, BmGrabsTier2 = null!;
    public static ConfigEntry<float> BmWaveGap = null!, BmWaveLength = null!, BmRelicAura = null!, BmTornadoGrouped = null!;
    public static ConfigEntry<float> BmScoutmasterSpeed = null!, BmTornadoApproach = null!, BmTornadoChase = null!, BmTornadoNear = null!;
    public static ConfigEntry<float> BmTornadoKilnWidth = null!, BmTornadoKilnPull = null!, BmTornadoKilnCatch = null!, BmTornadoSpawnPoint = null!;
    public static ConfigEntry<int> StoveGlass = null!;
    public static ConfigEntry<float> BmMeteorAimedEvery = null!, BmMeteorAimedEveryShared = null!, BmMeteorNearShare = null!, BmMeteorNearShareShared = null!;
    public static ConfigEntry<int> BmSurviveGlassHeal = null!;
    public static ConfigEntry<bool> VoidMoons = null!, BadgeRelics = null!;
    public static ConfigEntry<float> VoidSunSeconds = null!, VoidMoonSeconds = null!;
    public static ConfigEntry<float> BmTornadoSharedWidth = null!, BmTornadoSharedPull = null!, BmTornadoVeerMin = null!, BmTornadoVeerMax = null!, BmTornadoThrowGrace = null!, BmTornadoPinned = null!, BmTornadoSprintShare = null!, BmTornadoSprintRef = null!, BmTornadoPinnedSpeed = null!, BmTornadoHeldMax = null!, BmTornadoNearMax = null!;

    // --- suns ----------------------------------------------------------------------------
    public static ConfigEntry<bool> Suns = null!, SunFlareTint = null!, SunBlueFreeze = null!, SunBlueWear = null!, SunBlueBreaks = null!;
    public static ConfigEntry<float> SunSky = null!, SunYellowDrain = null!, SunGreenHunger = null!,
        SunRedJump = null!, GenerosityShare = null!,
        SunRedGravity = null!, SunYellowStumble = null!, SunGreenRainEvery = null!,
        SunSkyBlend = null!, SunLight = null!, SunBrightness = null!, SunShade = null!, SunGreenBite = null!,
        SunGreenShowerSeconds = null!, SunGreenFoodLife = null!, SunFadeSeconds = null!, MaxedTreeWeight = null!, SunscreenWeight = null!,
        SunBlueHealCap = null!, SunYellowRushCap = null!, SunGreenFoodCap = null!, SunRedMoveCap = null!;
    public static ConfigEntry<bool> SafeZones = null!, HarshSunsEverywhere = null!;
    public static ConfigEntry<float> SunRedSprintSpeed = null!, SunRedJumpHeight = null!, SunRedJetpackBurn = null!, SunRedItemPower = null!, SunRedLunge = null!, SunRedShoreJump = null!, SunBlueGlassSeconds = null!, ParasolShieldSeconds = null!;
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
    public static ConfigEntry<bool> CratesFirstCome = null!;
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
    public static ConfigEntry<float> XpCatchUp = null!, BmCloseStareDistance = null!, BmCloseStareSeconds = null!, BmCloseLungeTime = null!, BmCloseLungeCooldown = null!;
    public static ConfigEntry<float> LuckPullEarly = null!, LuckPullLate = null!, LuckStatueIngredientLate = null!, LuckCampfireFusion = null!;
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
        BmRelay = cfg.Bind("BloodMoon", "Relay", true, "The airport ticket's Blood moons setting. On = relay: the hazards take turns in the shuffled order, RelaySeconds each, every one at its hardest (the Kiln / Citadel tornado stays the small one). Off = stacked (harder): every hazard the tier brings rises together for the whole moon.");
        BmRelaySeconds = cfg.Bind("BloodMoon", "RelaySeconds", 30f, "Relay moons: how long each hazard gets. A relay moon lasts tier x this.");
        BmRelayTornadoFromBase = cfg.Bind("BloodMoon", "RelayTornadoFromBase", true, "Relay moons: the tornado rises at the base, where the squad came into this section (like a stacked moon's), and rushes up at TornadoApproachSpeed until it's near. Off = the old way: RelayTornadoBack metres down the mountain from the lowest scout.");
        BmRelayTornadoBack = cfg.Bind("BloodMoon", "RelayTornadoBack", 100f, "Relay moons with RelayTornadoFromBase off: the tornado rises this many metres back down the mountain from the lowest scout, in plain sight (the Kiln / Citadel and the Void keep their own spots).");
        BmZombieAhead = cfg.Bind("BloodMoon", "ZombieAhead", 8f, "Blood moon zombies rise this many metres (+-25%) ahead of their scout on the way up, a little above.");
        BmZombieLeash = cfg.Bind("BloodMoon", "ZombieLeash", 50f, "A scout this many metres from every zombie of their pack finds it rising right beside them again.");
        BmRelicAura = cfg.Bind("BloodMoon", "RelicAura", 24f, "The Scoutmaster and the tornado go after relic holders and anyone within this many metres of one (24 = vanilla's buddy distance). A holder with someone this close is 'grouped' (the tornado slows).");
        BmScoutmaster = cfg.Bind("BloodMoon", "Scoutmaster", true, "From tier 2: the Scoutmaster hunts (tiers 2-3), then only watches (tier 4).");
        BmZombies = cfg.Bind("BloodMoon", "Zombies", true, "2 relics: zombies rise beside every scout carrying a relic, the Cursed Skull or the Ritual Dagger.");
        BmZombieNear = cfg.Bind("BloodMoon", "ZombieDistance", 4f, "How far (m, +-25%) from its scout each blood moon zombie rises.");
        BmZombiesPerScout = cfg.Bind("BloodMoon", "ZombiesPerScout", 1, "How many zombies rise beside every scout in the open, from tier 1 (spread evenly around them).");
        BmZombiesPerCarrier = cfg.Bind("BloodMoon", "ZombiesPerCarrier", 1, "This many more beside each scout carrying a relic, the Cursed Skull or the Ritual Dagger (every tier).");
        BmZombiesSoloCarrier = cfg.Bind("BloodMoon", "ZombiesSoloCarrier", 3, "Solo runs, tier 1: how many zombies rise beside you when you carry a relic, the skull or the dagger (later tiers use the normal counts).");
        BmGrabsTier1 = cfg.Bind("BloodMoon", "GrabsPerScoutTier1", 1, "His first tier (hunter): grabs per scout per moon before the Scoutmaster only watches them.");
        BmGrabsTier2 = cfg.Bind("BloodMoon", "GrabsPerScoutTier2", 2, "His second tier: the same (from his third on he only watches).");
        BmTornado = cfg.Bind("BloodMoon", "Tornado", true, "A tornado that heads for relic holders (sand on Mesa, snow on Alpine). Its tier comes from the shuffled hazard order.");
        BmGlassMeteors = cfg.Bind("BloodMoon", "GlassMeteors", true, "4 relics, Gloom runs (Gloom + Citadel): a shower of purple glass - a direct hit adds glass.");
        BmMeteors = cfg.Bind("BloodMoon", "Meteors", true, "4 relics, Caldera runs: a meteor shower.");
        BmMeteorRate = cfg.Bind("BloodMoon", "MeteorRate", 1f, "Meteors per second around each scout.");
        BmMeteorSceneryBase = cfg.Bind("BloodMoon", "MeteorSceneryBase", 2f, "Meteor showers: harmless scenery rocks a second, crashing 30-150m from every scout (no mark, can't hit). 0 = none.");
        BmMeteorSceneryPerScout = cfg.Bind("BloodMoon", "MeteorSceneryPerScout", 1.5f, "Meteor showers: more scenery rocks a second for each scout beyond the first out in the open.");
        BmMeteorCoverAngle = cfg.Bind("BloodMoon", "MeteorCoverAngle", 35f, "Meteor showers: a rock blocked overhead still comes in at a slant up to this many degrees off straight down, if any such path is open - only cover on every side (a cave, a deep overhang) stops it. 0 = any roof stops it.");
        BmMoonLight = cfg.Bind("BloodMoon", "MoonLight", 1.6f, "Brightness of the red moonlight (a blood moon night is lit like a red-sun day; never darker than the vanilla moon).");
        BmNightDarkness = cfg.Bind("BloodMoon", "NightDarkness", 0.6f, "How dark a blood moon night is: 0 = the old bright red-day look (MoonLight), 1 = vanilla moonlight in red, a deep crimson sky and much darker shade. Tune live in F8 > World.");
        BmEclipseDarkness = cfg.Bind("BloodMoon", "EclipseDarkness", 0.6f, "How dark a blood eclipse (a blood moon by day) is: 0 = the old look (sun at 70%), 1 = sun at 20%, a near-black sky and much darker shade. Tune live in F8 > World.");
        BmMeteorHitGrace = cfg.Bind("BloodMoon", "MeteorHitGrace", 6f, "Meteors, every tier (fire and glass): after a rock hits you, no other rock can hit you for this many seconds, and none are aimed your way while you're down - time to get up and run.");
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
        BmCloseStareDistance = cfg.Bind("BloodMoon", "CloseStareDistance", 8f, "Hunting: a scout this close (m) who stares straight at him while he's frozen draws a lunge.");
        BmCloseStareSeconds = cfg.Bind("BloodMoon", "CloseStareSeconds", 2f, "Hunting: seconds of staring from up close before he lunges.");
        BmCloseLungeTime = cfg.Bind("BloodMoon", "CloseLungeTime", 2f, "Hunting: how long a close-stare lunge lasts before he gives up.");
        BmCloseLungeCooldown = cfg.Bind("BloodMoon", "CloseLungeCooldown", 10f, "Hunting: seconds after a close-stare lunge before staring can draw another.");
        Xp = cfg.Bind("Xp", "Enabled", true, "A gold XP bar: per biome, climbing (new height only), crates and using up crate items each fill up to 2 levels; reaching camp adds the fast-clear bonus and 100. Each level opens a crate card pick once you stand on flat ground.");
        XpBiomeMinutes = cfg.Bind("Xp", "BiomeMinutes", 20f, "The fast-clear bonus drains to nothing over this many minutes (starts when you leave camp).");
        XpClimbLevels = cfg.Bind("Xp", "ClimbLevels", 2.5f, "Levels a whole biome's climb is worth (bottom to campfire).");
        XpLevelCards = cfg.Bind("Xp", "LevelCards", 4, "Cards on a level-up: items you carry and trees you found before this run (those come with a copy). At least one is a tree you're not carrying, when you have one.");
        XpPickupsCount = cfg.Bind("Xp", "PickupsCount", true, "Any tree item that reaches your pockets (off the ground, from a scout) puts its tree at level 1 if it was lower, whatever level the item itself is - so level-ups keep offering it after it's gone. Off = only crate and level-up picks count.");
        XpCatchUp = cfg.Bind("Xp", "CatchUpMultiplier", 2f, "A scout who joined after the run began earns XP this many times faster while below the lowest level of the others (1 = off).");
        XpLandingXp = cfg.Bind("Xp", "LandingXp", 125f, "Caldera / Gloom (flat biomes): XP for landing on new ground instead of climbing...");
        XpLandingSpacing = cfg.Bind("Xp", "LandingSpacing", 15f, "...at least this many metres from every spot you've landed on in that biome...");
        XpLandingCap = cfg.Bind("Xp", "LandingLevels", 3f, "...up to this many levels...");
        XpLandingFloor = cfg.Bind("Xp", "LandingFloor", 2f, "...and reaching the campfire tops it up to at least this many.");
        XpFastClearLevels = cfg.Bind("Xp", "FastClearLevels", 1f, "Levels the fast-clear bonus is worth with the full time left.");
        XpMinClimb = cfg.Bind("Xp", "MinClimb", 50f, "A biome whose campfire is less than this many metres above its start pays its climb on arrival instead.");
        XpCampfire = cfg.Bind("Xp", "CampfireXp", 0, "XP for reaching each campfire (and the summit), on top of the fast-clear bonus. 0 since 0.12.49: the lit campfire offers a +1 blessing instead.");
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
        LuckCampfireFusion = cfg.Bind("Luck", "CampfireFusionChance", 0.02f, "Peak campfires: chance (per scout, per campfire) that the blessing's last card is a random finished fusion. One per scout per run (~8% a run over 4 campfires).");
        LuckGuarantee = cfg.Bind("Luck", "SquadGuarantee", true, "Reaching the campfire before the Kiln (Caldera / Gloom) with no fusion made by anyone: one scout (the closest to a fusion) is offered a guaranteed one.");
        XpBarScale = cfg.Bind("Xp", "BarScale", 0.75f, "Size of the XP bar (a copy of the stamina bar).");
        XpBarX = cfg.Bind("Xp", "BarOffsetX", 0f, "Nudge the XP bar right (+) / left (-), in UI pixels.");
        XpBarY = cfg.Bind("Xp", "BarOffsetY", 0f, "Nudge the XP bar up (+) / down (-), in UI pixels.");
        BmScoutmasterRecover = cfg.Bind("BloodMoon", "ScoutmasterRecover", 5f, "During a hunt, after a throw or a missed lunge he stands still this many seconds, then locks onto his next target (0 = vanilla's 2s after throws only).");
        BmTornadoGrouped = cfg.Bind("BloodMoon", "TornadoGroupedSpeed", 0.5f, "The tornado's speed when the relic holder nearest it is grouped (share of its normal speed).");
        BmTornadoApproach = cfg.Bind("BloodMoon", "TornadoApproachSpeed", 14f, "The tornado's speed (m/s) while it travels up from the section's starting campfire.");
        BmTornadoChase = cfg.Bind("BloodMoon", "TornadoChaseSpeed", 0.6f, "Unused since 0.12.43 (see TornadoSprintShare).");
        BmTornadoNear = cfg.Bind("BloodMoon", "TornadoChaseDistance", 150f, "Distance (m) from the nearest scout at which the tornado slows to its chase speed (a little slower than a sprinting scout).");
        BmTornadoSprintShare = cfg.Bind("BloodMoon", "TornadoSprintShare", 0.85f, "Up close the tornado moves at this share of a sprinting scout's speed (under a red sun, cut like sprints are).");
        BmTornadoSprintRef = cfg.Bind("BloodMoon", "ScoutSprintSpeed", 7.8f, "How fast a fresh scout sprints on flat ground (game units/s; measured in game: 7.8). The blood moon tornado chases at TornadoSprintShare of this.");
        BmTornadoSharedWidth = cfg.Bind("BloodMoon", "TornadoSharedWidth", 0.75f, "Moon tier 2+ (the tornado shares the moon): its width as a share of the game's tornado (1 = vanilla). Its pull and catch reach shrink with it. Alone at tier 1 it's always vanilla.");
        BmTornadoSharedPull = cfg.Bind("BloodMoon", "TornadoSharedPull", 0.75f, "Moon tier 2+: its pull and spin strength as a share of vanilla (the upward lift stays vanilla).");
        BmTornadoKilnWidth = cfg.Bind("BloodMoon", "TornadoKilnWidth", 0.35f, "In the Kiln / Citadel (any tier) the tornado rises in the middle of the room at this share of the game's width.");
        BmTornadoKilnPull = cfg.Bind("BloodMoon", "TornadoKilnPull", 0.3f, "In the Kiln / Citadel: its pull and spin strength as a share of vanilla.");
        BmTornadoSpawnPoint = cfg.Bind("BloodMoon", "TornadoSpawnPoint", 0.5f, "Where the tornado rises, between the section's starting campfire (0) and the campfire at its top (1), measured across the map and dropped to the ground below. Not in the Kiln / Void.");
        BmTornadoKilnCatch = cfg.Bind("BloodMoon", "TornadoKilnCatch", 6f, "In the Kiln / Citadel: how close (m) to its middle you must get to be caught (the game's is 10m at full width, 3.5m at the Kiln's).");
        BmMeteorAimedEvery = cfg.Bind("BloodMoon", "MeteorAimedSeconds", 2.5f, "Meteors at moon tier 1: one rock aimed at where each scout stands, about every this many seconds (it takes 4s to land - keep moving).");
        BmMeteorAimedEveryShared = cfg.Bind("BloodMoon", "MeteorAimedSecondsShared", 4.5f, "Meteors at moon tier 2+ (sharing the moon): seconds between rocks aimed at each scout.");
        BmMeteorNearShare = cfg.Bind("BloodMoon", "MeteorNearShare", 0.4f, "Meteors at tier 1: share of the other rocks that land 4-10m around a scout (never on them).");
        BmMeteorNearShareShared = cfg.Bind("BloodMoon", "MeteorNearShareShared", 0.25f, "Meteors at tier 2+: share of the other rocks that land 4-10m around a scout.");
        BadgeRelics = cfg.Bind("Badges", "Enabled", true, "Badge relics: blessing statues offer 3 (pick one, no level-ups), a revive gives every scout a random one, Ancient Luggage offers one. They last the whole run, the Void included.");
        VoidMoons = cfg.Bind("Void", "Moons", true, "In the Void, a tier 1 blood moon follows every sun: one hazard each, all four in a random order before any repeats. Off = suns only.");
        VoidSunSeconds = cfg.Bind("Void", "SunSeconds", 30f, "In the Void: how long each sun shows (with moons off: 60 is the old pace).");
        VoidMoonSeconds = cfg.Bind("Void", "MoonSeconds", 30f, "In the Void: how long each blood moon lasts (no sun during it, no glass shed after it).");
        BmSurviveGlassHeal = cfg.Bind("BloodMoon", "SurviveGlassHeal", 25, "Glass (out of 100) healed from every scout still alive when a blood moon or eclipse ends.");
        BmTornadoVeerMin = cfg.Bind("BloodMoon", "TornadoVeerMin", 60f, "When the scout it hunts fires a relic (or nobody can be hunted) it veers off to a spot at least this far (m) away...");
        BmTornadoThrowGrace = cfg.Bind("BloodMoon", "TornadoThrowGrace", 15f, "Blood moon tornadoes only: after throwing a scout (or freeing one stuck in it) it can't pull or catch them for this many seconds - it goes after someone else, or backs off 60-100m if there's nobody (0 = off).");
        BmTornadoPinned = cfg.Bind("BloodMoon", "TornadoPinnedSeconds", 3f, "Blood moon tornadoes only: a scout in its pull (caught or not) barely moving for this long (stuck on a wall) is thrown loose and gets the breather.");
        BmTornadoPinnedSpeed = cfg.Bind("BloodMoon", "TornadoPinnedSpeed", 1.5f, "...'barely moving' = slower than this (game units/s; a sprint is ~8).");
        BmTornadoHeldMax = cfg.Bind("BloodMoon", "TornadoHeldSeconds", 5f, "Blood moon tornadoes only: holding a scout this long without throwing them, it throws them loose.");
        BmTornadoNearMax = cfg.Bind("BloodMoon", "TornadoPullSeconds", 10f, "Blood moon tornadoes only: nobody stays in its pull longer than this (in total, since their last throw) - then they're thrown loose.");
        BmTornadoVeerMax = cfg.Bind("BloodMoon", "TornadoVeerMax", 100f, "...and at most this far (m).");

        Suns = cfg.Bind("Suns", "Enabled", true, "Coloured suns: each biome's days burn the colour of its statue amulet until the squad holds that relic; blood moon nights only after it.");
        SunSky = cfg.Bind("Suns", "SkyTint", 1f, "How strongly the sky and sunlight take the sun's colour (0 = vanilla sky, effects still apply).");
        SunYellowDrain = cfg.Bind("Suns", "YellowStaminaDrain", 1.4f, "Yellow sun: stamina use multiplier, on top of the ascent's own (Ascent 6+ is x1.4, so x1.96 there).");
        SunGreenFoodCap = cfg.Bind("Suns", "GreenFoodCap", 0.1f, "Unused since 0.12.7.");
        SunBlueHealCap = cfg.Bind("Suns", "BlueHealingCap", 0.5f, "Blue sun: healing items heal this fraction (lantern and relics never capped).");
        // One time only (0.12.25): the old 10% default becomes 50%; a value someone set stays.
        var healMoved = cfg.Bind("Suns", "BlueHealingCapUpdated", false, "Set by the mod once it has moved the old blue healing default. Leave it.");
        if (!healMoved.Value)
        {
            if (System.Math.Abs(SunBlueHealCap.Value - 0.1f) < 0.001f) SunBlueHealCap.Value = 0.5f;
            healMoved.Value = true;
        }
        SunBlueDamage = cfg.Bind("Suns", "BlueDamage", 1.5f, "Blue sun: every hit and every tick of harm (injury and falls, poison, cold, heat, spores, drowsiness, curse) lands this many times harder.");
        SunYellowRushCap = cfg.Bind("Suns", "YellowRushCap", 0.1f, "Yellow sun: infinite stamina from items lasts this fraction as long.");
        SunRedMoveCap = cfg.Bind("Suns", "RedMovementCap", 0.1f, "Unused since 0.12.25 (see RedItemPower and RedJetpackBurnRate).");
        SunSkyBlend = cfg.Bind("Suns", "SkyBlend", 0.8f, "How far the sky leans into the sun's colour (0..1).");
        SunFlareTint = cfg.Bind("Suns", "FlareTint", true, "Tint the game's sun glare (lens flare) to the sky's colour - coloured suns, and blood red on the menu and at the airport.");
        SunLight = cfg.Bind("Suns", "SunlightColour", 1f, "How strongly the sunlight itself takes the sun's colour (0 = the game's near-white light, 1 = the full colour, up to 1.5 = deeper than the sun's colour). Real light, not a filter: shadows stay shadows. Tune live in F8 > World.");
        SunBrightness = cfg.Bind("Suns", "SunBrightness", 1f, "A coloured sun's light is dimmer than white (blue ~37%, red ~47%): how much of that the sun makes up by shining harder (0 = none, the old darker look; 1 = as bright as a normal day; up to 1.5). Capped at x2.5. Tune live in F8 > World.");
        SunShade = cfg.Bind("Suns", "ShadeBoost", 0.15f, "Under a coloured sun, PEAK's ambient light (the shade) is raised this much (0.15 = +15%, up to 0.5) so shadows don't look murky next to the coloured light. Tune live in F8 > World.");
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
        SunGreenShoreWeight = cfg.Bind("Suns", "GreenShoreWeight", 1, "Unused since 0.12.25 (green on the Shore withers a crate card instead).");
        SunFadeSeconds = cfg.Bind("Suns", "FadeSeconds", 5f, "Seconds for a sun's colour to fade in or out.");
        SunRedJump = cfg.Bind("Suns", "RedJumpLaunch", 0.7f, "Unused since 0.12.25 (see RedJumpHeight).");
        SunRedGravity = cfg.Bind("Suns", "RedGravity", 1.6f, "Red sun on the Shore: gravity multiplier - you drop faster and land harder (jumps are launched to still reach RedShoreJumpHeight).");
        HarshSunsEverywhere = cfg.Bind("Suns", "HarshSunsEverywhere", false, "The airport ticket's Harsh suns setting (host). Off = Shore only: each sun's lesson (blue light turns you to glass, red gravity and low jumps, yellow drowsiness when out of stamina, a withered green crate card) happens on the Shore. On = every biome (harder).");
        SunRedSprintSpeed = cfg.Bind("Suns", "RedSprintSpeed", 0.75f, "Red sun: sprinting runs at this share of its vanilla speed (never slower than walking).");
        SunRedJumpHeight = cfg.Bind("Suns", "RedJumpHeight", 0.75f, "Unused (see RedShoreJumpHeight: off the Shore red leaves jumps alone).");
        SunRedShoreJump = cfg.Bind("Suns", "RedShoreJumpHeight", 0.5f, "Red sun on the Shore: jumps peak at this share of their vanilla height (the heavier gravity included).");
        SunRedLunge = cfg.Bind("Suns", "RedLungeLength", 0.75f, "Red sun: the climbing lunge carries you this share of its vanilla distance (stamina cost unchanged).");
        SunRedItemPower = cfg.Bind("Suns", "RedItemPower", 0.5f, "Red sun: the glider and balloons work at this share (their level is untouched).");
        SunRedJetpackBurn = cfg.Bind("Suns", "RedJetpackBurnRate", 2f, "Red sun: the jetpack burns its fuel this many times faster (2 = a full tank flies half as long).");
        SunBlueGlassSeconds = cfg.Bind("Suns", "BlueShoreGlassSeconds", 240f, "Blue sun on the Shore: seconds of direct sunlight that fill the glass bar from empty to stone. Shade stops it; nightfall or the blue relic melts the sun's share.");
        ParasolShieldSeconds = cfg.Bind("Suns", "ParasolShadeSeconds", 60f, "An open parasol spares you from every coloured sun for this many seconds in all, then burns.");
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
        DevShareRelics = cfg.Bind("Dev", "SharedRelicPickups", true, "The airport ticket's Relics setting (host). Shared: when a scout binds a relic from an amulet, every other scout gets that colour's relic choice too. Shared relics don't raise the moon's tier, and the pickup's moon waits until everyone has chosen. Off = first come (harder): only whoever takes the amulet gets a relic.");
        CratesFirstCome = cfg.Bind("Systems", "CratesFirstCome", false, "The airport ticket's Crates setting (host). Off = shared: every scout gets a pick from each crate, and a taken card is re-rolled for the next. On = first come (harder): the crate deals the game's own number of cards once, a taken card is gone, and anyone may take what's left.");
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
        StackKey = cfg.Bind("Storage", "StackLevelUpKey", Key.K, "Re-open the stack level-up pop-up (3 copies in one pocket) after choosing 'Later'.");
        RelicKey = cfg.Bind("Relics", "SharedRelicKey", Key.L, "Re-open anything saved for later (level-ups, relics, statue gifts...), one at a time, and read an unread amulet.");
        ConfirmSkip = cfg.Bind("Choices", "ConfirmSkip", true, "'Skip' on a card that didn't come from a crate asks 'are you sure?' first. The pop-up's 'Don't ask again' box turns this off.");
        AmuletCardsWhen = cfg.Bind("Relics", "AmuletCards", AmuletCards.OnRead, "When a statue amulet's relic cards open. OnRead: taking it raises the moon, its cards wait until you click it in your hand or press the relic key. OnPickup: its cards open as soon as it's in your pockets, and the moon rises when you bind it (the pre-0.12.68 way). Either way, other scouts' shared cards wait until it's bound.");
        DevPanelKey = cfg.Bind("Dev", "BuildPanelKey", Key.F8, "Show which build of the mod every player in the lobby is running.");
        BugReportKey = cfg.Bind("BugReports", "Key", Key.F7, "Save a bug report (screenshot, recent log, run state, config) on every modded player's PC. Saved in BepInEx/BugReports.");
        BugReportOnError = cfg.Bind("BugReports", "AutoOnError", true, "Save a report by itself the first time the mod hits an error in a run.");
        BugReportLogMinutes = cfg.Bind("BugReports", "LogMinutes", 5f, "Minutes of log kept in each report.");
        SugarRushSeconds = cfg.Bind("Fusion", "SugarRushShieldSeconds", 8f, "Sugar-Rush Cocktail: how long its milk shield and infinite stamina last before the healing crash (the energy drink and lollipop inside it run their own vanilla times).");
        JetpackFuel = cfg.Bind("Fusion", "JetpackFuel", 1.5f, "Fused Jetpack: fuel multiplier (its twist).");
        FaerieBoost = cfg.Bind("Fusion", "FaerieLanternBoost", 1.5f, "Fused Faerie Lantern: range, healing and burn time multiplier (its twist).");
        ChainLauncherUses = cfg.Bind("Fusion", "ChainLauncherUses", 2, "Fused Chain Launcher: uses (vanilla 1).");
        RescueClawUses = cfg.Bind("Fusion", "RescueClawUses", 6, "Fused Rescue Claw: uses (vanilla 3).");
        AntiGravUses = cfg.Bind("Fusion", "AntiGravBazookaUses", 6, "Fused Anti-Grav Bazooka: uses (vanilla 3).");
        MedicineRadius = cfg.Bind("Medicine", "AreaRadius", 10f, "Metres: a thrown Cure-All that breaks, and Pandora's Lunchbox, reach every scout this close.");
        HolyShieldSeconds = cfg.Bind("Medicine", "RadiantShieldSeconds", 15f, "Radiant Health Pack (holy level 6) and the Cure-All: seconds of milk invincibility.");
        PassOutKitCooldown = cfg.Bind("Medicine", "PassOutPackCooldown", 60f, "With a Radiant Health Pack scout in the squad, a scout who passes out gets one dropped beside them - at most once per this many seconds each (0 = off).");
        AntidotePoints = cfg.Bind("Medicine", "AntidotePoints", 10, "Antidote tree, per sip (0-100 of a bar): level 1 takes this much off your worst ailments (poison, cold, heat, drowsiness, spores - worst first), level 2 also this much injury, level 3 also this much hunger. The game's own cooling stays on top.");
        CookieGlass = cfg.Bind("Medicine", "CookieGlass", 5, "Scout Cookies: glass (0-100) each bite removes, flat (a box has 4 bites).");
        CookieInjury = cfg.Bind("Medicine", "CookieInjury", 2.5f, "Scout Cookies: injury (0-100 of a bar) each bite heals, flat.");
        AntidoteWeight = cfg.Bind("Medicine", "AntidoteCrateWeight", 0.5f, "Antidote: weight in every biome's crates, as a share of a Bandage's.");
        BookOfBonesWeight = cfg.Bind("Medicine", "BookOfBonesWeight", 0.5f, "The Book of Bones: weight in cursed luggage and coffins, as a share of its vanilla weight (= the Skull's and Dagger's).");
        FusionCards = cfg.Bind("Fusion", "UnlockedFusionCards", true, "Once you've made a fusion, crates that roll one of its ingredients also offer the fused item as an extra card.");
        StatueIngredientChance = cfg.Bind("Fusion", "StatueIngredientChance", 0.1f, "Blessing statues / Ancient Luggage: chance the +2 card becomes the one ingredient a maxed item of yours is missing.");
        StatueJackpotChance = cfg.Bind("Fusion", "StatueJackpotChance", 0.05f, "Revive statue blessings: chance the +2 card becomes a jackpot (a finished fusion or a dark item - see JackpotDarkShare). Every biome.");
        AncientJackpotChance = cfg.Bind("Fusion", "AncientJackpotChance", 0.1f, "Ancient Luggage: chance the +2 card becomes a jackpot. Every biome.");
        JackpotDarkShare = cfg.Bind("Fusion", "JackpotDarkShare", 0.5f, "Of the jackpots, this share is a dark item (Cursed Skull / Ritual Dagger / Book of Bones; solo: the Book only), the rest a finished fusion.");
        ReviveDarkChance = cfg.Bind("Fusion", "ReviveDarkChance", 0.1f, "A statue revive: chance one revived scout is offered the Skull / Dagger / Book of Bones (solo: the Book). Once per run for the whole squad.");
        EqualTreeItems = cfg.Bind("Loot", "EqualTreeItems", true, "Every tree's base item is equally common in every crate (Glider and Lantern join every crate); higher tree items never roll.");
        ShoreOtherItems = cfg.Bind("Loot", "ShoreOtherItems", 0.1f, "On the Shore, crate items outside the trees are this much as likely (1 = no change).");
        CursedCopies = cfg.Bind("Relics", "CursedCopies", true, "Cursed Skull: everyone else gets cursed copies of the user's relics. Ritual Dagger: the killer copies or stacks one relic. Copies shatter on death and don't raise the blood moon.");
        DaggerStackCooldown = cfg.Bind("Relics", "DaggerStackCooldown", 0.6f, "Each dagger stack multiplies that relic's cooldown by this (0.6 = 40% shorter per stack).");
        GlassPerTrigger = cfg.Bind("Relics", "GlassPerTrigger", 5, "Petrification ('glass', 0-100) added every time a relic triggers.");
        StoveGlass = cfg.Bind("Relics", "StoveFoodGlass", 5, "Glass (0-100) healed by eating food cooked on a Portable Stove (split across its bites; 0 = off).");
        RelicCooldown = cfg.Bind("Relics", "TriggerCooldown", 2f, "Seconds before the same relic can fire on the same scout again (each dagger stack shortens it, down to 0.5s).");
        // (New key: the old GlideLandSeconds = 1 in existing configs would keep the limit on.)
        GlideLandSeconds = cfg.Bind("Relics", "GlideRelicLandSeconds", 0f, "0 = every glide fires your relics (closing and reopening mid-air too). Above 0: after a glide fires them, the next only counts once you've stood on the ground this many seconds.");
        BadgeGlassScale = cfg.Bind("Relics", "BadgeGlassScale", 0.5f, "Glass a badge leaves on its tools' relics (0 = free, 0.5 = half, 1 = full cost).");
        SunscreenWeight = cfg.Bind("Loot", "SunscreenWeight", 0.25f, "Crates outside the Mesa: Sunscreen's weight as a share of one tree item's (then scaled by the rarity curve - rarer early, less rare late).");
        MaxedTreeWeight = cfg.Bind("Loot", "MaxedTreeWeight", 0.25f, "Crates: a tree the rolling scout has maxed comes up this much as often (1 = no change).");
        AuraRadius = cfg.Bind("Relics", "GenerosityMealRadius", 12f, "Range (m) of the Generosity relic's meal sharing.");
        RelicVeil = cfg.Bind("Relics", "RelicVeilSeconds", 5f, "Under a blood moon, a relic firing hides you from the Scoutmaster, zombies and tornado for this long (meteors still fall; 0 = off).");

        TreePower = cfg.Bind("ItemTrees", "PowerScale", 1f, "Scales how far below vanilla low-level items start (1 = as designed, 0 = no penalty).");
        AirlineHeal = cfg.Bind("ItemTrees", "AirlineFoodHeal", 0.1f, "Food tree: injury Airline Food heals on top of its vanilla meal (0-1 of the bar; cooking doesn't change it).");
        AirlineGlass = cfg.Bind("ItemTrees", "AirlineFoodGlass", 10, "Food tree: glass (0-100) Airline Food removes.");
        GranolaHeal = cfg.Bind("ItemTrees", "GranolaHeal", 0.05f, "Food tree: injury a full Granola Bar (level 6) heals; levels 4 / 5 give 3/5 and 4/5 of it.");
        GranolaGlass = cfg.Bind("ItemTrees", "GranolaGlass", 5, "Food tree: glass (0-100) a full Granola Bar removes; levels 4 / 5 give 3 / 4.");
        TopKitHunger = cfg.Bind("ItemTrees", "TopKitHunger", 0.05f, "Bandage tree: hunger (0-1 of the bar) the level-6 Radiant Health Pack and Abyssal Medkit also fill.");
        TopKitGlass = cfg.Bind("ItemTrees", "TopKitGlass", 5, "Bandage tree: glass (0-100) the level-6 Radiant Health Pack also removes (the Abyssal Medkit shatters all of it anyway).");
        BalloonFlightSeconds = cfg.Bind("ItemTrees", "BalloonFlightSeconds", "0, 2.5, 4, 6, 8, 10", "Seconds of flight per balloon level: three single-balloon levels, then three bunch levels (0 = vanilla floating). Every flight climbs at BalloonRiseSpeed: at 2 m/s that is 5 / 8 / 12 / 16 / 20m.");
        BalloonRiseSpeed = cfg.Bind("ItemTrees", "BalloonRiseSpeed", 2f, "How fast a balloon flight climbs (m/s).");
        BalloonAirControl = cfg.Bind("ItemTrees", "BalloonAirControl", 2f, "Steering multiplier while a balloon flies you.");
        BalloonPopJump = cfg.Bind("ItemTrees", "BalloonPopJump", 6f, "Upward speed (units/s) of popping a balloon by jumping in the air (0 = off).");
        TintItems = cfg.Bind("ItemTrees", "TintItems", true, "Colour items by their level.");

        RarityCurve = cfg.Bind("Loot", "RarityCurve", true, "Early biomes roll mostly common items; rarer items get more likely as the run climbs.");

        Blessings = cfg.Bind("Systems", "Blessings", true, "Revive statues bless every scout (+2 levels or a fusion) when nobody needs reviving. Ancient Luggage blesses whoever opens it.");
        FlareReroll = cfg.Bind("Flare", "Reroll", true, "An unlit flare in your pockets adds a REROLL button to crate screens: the flare is spent and the whole crate is dealt again.");
        FlareSupplyDrop = cfg.Bind("Flare", "SupplyDrop", true, "Lighting a flare anywhere but the peak slams a big supply crate down beside you (at the peak it still calls the helicopter).");
        FlareAncientChance = cfg.Bind("Flare", "AncientChance", 0.2f, "Chance a flare's crate is an Ancient Luggage (a blessing) instead of a big crate.");
        TimedDrops = cfg.Bind("Supply", "TimedDrops", true, "Host: while anyone is out on the mountain (not at a campfire, the crash site or a blood moon), a supply crate floats down on balloons near the scout carrying the least.");
        DropSolo = cfg.Bind("Supply", "IntervalSolo", "180-360", "Solo: seconds out on the mountain between timed supply drops, rolled fresh after each one (\"min-max\", or one number).");
        DropDuo = cfg.Bind("Supply", "IntervalTwoScouts", "180-300", "2 scouts: seconds between timed supply drops (\"min-max\").");
        DropSmall = cfg.Bind("Supply", "IntervalThreeToFour", "180-240", "3-4 scouts: seconds between timed supply drops (\"min-max\").");
        DropBig = cfg.Bind("Supply", "IntervalFivePlus", "180", "5+ scouts: seconds between timed supply drops (\"min-max\").");
        DropClusterScouts = cfg.Bind("Supply", "GroupDropsFrom", 5, "With at least this many scouts, every group more than GroupDistance apart gets its own supply crate (picks for that group only). Fewer scouts: one crate for the whole squad.");
        DropClusterMetres = cfg.Bind("Supply", "GroupDistance", 100f, "Supply drop groups: everyone in a group is within this many metres of everyone else in it.");
        EmptyHandedPicks = cfg.Bind("Supply", "EmptyHandedPicks", true, "Opening a supply crate with empty pockets lets you take two cards.");
        DropTrappedChance = cfg.Bind("Supply", "TrappedChance", 0.12f, "Chance a supply crate carries the level's own crate trap (the Citadel's sleep gas). Only where the level has one; 0 = never.");
        FindCards = cfg.Bind("Systems", "FindCards", true, "Flare and Lantern show a card the first time you pick each up in a run.");
        CurioRelics = cfg.Bind("Curios", "StartingRelics", true, "Bugle, Binoculars, Compass, Frisbee and Bing Bong offer a starting relic (one per scout, never raises a blood moon).");
        CurioWakeNotice = cfg.Bind("Curios", "WakeNotice", true, "Waking at the crash site shows \"Something survived the crash\" (once per run, only while you have no starting relic).");
        CurioPingSeconds = cfg.Bind("Curios", "PingSeconds", 10f, "Binoculars / Compass relics: seconds between pings.");
        CurioCrateBoost = cfg.Bind("Curios", "CrateBoost", 0.1f, "Bugle / Binoculars / Compass relics: chance each card of a crate you roll becomes healing / food / a tool.");
        FrisbeeHitsScouts = cfg.Bind("Curios", "FrisbeeHitsScouts", true, "Frisbee relic: throws knock back other scouts too (off = enemies only).");
        FrisbeeForce = cfg.Bind("Curios", "FrisbeeForce", 1500f, "Frisbee relic: knockback force of a fast throw.");
        BingBongMinSeconds = cfg.Bind("Curios", "BingBongMinSeconds", 120f, "Bing Bong relic: shortest wait between pranks.");
        BingBongMaxSeconds = cfg.Bind("Curios", "BingBongMaxSeconds", 240f, "Bing Bong relic: longest wait between pranks.");
        // One time only (0.12.24): the old default of 180s (2-3 min) becomes 240s (2-4 min); a value someone set stays.
        var bingTimer = cfg.Bind("Curios", "BingBongTimerUpdated", false, "Set by the mod once it has moved the old Bing Bong timer default. Leave it.");
        if (!bingTimer.Value)
        {
            if (System.Math.Abs(BingBongMaxSeconds.Value - 180f) < 0.01f) BingBongMaxSeconds.Value = 240f;
            bingTimer.Value = true;
        }
        BingBongHideMin = cfg.Bind("Curios", "BingBongHideMin", 30f, "Bing Bong hide and seek: shortest time (s) one of your items is gone.");
        BingBongHideMax = cfg.Bind("Curios", "BingBongHideMax", 60f, "Bing Bong hide and seek: longest time (s) one of your items is gone.");
        BingBongDarkSeconds = cfg.Bind("Curios", "BingBongDarkSeconds", 30f, "Bing Bong lights out: how long (s) your screen goes dark.");
        BingBongBlinkRange = cfg.Bind("Curios", "BingBongBlinkRange", 30f, "Bing Bong teleport: how far (m) he can blink you, up, down or sideways, to flat ground.");
        BingBongGiftMaxChance = cfg.Bind("Curios", "BingBongGiftMaxChance", 0.1f, "Bing Bong gift: the chance (0-1) it comes at max level instead of level 1.");
        FlashlightKey = cfg.Bind("Curios", "FlashlightKey", Key.F, "Bing Bong lights out: the key that turns your flashlight on and off.");
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
        if (defaults.Value < 6)
        {
            DevShareRelics.Value = true;
            defaults.Value = 6;
            Plugin.Log.LogInfo("Config: shared relic pickups on (new default).");
        }
        if (defaults.Value < 7)
        {
            BmTornadoNear.Value = 150f;
            defaults.Value = 7;
            Plugin.Log.LogInfo("Config: blood moon tornado slows to just under sprint speed within 150m (new default).");
        }
        if (defaults.Value < 8)
        {
            BmTornadoSprintRef.Value = 7.8f;
            defaults.Value = 8;
            Plugin.Log.LogInfo("Config: scout sprint speed locked at the measured 7.8 (new default).");
        }
        if (defaults.Value < 9)
        {
            BalloonFlightSeconds.Value = "0, 2.5, 4, 6, 8, 10";
            defaults.Value = 9;
            Plugin.Log.LogInfo("Config: balloon flights nerfed - 2 m/s, 20m at most (new default).");
        }
        if (defaults.Value < 10)
        {
            XpCampfire.Value = 0;
            defaults.Value = 10;
            Plugin.Log.LogInfo("Config: no campfire XP - the lit campfire blesses you instead (new default).");
        }
        if (defaults.Value < 11)
        {
            if (GlassPerTrigger.Value == 3) GlassPerTrigger.Value = 5;
            defaults.Value = 11;
            Plugin.Log.LogInfo("Config: relics cost 5 glass a trigger (new default).");
        }
        if (defaults.Value < 12)
        {
            BmRelay.Value = true;
            defaults.Value = 12;
            Plugin.Log.LogInfo("Config: blood moons are relays - one hazard at a time (new default; Stacked on the airport ticket).");
        }
    }
}

/// <summary>When a statue amulet's relic cards open (see [Relics] AmuletCards).</summary>
internal enum AmuletCards { OnRead, OnPickup }
