using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Peak.Afflictions;
using Peak.Network;
using UnityEngine;

namespace PeakRoguelike;

/// <summary>
/// The numbers behind each item tree level. Everything is a multiplier on the item's own
/// vanilla value, eased toward 1 by ModConfig.TreePower.
/// </summary>
internal static class Stats
{
    /// <summary>
    /// An item's level: its own stamp (ItemLevels). Not stamped yet (lying where it spawned):
    /// what its user's discovery level would stamp it - feeder first, then holder, then us.
    /// </summary>
    public static (int level, int branch) For(Item item, TreeId id)
    {
        if (ItemLevels.Of(item) is { } own) return own;
        Character? user = null;
        try { if (item.TryGetFeeder(out var feeder)) user = feeder; } catch { }
        user ??= item.holderCharacter ?? Character.localCharacter;
        var (lv, br) = RunState.LevelOf(user, id);
        if (ItemLevels.KindOf(item) is { } k) return (ItemLevels.Clamp(k, lv, br), k.Branch == 0 || k.BothPaths ? br : k.Branch);
        return (Mathf.Max(1, lv), br);
    }

    /// <summary>The level of the item in a pocket (stamp, else our discovery level for it).</summary>
    public static (int level, int branch) For(ItemSlot? slot, TreeId id)
    {
        if (slot?.prefab == null) return (Mathf.Max(1, RunState.Level(id)), RunState.Branch(id));
        var k = ItemLevels.KindOf(slot.prefab);
        int lv = ItemLevels.LevelOf(slot);
        return lv > 0 && k != null ? (lv, ItemLevels.BranchOf(k, slot.data)) : (Mathf.Max(1, RunState.Level(id)), RunState.Branch(id));
    }

    /// <summary>Blend a designed multiplier toward vanilla by the config's power scale.</summary>
    private static float Ease(float designed) => Mathf.Lerp(1f, designed, ModConfig.TreePower.Value);

    public static bool On => ModConfig.ItemTrees.Value;

    /// <summary>An item's own step (1-3): 33% / 66% / 100% of vanilla.</summary>
    public static float Third(int step) => Ease(step <= 1 ? 1f / 3f : step == 2 ? 2f / 3f : 1f);
    /// <summary>Its drawback per step: 200% / 150% / 100% of vanilla.</summary>
    public static float Drag(int step) => Ease(step <= 1 ? 2f : step == 2 ? 1.5f : 1f);

    /// <summary>
    /// The step of an evolved item (levels 4-6 of its tree). Someone holding it without having
    /// reached it (a friend's, a statue gift) uses it at its first step.
    /// </summary>
    private static int Evolved(int lv, int br, int needBranch = 0) =>
        lv >= 4 && (needBranch == 0 || br == needBranch) ? lv - 3 : 1;

    // --- rope ------------------------------------------------------------------------------

    // Cards promise real numbers (CardStats), so every levelled value below is snapped to what a
    // card shows: whole metres, half/whole seconds, easy fractions of a bar. Vanilla stays vanilla.

    public static float Metres(float units) => units * CharacterStats.unitsToMeters;
    public static float Units(float metres) => metres / CharacterStats.unitsToMeters;

    /// <summary>
    /// Rope a spool holds at this level, in segments (null = the vanilla spool). Levels are shares
    /// of one full rope (the game caps a rope at Rope.MaxSegments), in whole metres.
    /// </summary>
    public static float? RopeSegments(bool anti, int lv, int br, float vanillaFuel, float spacing)
    {
        if (anti) return null; // floating ropes are fusions: always the full vanilla spool
        float share = Third(lv);
        if (share >= 0.999f) return null;
        float full = Mathf.Min(vanillaFuel, Rope.MaxSegments) * spacing;
        float m = Mathf.Max(1f, Mathf.Round(Metres(full * share)));
        return Units(m) / spacing;
    }

    public static float RopeShooterRange(Item shooter, int lv, int br) =>
        Items.IsAntiRopeShooter(shooter) ? 1f : Third(Evolved(lv, br));

    /// <summary>A cannon's range multiplier, snapped to whole metres.</summary>
    public static float ShooterScale(Item shooter, int lv, int br, float baseUnits)
    {
        float m = RopeShooterRange(shooter, lv, br);
        if (m >= 0.999f || baseUnits <= 0f) return 1f;
        return Units(Mathf.Max(1f, Mathf.Round(Metres(baseUnits * m)))) / baseUnits;
    }

    /// <summary>Seconds of climbing a freshly tied rope takes before it snaps (null = never; blue sun handled apart).</summary>
    public static float? RopeSnapSeconds(int lv) => ModConfig.TreePower.Value <= 0f ? null : lv switch
    {
        <= 1 => 2f,
        2 => 4f,
        _ => null,
    };
    public static bool BlueRopeSnaps => GreenBreaks;

    // --- piton -----------------------------------------------------------------------------

    /// <summary>Total seconds a piton holds someone before it breaks (null = it never does).</summary>
    public static float? PitonHoldSeconds(int lv) => ModConfig.TreePower.Value <= 0f && !GreenBreaks ? null : lv switch
    {
        <= 1 => 2f,
        2 => 4f,
        _ => GreenBreaks ? Random.Range(8f, 12f) : null, // blue sun: even the best pitons give
    };

    /// <summary>What a piton's card promises (the blue sun's extra cracking isn't on the card).</summary>
    public static float? PitonSeconds(int lv) => ModConfig.TreePower.Value <= 0f ? null : lv switch
    {
        <= 1 => 2f,
        2 => 4f,
        _ => null,
    };

    private static bool GreenBreaks => Suns.Green && ModConfig.SunBlueBreaks.Value;

    // --- glider / balloons / lantern ---------------------------------------------------------

    public static float GliderStamina(int lv) => Drag(lv);
    public static float GliderLift(int lv) => Third(lv);

    /// <summary>
    /// Seconds a tied balloon flies you (0 = it only slows your fall, like vanilla). A single
    /// balloon is levels 1-3, a bunch levels 4-6 (config BalloonFlightSeconds, six numbers).
    /// </summary>
    public static float BalloonFlight(bool bunch, int lv)
    {
        var secs = BalloonFlightTable;
        int i = bunch ? 2 + Evolved(lv, 0) : Mathf.Clamp(lv, 1, 3) - 1;
        return On && i < secs.Length ? secs[i] : 0f;
    }

    /// <summary>Balloons one single-balloon item ties: two from level 3.</summary>
    public static int BalloonTies(int lv) => On && lv >= 3 ? 2 : 1;

    /// <summary>One steady climb speed (units/s) for every balloon flight (config BalloonRiseSpeed, m/s).</summary>
    public static float BalloonRiseSpeed => Mathf.Max(0.1f, ModConfig.BalloonRiseSpeed.Value) / CharacterStats.unitsToMeters;

    private static string flightKey = "";
    private static float[] flightTable = new float[0];
    private static float[] BalloonFlightTable
    {
        get
        {
            string s = ModConfig.BalloonFlightSeconds.Value;
            if (s == flightKey) return flightTable;
            flightKey = s;
            flightTable = s.Split(',').Select(p => float.TryParse(p.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? Mathf.Max(0f, f) : 0f).ToArray();
            return flightTable;
        }
    }

    /// <summary>Glider stamina per second at this level, snapped so a full bar glides whole/half seconds.</summary>
    public static float GliderStamUse(int lv, float baseStam, float opening)
    {
        float d = GliderStamina(lv);
        if (Mathf.Abs(d - 1f) < 0.001f || baseStam <= 0f) return baseStam;
        float secs = Amount.SnapSeconds((1f - opening) / (baseStam * d));
        return (1f - opening) / secs;
    }

    /// <summary>Seconds of gliding on a full stamina bar (after the opening cost).</summary>
    public static float GlideSeconds(int lv, float baseStam, float opening) =>
        (1f - opening) / Mathf.Max(0.0001f, GliderStamUse(lv, baseStam, opening));

    public static float BurnTime(int lv) => Third(lv);

    /// <summary>A lantern's glow (warmth, protection and light reach) multiplier, snapped to whole metres.</summary>
    public static float GlowScale(int lv, float baseUnits)
    {
        float m = On ? Third(lv) : 1f;
        if (m >= 0.999f || baseUnits <= 0f) return 1f;
        return Units(Mathf.Max(1f, Mathf.Round(Metres(baseUnits * m)))) / baseUnits;
    }

    /// <summary>A lantern's burn time in seconds (vanilla burns 1 fuel a second), whole seconds.</summary>
    public static float LanternSeconds(int lv, float startingFuel)
    {
        float b = BurnTime(lv);
        return b >= 0.999f ? startingFuel : Mathf.Max(1f, Mathf.Round(startingFuel * b));
    }

    // --- snapping consumables ------------------------------------------------------------------

    /// <summary>A status change scaled by level, snapped to an easy fraction of the bar.</summary>
    public static float SnapStatus(float vanilla, float scale) =>
        Mathf.Abs(scale - 1f) < 0.001f ? vanilla : Amount.Snap(vanilla * scale);

    /// <summary>
    /// The multiplier for an item's bonus stamina: its actions together (the Sports Drink has two)
    /// land on an easy fraction.
    /// </summary>
    public static float StaminaFactor(Item item, float scale)
    {
        if (Mathf.Abs(scale - 1f) < 0.001f) return scale;
        float total = item.GetComponents<Action_GiveExtraStamina>().Sum(a => a.amount);
        if (total <= 0f) return scale;
        return Amount.Snap(total * scale) / total;
    }

    public static float SnapTime(float vanilla, float scale) =>
        Mathf.Abs(scale - 1f) < 0.001f ? vanilla : Amount.SnapSeconds(vanilla * scale);

    public static float SnapCrash(float vanilla, float scale) =>
        Mathf.Abs(scale - 1f) < 0.001f ? vanilla : Amount.Snap(vanilla * scale);

    /// <summary>Seconds to apply a bandage / med kit at this level.</summary>
    public static float UseSeconds(Item item, int lv, int br, float baseTime)
    {
        float d = UseTime(item, lv, br);
        return Mathf.Abs(d - 1f) < 0.001f ? baseTime : Amount.SnapSeconds(baseTime * d);
    }

    // --- consumables ---------------------------------------------------------------------------

    /// <summary>How strong a levelled consumable is: (healing / stamina / cooling, buff duration, crash).</summary>
    public static (float status, float time, float crash) Potency(Item item, int lv, int br)
    {
        // Holy pack: half at its first step, everything after. The dark medkit converts instead (Medicine).
        if (Items.IsMedKit(item)) return (br == 2 ? 1f : Evolved(lv, br) <= 1 ? HolyFirstStep : 1f, 1f, 1f);
        if (Items.IsEnergyDrink(item)) { int s = Evolved(lv, br); return (1f, Third(s), Drag(s)); }
        return Trees.Classify(item) switch
        {
            TreeId.Bandage or TreeId.SportsDrink => (Third(lv), 1f, 1f),
            _ => (1f, 1f, 1f),
        };
    }

    /// <summary>Healing items apply at vanilla speed on every level - only the amount healed scales.</summary>
    public static float UseTime(Item item, int lv, int br) => 1f;

    /// <summary>The holy pack's first step heals this share; the dark medkit's converts it.</summary>
    public static float HolyFirstStep => Ease(0.5f);

    /// <summary>A medkit's own step (1-3 for levels 4-6).</summary>
    public static int KitStep(int lv, int br) => Evolved(lv, br);
}

/// <summary>
/// Lives on every item that belongs to a tree. Applies per-frame stat overrides from the
/// holder's level (vanilla values are cached on first sight) and colours the item.
/// </summary>
public class LeveledItem : MonoBehaviour
{
    public Item Item = null!;
    public TreeId Tree;
    /// <summary>The spool's own capacity / minimum, before levels touch them (-1 = not seen yet).</summary>
    public float VanillaRopeFuel = -1f, VanillaMinSegments;

    private Glider? glider;
    private RopeTier? ropeTier;
    private RopeShooter? shooter;
    private Lantern? lantern;
    private StatusField? warmth;
    private Peak.GloomSafeZone? glow;
    private float baseStam, baseYDrag, baseSoar, baseCast, baseShotMax, baseShotLen, baseUseTime;
    private float baseWarmth, baseGlow, baseGlowVisual, baseReach;

    private (int, int) shownLevel = (-1, -1);

    public (int level, int branch) Level => Stats.For(Item, Tree);

    private void Awake()
    {
        Item = GetComponent<Item>();
        Tree = Trees.Classify(Item);
        glider = GetComponentInChildren<Glider>(true);
        ropeTier = GetComponent<RopeTier>();
        shooter = GetComponent<RopeShooter>();
        lantern = GetComponent<Lantern>();
        if (glider) { baseStam = glider!.stamUse; baseYDrag = glider.extraYDrag; baseSoar = glider.soarForwardForce; }
        if (ropeTier) baseCast = ropeTier!.castTime;
        if (shooter) { baseShotMax = shooter!.maxLength; baseShotLen = shooter.length; }
        // The fused Faerie Lantern sizes its own glow (Crafted).
        if (lantern && !Items.IsFaerie(Item))
        {
            warmth = GetComponent<StatusField>();
            glow = GetComponent<Peak.GloomSafeZone>();
            if (warmth) baseWarmth = warmth!.radius;
            if (glow) { baseGlow = glow!.statusProtectionRadius; baseGlowVisual = glow.visualRadius; }
            if (lantern!.lanternLight) baseReach = lantern.lanternLight.range;
        }
        baseUseTime = Item.usingTimePrimary;
    }

    private void Update()
    {
        if (Tree == TreeId.None) return;
        var (lv, br) = Level;
        bool on = Stats.On;

        if (glider)
        {
            glider!.stamUse = on ? Stats.GliderStamUse(lv, baseStam, glider.extraOpeningCost) : baseStam;
            glider.extraYDrag = baseYDrag * (on ? Stats.GliderLift(lv) : 1f);
            glider.soarForwardForce = baseSoar * (on ? Stats.GliderLift(lv) : 1f);
        }
        if (ropeTier) ropeTier!.castTime = baseCast;
        if (shooter)
        {
            float m = on ? Stats.ShooterScale(Item, lv, br, baseShotMax) : 1f;
            shooter!.maxLength = baseShotMax * m;
            shooter.length = baseShotLen * m;
        }
        if (glow || warmth)
        {
            // One glow: warmth, protection and the light's reach shrink together, so what you see lit is what keeps you safe.
            float g = Stats.GlowScale(lv, glow ? baseGlow : baseWarmth);
            if (warmth) warmth!.radius = baseWarmth * g;
            if (glow) { glow!.statusProtectionRadius = baseGlow * g; glow.visualRadius = baseGlowVisual * g; }
            if (baseReach > 0f) lantern!.lanternLight.range = baseReach * g;
        }
        if (Tree == TreeId.Bandage) Item.usingTimePrimary = on ? Stats.UseSeconds(Item, lv, br, baseUseTime) : baseUseTime;

        // A sports drink can turn out to be the Sugar-Rush Cocktail (marked after it spawns).
        if (Tree == TreeId.SportsDrink && !sugar && Time.time >= nextSugarCheck)
        {
            nextSugarCheck = Time.time + 0.5f;
            if (SugarRush.IsMarked(Item))
            {
                sugar = true;
                tint.Apply(gameObject, Color.white);
                gameObject.AddComponent<SugarRushLook>();
            }
        }
        if (sugar) return;

        if ((lv, br) != shownLevel)
        {
            shownLevel = (lv, br);
            tint.Apply(gameObject, ItemTint.Multiplier(on && ModConfig.TintItems.Value ? Trees.LevelColor(Tree, lv, br) : Trees.Plain));
        }
    }

    private readonly ItemTint tint = new();
    private bool sugar;
    private float nextSugarCheck;
}

[HarmonyPatch]
internal static class TreePatches
{
    // --- attach + naming -------------------------------------------------------------------

    [HarmonyPostfix, HarmonyPatch(typeof(Item), "Start")]
    private static void Attach(Item __instance)
    {
        // Food-tree snacks are full-strength vanilla items: each one already *is* its level.
        if (__instance.GetComponent<LeveledItem>() || Trees.Classify(__instance) is TreeId.None or TreeId.Food) return;
        __instance.gameObject.AddComponent<LeveledItem>();
    }

    /// <summary>Items show their level name ("Frayed Cord") instead of the vanilla one.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(Item), nameof(Item.GetName))]
    private static void LevelName(Item __instance, ref string __result)
    {
        if (!ModConfig.Leveling.Value) return;
        var li = __instance.GetComponent<LeveledItem>();
        if (li == null || li.Tree == TreeId.None) return;
        var tree = Trees.Get(li.Tree);
        if (tree == null) return;
        var (lv, br) = li.Level;
        // Only the item your level hands out wears its name (a plain spool stays "Rope Spool"
        // once you're on the cannon path; a friend's med kit keeps its own name).
        var def = tree.At(lv, br);
        if (def.Grants == null || Items.Find(def.Grants) is not { } granted || granted.itemID != __instance.itemID) return;
        __result = def.Name;
    }

    // --- rope ----------------------------------------------------------------------------

    internal static float RopeSpacing(RopeSpool spool)
    {
        var rope = spool.rope != null ? spool.rope : spool.ropePrefab != null ? spool.ropePrefab.GetComponent<Rope>() : null;
        return rope != null && rope.spacing > 0.01f ? rope.spacing : 0.75f;
    }

    /// <summary>
    /// A level's rope length is the spool's real capacity (ropeStartFuel), so the game's own
    /// use bar is exact: full = all of your level's rope. Fuel is clamped, never stretched.
    /// Lengths are whole metres of one full rope - what the card says (Stats.RopeSegments).
    /// </summary>
    [HarmonyPrefix, HarmonyPatch(typeof(RopeSpool), "Update")]
    private static void RopeBudget(RopeSpool __instance)
    {
        if (!__instance.photonView.IsMine || __instance.item == null || __instance.item.itemState != ItemState.Held) return;
        var li = __instance.GetComponent<LeveledItem>();
        if (li == null) return;
        if (li.VanillaRopeFuel < 0f) { li.VanillaRopeFuel = __instance.ropeStartFuel; li.VanillaMinSegments = __instance.minSegments; }

        var (lv, br) = Stats.For(__instance.item, TreeId.Rope);
        float capacity = Stats.On
            ? Stats.RopeSegments(__instance.isAntiRope, lv, br, li.VanillaRopeFuel, RopeSpacing(__instance)) ?? li.VanillaRopeFuel
            : li.VanillaRopeFuel;
        if (Badges.Has(BadgeId.KnotTying)) capacity *= 1f + Badges.Buff;
        __instance.minSegments = Mathf.Min(li.VanillaMinSegments, capacity * 0.5f);
        __instance.ropeStartFuel = capacity;

        float fuel = __instance.RopeFuel;
        if (fuel > capacity + 0.01f)
        {
            __instance.GetData<FloatItemData>(DataEntryKey.Fuel).Value = capacity;
            __instance.ropeFuel = capacity;
        }
    }

    /// <summary>Frayed Cord / Knotted Line: the rope you just tied snaps after a few seconds of climbing.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(RopeTier), "SpawnRope")]
    private static void MarkFrayed(RopeTier __instance)
    {
        if (!Stats.On || __instance.item?.holderCharacter is not { IsLocal: true }) return;
        var spool = __instance.GetComponent<RopeSpool>();
        if (spool == null || spool.rope == null || spool.isAntiRope || spool.rope.GetComponent<FrayedRope>()) return;
        float? seconds = Stats.RopeSnapSeconds(Stats.For(__instance.item!, TreeId.Rope).level);
        if (seconds == null && !Stats.BlueRopeSnaps) return;
        var frayed = spool.rope.gameObject.AddComponent<FrayedRope>();
        frayed.ClimbSeconds = seconds ?? 0f; // 0 = the blue sun's rule: one climb, then it snaps
    }

    // --- piton ---------------------------------------------------------------------------

    private static readonly AccessTools.FieldRef<CharacterItems, ItemSlot> SpikeSlot =
        AccessTools.FieldRefAccess<CharacterItems, ItemSlot>("currentClimbingSpikeItemSlot");

    /// <summary>The piton about to be hammered in: its own level (the slot is emptied by then).</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(CharacterItems), "HammerClimbingSpike")]
    private static void PitonLevel(CharacterItems __instance, out int __state)
    {
        __state = 1;
        try { __state = Stats.For(SpikeSlot(__instance), TreeId.Piton).level; } catch { }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(CharacterItems), "HammerClimbingSpike")]
    private static void MarkBrittle(CharacterItems __instance, RaycastHit hit, int __state)
    {
        if (!Stats.On || !__instance.character.IsLocal || Badges.Has(BadgeId.Bouldering)) return;
        if (Stats.PitonHoldSeconds(__state) is { } seconds)
            Net.MarkBrittlePiton(hit.point, seconds);
    }

    // --- balloons ------------------------------------------------------------------------

    // Flight (levels 2+) lives in BalloonFlight. Plain floating is vanilla - but the red sun halves it.

    private static readonly Dictionary<CharacterBalloons, (float, float)> balloonBase = new();

    [HarmonyPrefix, HarmonyPatch(typeof(CharacterBalloons), "FixedUpdate")]
    private static void BalloonLift(CharacterBalloons __instance)
    {
        if (__instance.character == null || !__instance.character.IsLocal) return;
        if (!balloonBase.TryGetValue(__instance, out var b))
            balloonBase[__instance] = b = (__instance.balloonFloatAmount, __instance.balloonJumpAmount);
        float m = SunCaps.MoveCap;
        __instance.balloonFloatAmount = b.Item1 * m;
        __instance.balloonJumpAmount = b.Item2 * m;
    }

    // --- light (lanterns only; candles stay vanilla) ------------------------------------

    [HarmonyPostfix, HarmonyPatch(typeof(Lantern), "Update")]
    private static void LanternBurn(Lantern __instance)
    {
        if (!Stats.On || !__instance.lit || !__instance.HasAuthority() || __instance.fuel <= 0f || Items.IsFaerie(__instance.item)) return;
        __instance.fuel = AdjustBurn(__instance, __instance.fuel);
        __instance.GetData<FloatItemData>(DataEntryKey.Fuel).Value = __instance.fuel;
    }

    /// <summary>Vanilla burns 1 fuel/second; refund or charge extra so a full lantern lasts the card's seconds.</summary>
    private static float AdjustBurn(Lantern lantern, float fuel)
    {
        float full = Mathf.Max(1f, lantern.startingFuel);
        float burn = Stats.LanternSeconds(Stats.For(lantern.item, TreeId.Light).level, full) / full;
        return Mathf.Max(0.01f, fuel + Time.deltaTime * (1f - 1f / burn));
    }

    // --- consumables: scale the good parts of bandages / drinks / lollipops ---------------

    private static (float status, float time, float crash) Scale(Item item)
    {
        if (!Stats.On || item == null || SugarRush.IsMarked(item)) return (1f, 1f, 1f); // the cocktail is always full strength
        var li = item.GetComponent<LeveledItem>();
        if (li == null || li.Tree is not (TreeId.Bandage or TreeId.SportsDrink)) return (1f, 1f, 1f);
        var (lv, br) = Stats.For(item, li.Tree);
        return Stats.Potency(item, lv, br);
    }

    private static float ConsumableScale(Item item, out TreeId tree)
    {
        tree = TreeId.None;
        return Scale(item).status;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(Action_ModifyStatus), nameof(Action_ModifyStatus.RunAction))]
    private static void ScaleStatus(Action_ModifyStatus __instance, out float __state)
    {
        __state = __instance.changeAmount;
        if (__instance.changeAmount < 0f) __instance.changeAmount = Stats.SnapStatus(__instance.changeAmount, ConsumableScale(__instance.item, out _));
    }

    [HarmonyPostfix, HarmonyPatch(typeof(Action_ModifyStatus), nameof(Action_ModifyStatus.RunAction))]
    private static void ScaleStatusRestore(Action_ModifyStatus __instance, float __state) => __instance.changeAmount = __state;

    [HarmonyPrefix, HarmonyPatch(typeof(Action_GiveExtraStamina), nameof(Action_GiveExtraStamina.RunAction))]
    private static void ScaleStamina(Action_GiveExtraStamina __instance, out float __state)
    {
        __state = __instance.amount;
        if (__instance.item != null) __instance.amount *= Stats.StaminaFactor(__instance.item, ConsumableScale(__instance.item, out _));
    }

    [HarmonyPostfix, HarmonyPatch(typeof(Action_GiveExtraStamina), nameof(Action_GiveExtraStamina.RunAction))]
    private static void ScaleStaminaRestore(Action_GiveExtraStamina __instance, float __state) => __instance.amount = __state;

    [HarmonyPrefix, HarmonyPatch(typeof(Action_ApplyInfiniteStamina), nameof(Action_ApplyInfiniteStamina.RunAction))]
    private static void ScaleRush(Action_ApplyInfiniteStamina __instance, out float __state)
    {
        __state = __instance.buffTime;
        __instance.buffTime = Stats.SnapTime(__instance.buffTime, ConsumableScale(__instance.item, out _));
    }

    [HarmonyPostfix, HarmonyPatch(typeof(Action_ApplyInfiniteStamina), nameof(Action_ApplyInfiniteStamina.RunAction))]
    private static void ScaleRushRestore(Action_ApplyInfiniteStamina __instance, float __state) => __instance.buffTime = __state;

    /// <summary>
    /// Buff durations scale with level (the Energy Drink's boost lasts 33% / 66% / 100%), and the
    /// crash at the end runs the other way (200% / 150% / 100% of its drowsiness).
    /// </summary>
    [HarmonyPrefix, HarmonyPatch(typeof(Action_ApplyAffliction), nameof(Action_ApplyAffliction.RunAction))]
    private static void ScaleAffliction(Action_ApplyAffliction __instance, out float[]? __state)
    {
        __state = null;
        var a = __instance.affliction;
        if (a == null) return;
        var (status, time, crash) = Scale(__instance.item);
        float t = time * status; // a bandage-type buff scales with the item's strength too
        if (t == 1f && crash == 1f) return;
        var boost = a as Affliction_FasterBoi;
        __state = new[] { a.totalTime, boost?.drowsyOnEnd ?? 0f };
        if (a.totalTime > 0f) a.totalTime = Stats.SnapTime(a.totalTime, t);
        if (boost != null) boost.drowsyOnEnd = Stats.SnapCrash(boost.drowsyOnEnd, crash);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(Action_ApplyAffliction), nameof(Action_ApplyAffliction.RunAction))]
    private static void ScaleAfflictionRestore(Action_ApplyAffliction __instance, float[]? __state)
    {
        var a = __instance.affliction;
        if (__state == null || a == null) return;
        a.totalTime = __state[0];
        if (a is Affliction_FasterBoi boost) boost.drowsyOnEnd = __state[1];
    }

    /// <summary>Extra effects after the local player finishes a levelled consumable (none any more: every level is its item at 33/66/100%).</summary>
    internal static void AfterConsumed(Item item, Character consumer) { }
}

/// <summary>
/// Evolved consumables keep the effects of everything below them in their tree: the Energy
/// Drink still gives a full Sports Drink, Airline Food also feeds you like a Granola Bar and
/// Trail Mix. The Sugar-Rush Cocktail is a full Energy Drink + Sports Drink + Big Lollipop.
/// The lower items' effects are read off their prefabs, at full strength.
/// </summary>
internal static class Stacking
{
    private static readonly string[] Food = { "TrailMix", "Granola", "AirlineFood" };
    private static readonly string[] Drink = { "SportsDrink", "EnergyDrink" };

    internal static void Apply(Item item, Character consumer)
    {
        if (!Stats.On || !ModConfig.Leveling.Value || consumer == null || !consumer.IsLocal) return;
        var below = new List<string>();
        if (SugarRush.IsMarked(item))
        {
            below.Add("SportsDrink");
            below.Add("Lollipop");
        }
        else
        {
            foreach (var chain in new[] { Food, Drink })
            {
                int at = System.Array.FindIndex(chain, role => Items.Find(role) is { } p && p.itemID == item.itemID);
                if (at > 0) below.AddRange(chain.Take(at));
            }
        }
        foreach (string role in below)
            if (Items.Find(role) is { } prefab) ApplyEffects(prefab, consumer);
        if (below.Count > 0) Plugin.Log.LogInfo($"Stacked {string.Join(", ", below)} onto {item.gameObject.name}.");
    }

    /// <summary>What eating this item would do, done directly (no item needed).</summary>
    private static void ApplyEffects(Item prefab, Character c)
    {
        var aff = c.refs.afflictions;
        foreach (var a in prefab.GetComponents<Action_RestoreHunger>())
            aff.SubtractStatus(CharacterAfflictions.STATUSTYPE.Hunger, a.restorationAmount);
        foreach (var a in prefab.GetComponents<Action_GiveExtraStamina>())
            c.AddExtraStamina(a.amount);
        foreach (var a in prefab.GetComponents<Action_ModifyStatus>())
        {
            if (a.ifSkeleton) continue;
            if (a.changeAmount < 0f) aff.SubtractStatus(a.statusType, -a.changeAmount);
            else aff.AddStatus(a.statusType, a.changeAmount);
        }
        foreach (var a in prefab.GetComponents<Action_ApplyInfiniteStamina>())
            Relics.GiveInfiniteStamina(c, a.buffTime);
        foreach (var a in prefab.GetComponents<Action_ApplyAffliction>())
        {
            if (a.affliction != null) aff.AddAffliction(Relics.Copy(a.affliction));
            if (a.extraAfflictions != null)
                foreach (var x in a.extraAfflictions)
                    if (x != null) aff.AddAffliction(Relics.Copy(x));
        }
    }
}

