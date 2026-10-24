using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace PeakRoguelike;

public enum BadgeId
{
    Cooking, Bouldering, Foraging, Esoterica, LoneWolf, Balloon, LeaveNoTrace, Hasty, BingBong, Naturalist,
    FirstAid, AnimalSerenading, KnotTying, Plunderer, Ultimate, Aeronautics, TwentyFourKarat, Astronomy,
    BundledUp, Toxicology, TreadLightly, Nomad, DisasterResponse, CalciumIntake, Wanderer, HangGliding,
}

/// <summary>
/// Badge relics: the game's own scout badges as run-long relics. A blessing statue offers three
/// (pick one); a revive statue hands every scout a random one; an Ancient Luggage may offer one.
/// Unlike everything else in the mod, a badge card says exactly what it does. No duplicates, and
/// never a badge that copies your own starting relic. They stay through the Void.
/// </summary>
internal class Badges : MonoBehaviour
{
    private const string PropKey = "prl.bdg";
    /// <summary>Every badge buff tops out here (+25% / -25%).</summary>
    internal const float Buff = 0.25f;

    internal sealed class Def
    {
        public BadgeId Id;
        public ACHIEVEMENTTYPE Badge;
        public string Name = "";
        public string Desc = "";
        public Curio Curio = Curio.None;
    }

    private static readonly List<Def> defs = new();
    private static readonly List<BadgeId> owned = new();
    private static Badges? instance;

    internal static IReadOnlyList<BadgeId> Owned => owned;
    internal static bool Has(BadgeId id) => owned.Contains(id);
    internal static Def Get(BadgeId id) => defs.First(d => d.Id == id);

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Badges");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        instance = go.AddComponent<Badges>();
        Define();
    }

    private static int Pct => Mathf.RoundToInt(Buff * 100f);
    /// <summary>Esoterica: added to every Ancient Luggage's chance of a finished fusion.</summary>
    internal const float EsotericaBonus = 0.05f;
    private static int EsotericaPct => Mathf.RoundToInt(EsotericaBonus * 100f);
    /// <summary>Disaster Response: ailments the Rescue Claw heals (0-1 of the bar, shared out).</summary>
    internal const float RescueHeal = 0.25f;
    private static int RescueHealPct => Mathf.RoundToInt(RescueHeal * 100f);
    private static int GlassCutPct => Mathf.RoundToInt((1f - Mathf.Clamp01(ModConfig.BadgeGlassScale.Value)) * 100f);

    private static void Define()
    {
        string noGlass(string what) => $"{what} add {GlassCutPct}% less glass when they fire your relics.";
        void D(BadgeId id, ACHIEVEMENTTYPE badge, string name, string desc, Curio curio = Curio.None) =>
            defs.Add(new Def { Id = id, Badge = badge, Name = name, Desc = desc, Curio = curio });
        string copy(Curio c) => $"The {CurioRelics.Name(c)} starting relic, on top of your own:\n{CurioRelics.Desc(c)}";

        D(BadgeId.Cooking, ACHIEVEMENTTYPE.CookingBadge, "Cooking", $"{noGlass("Cooked foods")}\nCooked food feeds and heals {Pct}% more.");
        D(BadgeId.Bouldering, ACHIEVEMENTTYPE.BoulderingBadge, "Bouldering", $"{noGlass("Pitons")}\nYour pitons go in instantly.");
        D(BadgeId.Foraging, ACHIEVEMENTTYPE.ForagingBadge, "Foraging", $"{noGlass("Berries")}\nBerries feed {Pct}% more; bad berries do {Pct}% less harm.");
        D(BadgeId.Esoterica, ACHIEVEMENTTYPE.EsotericaBadge, "Esoterica", $"Mythical luck: every Ancient Luggage you open has a {EsotericaPct}% better chance to hold a finished fusion.");
        D(BadgeId.LoneWolf, ACHIEVEMENTTYPE.LoneWolfBadge, "Lone Wolf", $"Climbing with no scout within 24m costs {Pct}% less stamina.\nClimbing with company costs {Pct}% more.");
        D(BadgeId.Balloon, ACHIEVEMENTTYPE.BalloonBadge, "Balloon", $"{noGlass("Balloons")}\nBalloon flights go {Pct}% higher.");
        D(BadgeId.LeaveNoTrace, ACHIEVEMENTTYPE.LeaveNoTraceBadge, "Leave No Trace", $"You climb {Pct}% faster.\nEvery item you use adds {ModConfig.GlassPerTrigger.Value} glass.");
        D(BadgeId.Hasty, ACHIEVEMENTTYPE.SpeedClimberBadge, "Hasty", $"You move {Pct}% faster on foot.");
        D(BadgeId.BingBong, ACHIEVEMENTTYPE.BingBongBadge, "Bing Bong", copy(Curio.BingBong), Curio.BingBong);
        D(BadgeId.Naturalist, ACHIEVEMENTTYPE.NaturalistBadge, "Naturalist", $"Natural food (berries, mushrooms, fruit, eggs, honey, hot dogs, marshmallows) adds {GlassCutPct}% less glass when it fires your relics.\nPackaged food adds double glass.");
        D(BadgeId.FirstAid, ACHIEVEMENTTYPE.FirstAidBadge, "First Aid", $"Healing heals you {Pct}% more.\nFalls hurt {Pct}% less with a scout within 24m.");
        D(BadgeId.AnimalSerenading, ACHIEVEMENTTYPE.AnimalSerenadingBadge, "Animal Serenading", copy(Curio.Bugle), Curio.Bugle);
        D(BadgeId.KnotTying, ACHIEVEMENTTYPE.KnotTyingBadge, "Knot Tying", $"{noGlass("Ropes")}\nYour ropes are {Pct}% longer.");
        D(BadgeId.Plunderer, ACHIEVEMENTTYPE.PlundererBadge, "Plunderer", copy(Curio.Compass), Curio.Compass);
        D(BadgeId.Ultimate, ACHIEVEMENTTYPE.UltimateBadge, "Ultimate", copy(Curio.Frisbee), Curio.Frisbee);
        D(BadgeId.Aeronautics, ACHIEVEMENTTYPE.AeronauticsBadge, "Aeronautics", $"{noGlass("Jetpacks and the Scout Cannon")}\nJetpack fuel lasts {Pct}% longer; the cannon launches you {Pct}% harder.");
        D(BadgeId.TwentyFourKarat, ACHIEVEMENTTYPE.TwentyFourKaratBadge, "24 Karat", "You get the Golden Bing Bong: invincible while you hold it - but it's heavy.");
        D(BadgeId.Astronomy, ACHIEVEMENTTYPE.AstronomyBadge, "Astronomy", copy(Curio.Binoculars), Curio.Binoculars);
        D(BadgeId.BundledUp, ACHIEVEMENTTYPE.BundledUpBadge, "Bundled Up", $"You take {Pct}% less cold.");
        D(BadgeId.Toxicology, ACHIEVEMENTTYPE.ToxicologyBadge, "Toxicology", $"You take {Pct}% less poison.");
        D(BadgeId.TreadLightly, ACHIEVEMENTTYPE.TreadLightlyBadge, "Tread Lightly", $"You take {Pct}% less spores.");
        D(BadgeId.Nomad, ACHIEVEMENTTYPE.NomadBadge, "Nomad", $"You take {Pct}% less heat.");
        D(BadgeId.DisasterResponse, ACHIEVEMENTTYPE.DisasterResponseBadge, "Disaster Response", $"Your Rescue Claw heals the scout it pulls: up to {RescueHealPct} points of ailments (not curse or hunger) - enough to wake a passed-out scout.");
        D(BadgeId.CalciumIntake, ACHIEVEMENTTYPE.CalciumIntakeBadge, "Calcium Intake", $"Milk adds {GlassCutPct}% less glass when it fires your relics.\nMilk's invincibility lasts {Pct}% longer.");
        D(BadgeId.Wanderer, ACHIEVEMENTTYPE.WandererBadge, "Wanderer", $"You take {Pct}% less drowsiness.");
        D(BadgeId.HangGliding, ACHIEVEMENTTYPE.HangGlidingBadge, "Hang Gliding", $"{noGlass("Gliders")}\nYou glide down {Pct}% slower.");
    }

    internal static void Reset()
    {
        owned.Clear();
        Publish();
    }

    // --- owning ---------------------------------------------------------------------------------

    /// <summary>Badges you could still get: not owned, and not a copy of your own starting relic.</summary>
    private static List<Def> Pool() =>
        defs.Where(d => !owned.Contains(d.Id) && (d.Curio == Curio.None || d.Curio != CurioRelics.Mine)).ToList();

    internal static bool AnyLeft => Pool().Count > 0;

    private static void Grant(BadgeId id, string head)
    {
        if (owned.Contains(id)) return;
        owned.Add(id);
        Publish();
        var def = Get(id);
        if (def.Curio != Curio.None) CurioRelics.BadgeBound(def.Curio);
        if (id == BadgeId.TwentyFourKarat) GiveGoldenBingBong();
        RunState.Touch();
        Hud.Banner(head, def.Name, ChoiceScreen.Cream);
        Plugin.Log.LogInfo($"Badge relic: {def.Name} ({string.Join(", ", owned)}).");
    }

    private static void GiveGoldenBingBong()
    {
        var me = Character.localCharacter;
        var golden = Items.Database.FirstOrDefault(i => i != null && i.GetComponent<BingBongShieldWhileHolding>() != null);
        if (me == null || golden == null) { Plugin.Log.LogWarning("24 Karat: no Golden Bing Bong found."); return; }
        GameUtils.instance.InstantiateAndGrab(golden, me);
        Plugin.Log.LogInfo($"24 Karat: gave {golden.gameObject.name}.");
    }

    private static void Publish()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null) return;
        try { PhotonNetwork.LocalPlayer.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { [PropKey] = string.Join(",", owned.Select(b => (int)b)) }); }
        catch (Exception e) { Plugin.Log.LogWarning($"Couldn't publish badges: {e.Message}"); }
    }

    /// <summary>Another scout's badges (from their player property).</summary>
    internal static bool Holds(Photon.Realtime.Player? p, BadgeId id)
    {
        if (p == null) return false;
        if (p.IsLocal) return Has(id);
        if (!p.CustomProperties.TryGetValue(PropKey, out object raw) || raw is not string s || s.Length == 0) return false;
        return s.Split(',').Any(x => int.TryParse(x, out int v) && v == (int)id);
    }

    /// <summary>A starting-relic copy this player's badges give them.</summary>
    internal static bool GrantsCurio(Photon.Realtime.Player? p, Curio c) =>
        c != Curio.None && defs.Any(d => d.Curio == c && Holds(p, d.Id));

    // --- offers -----------------------------------------------------------------------------------

    /// <summary>A blessing statue: pick one of three badges. False when there's none left to give.</summary>
    internal static bool OfferPick(string subtitle, int seed, Action onTaken, Action onBack, Action? onSkip = null)
    {
        var picks = Seeded(seed, () => Pool().OrderBy(_ => UnityEngine.Random.value).Take(3).ToList());
        if (picks.Count == 0) return false;
        return ChoiceScreen.Show(new ScreenSpec
        {
            Title = "CHOOSE A BADGE",
            Subtitle = subtitle,
            Confirm = "TAKE",
            BackHint = "Decide later",
            Cards = picks.Select(d => Card(d)).ToList(),
            OnConfirm = i => { Grant(picks[i].Id, "THE STATUE GIVES"); onTaken(); },
            OnBack = onBack,
            OnSkip = onSkip,
            SkipName = "Statue badge",
        });
    }

    /// <summary>A revive statue: everyone gets one random badge, no pick.</summary>
    internal static void GiveRandom()
    {
        var pool = Pool();
        if (pool.Count == 0) { Plugin.Log.LogInfo("Badge relic: none left to give."); return; }
        Grant(pool[UnityEngine.Random.Range(0, pool.Count)].Id, "A BADGE FOR YOU");
    }

    /// <summary>One random badge you could get, for an Ancient Luggage card (the blessing's own seed keeps it the same on reopening).</summary>
    internal static BadgeId? RandomPick() =>
        Pool().OrderBy(_ => UnityEngine.Random.value).Select(d => (BadgeId?)d.Id).FirstOrDefault();

    internal static void Take(BadgeId id, string head) => Grant(id, head);

    private static T Seeded<T>(int seed, Func<T> build)
    {
        var saved = UnityEngine.Random.state;
        UnityEngine.Random.InitState(unchecked(seed * 7919 + (PhotonNetwork.LocalPlayer?.ActorNumber ?? 0)));
        try { return build(); }
        finally { UnityEngine.Random.state = saved; }
    }

    internal static CardSpec Card(Def d) => new()
    {
        Style = CardStyle.Perk,
        Theme = PerkTheme.Explorer,
        Title = d.Name,
        Tag = "Badge relic",
        TagColor = ChoiceScreen.ThemeGlow(PerkTheme.Explorer),
        Body = d.Desc,
        Footer = "Lasts the whole run",
        Icon = Icon(d.Id),
    };

    // --- item cards: a gold line when one of your badges changes that item ------------------------

    private const string GoldLine = "<color=#FFC84F>";

    /// <summary>Adds a gold line per badge of yours that changes the card's item (crate, blessing, fusion cards).</summary>
    internal static void MarkCard(CardSpec card)
    {
        if (owned.Count == 0 || card.Style == CardStyle.Relic || card.Icon == null || card.Body.Contains(GoldLine) || card.Tag == "Badge relic") return;
        var item = Items.Database.FirstOrDefault(i => i != null && Items.Icon(i) == card.Icon);
        if (item == null || CurioRelics.KindOf(item) != Curio.None) return;
        var lines = new List<string>();
        void If(BadgeId id, bool applies, string what) { if (applies && Has(id)) lines.Add($"{Get(id).Name}: {what}"); }
        var spool = item.GetComponent<RopeSpool>();
        If(BadgeId.KnotTying, spool != null && !spool.isAntiRope, $"{Pct}% longer, {GlassCutPct}% less relic glass.");
        If(BadgeId.KnotTying, item.GetComponent<RopeShooter>() != null || spool is { isAntiRope: true }, $"{GlassCutPct}% less relic glass.");
        If(BadgeId.Bouldering, item.GetComponent<ClimbingSpikeComponent>() != null, $"goes in instantly, {GlassCutPct}% less relic glass.");
        If(BadgeId.HangGliding, item.GetComponentInChildren<Glider>(true) != null, $"falls {Pct}% slower, {GlassCutPct}% less relic glass.");
        If(BadgeId.Balloon, item.GetComponent<Balloon>() != null, $"flies {Pct}% higher, {GlassCutPct}% less relic glass.");
        If(BadgeId.Aeronautics, item.GetComponent<JetpackItem>() != null, $"{Pct}% more fuel, {GlassCutPct}% less relic glass.");
        If(BadgeId.Aeronautics, Items.IsScoutCannon(item), $"launches you {Pct}% harder, {GlassCutPct}% less relic glass.");
        If(BadgeId.CalciumIntake, Items.IsMilk(item), $"invincible {Pct}% longer, {GlassCutPct}% less relic glass.");
        If(BadgeId.Foraging, IsBerry(item), $"feeds {Pct}% more, {GlassCutPct}% less relic glass.");
        If(BadgeId.Naturalist, Relics.IsFood(item), IsPackaged(item) ? "double relic glass." : "no relic glass.");
        If(BadgeId.FirstAid, Relics.IsHealing(item), $"heals you {Pct}% more.");
        If(BadgeId.DisasterResponse, Items.IsRescueClaw(item), $"heals the scout it pulls ({RescueHealPct} points).");
        if (lines.Count > 0) card.Body += "\n" + string.Join("\n", lines.Select(l => $"{GoldLine}{l}</color>"));
    }

    // --- the game's badge pictures ---------------------------------------------------------------

    private static Dictionary<ACHIEVEMENTTYPE, Texture>? icons;
    private static float iconsTried = -99f;

    internal static Texture? Icon(BadgeId id)
    {
        if (icons == null || (icons.Count == 0 && Time.unscaledTime - iconsTried > 10f)) LoadIcons();
        return icons != null && icons.TryGetValue(Get(id).Badge, out var t) ? t : null;
    }

    private static void LoadIcons()
    {
        iconsTried = Time.unscaledTime;
        icons = new Dictionary<ACHIEVEMENTTYPE, Texture>();
        try
        {
            var all = Resources.FindObjectsOfTypeAll<BadgeManager>().SelectMany(m => m.badgeData ?? new BadgeData[0])
                .Concat(Resources.FindObjectsOfTypeAll<BadgeData>());
            foreach (var b in all)
                if (b != null && b.icon != null && !icons.ContainsKey(b.linkedAchievement)) icons[b.linkedAchievement] = b.icon;
            var missing = defs.Where(d => !icons.ContainsKey(d.Badge)).Select(d => d.Name).ToList();
            Plugin.Log.LogInfo($"Badge relics: {icons.Count} game badge pictures found{(missing.Count > 0 ? $", none for {string.Join(", ", missing)}" : "")}.");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Badge pictures not found: {e.Message}"); }
    }

    // --- per frame: Leave No Trace's climbing speed -----------------------------------------------

    private Character? sped;

    private void Update()
    {
        var me = Character.localCharacter;
        Character? want = me != null && Has(BadgeId.LeaveNoTrace) ? me : null;
        if (want == sped) return;
        if (sped != null) Speed(sped, -Buff);
        if (want != null) Speed(want, Buff);
        sped = want;
    }

    private static void Speed(Character c, float add)
    {
        try
        {
            c.refs.climbing.climbSpeedMod += add;
            c.refs.ropeHandling.climbSpeedMod += add;
            c.refs.vineClimbing.climbSpeedMod += add;
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Leave No Trace climb speed failed: {e.Message}"); }
    }

    // --- glass: which relic triggers a badge spares -------------------------------------------------

    /// <summary>A badge eases its tools' relics: BadgeGlassScale of the glass (half by default).</summary>
    private static int Scaled(int glass) => Mathf.RoundToInt(glass * Mathf.Clamp01(ModConfig.BadgeGlassScale.Value));

    /// <summary>The glass a relic trigger costs, given what set it off (an item and/or a tool you got off).</summary>
    internal static int GlassFor(int glass, Item? item, string kind)
    {
        if (owned.Count == 0) return glass;
        bool k(string s) => kind.Contains(s);
        if (Has(BadgeId.Cooking) && item != null && Cooked(item)) return Scaled(glass);
        if (Has(BadgeId.Bouldering) && (k("piton") || item?.GetComponent<ClimbingSpikeComponent>() != null)) return Scaled(glass);
        if (Has(BadgeId.Foraging) && item != null && IsBerry(item)) return Scaled(glass);
        if (Has(BadgeId.Balloon) && (k("balloon") || item?.GetComponent<Balloon>() != null)) return Scaled(glass);
        if (Has(BadgeId.KnotTying) && (k("rope") || item?.GetComponent<RopeSpool>() != null || item?.GetComponent<RopeShooter>() != null)) return Scaled(glass);
        if (Has(BadgeId.Aeronautics) && (k("jetpack") || k("rocket") || item?.GetComponent<JetpackItem>() != null || (item != null && Items.IsScoutCannon(item)))) return Scaled(glass);
        if (Has(BadgeId.CalciumIntake) && item != null && Items.IsMilk(item)) return Scaled(glass);
        if (Has(BadgeId.HangGliding) && (k("glide") || item?.GetComponentInChildren<Glider>(true) != null)) return Scaled(glass);
        if (Has(BadgeId.Naturalist) && item != null && Relics.IsFood(item))
        {
            if (IsPackaged(item)) return glass * 2;
            return Scaled(glass);
        }
        return glass;
    }

    internal static bool Cooked(Item item)
    {
        try { return item.GetData<IntItemData>(DataEntryKey.CookedAmount).Value > 0; }
        catch { return false; }
    }

    internal static bool IsBerry(Item item) => Items.Key(item).Contains("berry");

    private static readonly string[] Packaged =
        { "airline", "granola", "trail mix", "trailmix", "cookie", "lollipop", "energy drink", "sports drink", "milk", "fortified" };

    internal static bool IsPackaged(Item item)
    {
        string key = Items.Key(item);
        return Packaged.Any(key.Contains) || Trees.Classify(item) is TreeId.Food or TreeId.SportsDrink;
    }

    // --- helpers for the effects --------------------------------------------------------------------

    private static readonly CharacterAfflictions.STATUSTYPE[] Healable =
    {
        CharacterAfflictions.STATUSTYPE.Injury, CharacterAfflictions.STATUSTYPE.Poison, CharacterAfflictions.STATUSTYPE.Cold,
        CharacterAfflictions.STATUSTYPE.Hot, CharacterAfflictions.STATUSTYPE.Drowsy, CharacterAfflictions.STATUSTYPE.Spores,
        CharacterAfflictions.STATUSTYPE.Thorns, CharacterAfflictions.STATUSTYPE.Web, CharacterAfflictions.STATUSTYPE.Crab,
    };

    /// <summary>Disaster Response, on the clawed scout's side: up to 25 points off their ailments, worst first (never curse, hunger, weight or glass).</summary>
    internal static void ReceiveRescueHeal(int fromActor)
    {
        var me = Character.localCharacter;
        if (me == null || me.data.dead) return;
        var aff = me.refs.afflictions;
        float left = RescueHeal, healed = 0f;
        foreach (var s in Healable.OrderByDescending(aff.GetCurrentStatus))
        {
            if (left <= 0f) break;
            float take = Mathf.Min(left, aff.GetCurrentStatus(s));
            if (take <= 0f) continue;
            aff.SubtractStatus(s, take);
            left -= take;
            healed += take;
        }
        Plugin.Log.LogInfo($"Disaster Response: {Relics.ScoutOf(fromActor)?.characterName ?? "a scout"}'s claw healed {healed * 100f:0} points{(me.data.passedOut ? " (passed out: wakes once under the line)" : "")}.");
    }

    /// <summary>Another living scout within 24m of this one.</summary>
    internal static bool Company(Character me) =>
        Squad.Alive().Any(c => c != me && Vector3.Distance(c.Center, me.Center) <= 24f * Squad.M);

    /// <summary>The item whose action is running on the local scout (food, healing) - its badge effects apply.</summary>
    internal static Item? Eating;
    /// <summary>A fall is hurting the local scout right now.</summary>
    internal static bool Falling;
}

[HarmonyPatch]
internal static class BadgeFoodPatches
{
    internal static bool Local(Character? c) => c != null && c.IsLocal;
    internal static float More => 1f + Badges.Buff;
    internal static float Less => 1f - Badges.Buff;

    // --- food and healing ---------------------------------------------------------------------------

    [HarmonyPrefix, HarmonyPatch(typeof(Action_RestoreHunger), nameof(Action_RestoreHunger.RunAction))]
    private static void EatStart(ItemAction __instance) => Begin(__instance);
    [HarmonyFinalizer, HarmonyPatch(typeof(Action_RestoreHunger), nameof(Action_RestoreHunger.RunAction))]
    private static void EatEnd() => Badges.Eating = null;
    [HarmonyPrefix, HarmonyPatch(typeof(Action_ModifyStatus), nameof(Action_ModifyStatus.RunAction))]
    private static void ModStart(ItemAction __instance) => Begin(__instance);
    [HarmonyFinalizer, HarmonyPatch(typeof(Action_ModifyStatus), nameof(Action_ModifyStatus.RunAction))]
    private static void ModEnd() => Badges.Eating = null;

    private static void Begin(ItemAction a)
    {
        var item = a.GetComponent<Item>();
        Badges.Eating = item != null && Local(item.holderCharacter) ? item : null;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.SubtractStatus))]
    private static void Relief(CharacterAfflictions __instance, CharacterAfflictions.STATUSTYPE statusType, ref float amount)
    {
        if (amount <= 0f || Badges.Owned.Count == 0 || !Local(__instance.character)) return;
        var eating = Badges.Eating;
        if (statusType == CharacterAfflictions.STATUSTYPE.Hunger && eating != null)
        {
            if (Badges.Has(BadgeId.Cooking) && Badges.Cooked(eating)) amount *= More;
            if (Badges.Has(BadgeId.Foraging) && Badges.IsBerry(eating)) amount *= More;
        }
        if (statusType == CharacterAfflictions.STATUSTYPE.Injury)
        {
            if (eating != null && Badges.Has(BadgeId.Cooking) && Badges.Cooked(eating)) amount *= More;
            if (Badges.Has(BadgeId.FirstAid)) amount *= More;
        }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddStatus))]
    private static void Harm(CharacterAfflictions __instance, CharacterAfflictions.STATUSTYPE statusType, ref float amount)
    {
        if (amount <= 0f || Badges.Owned.Count == 0 || !Local(__instance.character)) return;
        var s = statusType;
        if ((s == CharacterAfflictions.STATUSTYPE.Cold && Badges.Has(BadgeId.BundledUp))
            || (s == CharacterAfflictions.STATUSTYPE.Poison && Badges.Has(BadgeId.Toxicology))
            || (s == CharacterAfflictions.STATUSTYPE.Spores && Badges.Has(BadgeId.TreadLightly))
            || (s == CharacterAfflictions.STATUSTYPE.Hot && Badges.Has(BadgeId.Nomad))
            || (s == CharacterAfflictions.STATUSTYPE.Drowsy && Badges.Has(BadgeId.Wanderer)))
            amount *= Less;
        // A bad berry: whatever it does to you, a quarter less.
        if (Badges.Eating is { } e && Badges.Has(BadgeId.Foraging) && Badges.IsBerry(e) && s != CharacterAfflictions.STATUSTYPE.Petrify)
            amount *= Less;
        if (s == CharacterAfflictions.STATUSTYPE.Injury && Badges.Falling && Badges.Has(BadgeId.FirstAid) && Badges.Company(__instance.character))
            amount *= Less;
    }
}

[HarmonyPatch]
internal static class BadgeFallPatches
{
    [HarmonyPrefix, HarmonyPatch(typeof(CharacterMovement), "CheckFallDamage")]
    private static void FallStart() => Badges.Falling = true;
    [HarmonyFinalizer, HarmonyPatch(typeof(CharacterMovement), "CheckFallDamage")]
    private static void FallEnd() => Badges.Falling = false;
    [HarmonyPrefix, HarmonyPatch(typeof(CharacterClimbing), "CheckFallDamage")]
    private static void WallFallStart() => Badges.Falling = true;
    [HarmonyFinalizer, HarmonyPatch(typeof(CharacterClimbing), "CheckFallDamage")]
    private static void WallFallEnd() => Badges.Falling = false;
}

[HarmonyPatch]
internal static class BadgeMilkPatches
{
    /// <summary>Calcium Intake: milk's invincibility lasts longer (a copy - never the item's own affliction).</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddAffliction))]
    private static void Milk(CharacterAfflictions __instance, ref Peak.Afflictions.Affliction affliction)
    {
        if (affliction is not Peak.Afflictions.Affliction_Invincibility { isFromMilk: true } inv) return;
        if (!Badges.Has(BadgeId.CalciumIntake) || !BadgeFoodPatches.Local(__instance.character)) return;
        var longer = Relics.Copy(inv);
        longer.totalTime = inv.totalTime * BadgeFoodPatches.More;
        affliction = longer;
    }
}

[HarmonyPatch]
internal static class BadgeMovePatches
{
    /// <summary>Hasty: faster on foot.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(CharacterMovement), "GetMovementForce")]
    private static void Hasty(CharacterMovement __instance, ref float __result)
    {
        if (__result > 0f && Badges.Has(BadgeId.Hasty) && BadgeFoodPatches.Local(__instance.character)) __result *= BadgeFoodPatches.More;
    }

    private static float companyCheckAt;
    private static bool company;

    /// <summary>Lone Wolf: climbing alone is cheaper, climbing in company dearer.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(Character), nameof(Character.UseStamina))]
    private static void LoneWolf(Character __instance, ref float usage)
    {
        if (usage <= 0f || !Badges.Has(BadgeId.LoneWolf) || !__instance.IsLocal || !__instance.data.isClimbing) return;
        if (Time.time >= companyCheckAt)
        {
            companyCheckAt = Time.time + 1f;
            company = Badges.Company(__instance);
        }
        usage *= company ? BadgeFoodPatches.More : BadgeFoodPatches.Less;
    }
}

[HarmonyPatch]
internal static class BadgeGlidePatches
{
    /// <summary>Hang Gliding: the glider holds more air (slower fall).</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(CharacterMovement), nameof(CharacterMovement.ApplyGlider))]
    private static void Glide(CharacterMovement __instance, ref float extraDrag)
    {
        if (!Badges.Has(BadgeId.HangGliding) || !BadgeFoodPatches.Local(__instance.character)) return;
        extraDrag = Mathf.Clamp01(1f - (1f - extraDrag) * BadgeFoodPatches.More);
    }
}

[HarmonyPatch]
internal static class BadgeCannonPatches
{
    /// <summary>Aeronautics: the Scout Cannon launches you harder.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(ScoutCannon), nameof(ScoutCannon.RPCA_LaunchTarget))]
    private static void CannonStart(ScoutCannon __instance, int targetID, out float __state)
    {
        __state = __instance.launchForce;
        var view = PhotonNetwork.GetPhotonView(targetID);
        if (view != null && BadgeFoodPatches.Local(view.GetComponent<Character>()) && Badges.Has(BadgeId.Aeronautics)) __instance.launchForce *= BadgeFoodPatches.More;
    }

    [HarmonyFinalizer, HarmonyPatch(typeof(ScoutCannon), nameof(ScoutCannon.RPCA_LaunchTarget))]
    private static void CannonEnd(ScoutCannon __instance, float __state)
    {
        if (__state > 0f) __instance.launchForce = __state;
    }
}

[HarmonyPatch]
internal static class BadgeRescuePatches
{
    /// <summary>Your Rescue Claw pulls a scout: their game heals them (a passed-out scout wakes once they're under the line).</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(RescueHook), nameof(RescueHook.RPCA_RescueCharacter))]
    private static void Rescue(RescueHook __instance, PhotonView characterView)
    {
        try
        {
            if (!__instance.photonView.IsMine || !Badges.Has(BadgeId.DisasterResponse)) return;
            var target = characterView != null ? characterView.GetComponent<Character>() : null;
            var me = Character.localCharacter;
            if (target == null || me == null || target == me || target.data.dead || target.photonView?.Owner is not { } owner) return;
            Net.RescueHeal(owner.ActorNumber);
            Plugin.Log.LogInfo($"Disaster Response: healing {target.characterName} with the Rescue Claw.");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Disaster Response failed: {e.Message}"); }
    }
}

[HarmonyPatch]
internal static class BadgePitonPatches
{
    private static readonly AccessTools.FieldRef<CharacterItems, float> Tick = AccessTools.FieldRefAccess<CharacterItems, float>("climbingSpikeTick");
    private static readonly AccessTools.FieldRef<CharacterItems, ItemSlot> Slot = AccessTools.FieldRefAccess<CharacterItems, ItemSlot>("currentClimbingSpikeItemSlot");

    /// <summary>Bouldering: once you start hammering, the piton is in.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(CharacterItems), "UpdateClimbingSpikeUse")]
    private static void Instant(CharacterItems __instance)
    {
        if (!Badges.Has(BadgeId.Bouldering) || !BadgeFoodPatches.Local(__instance.character)) return;
        ref float t = ref Tick(__instance);
        if (t > 0f && Slot(__instance)?.prefab is { } p) t = Mathf.Max(t, p.usingTimePrimary);
    }
}
