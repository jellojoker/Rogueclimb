using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace PeakRoguelike;

/// <summary>
/// Blessings: touching a revive statue when nobody needs reviving blesses every scout, and
/// opening an Ancient Luggage blesses whoever opened it. Each player picks one of four build
/// cards (see BuildOptions): Specialize, Partner, Switch Lanes, and Fusion or Synergy.
/// </summary>
internal class Blessing : MonoBehaviour
{
    private const string Gold = "#FFC84F";

    private abstract class Option
    {
        public abstract CardSpec Card();
        /// <summary>Apply it. Calls done(true) when finished, done(false) to go back to the cards.</summary>
        public abstract void Take(Action<bool> done);
    }

    private static Blessing? instance;
    private static readonly HashSet<int> blessedStatues = new();
    /// <summary>A statue blessing waiting for its screen (another screen was open, or "Decide later").</summary>
    private static bool pending;
    private static float pendingAt;

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Blessings");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        instance = go.AddComponent<Blessing>();
    }

    internal static void Reset()
    {
        blessedStatues.Clear();
        pending = false;
    }

    public static bool On => ModConfig.Blessings.Value && ModConfig.Leveling.Value;

    // --- sources ----------------------------------------------------------------------------

    /// <summary>Everyone: the host says a statue blessed the team.</summary>
    internal static void FromStatue(int statueViewId)
    {
        if (!On || !blessedStatues.Add(statueViewId)) return;
        Hud.Banner("", "The statue blesses you", Trees.Gold);
        pending = true;
        pendingAt = Time.time + 1.5f;
    }

    private void Update()
    {
        if (!pending || Time.time < pendingAt || ChoiceScreen.IsOpen) return;
        Character me = Character.localCharacter;
        if (me == null || me.data.dead || me.data.passedOut) return;
        pending = false;
        Show("Revive statue", onTaken: () => { }, onBack: () =>
        {
            pending = true;
            pendingAt = Time.time + 8f;
        }, stillValid: null);
    }

    /// <summary>Dev panel: a statue blessing for you, right now.</summary>
    internal static void DevBless()
    {
        if (ChoiceScreen.IsOpen) return;
        Show("Dev blessing", onTaken: () => { }, onBack: () => { }, stillValid: null);
    }

    /// <summary>Ancient Luggage: only the opener is blessed, and the crate opens once they choose.</summary>
    internal static bool TryCrate(Luggage luggage, PerkLuggage perk, Character interactor)
    {
        if (!On || !IsAncient(luggage)) return false;
        string name = "Ancient Luggage";
        try { if (!string.IsNullOrEmpty(luggage.GetName())) name = luggage.GetName(); } catch { }
        bool shown = Show(name,
            onTaken: () =>
            {
                luggage.photonView.RPC(nameof(PerkLuggage.RPC_PerkOpen), RpcTarget.All);
                perk.ReleaseLock();
                GlobalEvents.TriggerLuggageOpened(luggage, interactor);
            },
            onBack: () => perk.ReleaseLock(),
            stillValid: () => luggage != null && !luggage.IsOpen && Character.localCharacter != null
                              && Vector3.Distance(Character.localCharacter.Center, luggage.Center()) <= 8f);
        if (!shown) perk.ReleaseLock();
        return true;
    }

    internal static bool IsAncient(Luggage luggage) => (luggage.displayName ?? "").ToLowerInvariant().Contains("ancient");

    // --- the cards --------------------------------------------------------------------------

    private static bool Show(string subtitle, Action onTaken, Action onBack, Func<bool>? stillValid)
    {
        var options = BuildOptions();
        if (options.Count == 0)
        {
            Hud.Toast("The statue is silent", Trees.Gold);
            onTaken();
            return true;
        }
        return ChoiceScreen.Show(new ScreenSpec
        {
            Title = "CHOOSE YOUR BLESSING",
            Subtitle = subtitle,
            Confirm = "ACCEPT",
            BackHint = "Decide later",
            Cards = options.Select(o => o.Card()).ToList(),
            StillValid = stillValid,
            OnConfirm = i => options[i].Take(ok =>
            {
                if (ok) onTaken();
                else Show(subtitle, onTaken, onBack, stillValid);
            }),
            OnBack = onBack,
        });
    }

    /// <summary>Something you're levelling: an item tree.</summary>
    private sealed class Lane
    {
        public TreeDef Tree = null!;
        public int Level;
        public string Key => "t" + Tree.Id;
        public RelicTrigger Class => ClassOf(Tree.Id);

        /// <summary>Up to +2, never past max (null when it's already maxed).</summary>
        public Option? Option(string role)
        {
            int br = RunState.Branch(Tree.Id), add = Mathf.Min(2, Tree.MaxFor(br) - Level);
            return add >= 1 ? new TreeOption(Tree, Level, br, add, role) : null;
        }
    }

    /// <summary>
    /// Four ways to shape your build, all keyed off your top item:
    ///  Specialize (+2 on it), Partner (+2 on the item it fuses / pairs with),
    ///  Switch Lanes (+2 on your second-best), and a fusion you can make now - else Synergy,
    ///  a new item in the same relic class as your top item.
    /// </summary>
    private static List<Option> BuildOptions()
    {
        var list = new List<Option>();
        var used = new HashSet<string>();
        var lanes = Lanes();

        void Add(Lane? lane, string role)
        {
            if (lane == null || used.Contains(lane.Key) || lane.Option(role) is not { } o) return;
            used.Add(lane.Key);
            list.Add(o);
        }

        Lane? top = lanes.FirstOrDefault();
        if (top == null)
        {
            // Nothing to build on yet: three brand-new paths.
            foreach (var t in Fresh(null).Take(3)) list.Add(new TreeOption(t, 0, 0, 1, "New path"));
        }
        else
        {
            Add(top, "Specialize");
            Add(PartnerOf(top), "Partner");
            Add(lanes.Skip(1).FirstOrDefault(l => !used.Contains(l.Key) && l.Option("") != null), "Switch lanes");
            // Thin builds: still three build cards - a new path in another relic class.
            if (list.Count < 3)
                foreach (var t in Fresh(null).Where(t => ClassOf(t.Id) != top.Class && used.Add("t" + t.Id)).Take(3 - list.Count))
                    list.Add(new TreeOption(t, 0, 0, 1, "Switch lanes"));
        }

        // A fusion you could make right now - not free, it still uses the ingredients.
        if (ModConfig.Fusion.Value && Fusion.Available().FirstOrDefault() is { recipe: not null } ready)
            list.Add(new FusionOption(ready.recipe, ready.slots));
        else if (top != null && Fresh(top.Class).FirstOrDefault(t => !used.Contains("t" + t.Id)) is { } syn)
            list.Add(new TreeOption(syn, 0, 0, 1, "Synergy"));

        // The luck of the statue: the +2 card (or the first card) sometimes turns into the one
        // ingredient a maxed item of yours is missing - and once in a long while, a whole fusion.
        if (ModConfig.Fusion.Value && list.Count > 0)
        {
            int at = Mathf.Max(0, list.FindIndex(o => o is TreeOption { Role: "Specialize" }));
            float roll = UnityEngine.Random.value;
            if (roll < ModConfig.StatueFusionChance.Value)
            {
                var jackpot = Fusion.Recipes.Where(r => r.Result != null && Items.Find(r.Result) != null).OrderBy(_ => UnityEngine.Random.value).FirstOrDefault();
                if (jackpot != null) { list[at] = new GiftOption(jackpot); Plugin.Log.LogInfo($"Blessing: jackpot - {jackpot.Name}."); }
            }
            else if (roll < ModConfig.StatueFusionChance.Value + ModConfig.StatueIngredientChance.Value && MissingIngredient() is { } gift)
            {
                list[at] = new IngredientOption(gift.recipe, gift.ingredient);
                Plugin.Log.LogInfo($"Blessing: {gift.ingredient.Name} for the {gift.recipe.Name}.");
            }
        }
        return list;
    }

    /// <summary>
    /// The one thing between you and a fusion: every maxed-tree ingredient ready, and exactly one
    /// other ingredient missing from your pockets that a statue may give. Most complete recipe first.
    /// </summary>
    private static (Recipe recipe, Ingredient ingredient)? MissingIngredient()
    {
        var p = Player.localPlayer;
        if (p == null) return null;
        var pockets = p.itemSlots.Where(s => s != null && !s.IsEmpty() && s.prefab != null).Select(s => s.prefab).ToList();
        (Recipe, Ingredient)? best = null;
        int bestHeld = -1;
        foreach (var r in Fusion.Recipes)
        {
            if (!r.Unlocked() || r.Ingredients.Any(i => i.FromTree && !i.Ready())) continue;
            var missing = r.Ingredients.Where(i => !i.FromTree && !pockets.Any(i.Match)).ToList();
            if (missing.Count != 1 || missing[0].Gift == null || Items.Find(missing[0].Gift!) == null) continue;
            int held = r.Ingredients.Length - 1;
            if (held > bestHeld) { bestHeld = held; best = (r, missing[0]); }
        }
        return best;
    }

    /// <summary>What you're levelling, best first (ties shuffled).</summary>
    private static List<Lane> Lanes()
    {
        var lanes = new List<Lane>();
        foreach (var tree in Trees.All.Values)
            if (RunState.Level(tree.Id) is var lv and >= 1) lanes.Add(new Lane { Tree = tree, Level = lv });
        // Maxed lanes can't be blessed any further.
        var open = lanes.Where(l => l.Option("") != null).ToList();
        return open.OrderByDescending(l => l.Level).ThenBy(_ => UnityEngine.Random.value).ToList();
    }

    /// <summary>Trees you don't have yet, shuffled; only one relic class if given.</summary>
    private static IEnumerable<TreeDef> Fresh(RelicTrigger? cls) =>
        Trees.All.Values.Where(t => RunState.Level(t.Id) == 0 && (cls == null || ClassOf(t.Id) == cls))
            .OrderBy(_ => UnityEngine.Random.value);

    /// <summary>The item that fuses or pairs with a lane (its partner on the way to a fusion).</summary>
    private static Lane? PartnerOf(Lane lane)
    {
        TreeId partner = lane.Tree.Id switch
        {
            TreeId.Rope => TreeId.Piton,        // chain launcher
            TreeId.Piton => TreeId.Rope,
            TreeId.Glider => TreeId.Balloons,   // both want the scout cannon
            TreeId.Balloons => TreeId.Glider,
            TreeId.Bandage => TreeId.Light,     // faerie lantern
            TreeId.Light => TreeId.Bandage,
            TreeId.SportsDrink => TreeId.Food,  // sugar-rush
            TreeId.Food => TreeId.SportsDrink,
            _ => TreeId.None,
        };
        var tree = Trees.Get(partner);
        return tree == null ? null : new Lane { Tree = tree, Level = RunState.Level(partner) };
    }

    internal static RelicTrigger ClassOf(TreeId id) => id switch
    {
        TreeId.Bandage or TreeId.Light => RelicTrigger.Healing,
        TreeId.SportsDrink or TreeId.Food => RelicTrigger.Food,
        _ => RelicTrigger.Movement,
    };

    private static string RoleTag(string role) => $"<color={Gold}>{role.ToUpperInvariant()}</color>";

    private static void Grant(Item? prefab)
    {
        Character me = Character.localCharacter;
        if (prefab != null && me != null) GameUtils.instance.InstantiateAndGrab(prefab, me);
    }

    private sealed class TreeOption : Option
    {
        private readonly TreeDef tree;
        private readonly int from, branch, add;
        private readonly string role;
        public string Role => role;

        public TreeOption(TreeDef tree, int from, int branch, int add, string role)
        {
            this.tree = tree; this.from = from; this.branch = branch; this.add = add; this.role = role;
        }

        private int Target(int br) => Mathf.Min(from + add, tree.MaxFor(br));

        /// <summary>The first level on the way up that asks you to choose a path, or 0.</summary>
        private int ForkLevel()
        {
            for (int lv = from + 1; lv <= from + add; lv++)
                if (tree.ForksAt(lv, branch)) return lv;
            return 0;
        }

        public override CardSpec Card()
        {
            int to = Target(branch);
            bool fork = ForkLevel() > 0;
            var def = tree.At(to, branch);
            Item? shown = fork ? null : Offers.Grant(def, Items.Find(Trees.BaseRole(tree.Id))!);
            return new CardSpec
            {
                Style = CardStyle.Perk,
                Theme = tree.Theme,
                Title = fork ? $"{tree.Name}: Fork" : def.Name,
                Tag = from == 0 ? (to > 1 ? $"New · Lv {to}" : "New") : $"Lv {from} → {to}",
                TagColor = Trees.LevelColor(tree.Id, to, branch),
                Body = fork ? string.Join("  or  ", Offers.Paths(tree, ForkLevel()).Select(x => x.Name)) + "\nChoose your path next." : CardStats.Tree(tree, def, to, branch),
                Footer = $"{RoleTag(role)} · {tree.Name} +{to - from}",
                Icon = Items.Icon(shown ?? Items.Find(Trees.BaseRole(tree.Id))),
            };
        }

        public override void Take(Action<bool> done)
        {
            int forkAt = ForkLevel();
            if (forkAt == 0)
            {
                Finish(branch);
                done(true);
                return;
            }
            var options = Offers.Paths(tree, forkAt);
            var baseItem = Items.Find(Trees.BaseRole(tree.Id))!;
            ChoiceScreen.Show(new ScreenSpec
            {
                Title = "CHOOSE YOUR PATH",
                Subtitle = $"{tree.Name} · level {forkAt}",
                Confirm = "CHOOSE",
                Cards = options.Select(o => CrateCards.ForPath(tree, forkAt, o, baseItem)).ToList(),
                OnConfirm = i => { Finish(options[i].Branch); done(true); },
                OnBack = () => done(false),
            });
        }

        private void Finish(int br)
        {
            int to = Target(br);
            RunState.SetLevel(tree.Id, to, br);
            var def = tree.At(to, br);
            Hud.PickChime();
            Hud.PickBanner($"{tree.Name.ToUpperInvariant()} · LEVEL {to}", def.Name, Trees.LevelColor(tree.Id, to, br));
            Grant(Offers.Grant(def, Items.Find(Trees.BaseRole(tree.Id))!));
            Plugin.Log.LogInfo($"Blessing: {tree.Name} {from} -> {to}.");
        }
    }

    /// <summary>The statue's rare gift: the one ingredient a maxed item of yours still needs.</summary>
    private sealed class IngredientOption : Option
    {
        private readonly Recipe recipe;
        private readonly Ingredient ingredient;

        public IngredientOption(Recipe recipe, Ingredient ingredient) { this.recipe = recipe; this.ingredient = ingredient; }

        public override CardSpec Card()
        {
            var item = Items.Find(ingredient.Gift!);
            var card = item != null ? ItemPerkInfo.From(item).ToCard() : new CardSpec { Style = CardStyle.Perk };
            card.Title = item != null ? Items.DisplayName(item) : ingredient.Name;
            card.Tag = "Gift";
            card.TagColor = Trees.Gold;
            card.Body = $"The last piece of the\n{recipe.Name}.";
            card.Footer = $"{RoleTag("Statue's gift")}";
            return card;
        }

        public override void Take(Action<bool> done)
        {
            Grant(Items.Find(ingredient.Gift!));
            Hud.Banner("THE STATUE GIVES", ingredient.Name, Trees.Gold);
            done(true);
        }
    }

    /// <summary>The statue's jackpot: a finished fusion item, no ingredients needed.</summary>
    private sealed class GiftOption : Option
    {
        private readonly Recipe recipe;

        public GiftOption(Recipe recipe) => this.recipe = recipe;

        public override CardSpec Card() => Fusion.ItemCard(recipe, "A gift from the statue");

        public override void Take(Action<bool> done)
        {
            Fusion.Give(recipe);
            Hud.Banner("THE STATUE GIVES", recipe.Name, ChoiceScreen.FusionGlow);
            done(true);
        }
    }

    private sealed class FusionOption : Option
    {
        private readonly Recipe recipe;
        private readonly byte[] slots;

        public FusionOption(Recipe recipe, byte[] slots) { this.recipe = recipe; this.slots = slots; }

        public override CardSpec Card() => Fusion.Card(recipe, slots);

        public override void Take(Action<bool> done)
        {
            Fusion.Fuse(recipe, slots);
            done(true);
        }
    }
}

[HarmonyPatch]
internal static class BlessingPatches
{
    /// <summary>
    /// Host: the statue opens. Someone dead or down = the vanilla revive. Otherwise, instead
    /// of dropping items, every scout gets a blessing.
    /// </summary>
    [HarmonyPrefix, HarmonyPatch(typeof(RespawnChest), nameof(RespawnChest.SpawnItems))]
    private static bool StatueBlessing(RespawnChest __instance, ref List<PhotonView> __result)
    {
        if (!Blessing.On || !PhotonNetwork.IsMasterClient) return true;
        if (Ascents.canReviveDead && Character.PlayerIsDeadOrDown()) return true;
        __result = new List<PhotonView>();
        Net.Bless(__instance.photonView.ViewID);
        Plugin.Log.LogInfo("Revive statue: nobody to revive - blessing the team.");
        return false;
    }
}
