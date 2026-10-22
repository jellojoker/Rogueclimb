using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Zorro.Core;

namespace PeakRoguelike;

/// <summary>
/// The gold XP bar. Per biome, each source has its own cap:
///   climbing   - new height only, from where the biome starts (the crash site / last campfire) up to
///                its campfire's height (the summit at the end): ClimbLevels. Going back down,
///                walking forward or re-climbing the same wall earns nothing.
///   crates     - every crate you claim from: CrateXp each, up to CrateLevels.
///   crate items- using up an item that came from a card pick (crate, level-up, statue blessing):
///                healing / food / anything else, up to ItemLevels. Wild food never counts.
/// Uncapped extras on reaching the campfire: the fast-clear bonus (drains over BiomeMinutes from
/// when you leave camp) and CampfireXp. Each level levels up an item you carry (a card pick on
/// flat ground; with nothing to level, a crate card instead).
/// Per scout and local; feeding / bandaging a friend sends the XP back to the feeder.
/// </summary>
internal class Xp : MonoBehaviour
{
    private const float PerLevel = 1000f;
    private static Xp? instance;

    // --- run state -------------------------------------------------------------------------------
    private float xp;
    private int claimed;
    private int biome = -1;           // the biome we're climbing (map segment index); int.MaxValue = summit reached
    private float best;               // best height through it, 0-1
    private float crateXp, itemXp;    // this biome's crate and crate-item XP so far
    private int merges;               // this biome's 3-copy merges (for the campfire log)
    private float clockFrom = -1f;    // when we left its start (-1 = still at camp)
    private float deferUntil, flatSince = -1f;
    private int announced;            // levels already chimed

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - XP");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        instance = go.AddComponent<Xp>();
    }

    internal static void Reset()
    {
        if (instance == null) return;
        instance.xp = 0f;
        instance.claimed = instance.announced = 0;
        instance.biome = -1;
        instance.best = instance.crateXp = instance.itemXp = 0f;
        instance.merges = 0;
        instance.clockFrom = -1f;
        instance.deferUntil = 0f;
        instance.flatSince = -1f;
        instance.targets.Clear();
        instance.summits.Clear();
        instance.landings.Clear();
        instance.landed = 0f;
        instance.counted = false;
        crateItems.Clear();
        expected.Clear();
        seen.Clear();
    }

    /// <summary>One line for bug reports.</summary>
    internal static string Summary => instance == null ? "-"
        : $"{instance.xp:0} ({Mathf.FloorToInt(instance.xp / PerLevel)} levels, {instance.claimed} picked), biome {instance.biome}, best {instance.best:0.00}, crate {instance.crateXp:0}, items {instance.itemXp:0}";

    internal static void DevAdd(float amount)
    {
        if (instance == null) return;
        instance.xp += amount;
        Plugin.Log.LogInfo($"XP: dev +{amount:0} -> {instance.xp:0}.");
    }

    /// <summary>Levels reached this run (for the run report).</summary>
    internal static int Level => instance == null ? 0 : instance.Earned;

    private int Earned =>Mathf.FloorToInt(xp / PerLevel + 0.0001f);
    private int Pending => Mathf.Max(0, Earned - claimed);
    // The Kiln is a short climb: its climb pays more.
    private float ClimbXp => Mathf.Max(0f, ModConfig.XpClimbLevels.Value) * PerLevel
        * ((Segment)biome == Segment.TheKiln ? Mathf.Max(0f, ModConfig.XpKilnClimb.Value) : 1f);
    private static float FastXp => Mathf.Max(0f, ModConfig.XpFastClearLevels.Value) * PerLevel;
    private float BonusLeft => clockFrom < 0f ? 1f
        : Mathf.Clamp01(1f - (Time.time - clockFrom) / Mathf.Max(1f, ModConfig.XpBiomeMinutes.Value * 60f));

    private void Update()
    {
        Character me = Character.localCharacter;
        bool on = ModConfig.Xp.Value && MapHandler.Exists && me != null;
        if (on) { Track(me!); TagCrateItems(); LevelUps(me!); }
        UpdateBar(on);
    }

    // --- crates and crate items ------------------------------------------------------------------

    /// <summary>We claimed from a crate (one count per crate, a Generosity second pick included).</summary>
    internal static void Crate(string from)
    {
        if (instance == null || !ModConfig.Xp.Value) return;
        float cap = Mathf.Max(0f, ModConfig.XpCrateLevels.Value) * PerLevel;
        float add = Mathf.Min(ModConfig.XpPerCrate.Value, cap - instance.crateXp);
        if (add <= 0f)
        {
            Plugin.Log.LogInfo($"XP: {from} - crate XP capped for this biome.");
            if (!instance.crateCapShown) { instance.crateCapShown = true; instance.Maxed(); }
            return;
        }
        instance.crateXp += add;
        instance.xp += add;
        instance.Pop(add, "crate");
        Plugin.Log.LogInfo($"XP: {from} +{add:0} (crates {instance.crateXp:0}/{cap:0}).");
    }

    /// <summary>
    /// A consumable was used up by the local scout. If it came from a card pick it's worth XP - to
    /// whoever used it on us (a friend's bandage or meal) or to us.
    /// </summary>
    internal static void Used(Item item, Character consumer)
    {
        if (instance == null || !ModConfig.Xp.Value || item == null || item.data == null) return;
        if (!IsCrateItem(item.data.guid)) return;
        if (Items.IsSkull(item) || Items.IsDagger(item) || Items.IsBookOfBones(item)) return;
        int worth = Relics.IsHealing(item) ? ModConfig.XpHealingUse.Value
            : Relics.IsFood(item) ? ModConfig.XpFoodUse.Value
            : ModConfig.XpItemUse.Value;
        var feeder = item.trueHolderCharacter;
        if (feeder != null && feeder != consumer && !feeder.IsLocal && feeder.photonView?.Owner is { } owner)
        {
            Net.XpFed(owner.ActorNumber, worth);
            return;
        }
        string label;
        try { label = item.GetItemName().ToLowerInvariant(); } catch { label = "item"; }
        instance.AddItemXp(worth, item.gameObject.name.Replace("(Clone)", "").Trim(), label);
    }

    /// <summary>Three copies merged a level up (counted for the campfire log).</summary>
    internal static void Merged()
    {
        if (instance != null) instance.merges++;
    }

    /// <summary>A friend used one of our crate items on themselves... or we fed them one.</summary>
    internal static void ReceiveFed(int worth) => instance?.AddItemXp(worth, "fed / bandaged a scout", "helped a scout");

    private void AddItemXp(float worth, string what, string label)
    {
        float cap = Mathf.Max(0f, ModConfig.XpItemLevels.Value) * PerLevel;
        float add = Mathf.Min(worth, cap - itemXp);
        if (add <= 0f)
        {
            Plugin.Log.LogInfo($"XP: {what} - item XP capped for this biome.");
            if (!itemCapShown) { itemCapShown = true; Maxed(); }
            return;
        }
        itemXp += add;
        xp += add;
        Pop(add, label);
        Plugin.Log.LogInfo($"XP: used up {what} +{add:0} (items {itemXp:0}/{cap:0}).");
    }

    // Items from card picks are marked by their instance guid (in the room, so whoever ends up
    // eating it knows). We can't know the guid when asking the host for the item, so we expect
    // one of that kind and mark the next new one that turns up in our pockets.
    private const string CratePrefix = "prl.cr.";
    private static readonly HashSet<System.Guid> crateItems = new();
    private static readonly List<(ushort id, float until)> expected = new();
    private static readonly HashSet<System.Guid> seen = new();
    private float nextTag;

    internal static void ExpectCrateItem(Item? item)
    {
        if (item == null) return;
        expected.Add((item.itemID, Time.time + 30f));
        ItemLevels.Claimed(item);
    }

    private static bool IsCrateItem(System.Guid guid)
    {
        if (crateItems.Contains(guid)) return true;
        if (Photon.Pun.PhotonNetwork.InRoom
            && Photon.Pun.PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(CratePrefix + guid.ToString("N")))
        {
            crateItems.Add(guid);
            return true;
        }
        return false;
    }

    private void TagCrateItems()
    {
        if (Time.time < nextTag) return;
        nextTag = Time.time + 0.25f;
        expected.RemoveAll(e => Time.time > e.until);
        var p = Player.localPlayer;
        if (p == null) return;
        var slots = p.itemSlots.Append(p.tempFullSlot);
        foreach (var s in slots)
        {
            if (s == null || s.IsEmpty() || s.prefab == null || s.data == null) continue;
            if (!seen.Add(s.data.guid)) continue; // been in our pockets before
            int i = expected.FindIndex(e => e.id == s.prefab.itemID);
            if (i < 0) continue;
            expected.RemoveAt(i);
            crateItems.Add(s.data.guid);
            if (Photon.Pun.PhotonNetwork.InRoom)
            {
                try
                {
                    Photon.Pun.PhotonNetwork.CurrentRoom.SetCustomProperties(
                        new ExitGames.Client.Photon.Hashtable { [CratePrefix + s.data.guid.ToString("N")] = true });
                }
                catch (System.Exception e) { Plugin.Log.LogWarning($"XP: couldn't share a crate item mark: {e.Message}"); }
            }
            Plugin.Log.LogInfo($"XP: {s.prefab.gameObject.name} marked as a crate item.");
        }
    }

    // --- climbing ------------------------------------------------------------------------------

    private void Track(Character me)
    {
        var map = Singleton<MapHandler>.Instance;
        if (map == null || map.segments == null || map.segments.Length == 0) return;
        if (!counted) CountCrates(map);
        int seg = Mathf.Clamp(map.currentSegment, 0, map.segments.Length - 1);
        // First look, or the run moved on past us (joined late, revived further up): climb from here.
        if (biome < 0 || (biome != int.MaxValue && seg > biome + 1)) StartBiome(seg);
        if (biome == int.MaxValue || me.data.dead) return;

        if (!Ends(biome, out Vector3 from, out Vector3 to, out float arrive)) return;
        Vector3 pos = me.Center;
        // The fast-clear clock: from the crash site, once you leave it; after that, once the fire
        // you start from is lit (or, if nobody lights it, once you're well past it).
        if (clockFrom < 0f)
        {
            float away = Vector3.Distance(pos, from);
            string? why = biome == 0 ? (away > 20f * Squad.M ? "left the crash site" : null)
                : StartFireLit(biome) ? "the fire is lit"
                : away > 60f * Squad.M ? "climbed on past an unlit fire" : null;
            if (why != null)
            {
                clockFrom = Time.time;
                Plugin.Log.LogInfo($"XP: {why} - the fast-clear clock runs ({ModConfig.XpBiomeMinutes.Value:0} min).");
            }
        }
        if (Flat) Landing(me);
        else
        {
            // Height only. A biome that barely climbs pays its climb on arrival instead.
            float span = to.y - from.y;
            if (span >= ModConfig.XpMinClimb.Value * Squad.M)
            {
                float frac = Mathf.Clamp01((pos.y - from.y) / span);
                if (frac > best)
                {
                    xp += (frac - best) * ClimbXp;
                    best = frac;
                }
            }
        }
        if (Vector3.Distance(pos, to) > arrive) return;

        // Reached the campfire (or the summit): the rest of the climb (flat biomes: the landings
        // topped up to their floor), the fast-clear bonus and the fire's own.
        float climbed;
        if (Flat)
        {
            float floor = Mathf.Max(0f, ModConfig.XpLandingFloor.Value) * PerLevel;
            float top = Mathf.Max(0f, floor - landed);
            xp += top;
            climbed = landed + top;
        }
        else
        {
            xp += (1f - best) * ClimbXp;
            climbed = ClimbXp;
        }
        int bonus = Mathf.RoundToInt(BonusLeft * FastXp);
        int fire = Mathf.Max(0, ModConfig.XpCampfire.Value);
        xp += bonus + fire;
        Plugin.Log.LogInfo($"XP: cleared biome {biome} in {(clockFrom < 0f ? 0f : Time.time - clockFrom):0}s - fast clear +{bonus}, campfire +{fire} (total {xp:0}).");
        if (bonus > 0) Pop(bonus, "fast clear", merge: false);
        if (fire > 0) Pop(fire, "campfire", merge: false);
        // For tuning: what this biome paid, in levels.
        Plugin.Log.LogInfo($"Biome done: {(Segment)biome} - {(climbed + crateXp + itemXp + bonus + fire) / PerLevel:0.##} levels "
            + $"({(Flat ? $"landings {landings.Count} = " : "climb ")}{climbed / PerLevel:0.##} / crates {crateXp / PerLevel:0.##} / items {itemXp / PerLevel:0.##} / fast clear {bonus / PerLevel:0.##} / campfire {fire / PerLevel:0.##}), "
            + $"{merges} merge(s); {Earned} levels earned this run.");
        Luck.BiomeCleared(biome);
        bool last = summits.Contains(biome) || biome + 1 >= map.segments.Length || !Ends(biome + 1, out _, out _, out _);
        if (last) { biome = int.MaxValue; Plugin.Log.LogInfo("XP: summit - no more biomes."); }
        else StartBiome(biome + 1);
    }

    // --- flat biomes: new ground instead of new height --------------------------------------------

    /// <summary>Caldera / Gloom: crossing, not climbing - XP comes from landing on new ground.</summary>
    private bool Flat => biome == (int)Segment.Caldera;

    private readonly List<Vector3> landings = new();
    private float landed, groundedSince = -1f;

    private void Landing(Character me)
    {
        var d = me.data;
        if (!d.isGrounded || d.isClimbingAnything || d.isCarried || BloodTornado.Carrying(me)) { groundedSince = -1f; return; }
        if (groundedSince < 0f) groundedSince = Time.time;
        if (Time.time - groundedSince < 0.5f) return;
        float cap = Mathf.Max(0f, ModConfig.XpLandingCap.Value) * PerLevel;
        if (landed >= cap) return;
        Vector3 pos = me.Center;
        float gap = Mathf.Max(1f, ModConfig.XpLandingSpacing.Value) * Squad.M;
        if (landings.Any(l => Vector3.Distance(l, pos) < gap)) return;
        landings.Add(pos);
        float add = Mathf.Min(Mathf.Max(0f, ModConfig.XpLandingXp.Value), cap - landed);
        landed += add;
        xp += add;
        Plugin.Log.LogInfo($"XP: landed on new ground +{add:0} ({landings.Count}, {landed:0}/{cap:0}).");
    }

    private void StartBiome(int b)
    {
        biome = b;
        best = crateXp = itemXp = 0f;
        crateCapShown = itemCapShown = false;
        merges = 0;
        landings.Clear();
        landed = 0f;
        groundedSince = -1f;
        clockFrom = -1f;
        if (Ends(b, out var from, out var to, out _))
        {
            float span = (to.y - from.y) * CharacterStats.unitsToMeters;
            Plugin.Log.LogInfo($"XP: biome {b} ({(Segment)b}) - climb from {from.y * CharacterStats.unitsToMeters:0}m to {to.y * CharacterStats.unitsToMeters:0}m ({span:0}m up"
                + (span < ModConfig.XpMinClimb.Value ? ", too little - paid on arrival" : "") + ").");
        }
        else Plugin.Log.LogInfo($"XP: biome {b} has no campfire / summit to climb to.");
    }

    private bool counted;

    /// <summary>Once per map: how many crates of each kind every biome has (for tuning crate XP).</summary>
    private void CountCrates(MapHandler map)
    {
        counted = true;
        try
        {
            var all = FindObjectsByType<Luggage>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var parts = new List<string>();
            for (int i = 0; i < map.segments.Length; i++)
            {
                var root = map.segments[i].segmentParent;
                if (root == null) continue;
                var here = all.Where(l => l != null && l is not RespawnChest && l.transform.IsChildOf(root.transform)).ToList();
                if (here.Count == 0) continue;
                parts.Add($"{(Segment)i} {here.Count} (" + string.Join(", ", here.GroupBy(l => l.displayName).Select(g => $"{g.Key} {g.Count()}")) + ")");
            }
            Plugin.Log.LogInfo($"Crates on this map ({all.Count(l => l is not RespawnChest)} in all): " + (parts.Count > 0 ? string.Join(" · ", parts) : "none matched a biome"));
        }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Crate count failed: {e.Message}"); }
    }

    /// <summary>Is the campfire biome b starts from (the end of biome b-1) lit?</summary>
    private static bool StartFireLit(int b)
    {
        var map = Singleton<MapHandler>.Instance;
        if (map == null || b < 1 || b - 1 >= map.segments.Length) return false;
        try
        {
            var root = map.segments[b - 1].segmentCampfire;
            var fire = root != null ? root.GetComponentInChildren<Campfire>(true) : null;
            return fire != null && fire.Lit;
        }
        catch { return false; }
    }

    private readonly Dictionary<int, (Vector3 pos, float radius)> targets = new();
    /// <summary>Biomes that end at the summit (nothing after them).</summary>
    private readonly HashSet<int> summits = new();

    /// <summary>Where biome b starts and ends, and how close counts as arriving.</summary>
    private bool Ends(int b, out Vector3 from, out Vector3 to, out float arrive)
    {
        from = to = default;
        arrive = 0f;
        var map = Singleton<MapHandler>.Instance;
        if (!Target(b, out to, out arrive)) return false;
        if (b == 0)
        {
            var crash = map.segments[0].reconnectSpawnPos;
            if (crash == null) return false;
            from = crash.position;
            return true;
        }
        return Target(b - 1, out from, out _);
    }

    private bool Target(int b, out Vector3 pos, out float radius)
    {
        pos = default;
        radius = 0f;
        if (targets.TryGetValue(b, out var t) && t.radius > 0f) { pos = t.pos; radius = t.radius; return true; }
        var map = Singleton<MapHandler>.Instance;
        if (map == null || b < 0 || b >= map.segments.Length) return false;
        Campfire? fire = null;
        try
        {
            var root = map.segments[b].segmentCampfire;
            if (root != null) fire = root.GetComponentInChildren<Campfire>(true);
        }
        catch { /* variant lookups before the map is set up */ }
        if (fire != null)
        {
            pos = fire.transform.position;
            radius = fire.moraleBoostRadius > 1f ? fire.moraleBoostRadius : 15f * Squad.M;
        }
        // No campfire of its own (the Kiln runs straight on to the summit): the summit ends it.
        else if (b == map.segments.Length - 1 || (Segment)b >= Segment.TheKiln)
        {
            summits.Add(b);
            var peak = FindObjectsByType<PeakSequence>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            if (peak == null) return false;
            pos = peak.transform.position;
            radius = ModConfig.SummitRadius.Value * Squad.M;
        }
        else return false;
        targets[b] = (pos, radius);
        return true;
    }

    // --- level ups ----------------------------------------------------------------------------------

    private void LevelUps(Character me)
    {
        if (Earned > announced)
        {
            announced = Earned;
            Hud.PickChime();
            Plugin.Log.LogInfo($"XP: level {announced} earned ({Pending} waiting for flat ground).");
        }
        if (Pending == 0 || ChoiceScreen.IsOpen || Time.time < deferUntil) { flatSince = -1f; return; }
        if (!OnFlatGround(me)) { flatSince = -1f; return; }
        if (flatSince < 0f) flatSince = Time.time;
        if (Time.time - flatSince < 1f) return;
        flatSince = -1f;
        OpenPick();
    }

    /// <summary>Standing still-ish on level ground, on your own feet: no pop-up mid-climb or mid-fall.</summary>
    internal static bool OnFlatGround(Character me)
    {
        var d = me.data;
        return d.isGrounded && !d.isClimbingAnything && !d.isJumping && !d.dead && !d.passedOut && d.fullyConscious
               && !d.isCarried && d.groundNormal.y >= 0.9f && !BloodTornado.Carrying(me);
    }

    // This level-up's cards: items you carry going up a level - or, with nothing to level, a crate pick.
    private List<Offer> levels = new();
    private List<Item> rolled = new();

    private void OpenPick()
    {
        if (levels.Count == 0 && rolled.Count == 0)
        {
            levels = Offers.Carried();
            if (levels.Count == 0) rolled = Roll();
        }
        if (levels.Count == 0 && rolled.Count == 0) { deferUntil = Time.time + 10f; Plugin.Log.LogWarning("XP: nothing to level and no crate pool to roll from - trying again."); return; }

        var cards = new List<CardSpec>();
        var picks = new List<System.Action>();
        foreach (var o in levels) { cards.Add(CrateCards.ForCarried(o)); picks.Add(() => PickedLevel(o)); }
        foreach (var item in rolled) { cards.Add(CrateCards.ForItem(item)); picks.Add(() => Picked(item)); }
        foreach (var r in levels.Count > 0 ? new List<Recipe>() : Offers.UnlockedFusions(rolled))
        {
            cards.Add(Fusion.ItemCard(r, "Fusion unlocked"));
            picks.Add(() => Done(() => { Fusion.Give(r); Hud.Banner("FUSION", r.Name, ChoiceScreen.FusionGlow); }));
        }

        bool shown = ChoiceScreen.Show(new ScreenSpec
        {
            Title = "LEVEL UP",
            Subtitle = $"Level {claimed + 1}",
            Confirm = "CLAIM",
            Cards = LevelCards(cards),
            StillValid = Alive,
            OnConfirm = i => picks[i](),
            OnBack = Later,
            OnCancelled = Later,
        });
        if (shown) Hud.LevelUpSound();
        if (shown) Plugin.Log.LogInfo($"XP: level-up pick ({string.Join(", ", levels.Select(o => $"{o.Tree!.Name} {o.Level}").Concat(rolled.Select(r => r.gameObject.name)))}).");
        else Later();
    }

    private static bool Alive() => Character.localCharacter is { } me && !me.data.dead && !me.data.passedOut;

    /// <summary>Backed out (or knocked over): the pick waits, same cards.</summary>
    private void Later() => deferUntil = Time.time + 20f;

    /// <summary>A carried item goes up a level (a fork asks for the path first).</summary>
    private void PickedLevel(Offer offer)
    {
        if (offer.Kind != OfferKind.Fork)
        {
            Done(() => { Hud.PickChime(); Offers.ApplyCarried(offer, offer.Branch); });
            return;
        }
        var tree = offer.Tree!;
        int next = offer.Level;
        var options = Offers.Paths(tree, next);
        ChoiceScreen.Show(new ScreenSpec
        {
            Title = "CHOOSE YOUR PATH",
            Subtitle = $"{tree.Name} · level {next}",
            Confirm = "CHOOSE",
            Cards = LevelCards(options.Select(o => CrateCards.ForPath(tree, next, o, offer.CrateItem))),
            StillValid = Alive,
            OnConfirm = i => Done(() => { Hud.PickChime(); Offers.ApplyCarried(offer, options[i].Branch); }),
            OnBack = OpenPick,
            OnCancelled = Later,
        });
    }

    /// <summary>Nothing to level: a crate card, claimed the way a crate would hand it over.</summary>
    private void Picked(Item item)
    {
        var offer = Offers.ForCrate(item);
        switch (offer.Kind)
        {
            case OfferKind.TreeSpare:
                Done(() => Grant(offer.Grant));
                return;
            case OfferKind.TreeLevel:
                Done(() =>
                {
                    if (!Offers.ConsumeMerge(offer)) { Grant(LuggagePatches.Spare(offer, item)); return; }
                    LuggagePatches.LevelUp(offer.Tree!, offer.Level, offer.Branch);
                    Grant(offer.Grant);
                });
                return;
            case OfferKind.Fork:
                break;
            default:
                Done(() => Grant(item));
                return;
        }
        var tree = offer.Tree!;
        int next = offer.Level;
        var options = Offers.Paths(tree, next);
        ChoiceScreen.Show(new ScreenSpec
        {
            Title = "CHOOSE YOUR PATH",
            Subtitle = $"{tree.Name} · level {next}",
            Confirm = "CHOOSE",
            Cards = LevelCards(options.Select(o => CrateCards.ForPath(tree, next, o, item))),
            StillValid = Alive,
            OnConfirm = i => Done(() =>
            {
                if (!Offers.ConsumeMerge(offer)) { Grant(LuggagePatches.Spare(offer, item)); return; }
                LuggagePatches.LevelUp(tree, next, options[i].Branch);
                Grant(Offers.Grant(options[i], item));
            }),
            OnBack = OpenPick,
            OnCancelled = Later,
        });
    }

    private void Done(System.Action give)
    {
        claimed++;
        levels = new List<Offer>();
        rolled = new List<Item>();
        deferUntil = Time.time + 1.5f; // the next level's cards don't slam in on top of this one
        Hud.PickTint = ChoiceScreen.LevelGold;
        try { give(); }
        finally { Hud.PickTint = null; }
        Plugin.Log.LogInfo($"XP: level {claimed} claimed.");
    }

    /// <summary>Level-up item cards wear gold (fusion cards keep their own look).</summary>
    private static List<CardSpec> LevelCards(IEnumerable<CardSpec> cards)
    {
        var list = cards.ToList();
        foreach (var c in list) if (c.Style == CardStyle.Perk) c.LevelUp = true;
        return list;
    }

    private static void Grant(Item item)
    {
        if (Character.localCharacter is not { } me) return;
        Hud.PickChime();
        Xp.ExpectCrateItem(item);
        GameUtils.instance.InstantiateAndGrab(item, me);
    }

    /// <summary>Three cards the way the nearest supply crate would roll them (its pool, the loot curve, no earned-only items).</summary>
    private static List<Item> Roll()
    {
        var result = new List<Item>();
        var me = Character.localCharacter;
        if (me == null) return result;
        var crate = FindObjectsByType<Luggage>(FindObjectsSortMode.None)
            .Where(l => l != null && l.GetType() == typeof(Luggage) && l.spawnMode != Spawner.SpawnMode.SingleItem
                        && (l.spawnMode == Spawner.SpawnMode.HeightBasedSpawnPools || LootCurve.Crates.Contains(l.spawnPool)))
            .OrderBy(l => Vector3.Distance(l.transform.position, me.Center))
            .FirstOrDefault();
        if (crate == null) return result;
        try
        {
            for (int attempt = 0; attempt < 12 && result.Count < 3; attempt++)
            {
                var objects = crate.GetObjectsToSpawn(3, true);
                if (objects == null) break;
                foreach (var item in objects.Where(o => o != null).Select(o => o.GetComponent<Item>()))
                {
                    if (item == null || result.Count >= 3) continue;
                    if (ModConfig.Leveling.Value && Items.IsEarnedOnly(item)) continue;
                    if (result.Any(r => r.itemID == item.itemID)) continue;
                    result.Add(item);
                }
            }
        }
        catch (System.Exception e) { Plugin.Log.LogError($"XP: failed to roll level-up cards: {e}"); }
        return result;
    }

    // --- the bar: a copy of the game's stamina bar, in gold ----------------------------------------

    private GUIManager? builtFor;
    private RectTransform? barRoot, full, fill, outline, srcFull, track;
    private Image? glow;
    private float offset, minWidth, shown;
    private TextMeshProUGUI? left, right;
    private static readonly Color GoldText = new Color32(0xFF, 0xD8, 0x6A, 0xFF);
    private const float GoldHue = 0.115f;

    private void UpdateBar(bool on)
    {
        var gui = GUIManager.instance;
        if (gui != builtFor) { builtFor = gui; barRoot = null; if (gui != null) Build(gui); }
        if (barRoot == null) return;
        bool show = on && !ChoiceScreen.IsOpen;
        if (barRoot.gameObject.activeSelf != show) barRoot.gameObject.SetActive(show);
        var bonusLabel = FastClearLabel();
        if (corner != null && corner.gameObject.activeSelf != show) corner.gameObject.SetActive(show);
        if (!show) return;

        Place();
        float target = Pending > 0 ? 1f : (xp - Earned * PerLevel) / PerLevel;
        if (target < shown - 0.5f) shown = 0f; // a level was spent: the bar empties, then fills to what's left
        shown = Mathf.Lerp(shown, target, Time.deltaTime * 6f);
        float w = full!.sizeDelta.x;
        fill!.sizeDelta = new Vector2(Mathf.Max(0f, shown * w + offset), fill.sizeDelta.y);
        fill.gameObject.SetActive(fill.sizeDelta.x > minWidth);
        outline!.sizeDelta = new Vector2(14f + w, outline.sizeDelta.y);
        // The dark track the fill runs in (the stamina bar's "max stamina"): always the full width.
        if (track != null)
        {
            if (!track.gameObject.activeSelf) track.gameObject.SetActive(true);
            track.sizeDelta = new Vector2(w + offset, track.sizeDelta.y);
        }
        if (glow != null)
        {
            var c = glow.color;
            c.a = Pending > 0 ? 0.25f + Mathf.Abs(Mathf.Sin(Time.time * 3f)) * 0.35f : 0f;
            glow.color = c;
        }

        left!.text = Pending > 0 ? "LEVEL UP" : "CLIMBER XP";
        left.alpha = Pending > 0 ? 0.6f + Mathf.Abs(Mathf.Sin(Time.time * 3f)) * 0.4f : 1f;
        if (biome < 0 || biome == int.MaxValue) bonusLabel.text = "";
        else
        {
            int bonus = Mathf.RoundToInt(BonusLeft * FastXp);
            string clock = "";
            if (clockFrom >= 0f)
            {
                float s = Mathf.Max(0f, ModConfig.XpBiomeMinutes.Value * 60f - (Time.time - clockFrom));
                clock = $"   <alpha=#99>{(int)s / 60}:{(int)s % 60:00}";
            }
            bonusLabel.text = $"FAST CLEAR BONUS  +{bonus} XP{clock}";
        }
        PlaceCorner();
        UpdatePops();
    }

    // --- "+100 XP" pop-ups above the bar's right end (everything but climbing) --------------------

    private class PopReq { public float Amount, ReadyAt; public string Label = ""; public bool Merge, Grey; }
    private class LivePop { public TextMeshProUGUI Text = null!; public float Born; }
    private readonly List<PopReq> pops = new();
    private readonly List<LivePop> live = new();
    private float nextPopAt;
    private bool crateCapShown, itemCapShown;
    private const float PopLife = 1.5f, PopRise = 40f, PopGather = 0.5f, PopGap = 0.45f;
    private static readonly Color PopGrey = new Color32(0xB8, 0xB4, 0xAC, 0xFF);

    /// <summary>Gains within half a second share one pop-up; merge: false always gets its own (campfire pair).</summary>
    private void Pop(float amount, string label, bool merge = true)
    {
        if (amount <= 0f) return;
        var last = pops.Count > 0 ? pops[pops.Count - 1] : null;
        if (merge && last != null && last.Merge && !last.Grey && Time.time < last.ReadyAt)
        {
            last.Amount += amount;
            if (last.Label != label) last.Label = "";
            return;
        }
        pops.Add(new PopReq { Amount = amount, Label = label, Merge = merge, ReadyAt = Time.time + (merge ? PopGather : 0f) });
    }

    /// <summary>A crate or item hit this biome's XP cap (once per biome each).</summary>
    private void Maxed() => pops.Add(new PopReq { Grey = true, Label = "XP maxed", ReadyAt = Time.time });

    private void UpdatePops()
    {
        if (full == null) return;
        if (pops.Count > 0 && Time.time >= pops[0].ReadyAt && Time.time >= nextPopAt)
        {
            var p = pops[0];
            pops.RemoveAt(0);
            nextPopAt = Time.time + PopGap;
            string text = p.Grey ? p.Label
                : $"+{p.Amount:0} XP" + (p.Label.Length > 0 ? $"<size=70%><alpha=#99>  {p.Label}</size>" : "");
            var t = Label(full, "XP Pop", new Vector2(1f, 1f), TextAlignmentOptions.BottomRight, left);
            t.fontSize = p.Grey ? 18f : 22f;
            t.color = p.Grey ? PopGrey : GoldText;
            t.text = text;
            t.alpha = 0f;
            live.Add(new LivePop { Text = t, Born = Time.time });
        }
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var lp = live[i];
            float age = Time.time - lp.Born;
            if (lp.Text == null || age >= PopLife)
            {
                if (lp.Text != null) Destroy(lp.Text.gameObject);
                live.RemoveAt(i);
                continue;
            }
            float k = age / PopLife;
            ((RectTransform)lp.Text.transform).anchoredPosition = new Vector2(0f, 38f + PopRise * Ease.OutQuad(k));
            lp.Text.alpha = Mathf.Min(Mathf.Clamp01(age / 0.15f), Mathf.Clamp01((PopLife - age) / 0.6f));
        }
    }

    private TextMeshProUGUI? corner, version;
    private float nextVersionLook;

    /// <summary>The fast-clear bonus sits top left, after the game's build number (on the bar if there's none).</summary>
    private TextMeshProUGUI FastClearLabel()
    {
        if (corner != null) return corner;
        if (Time.time < nextVersionLook) return right!;
        nextVersionLook = Time.time + 2f;
        var vt = FindObjectsByType<VersionString>(FindObjectsSortMode.None)
            .Where(v => v != null && v.isActiveAndEnabled).Select(v => v.GetComponent<TextMeshProUGUI>()).FirstOrDefault(t => t != null);
        if (vt == null) return right!;
        version = vt;
        var rt = Ui.Rect(vt.rectTransform, "RogueClimb FastClear");
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(700f, 40f);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = vt.font;
        t.fontSharedMaterial = vt.fontSharedMaterial;
        t.fontSize = Mathf.Max(vt.fontSize, 18f);
        t.color = GoldText;
        t.alignment = TextAlignmentOptions.TopLeft;
        t.characterSpacing = 2f;
        t.enableWordWrapping = false;
        t.raycastTarget = false;
        corner = t;
        right!.text = "";
        Plugin.Log.LogInfo($"XP: fast-clear bonus moved beside the build number '{vt.text}' ({vt.fontSize:0}pt).");
        return t;
    }

    /// <summary>Just right of the build number's text, tops lined up.</summary>
    private void PlaceCorner()
    {
        if (corner == null || version == null) return;
        var r = version.rectTransform.rect;
        var b = version.textBounds;
        ((RectTransform)corner.transform).anchoredPosition = new Vector2(b.max.x - r.xMin + 24f, b.max.y - r.yMax);
    }

    private void Build(GUIManager gui)
    {
        try
        {
            var src = gui.bar;
            if (src == null) return;
            srcFull = src.fullBar;
            var go = Instantiate(src.gameObject, src.transform.parent, false);
            go.name = "RogueClimb XP Bar";
            var sb = go.GetComponent<StaminaBar>();
            sb.enabled = false;
            full = sb.fullBar;
            fill = sb.staminaBar;
            outline = sb.staminaBarOutline;
            glow = sb.staminaGlow;
            offset = sb.staminaBarOffset;
            minWidth = sb.minStaminaBarWidth;
            track = sb.maxStaminaBar;
            foreach (var extra in new Component?[] { sb.staminaBarOutlineOverflowBar, sb.extraBar, sb.rainbowStamina,
                         sb.moraleBoostText, sb.petrifyAffliction, sb.extraStaminaIcon })
                if (extra != null) extra.gameObject.SetActive(false);
            if (sb.shield != null) sb.shield.SetActive(false);
            if (sb.campfire != null) sb.campfire.SetActive(false);
            foreach (var a in go.GetComponentsInChildren<BarAffliction>(true)) a.gameObject.SetActive(false);
            // Only the UI's own drawing and layout stays awake: nothing in the copy reads the scout.
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb is not UIBehaviour) mb.enabled = false;
            foreach (var anim in go.GetComponentsInChildren<Animator>(true)) anim.enabled = false;
            Destroy(sb);

            foreach (var img in fill.GetComponentsInChildren<Image>(true)) img.color = Gold(img.color);
            if (glow != null) glow.color = Gold(glow.color);

            barRoot = (RectTransform)go.transform;
            // If the HUD lays its children out, ours is placed by hand instead.
            (go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>()).ignoreLayout = true;
            go.GetComponent<LayoutElement>().enabled = true;
            float s = Mathf.Clamp(ModConfig.XpBarScale.Value, 0.3f, 2f);
            barRoot.localScale = Vector3.one * s;
            var font = src.moraleBoostText != null ? src.moraleBoostText : null;
            left = Label(full, "Level", new Vector2(0f, 1f), TextAlignmentOptions.BottomLeft, font);
            right = Label(full, "FastClear", new Vector2(1f, 1f), TextAlignmentOptions.BottomRight, font);
            shown = 0f;
            Plugin.Log.LogInfo($"XP bar: copied the stamina bar ({srcFull.rect.width:0}x{srcFull.rect.height:0}, fill sprite '{fill.GetComponent<Image>()?.sprite?.name}'), scale {s:0.##}.\n"
                + Hierarchy(go.transform, ""));
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"XP bar: couldn't copy the stamina bar: {e}");
            barRoot = null;
        }
    }

    /// <summary>The stamina green, turned gold (same brightness and shading).</summary>
    private static Color Gold(Color c)
    {
        Color.RGBToHSV(c, out _, out float s, out float v);
        var g = Color.HSVToRGB(GoldHue, Mathf.Max(s, 0.6f), Mathf.Max(v, 0.55f));
        g.a = c.a;
        return g;
    }

    private static TextMeshProUGUI Label(RectTransform parent, string name, Vector2 corner, TextAlignmentOptions align, TextMeshProUGUI? like)
    {
        var rt = Ui.Rect(parent, name);
        rt.anchorMin = rt.anchorMax = corner;
        rt.pivot = new Vector2(corner.x, 0f);
        rt.anchoredPosition = new Vector2(0f, 6f);
        rt.sizeDelta = new Vector2(600f, 34f);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (like != null) { t.font = like.font; t.fontSharedMaterial = like.fontSharedMaterial; }
        else if (Hud.PromptFont is { } f) t.font = f;
        t.fontSize = 26f;
        t.color = GoldText;
        t.alignment = align;
        t.characterSpacing = 2f;
        t.enableWordWrapping = false;
        t.raycastTarget = false;
        return t;
    }

    /// <summary>Sits right above the stamina bar, left edges lined up (plus the config nudge).</summary>
    private void Place()
    {
        if (srcFull == null || full == null || barRoot == null) return;
        var a = new Vector3[4];
        var b = new Vector3[4];
        srcFull.GetWorldCorners(a);   // 0 bottom-left, 1 top-left
        full.GetWorldCorners(b);
        float px = srcFull.lossyScale.y; // one canvas pixel in world units
        Vector3 want = new Vector3(a[1].x + ModConfig.XpBarX.Value * px, a[1].y + (46f + ModConfig.XpBarY.Value) * px, b[0].z);
        Vector3 delta = want - b[0];
        if (delta.sqrMagnitude > 0.0001f) barRoot.position += delta;
    }

    private static string Hierarchy(Transform t, string indent)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(indent).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (off)");
        if (t is RectTransform r) sb.Append($" [{r.rect.width:0}x{r.rect.height:0}]");
        var img = t.GetComponent<Image>();
        if (img != null) sb.Append($" img '{img.sprite?.name}' #{ColorUtility.ToHtmlStringRGBA(img.color)}");
        sb.Append('\n');
        if (indent.Length < 12)
            foreach (Transform c in t) sb.Append(Hierarchy(c, indent + "  "));
        return sb.ToString();
    }
}
