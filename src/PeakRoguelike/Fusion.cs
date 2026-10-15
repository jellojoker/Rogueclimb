using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Peak.Afflictions;
using UnityEngine;
using UnityEngine.InputSystem;
using Zorro.Core;

namespace PeakRoguelike;

/// <summary>One ingredient: which pocket items match, and whether your tree must be maxed.</summary>
internal class Ingredient
{
    public string Name = "";
    public Func<Item, bool> Match = _ => false;
    public Func<bool> Ready = () => true;
    /// <summary>Items.Find role a blessing statue may hand out when this is the one thing missing (null = never).</summary>
    public string? Gift;
    /// <summary>A maxed-tree ingredient: counts as "have it" for statue gifts even when not in pockets.</summary>
    public bool FromTree;
    /// <summary>Fusing: is this pocket item good enough (a maxed item - anyone's)? Null = any match.</summary>
    public Func<ItemSlot, bool>? SlotReady;
}

internal class Recipe
{
    public string Id = "";
    public string Name = "";
    public string Desc = "";
    public Ingredient[] Ingredients = new Ingredient[0];
    /// <summary>Extra condition beyond holding the ingredients (e.g. the dark rites).</summary>
    public Func<bool> Unlocked = () => true;
    /// <summary>Prefab role handed to you (Items.Find).</summary>
    public string? Result;
    public string ResultIconRole = "";
    /// <summary>The fused item's own twist (Jetpack fuel, Faerie range, Sugar-Rush crash).</summary>
    public CraftId Craft;
    /// <summary>Trees whose crate cards bring this fusion along as an extra card once you've made it.</summary>
    public TreeId[] Trees = new TreeId[0];

    /// <summary>Does a crate item belong to this recipe (its trees, or one of its ingredients)?</summary>
    public bool Uses(Item crateItem) =>
        Trees.Contains(PeakRoguelike.Trees.Classify(crateItem)) || Ingredients.Any(i => i.Match(crateItem));
}

/// <summary>
/// Fusions: when your pockets hold every ingredient of a recipe (tree items must be maxed),
/// a fusion screen offers it. Fusing consumes the ingredients. "Not now" hides the offer
/// until your pockets change; the fusion key brings it back.
/// </summary>
internal class Fusion : MonoBehaviour
{
    /// <summary>
    /// A maxed tree item. Fusing takes any maxed one - a friend's counts. Ready (your own tree
    /// maxed, on the given path) is what statue gifts go by.
    /// </summary>
    private static Ingredient Tree(TreeId id, string name, Func<Item, bool> match, int branch = 0) => new()
    {
        Name = name,
        Match = match,
        Ready = () => !ModConfig.Leveling.Value || (RunState.IsMaxed(id) && (branch == 0 || RunState.Branch(id) == branch)),
        FromTree = true,
        // Food-tree snacks are their own level: Airline Food is always the top of the tree.
        SlotReady = s => !ModConfig.Leveling.Value || id == TreeId.Food || ItemLevels.IsMaxed(s),
    };

    private static Ingredient Any(string name, Func<Item, bool> match, string? gift = null) => new() { Name = name, Match = match, Gift = gift };

    /// <summary>A maxed Rope Cannon - the Anti-Rope Cannon counts too.</summary>
    private static Ingredient Cannon() => new()
    {
        Name = "Rope Cannon (max)",
        Match = i => i.GetComponent<RopeShooter>(),
        Ready = () => !ModConfig.Leveling.Value || (RunState.Branch(TreeId.Rope) == 1 && RunState.Level(TreeId.Rope) >= 6),
        FromTree = true,
        SlotReady = s => !ModConfig.Leveling.Value || Items.IsAntiRopeShooter(s.prefab) || ItemLevels.IsMaxed(s),
    };

    public static readonly List<Recipe> Recipes = new()
    {
        new Recipe
        {
            Id = "jetpack", Name = "Jetpack", Result = "Jetpack", Craft = CraftId.Jetpack,
            Desc = "Strap the cannon to your wings.\nFused: 150% fuel.",
            Ingredients = new[] { Tree(TreeId.Glider, "Glider (max)", i => Trees.Classify(i) == TreeId.Glider), Any("Scout Cannon", Items.IsScoutCannon, "ScoutCannon") },
            Trees = new[] { TreeId.Glider },
        },
        new Recipe
        {
            Id = "lollipop", Name = "Big Lollipop", Result = "Lollipop",
            Desc = "Infinite stamina - and the milk makes\nyou invincible for as long. Then sleepy.",
            Ingredients = new[] { Tree(TreeId.Food, "Airline Food (max)", i => Trees.Classify(i) == TreeId.Food && Items.Key(i).Contains("air")), Any("Fortified Milk", Items.IsMilk, "Milk") },
            Trees = new[] { TreeId.Food },
        },
        new Recipe
        {
            Id = "sugarrush", Name = "Sugar-Rush Cocktail", Result = "EnergyDrink", Craft = CraftId.SugarRush,
            Desc = "A full energy drink and Big Lollipop.\nThe crash is a healing sleep.",
            Ingredients = new[] { Tree(TreeId.SportsDrink, "Energy Drink (max)", Items.IsEnergyDrink), Any("Big Lollipop", Items.IsLollipop) },
            Trees = new[] { TreeId.SportsDrink, TreeId.Food },
        },
        new Recipe
        {
            Id = "faerie", Name = "Faerie Lantern", Result = "FaerieLantern", Craft = CraftId.FaerieLantern,
            Desc = "Heals and warms scouts around you.\nFused: 150% range and burn time.",
            Ingredients = new[] { Tree(TreeId.Light, "Lantern (max)", i => Trees.Classify(i) == TreeId.Light), Any("Candle", Items.IsCandle, "Candle"), Any("First Aid Kit", Items.IsMedKit, "FirstAidKit") },
            Trees = new[] { TreeId.Light, TreeId.Bandage },
        },
        new Recipe
        {
            Id = "chain", Name = "Chain Launcher", Result = "ChainShooter",
            Desc = "Fire a climbable chain up the wall.\nFused: 2 uses.",
            Ingredients = new[] { Tree(TreeId.Piton, "Piton (max)", i => Trees.Classify(i) == TreeId.Piton), Cannon() },
            Trees = new[] { TreeId.Piton, TreeId.Rope },
        },
        new Recipe
        {
            Id = "rescue", Name = "Rescue Claw", Result = "RescueHook",
            Desc = "Grab a scout - or a wall - and pull.\nFused: 6 uses.",
            Ingredients = new[] { Any("Chain Launcher", Items.IsChainLauncher), Cannon() },
            Trees = new[] { TreeId.Piton, TreeId.Rope },
        },
        new Recipe
        {
            Id = "antigrav", Name = "Anti-Grav Bazooka", Result = "AntiZooka",
            Desc = "Blast scouts into low gravity.\nFused: 6 uses.",
            Ingredients = new[] { Tree(TreeId.Balloons, "Balloon Bunch (max)", Items.IsBalloonBunch), Any("Scout Cannon", Items.IsScoutCannon, "ScoutCannon") },
            Trees = new[] { TreeId.Balloons },
        },
        new Recipe
        {
            Id = "effigy", Name = "Scout Effigy", Result = "ScoutEffigy",
            Desc = "Cleanse both dark artifacts into\na totem of the scouts.",
            Ingredients = new[] { Any("Cursed Skull", Items.IsSkull), Any("Ritual Dagger", Items.IsDagger) },
        },
        new Recipe
        {
            Id = "bookofbones", Name = "The Book of Bones", Result = "BookOfBones",
            Desc = "Bind dark medicine into the book.\nRaise a fallen scout from their bones.",
            Ingredients = new[] { Any("Skull or Dagger", i => Items.IsSkull(i) || Items.IsDagger(i)), Tree(TreeId.Bandage, "Gilded Lunchbox (max)", Items.IsLunchbox, branch: 2) },
            Trees = new[] { TreeId.Bandage },
        },
    };

    private static Fusion? instance;
    private string snoozed = "";
    private float nextScan;
    public static bool Ready { get; private set; }

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Fusion");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        instance = go.AddComponent<Fusion>();
    }

    /// <summary>Recipes you can make right now, with the pocket slots each would consume.</summary>
    internal static List<(Recipe recipe, byte[] slots)> Available()
    {
        var result = new List<(Recipe, byte[])>();
        Player p = Player.localPlayer;
        if (p == null) return result;
        // The finished cocktail is never an ingredient.
        var pockets = p.itemSlots.Where(s => s != null && !s.IsEmpty() && s.prefab != null && !SugarRush.IsMarked(s.data)).ToList();
        foreach (var recipe in Recipes)
        {
            if (!recipe.Unlocked()) continue;
            var used = new List<byte>();
            bool ok = true;
            foreach (var ing in recipe.Ingredients)
            {
                var slot = pockets.FirstOrDefault(s => !used.Contains(s.itemSlotID) && ing.Match(s.prefab)
                    && (ing.SlotReady == null ? ing.Ready() : ing.SlotReady(s)));
                if (slot == null) { ok = false; break; }
                used.Add(slot.itemSlotID);
            }
            if (ok) result.Add((recipe, used.ToArray()));
        }
        return result;
    }

    /// <summary>A fingerprint of what's in your pockets; "Not now" lasts until it changes.</summary>
    private static string PocketKey()
    {
        Player p = Player.localPlayer;
        if (p == null) return "";
        return string.Join(",", p.itemSlots.Select(s => s == null || s.IsEmpty() ? "-"
                : (s.data?.guid.ToString() ?? s.prefab.itemID.ToString()) + ":" + ItemLevels.LevelOf(s)))
            + "|" + string.Join(",", Recipes.Select(r => r.Unlocked() ? "1" : "0"));
    }

    private void Update()
    {
        if (!ModConfig.Fusion.Value) { Ready = false; return; }
        Character me = Character.localCharacter;
        if (me == null || me.data.dead || me.data.passedOut) { Ready = false; return; }

        bool keyPressed = Keyboard.current != null && Keyboard.current[ModConfig.FusionKey.Value].wasPressedThisFrame;
        if (!keyPressed && Time.time < nextScan) return;
        nextScan = Time.time + 0.5f;
        Items.ApplyFusionTwists();

        var available = Available();
        Ready = available.Count > 0;
        if (!Ready || ChoiceScreen.IsOpen) return;

        string key = PocketKey();
        bool calm = me.data.isGrounded && !me.data.isClimbing && !me.data.isRopeClimbing;
        if (keyPressed || (key != snoozed && calm)) Show(available, key);
    }

    /// <summary>Dev panel: open the fusion screen now (if anything in your pockets can fuse).</summary>
    internal static void DevOpen()
    {
        var available = Available();
        if (available.Count == 0) { Hud.Toast("Nothing to fuse", ChoiceScreen.Cream); return; }
        if (instance != null && !ChoiceScreen.IsOpen) instance.Show(available, PocketKey());
    }

    private void Show(List<(Recipe recipe, byte[] slots)> available, string key)
    {
        Player p = Player.localPlayer;
        ChoiceScreen.Show(new ScreenSpec
        {
            Title = "FUSION",
            Subtitle = available.Count == 1 ? "A recipe is ready" : $"{available.Count} recipes are ready",
            Confirm = "FUSE",
            BackHint = $"Not now ({ModConfig.FusionKey.Value} to reopen)",
            Cards = available.Select(a => Card(a.recipe, a.slots)).ToList(),
            OnConfirm = i => Fuse(available[i].recipe, available[i].slots),
            OnBack = () => snoozed = key,
        });
    }

    /// <summary>The card a recipe shows (fusion screen and statue blessings).</summary>
    internal static CardSpec Card(Recipe recipe, byte[] slots)
    {
        Player p = Player.localPlayer;
        return new CardSpec
        {
            Style = CardStyle.Fusion,
            Title = recipe.Name,
            Heading = string.Join("  +  ", recipe.Ingredients.Select(i => i.Name)),
            Body = CardStats.Fusion(recipe),
            Icon = Items.Icon(Items.Find(recipe.Result ?? recipe.ResultIconRole)),
            Ingredients = slots.Select(s => (Texture?)Items.Icon(p?.GetItemSlot(s)?.prefab)).ToArray(),
        };
    }

    internal static void Fuse(Recipe recipe, byte[] slots)
    {
        Character me = Character.localCharacter;
        Player p = Player.localPlayer;
        if (me == null || p == null) return;
        // Re-check: the pockets may have changed while the screen was open.
        var now = Available().FirstOrDefault(a => a.recipe == recipe);
        if (now.recipe == null) { Hud.Toast("The ingredients are gone", Color.white); return; }
        slots = now.slots;

        Item? result = recipe.Result != null ? Items.Find(recipe.Result) : null;
        if (recipe.Result != null && result == null) { Hud.Toast($"{recipe.Name} isn't in this version of the game", Color.white); return; }

        bool heldConsumed = me.refs.items.currentSelectedSlot.IsSome && slots.Contains(me.refs.items.currentSelectedSlot.Value);
        foreach (byte s in slots)
        {
            // Consuming a dark artifact in a recipe counts as using it.
            if (p.GetItemSlot(s)?.prefab is { } consumed)
            {
                if (Items.IsSkull(consumed)) RunState.UsedSkull = true;
                if (Items.IsDagger(consumed)) RunState.UsedDagger = true;
            }
            p.EmptySlot(Optionable<byte>.Some(s));
        }
        if (heldConsumed) me.refs.items.EquipSlot(Optionable<byte>.None);

        // From now on, crates that roll its ingredients also offer it.
        RunState.Fused.Add(recipe.Id);
        Give(recipe);
        Hud.Banner("FUSION", recipe.Name, ChoiceScreen.FusionGlow);
        Plugin.Log.LogInfo($"Fused {recipe.Name}.");
    }

    /// <summary>Hand over a recipe's finished item (fusion, unlocked crate card, statue jackpot).</summary>
    internal static void Give(Recipe recipe)
    {
        Character me = Character.localCharacter;
        if (me == null || recipe.Result == null || Items.Find(recipe.Result) is not { } result) return;
        if (recipe.Craft == CraftId.SugarRush) SugarRush.ExpectGrant();
        GameUtils.instance.InstantiateAndGrab(result, me);
    }

    public static Recipe? Get(string id) => Recipes.FirstOrDefault(r => r.Id == id);

    /// <summary>The card for a fused item handed out whole (unlocked crate card, statue jackpot).</summary>
    internal static CardSpec ItemCard(Recipe recipe, string tag) => new()
    {
        Style = CardStyle.Fusion,
        Title = recipe.Name,
        Heading = tag,
        Body = CardStats.Fusion(recipe),
        Icon = Items.Icon(Items.Find(recipe.Result ?? recipe.ResultIconRole)),
        Ingredients = new Texture?[0],
    };
}
