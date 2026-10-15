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

        if (lines.Count == 0) lines.Add("Trusty expedition gear");
        return string.Join("\n", lines.Take(3));
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

    /// <summary>The paths open at a fork: the dark bandage path stays sealed until a skull or dagger was used.</summary>
    public static LevelDef[] Paths(TreeDef tree, int level) =>
        tree.Levels[level - 1].Where(o => !(tree.Id == TreeId.Bandage && o.Branch == 2 && !(RunState.UsedSkull || RunState.UsedDagger))).ToArray();

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
        var card = ItemPerkInfo.From(item).ToCard();
        var o = Offers.Resolve(item);
        var tree = o.Tree;

        switch (o.Kind)
        {
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
            {
                var def = tree!.At(o.Level, o.Branch);
                card.Theme = tree.Theme;
                card.Tag = o.Level == 1 ? "New" : $"Lv {o.Level - 1}{Arrow}{o.Level}";
                card.Title = def.Name;
                card.TagColor = Trees.LevelColor(tree.Id, o.Level, o.Branch);
                card.Body = CardStats.Tree(tree, def, o.Level, o.Branch);
                card.Footer = $"{tree.Name}{Dot}level {o.Level} / {tree.MaxFor(o.Branch)}";
                card.Icon = Items.Icon(o.Grant);
                return card;
            }
            default:
                return card;
        }
    }

    public static CardSpec ForPath(TreeDef tree, int level, LevelDef option, Item crateItem)
    {
        Item shown = Offers.Grant(option, crateItem);
        return new CardSpec
        {
            Style = CardStyle.Perk,
            Theme = tree.Theme,
            Title = option.Name,
            Tag = Trees.PathName(tree.Id, option.Branch),
            TagColor = Trees.LevelColor(tree.Id, level, option.Branch),
            Body = CardStats.Tree(tree, option, level, option.Branch),
            Footer = $"{tree.Name}{Dot}level {level} / {tree.MaxFor(option.Branch)}",
            Icon = Items.Icon(shown),
        };
    }
}