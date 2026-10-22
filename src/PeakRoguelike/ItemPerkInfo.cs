using System.Collections.Generic;
using System.Linq;
using Peak.Afflictions;
using UnityEngine;

namespace PeakRoguelike;

public enum PerkTheme { Climber, Mountaineer, Explorer }

/// <summary>
/// Everything a perk card shows about an item. For now this is derived from the vanilla
/// item (its actions/components); later the roguelike perk data will replace it.
/// </summary>
public class ItemPerkInfo
{
    public Item Item = null!;
    public string Title = "";
    public string Tier = "";
    public Color TierColor = Color.white;
    public string Description = "";
    public string Footer = "";
    public PerkTheme Theme;

    public static ItemPerkInfo From(Item item)
    {
        var info = new ItemPerkInfo { Item = item };
        info.Title = SafeName(item);
        info.Theme = ClassifyTheme(item, info.Title.ToLowerInvariant());

        Rarity rarity = item.GetComponent<LootData>()?.Rarity ?? Rarity.Common;
        info.Tier = rarity switch
        {
            Rarity.RidiculouslyRare => "Ultra Rare",
            _ => rarity.ToString(),
        };
        info.TierColor = rarity switch
        {
            Rarity.Common => new Color32(0xE4, 0xDC, 0xC8, 0xFF),
            Rarity.Uncommon => new Color32(0x9B, 0xE0, 0x7A, 0xFF),
            Rarity.Rare => new Color32(0x7C, 0xC4, 0xFF, 0xFF),
            Rarity.Epic => new Color32(0xCB, 0x9C, 0xFF, 0xFF),
            Rarity.Legendary => new Color32(0xFF, 0xC8, 0x4F, 0xFF),
            Rarity.Mythic => new Color32(0xFF, 0x7A, 0x96, 0xFF),
            _ => new Color32(0xFF, 0x9E, 0x3D, 0xFF),
        };

        info.Description = Describe(item);
        var footer = new List<string>();
        if (item.totalUses > 1) footer.Add($"{item.totalUses} uses");
        footer.Add($"Weight {Mathf.Max(0, item.CarryWeight)}");
        info.Footer = string.Join("   •   ", footer);
        return info;
    }

    public CardSpec ToCard() => new()
    {
        Style = CardStyle.Perk,
        Theme = Theme,
        Title = Title,
        Tag = Tier,
        TagColor = TierColor,
        Body = Description,
        Footer = Footer,
        Icon = Items.Icon(Item),
    };

    private static string SafeName(Item item) => Items.DisplayName(item);

    private static readonly string[] ClimberWords = { "rope", "piton", "spike", "chalk", "cannon", "grappl", "chain", "ladder", "bean", "shelf", "climb", "anchor", "balloon", "glider", "jetpack" };
    private static readonly string[] MountaineerWords = { "bandage", "aid", "cure", "remedy", "antidote", "heat", "lantern", "torch", "flare", "parasol", "sunscreen", "blanket", "medic", "salve", "elixir" };

    private static PerkTheme ClassifyTheme(Item item, string name)
    {
        if (item.GetComponent<RopeSpool>() || item.GetComponent<RopeShooter>() || item.GetComponent<ClimbingSpikeComponent>()
            || item.GetComponent<MagicBean>() || item.GetComponent<ShelfShroom>() || item.GetComponent<Balloon>()
            || item.GetComponent<JetpackItem>() || item.GetComponent<Antigrav>()
            || item.GetComponent<Action_ApplySuperJump>() || item.GetComponent<Action_GiveExtraStamina>()
            || item.GetComponent<Action_ApplyInfiniteStamina>()
            || ClimberWords.Any(name.Contains))
            return PerkTheme.Climber;

        bool heals = item.GetComponents<Action_ModifyStatus>().Any(a => a.changeAmount < 0f);
        if (heals || item.GetComponent<Action_ClearAllStatus>() || item.GetComponent<Lantern>() || item.GetComponent<ItemTorch>()
            || item.GetComponent<Flare>() || item.GetComponent<Parasol>()
            || MountaineerWords.Any(name.Contains))
            return PerkTheme.Mountaineer;

        return PerkTheme.Explorer;
    }

    private static string Describe(Item item)
    {
        var lines = new List<string>();
        void Add(string s) { if (!string.IsNullOrEmpty(s) && !lines.Contains(s)) lines.Add(s); }

        float hunger = item.GetComponents<Action_RestoreHunger>().Sum(a => a.restorationAmount);
        if (hunger > 0f) Add($"Fills {Amount.Of(hunger, "your hunger")}");
        foreach (var a in item.GetComponents<Action_ModifyStatus>()) if (!a.ifSkeleton) Add(DescribeStatus(a.statusType, a.changeAmount));
        float stam = item.GetComponents<Action_GiveExtraStamina>().Sum(a => a.amount);
        if (stam > 0f) Add($"+{Amount.Bars(stam, "bonus stamina")}");
        foreach (var a in item.GetComponents<Action_ApplyInfiniteStamina>()) Add($"Infinite stamina for {Amount.Seconds(a.buffTime)}");
        if (item.GetComponent<Action_ClearAllStatus>()) Add("Cures all ailments");
        foreach (var a in item.GetComponents<Action_ApplyAffliction>())
        {
            Add(DescribeAffliction(a.affliction));
            if (a.extraAfflictions != null) foreach (var x in a.extraAfflictions) Add(DescribeAffliction(x));
        }
        if (item.GetComponent<Action_InflictPoison>()) Add("Poisonous - handle with care");
        if (item.GetComponent<Action_MoraleBoost>()) Add("Boosts the stamina of nearby scouts");
        if (item.GetComponent<Action_ApplySuperJump>()) Add("Grants a mighty super jump");
        if (item.GetComponent<Action_RandomMushroomEffect>()) Add("Random effect - feeling lucky?");
        if (item.GetComponent<Action_WarpRandomly>() || item.GetComponent<Action_WarpToBiome>() || item.GetComponent<Action_WarpToRandomPlayer>()) Add("Teleports you somewhere else");
        if (item.GetComponent<Action_LaunchPlayer>()) Add("Launches you skyward");

        var spool = item.GetComponent<RopeSpool>();
        if (spool) Add(spool.isAntiRope ? "Ties a rope that floats upward" : "Ties off a rope anyone can climb");
        if (item.GetComponent<RopeShooter>()) Add("Fires an anchored rope up a wall");
        if (item.GetComponent<ClimbingSpikeComponent>()) Add("Hammer a handhold into any wall");
        if (item.GetComponent<MagicBean>()) Add("Grows a climbable vine");
        if (item.GetComponent<ShelfShroom>()) Add("Sprouts a ledge to stand on");
        if (item.GetComponent<Lantern>()) Add("Light and warmth through the night");
        if (item.GetComponent<ItemTorch>()) Add("A warm, bright flame");
        if (item.GetComponent<Flare>()) Add("Lights the way for your team");
        if (item.GetComponent<CompassPointer>()) Add("Always points toward the peak");
        if (item.GetComponent<Actions_Binoculars>()) Add("Scout the route far ahead");
        if (item.GetComponent<Parasol>()) Add("Drift gently down and block the sun");
        if (item.GetComponent<Balloon>()) Add("Lifts you off your feet");
        if (item.GetComponent<JetpackItem>()) Add("Blast through the air");
        if (item.GetComponent<Antigrav>()) Add("Defies gravity");
        if (item.GetComponent<MagicBugle>()) Add("A tune that rallies the team");
        if (item.GetComponent<Frisbee>()) Add("Fun for the whole expedition");

        // The mod's own extras go in before the cut to three lines.
        if (ModConfig.ItemTrees.Value && Cookies.Is(item) && ModConfig.CookieGlass.Value > 0)
            lines.Insert(Mathf.Min(1, lines.Count), $"Each bite removes {Amount.Of(ModConfig.CookieGlass.Value * 0.01f, "your glass")}"
                + (ModConfig.CookieInjury.Value > 0f ? $"\nand heals {Amount.Of(ModConfig.CookieInjury.Value * 0.01f, "your injury")}" : ""));
        if (Stove.IsStoveItem(item) && ModConfig.StoveGlass.Value > 0)
            lines.Insert(0, $"Food cooked on it removes\n{Amount.Of(ModConfig.StoveGlass.Value * 0.01f, "your glass")}");
        if (lines.Count == 0) lines.Add("Trusty expedition gear");
        var shown = lines.Take(3).ToList();
        // Milk, Antidote, Candle, skull, dagger... anything that goes into a fusion says so.
        if (Fusion.IsPlainIngredient(item)) shown.Add("Fusion-ready.");
        return string.Join("\n", shown);
    }

    private static string DescribeStatus(CharacterAfflictions.STATUSTYPE status, float change)
    {
        string name = status switch
        {
            CharacterAfflictions.STATUSTYPE.Hot => "heat",
            CharacterAfflictions.STATUSTYPE.Drowsy => "drowsiness",
            _ => status.ToString().ToLowerInvariant(),
        };
        if (change < 0f)
        {
            string of = Amount.Of(change, "your " + name);
            return status switch
            {
                CharacterAfflictions.STATUSTYPE.Hunger => $"Fills {of}",
                CharacterAfflictions.STATUSTYPE.Cold => $"Warms {of}",
                CharacterAfflictions.STATUSTYPE.Hot => $"Cools {of}",
                CharacterAfflictions.STATUSTYPE.Drowsy => $"Clears {of}",
                CharacterAfflictions.STATUSTYPE.Curse => $"Lifts {of}",
                _ => $"Heals {of}",
            };
        }
        return $"Adds {Amount.Bars(change, name)}";
    }

    private static string DescribeAffliction(Affliction? a)
    {
        if (a == null) return "";
        string time = a.totalTime > 0f ? $" for {Amount.Seconds(a.totalTime)}" : " for a while";
        return a.GetType().Name switch
        {
            nameof(Affliction_FasterBoi) => "Run faster" + time,
            nameof(Affliction_InfiniteStamina) => "Infinite stamina" + time,
            nameof(Affliction_ClimbingChalk) => "Better grip while climbing",
            nameof(Affliction_LowGravity) => "Low gravity" + time,
            nameof(Affliction_HealAll) => "Heals all ailments over time",
            nameof(Affliction_AddBonusStamina) => "Grants bonus stamina",
            nameof(Affliction_Sunscreen) => "Protects you from the scorching sun",
            nameof(Affliction_NoHunger) => "No hunger" + time,
            nameof(Affliction_Invincibility) => "Invincible" + time,
            nameof(Affliction_Glowing) => "You glow in the dark",
            nameof(Affliction_Numb) => "Numbs the pain",
            nameof(Affliction_AdjustColdOverTime) => "Keeps the cold at bay",
            nameof(Affliction_AdjustStatusOverTime) => "Heals over time",
            nameof(Affliction_PoisonOverTime) => "Poisons you over time",
            nameof(Affliction_Blind) => "Blinds you" + time,
            nameof(Affliction_Chaos) => "Unpredictable effects",
            nameof(Affliction_Exhaustion) => "Leaves you exhausted",
            nameof(Affliction_DoubleJumpAmulet) => "Grants a double jump",
            nameof(Affliction_BingBongShield) => "Shields you from harm",
            nameof(Affliction_MassSuperJump) => "Super jump for the whole team",
            _ => "",
        };
    }
}

internal enum OfferKind { Plain, TreeLevel, Fork, TreeSpare, Craft, Locked }

/// <summary>What picking a crate card will do for the local player.</summary>
internal class Offer
{
    public OfferKind Kind;
    public Item CrateItem = null!;
    public Item Grant = null!;
    public TreeDef? Tree;
    public CraftDef? Craft;
    public int Level, Branch;
    /// <summary>A full stack (3 copies in one pocket) merging into one item a level up (StackMerge).</summary>
    public bool Merge;
    /// <summary>The pocket it works on (the stack that merges / the item a level-up levels) and what was in it.</summary>
    public byte Slot;
    /// <summary>Trade-up: the pocket holding the 4th copy (null when the stack itself holds it).</summary>
    public byte? ExtraSlot;
    public ushort Carried;
    /// <summary>Crates: copies of this spare already in one pocket.</summary>
    public int Have;
    /// <summary>XP level-up of a tree you found before but don't carry: claiming it hands you a copy.</summary>
    public bool Fresh;
}

/// <summary>
/// Turns a crate item into the local player's offer: the item's tree levels up (asking for a
/// path at a fork), or - once it's maxed - you take its top item again.
/// </summary>
internal static class Offers
{
    public static Offer Resolve(Item item)
    {
        var o = new Offer { Kind = OfferKind.Plain, CrateItem = item, Grant = item };
        if (!ModConfig.Leveling.Value) return o;

        var tree = Trees.Get(Trees.Classify(item));
        if (tree == null) return o;
        o.Tree = tree;
        int cur = RunState.Level(tree.Id), br = RunState.Branch(tree.Id);
        o.Branch = br;
        if (cur >= tree.MaxFor(br))
        {
            o.Level = cur;
            o.Kind = OfferKind.TreeSpare;
            o.Grant = Grant(tree.At(cur, br), item);
            return o;
        }
        o.Level = cur + 1;
        if (tree.ForksAt(o.Level, br)) { o.Kind = OfferKind.Fork; return o; }
        o.Kind = OfferKind.TreeLevel;
        o.Grant = Grant(tree.At(o.Level, br), item);
        return o;
    }

    public static Item Grant(LevelDef def, Item crateItem) =>
        def.Grants != null && Items.Find(def.Grants) is { } prefab ? prefab : crateItem;

    /// <summary>
    /// What a crate card does: a new tree's first item; otherwise a spare at your level. The spare
    /// joins its stack; a full stack of 3 then asks to level up (StackMerge).
    /// </summary>
    public static Offer ForCrate(Item item)
    {
        var o = Resolve(item);
        var tree = o.Tree;
        if (tree == null || o.Kind == OfferKind.TreeSpare) return o;
        int cur = RunState.Level(tree.Id), br = RunState.Branch(tree.Id);
        if (cur == 0) return o;
        var spare = Grant(tree.At(cur, br), item);
        var (have, _) = Stack(spare.itemID, br);
        if (Stacks.GreenBlocks(Player.localPlayer)) have = 0;
        return new Offer { Kind = OfferKind.TreeSpare, CrateItem = item, Grant = spare, Tree = tree, Level = cur, Branch = br, Have = have };
    }

    /// <summary>
    /// A crate card's trade-up twin: you carry a full stack of this card's item, so instead of the
    /// 4th copy you may trade the stack (and the copy) for one item two levels up. Null = no trade.
    /// </summary>
    public static Offer? ForCrateTrade(Item item)
    {
        var spare = ForCrate(item);
        var tree = spare.Tree;
        if (tree == null || spare.Kind != OfferKind.TreeSpare || !Stacks.On || Stacks.GreenBlocks(Player.localPlayer)) return null;
        int cur = spare.Level, br = spare.Branch;
        if (cur < 1 || cur >= tree.MaxFor(br) || Items.Find(Trees.BaseRole(tree.Id)) is not { } baseItem) return null;
        var (have, slot) = Stack(spare.Grant.itemID, br);
        if (have < Stacks.Limit) return null;
        int level = Mathf.Min(cur + TradeLevels, br == 0 ? tree.Levels.Length : tree.MaxFor(br));
        var o = new Offer
        {
            Tree = tree, Level = level, Branch = br, CrateItem = spare.Grant, Merge = true,
            Slot = slot, ExtraSlot = null, Carried = spare.Grant.itemID, Have = have,
            Kind = br == 0 && tree.Levels[level - 1].Length > 1 ? OfferKind.Fork : OfferKind.TreeLevel,
        };
        if (o.Kind == OfferKind.TreeLevel) o.Grant = Grant(tree.At(level, br), baseItem);
        return o;
    }

    /// <summary>The biggest stack of this item in your pockets (on the given path): copies and pocket.</summary>
    private static (int have, byte slot) Stack(ushort itemID, int br)
    {
        int have = 0;
        byte slot = 0;
        if (Player.localPlayer is { } p)
            foreach (var s in p.itemSlots.Append(p.tempFullSlot))
            {
                if (s == null || s.IsEmpty() || s.prefab == null || s.prefab.itemID != itemID || SugarRush.IsMarked(s.data)) continue;
                if (ItemLevels.KindOf(s.prefab) is { BothPaths: true } && ItemLevels.IsSecondPath(s.data) != (br == 2)) continue;
                int n = Stacks.Count(s.data);
                if (n > have) { have = n; slot = s.itemSlotID; }
            }
        return (have, slot);
    }

    /// <summary>Levels a trade-up adds (a full stack and one more copy).</summary>
    public const int TradeLevels = 2;

    /// <summary>
    /// A trade-up: a full stack (3) of a tree's current item plus a 4th copy somewhere in your
    /// pockets (a crate card or a pickup just brought it in). All four go for one item two levels
    /// higher (one, if the tree tops out sooner); a path to choose if the jump reaches a fork.
    /// </summary>
    public static Offer? ForStack()
    {
        if (!Stacks.On || Stacks.GreenBlocks(Player.localPlayer) || Player.localPlayer is not { } p) return null;
        foreach (var tree in Trees.All.Values)
        {
            int cur = RunState.Level(tree.Id), br = RunState.Branch(tree.Id);
            if (cur < 1 || cur >= tree.MaxFor(br) || Items.Find(Trees.BaseRole(tree.Id)) is not { } baseItem) continue;
            var spare = Grant(tree.At(cur, br), baseItem);
            var (have, slot) = Stack(spare.itemID, br);
            if (have < Stacks.Limit) continue;
            // The 4th copy: in any other pocket, or the hand slot a full pocket row puts it in.
            byte? extra = null;
            foreach (var s in p.itemSlots.Append(p.tempFullSlot))
            {
                if (s == null || s.itemSlotID == slot || s.IsEmpty() || s.prefab == null || s.prefab.itemID != spare.itemID || SugarRush.IsMarked(s.data)) continue;
                if (ItemLevels.KindOf(s.prefab) is { BothPaths: true } && ItemLevels.IsSecondPath(s.data) != (br == 2)) continue;
                extra = s.itemSlotID;
                break;
            }
            if (extra == null && have <= Stacks.Limit) continue;
            int level = Mathf.Min(cur + TradeLevels, br == 0 ? tree.Levels.Length : tree.MaxFor(br));
            var o = new Offer
            {
                Tree = tree, Level = level, Branch = br, CrateItem = spare, Merge = true,
                Slot = slot, ExtraSlot = extra, Carried = spare.itemID, Have = have,
                // No path yet and the jump lands on a fork level: choose the path.
                Kind = br == 0 && tree.Levels[level - 1].Length > 1 ? OfferKind.Fork : OfferKind.TreeLevel,
            };
            if (o.Kind == OfferKind.TreeLevel) o.Grant = Grant(tree.At(level, br), baseItem);
            return o;
        }
        return null;
    }

    /// <summary>Merging: the stack of copies goes (false if it's gone since the card was shown).</summary>
    public static bool ConsumeMerge(Offer o)
    {
        if (!o.Merge) return true;
        var p = Player.localPlayer;
        var me = Character.localCharacter;
        var s = p?.GetItemSlot(o.Slot);
        if (p == null || me == null || s == null || s.IsEmpty() || s.prefab == null || s.prefab.itemID != o.Carried || Stacks.Count(s.data) < Stacks.Limit)
        {
            Plugin.Log.LogInfo("Merge: the stack is gone - a spare instead.");
            return false;
        }
        int n = Stacks.Count(s.data);
        string name = s.prefab.gameObject.name; // the slot forgets its prefab once emptied
        // The 4th copy goes too (a trade-up is the whole stack and one more).
        if (o.ExtraSlot is { } x && p.GetItemSlot(x) is { } es && !es.IsEmpty() && es.prefab != null && es.prefab.itemID == o.Carried)
        {
            if (!Stacks.TakeOne(p, x))
            {
                bool heldExtra = me.refs.items.currentSelectedSlot.IsSome && me.refs.items.currentSelectedSlot.Value == x;
                p.EmptySlot(Zorro.Core.Optionable<byte>.Some(x));
                if (heldExtra) me.refs.items.EquipSlot(Zorro.Core.Optionable<byte>.None);
            }
            n++;
        }
        bool held = me.refs.items.currentSelectedSlot.IsSome && me.refs.items.currentSelectedSlot.Value == o.Slot;
        p.EmptySlot(Zorro.Core.Optionable<byte>.Some(o.Slot));
        if (held) me.refs.items.EquipSlot(Zorro.Core.Optionable<byte>.None);
        Xp.Merged();
        Plugin.Log.LogInfo($"Trade-up: {name} x{n} -> {o.Tree?.Name} level {o.Level}.");
        return true;
    }

    /// <summary>
    /// XP level-ups: one card per tree that can still go up - every tree you carry, and every tree
    /// you've picked up before this run (those come with a copy). Your carried item closest to max
    /// always shows; up to [Xp] LevelCards cards.
    /// </summary>
    public static List<Offer> Carried()
    {
        var list = new List<Offer>();
        var p = Player.localPlayer;
        if (p == null || !ModConfig.Leveling.Value) return list;
        var trees = new HashSet<TreeId>();
        foreach (var s in p.itemSlots.Append(p.tempFullSlot))
        {
            if (s == null || s.IsEmpty() || s.prefab == null || SugarRush.IsMarked(s.data)) continue;
            var o = Resolve(s.prefab);
            if (o.Tree == null || o.Kind is not (OfferKind.TreeLevel or OfferKind.Fork) || !trees.Add(o.Tree.Id)) continue;
            o.Slot = s.itemSlotID;
            o.Carried = s.prefab.itemID;
            list.Add(o);
        }
        int carried = list.Count;
        // Trees found earlier but not in your pockets now: levelling one hands you a copy.
        foreach (var tree in Trees.All.Values)
        {
            if (trees.Contains(tree.Id) || RunState.Level(tree.Id) < 1 || Items.Find(Trees.BaseRole(tree.Id)) is not { } baseItem) continue;
            var o = Resolve(baseItem);
            if (o.Tree == null || o.Kind is not (OfferKind.TreeLevel or OfferKind.Fork)) continue;
            o.Fresh = true;
            list.Add(o);
        }
        int cards = Mathf.Clamp(ModConfig.XpLevelCards.Value, 1, 6);
        if (list.Count <= 1) return list;
        int Left(Offer o) => o.Tree!.MaxFor(o.Branch) - (o.Level - 1);
        var first = (carried > 0 ? list.Take(carried) : list).OrderBy(Left).ThenBy(_ => Random.value).First();
        return new[] { first }.Concat(list.Where(o => o != first).OrderBy(_ => Random.value).Take(cards - 1)).ToList();
    }

    /// <summary>
    /// An XP level-up on a carried item. Its tree goes up (raising that tree's items in your pockets);
    /// if the new level is a different item (Sports Drink -> Energy Drink, Bandage -> Medkit), you also get
    /// one of it - the carried item (or stack) stays.
    /// </summary>
    public static void ApplyCarried(Offer o, int branch)
    {
        var tree = o.Tree!;
        var p = Player.localPlayer;
        var me = Character.localCharacter;
        var def = tree.At(o.Level, branch);
        var baseItem = Items.Find(Trees.BaseRole(tree.Id));
        Item next = baseItem != null ? Grant(def, baseItem) : (Items.TryGet(o.Carried, out var c) ? c : null!);
        Items.TryGet(o.Carried, out var carried);
        bool evolves = carried != null && next != null && (ItemLevels.KindOf(carried) is { } k
            ? o.Level > k.Max || (k.Branch != 0 && !k.BothPaths && branch != k.Branch)
            : carried.itemID != next.itemID);
        LuggagePatches.LevelUp(tree, o.Level, branch);
        if (o.Fresh && me != null && next != null)
        {
            // A tree you found before: here's one at the new level.
            Xp.ExpectCrateItem(next);
            GameUtils.instance.InstantiateAndGrab(next, me);
            Plugin.Log.LogInfo($"Level-up: {tree.Name} {o.Level} - a fresh {next.gameObject.name}.");
            return;
        }
        if (!evolves || p == null || me == null) return;
        // An evolution never takes anything: your stack stays as it is, and the new item is extra.
        Xp.ExpectCrateItem(next);
        GameUtils.instance.InstantiateAndGrab(next, me);
        Plugin.Log.LogInfo($"Level-up: {tree.Name} {o.Level} evolved - a new {next!.gameObject.name} (your {carried!.gameObject.name} stays).");
    }

    /// <summary>The paths open at a fork (both, always).</summary>
    public static LevelDef[] Paths(TreeDef tree, int level) => tree.Levels[level - 1];

    /// <summary>Fusions you've made whose ingredients this crate rolled: each one comes along as an extra card.</summary>
    public static List<Recipe> UnlockedFusions(IEnumerable<Item> crateItems)
    {
        var list = new List<Recipe>();
        if (!ModConfig.Leveling.Value || !ModConfig.FusionCards.Value || RunState.Fused.Count == 0) return list;
        var items = crateItems.ToList();
        foreach (var r in Fusion.Recipes)
            if (RunState.Fused.Contains(r.Id) && items.Any(r.Uses)) list.Add(r);
        return list;
    }
}

/// <summary>Builds the cards a crate offers: plain perks, tree level-ups and crafted upgrades.</summary>
internal static class CrateCards
{
    private const string Dot = " · ";
    private const string Arrow = " → ";

    public static CardSpec ForItem(Item item)
    {
        if (item.GetComponent<Flare>() != null) return FindCards.FlareCard(item);
        var card = ItemPerkInfo.From(item).ToCard();
        var o = Offers.ForCrate(item);
        var tree = o.Tree;
        switch (o.Kind)
        {
            case OfferKind.TreeSpare when o.Level < tree!.MaxFor(o.Branch):
            {
                var def = tree.At(o.Level, o.Branch);
                card.Theme = tree.Theme;
                card.Title = def.Name;
                card.Tag = $"Lv {o.Level}";
                card.TagColor = Trees.LevelColor(tree.Id, o.Level, o.Branch);
                card.Body = CardStats.Tree(tree, def, o.Level, o.Branch);
                card.Footer = Stacks.On && !Stacks.GreenBlocks(Player.localPlayer) && o.Have < Stacks.Limit
                    ? $"{tree.Name}{Dot}x{o.Have + 1} / {Stacks.Limit}"
                    : Stacks.On && !Stacks.GreenBlocks(Player.localPlayer) && o.Have >= Stacks.Limit ? $"{tree.Name}{Dot}a 4th copy" : $"{tree.Name}{Dot}level {o.Level}";
                card.Icon = Items.Icon(o.Grant);
                return card;
            }
            case OfferKind.TreeSpare:
            {
                var def = tree!.At(o.Level, o.Branch);
                card.Theme = tree.Theme;
                card.Title = def.Name;
                card.Tag = "Maxed";
                card.TagColor = Trees.Gold;
                card.Body = $"Your {tree.Name.ToLowerInvariant()} is fully upgraded.\nTake a spare.";
                card.Footer = $"{tree.Name}{Dot}level {o.Level} / {tree.MaxFor(o.Branch)}";
                card.Icon = Items.Icon(o.Grant);
                return card;
            }
            case OfferKind.Fork:
                card.Theme = tree!.Theme;
                card.Tag = o.Level == 1 ? "New" : $"Lv {o.Level - 1}{Arrow}{o.Level}";
                card.Title = $"{tree.Name}: Fork";
                card.TagColor = Trees.Steel;
                card.Body = string.Join("  or  ", Offers.Paths(tree, o.Level).Select(x => x.Name)) + "\nChoose your path on claim.";
                card.Footer = $"{tree.Name}{Dot}level {o.Level}";
                return card;
            case OfferKind.TreeLevel:
                return Level(card, o);
            default:
                return card;
        }
    }

    /// <summary>The trade-up pop-up's card: the stack and a 4th copy become one item two levels up.</summary>
    public static CardSpec ForStack(Offer o)
    {
        var card = Level(ItemPerkInfo.From(o.CrateItem).ToCard(), o);
        if (card.Tag != "NEW ITEM") card.Tag = $"Lv {RunState.Level(o.Tree!.Id)}{Arrow}{o.Level}";
        card.Footer = $"{o.Tree!.Name}{Dot}x{Stacks.Limit + 1}{Arrow}level {o.Level}";
        return card;
    }

    /// <summary>A crate's trade-up card, beside the plain 4th copy: "Trade up" over the level jump.</summary>
    public static CardSpec ForCrateTrade(Offer o)
    {
        if (o.Kind == OfferKind.Fork)
        {
            var tree = o.Tree!;
            var card = ItemPerkInfo.From(o.CrateItem).ToCard();
            card.Theme = tree.Theme;
            card.Title = $"Trade up: {tree.Name}";
            card.Tag = $"Lv {RunState.Level(tree.Id)}{Arrow}{o.Level}";
            card.TagColor = Trees.Steel;
            card.Body = $"Your {Stacks.Limit} {tree.Name.ToLowerInvariant()}s and this one become one, level {o.Level}.\n"
                + string.Join("  or  ", Offers.Paths(tree, o.Level).Select(x => x.Name)) + " - choose on claim.";
            card.Footer = $"{tree.Name}{Dot}trade up{Arrow}level {o.Level}";
            return card;
        }
        var trade = ForStack(o);
        trade.Title = $"Trade up: {trade.Title}";
        trade.Footer = $"{o.Tree!.Name}{Dot}your {Stacks.Limit} + this{Arrow}level {o.Level}";
        return trade;
    }

    /// <summary>A tree going up a level (new item, merge, XP level-up).</summary>
    private static CardSpec Level(CardSpec card, Offer o)
    {
        var tree = o.Tree!;
        var def = tree.At(o.Level, o.Branch);
        card.Theme = tree.Theme;
        card.Tag = CardStats.IsEvolution(def) ? "NEW ITEM" : o.Level == 1 ? "New" : $"Lv {o.Level - 1}{Arrow}{o.Level}";
        card.Title = def.Name;
        card.TagColor = Trees.LevelColor(tree.Id, o.Level, o.Branch);
        card.Body = CardStats.Tree(tree, def, o.Level, o.Branch);
        card.Footer = $"{tree.Name}{Dot}level {o.Level} / {tree.MaxFor(o.Branch)}";
        card.Icon = Items.Icon(o.Grant);
        return card;
    }

    /// <summary>An XP level-up card for an item you carry.</summary>
    public static CardSpec ForCarried(Offer o)
    {
        var tree = o.Tree!;
        var card = ItemPerkInfo.From(o.CrateItem).ToCard();
        if (o.Kind == OfferKind.Fork)
        {
            card.Theme = tree.Theme;
            card.Tag = $"Lv {o.Level - 1}{Arrow}{o.Level}";
            card.Title = $"{tree.Name}: Fork";
            card.TagColor = Trees.Steel;
            card.Body = string.Join("  or  ", Offers.Paths(tree, o.Level).Select(x => x.Name)) + "\nChoose your path on claim.";
            card.Footer = $"{tree.Name}{Dot}level {o.Level}" + (o.Fresh ? $"{Dot}+1 item" : "");
            return card;
        }
        card = Level(card, o);
        if (o.Fresh) card.Footer += $"{Dot}+1 item";
        return card;
    }

    public static CardSpec ForPath(TreeDef tree, int level, LevelDef option, Item crateItem)
    {
        Item shown = Offers.Grant(option, crateItem);
        return new CardSpec
        {
            Style = CardStyle.Perk,
            Theme = tree.Theme,
            Title = option.Name,
            Tag = (CardStats.IsEvolution(option) ? $"NEW ITEM{Dot}" : "") + Trees.PathName(tree.Id, option.Branch),
            TagColor = Trees.LevelColor(tree.Id, level, option.Branch),
            Body = CardStats.Tree(tree, option, level, option.Branch),
            Footer = $"{tree.Name}{Dot}level {level} / {tree.MaxFor(option.Branch)}",
            Icon = Items.Icon(shown),
        };
    }
}
