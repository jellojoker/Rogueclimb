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
    private static readonly HashSet<int> blessedStatues = new(), revivedStatues = new();
    /// <summary>A statue blessing waiting for its screen (another screen was open, or "Decide later").</summary>
    private static bool pending;
    private static float pendingAt;
    private static int pendingSource;
    /// <summary>New every run, so the same crate or statue blesses differently next run.</summary>
    private static int runSalt = UnityEngine.Random.Range(1, int.MaxValue);

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
        revivedStatues.Clear();
        pending = false;
        darkTaken = darkLater = false;
        darkFlat = -1f;
        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient && PhotonNetwork.CurrentRoom != null)
        {
            try { PhotonNetwork.CurrentRoom.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { [DarkKey] = null }); }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't reset the statue's dark offer: {e.Message}"); }
        }
        runSalt = UnityEngine.Random.Range(1, int.MaxValue);
    }

    /// <summary>
    /// A blessing's luck is locked to its crate or statue and to you: backing out and reopening shows
    /// the same shuffle and the same statue's luck. The cards still follow your build as it is now.
    /// </summary>
    private static T Seeded<T>(int source, Func<T> build)
    {
        int actor = Photon.Pun.PhotonNetwork.LocalPlayer?.ActorNumber ?? 0;
        var saved = UnityEngine.Random.state;
        UnityEngine.Random.InitState(unchecked(runSalt * 31 + source * 397 + actor * 7919));
        try { return build(); }
        finally { UnityEngine.Random.state = saved; }
    }

    public static bool On => ModConfig.Blessings.Value && ModConfig.Leveling.Value;

    // --- sources ----------------------------------------------------------------------------

    /// <summary>Everyone: the host says a statue blessed the team.</summary>
    internal static void FromStatue(int statueViewId, bool revive = false)
    {
        if (revive)
        {
            // It revived someone: every scout gets a random badge, no pick.
            if (ModConfig.BadgeRelics.Value && revivedStatues.Add(statueViewId)) Badges.GiveRandom();
            return;
        }
        if (!On || !blessedStatues.Add(statueViewId)) return;
        pendingSource = unchecked(statueViewId + 1000003);
        Hud.Banner("", "The statue blesses you", Trees.Gold);
        pending = true;
        pendingAt = Time.time + 1.5f;
    }

    private void Update()
    {
        try { DarkTick(); }
        catch (Exception e) { darkTaken = true; Plugin.Log.LogError($"Revive dark offer failed: {e}"); }
        if (!pending || Time.time < pendingAt || ChoiceScreen.IsOpen) return;
        Character me = Character.localCharacter;
        if (me == null || me.data.dead || me.data.passedOut) return;
        pending = false;
        void Later()
        {
            pending = true;
            pendingAt = Time.time + 8f;
        }
        // A blessing statue offers badge relics now - no level-ups.
        if (ModConfig.BadgeRelics.Value)
        {
            if (!Badges.OfferPick("Blessing statue", pendingSource, onTaken: () => { }, onBack: Later))
            {
                if (Badges.AnyLeft) Later(); // the screen couldn't open: try again shortly
                else Hud.Toast("The statue is silent", Trees.Gold);
            }
            return;
        }
        Show("Revive statue", pendingSource, onTaken: () => { }, onBack: Later, stillValid: null);
    }

    /// <summary>Dev panel: a statue blessing for you, right now.</summary>
    internal static void DevBless()
    {
        if (ChoiceScreen.IsOpen) return;
        Show("Dev blessing", UnityEngine.Random.Range(1, int.MaxValue), onTaken: () => { }, onBack: () => { }, stillValid: null);
    }

    /// <summary>Ancient Luggage: only the opener is blessed, and the crate opens once they choose.</summary>
    internal static bool TryCrate(Luggage luggage, PerkLuggage perk, Character interactor)
    {
        if (!On || !IsAncient(luggage)) return false;
        string name = "Ancient Luggage";
        Vector3 from = interactor != null ? interactor.Center : luggage.Center();
        try { if (!string.IsNullOrEmpty(luggage.GetName())) name = luggage.GetName(); } catch { }
        bool shown = Show(name, luggage.photonView != null ? luggage.photonView.ViewID : luggage.GetInstanceID(),
            onTaken: () =>
            {
                luggage.photonView.RPC(nameof(PerkLuggage.RPC_PerkOpen), RpcTarget.All);
                perk.ReleaseLock();
                GlobalEvents.TriggerLuggageOpened(luggage, interactor);
            },
            onBack: () => perk.ReleaseLock(),
            stillValid: () => luggage != null && !luggage.IsOpen && LuggagePatches.StillHere(from),
            from: Source.Ancient);
        if (!shown) perk.ReleaseLock();
        return true;
    }

    internal static bool IsAncient(Luggage luggage) => (luggage.displayName ?? "").ToLowerInvariant().Contains("ancient");

    // --- the cards --------------------------------------------------------------------------

    private enum Source { Statue, Ancient }

    /// <summary>Whose blessing BuildOptions is dealing (their jackpots differ).</summary>
    private static Source building;

    /// <summary>F8: the next blessing's jackpot is forced (true = dark item, false = fusion).</summary>
    internal static bool? DevJackpot;

    private static bool Show(string subtitle, int source, Action onTaken, Action onBack, Func<bool>? stillValid, Source from = Source.Statue)
    {
        building = from;
        List<Option> options;
        try { options = Seeded(source, BuildOptions); }
        finally { DevJackpot = null; }
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
                else Show(subtitle, source, onTaken, onBack, stillValid, from);
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
        if (building == Source.Ancient && ModConfig.BadgeRelics.Value) return AncientOptions();
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
        // ingredient a maxed item of yours is missing - and once in a while, a jackpot: a whole
        // fusion or a dark item. Ancient Luggage hits the jackpot more often than statues.
        if (ModConfig.Fusion.Value && list.Count > 0)
        {
            int at = Mathf.Max(0, list.FindIndex(o => o is TreeOption { Role: "Specialize" }));
            float jackpotChance = Mathf.Clamp01(building == Source.Ancient ? ModConfig.AncientJackpotChance.Value : ModConfig.StatueJackpotChance.Value);
            float roll = UnityEngine.Random.value;
            Option? jackpot = null;
            if (roll < jackpotChance || DevJackpot != null)
            {
                bool dark = DevJackpot ?? (UnityEngine.Random.value < Mathf.Clamp01(ModConfig.JackpotDarkShare.Value));
                var fusion = Fusion.Recipes.Where(r => r.Result != null && Items.Find(r.Result) != null).OrderBy(_ => UnityEngine.Random.value).FirstOrDefault();
                var darkItem = DarkItems().OrderBy(_ => UnityEngine.Random.value).FirstOrDefault();
                if (dark && darkItem != null) jackpot = new DarkOption(darkItem);
                else if (fusion != null) jackpot = new GiftOption(fusion);
            }
            if (jackpot != null)
            {
                list[at] = jackpot;
                Plugin.Log.LogInfo($"Blessing ({building}): jackpot - {jackpot.Card().Title}.");
            }
            else if (roll >= jackpotChance && roll < jackpotChance + Luck.StatueIngredientChance && MissingIngredient() is { } gift)
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
    internal static (Recipe recipe, Ingredient ingredient)? MissingIngredient()
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
        if (prefab != null && me != null) { Xp.ExpectCrateItem(prefab); GameUtils.instance.InstantiateAndGrab(prefab, me); }
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
                Tag = from == 0 ? (to > 1 ? $"New · Lv {to}" : "New")
                      : !fork && tree.At(from, branch).Grants != def.Grants ? $"NEW ITEM · Lv {from} → {to}" : $"Lv {from} → {to}",
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

    /// <summary>The other jackpot: a dark item, found like any other (using it later raises the moon as always).</summary>
    /// <summary>
    /// Ancient Luggage: +2 on your top item (a new path if you have none), a badge relic, and -
    /// when the luggage's luck hits (Esoterica raises it) - a mythical gift: a fusion or a dark item.
    /// </summary>
    private static List<Option> AncientOptions()
    {
        var list = new List<Option>();
        if (Lanes().FirstOrDefault()?.Option("Specialize") is { } top) list.Add(top);
        else if (Fresh(null).FirstOrDefault() is { } fresh) list.Add(new TreeOption(fresh, 0, 0, 1, "New path"));
        if (Badges.RandomPick() is { } badge) list.Add(new BadgeOption(badge));
        if (ModConfig.Fusion.Value)
        {
            float chance = Mathf.Clamp01(ModConfig.AncientJackpotChance.Value * (Badges.Has(BadgeId.Esoterica) ? 1f + Badges.Buff : 1f));
            if (DevJackpot != null || UnityEngine.Random.value < chance)
            {
                bool dark = DevJackpot ?? (UnityEngine.Random.value < Mathf.Clamp01(ModConfig.JackpotDarkShare.Value));
                var fusion = Fusion.Recipes.Where(r => r.Result != null && Items.Find(r.Result) != null).OrderBy(_ => UnityEngine.Random.value).FirstOrDefault();
                var darkItem = DarkItems().OrderBy(_ => UnityEngine.Random.value).FirstOrDefault();
                Option? jackpot = dark && darkItem != null ? new DarkOption(darkItem) : fusion != null ? new GiftOption(fusion) : null;
                if (jackpot != null)
                {
                    list.Add(jackpot);
                    Plugin.Log.LogInfo($"Ancient Luggage: jackpot - {jackpot.Card().Title} (chance {chance:P0}).");
                }
            }
        }
        return list;
    }

    private sealed class BadgeOption : Option
    {
        private readonly BadgeId badge;

        public BadgeOption(BadgeId badge) => this.badge = badge;

        public override CardSpec Card() => Badges.Card(Badges.Get(badge));

        public override void Take(Action<bool> done)
        {
            Badges.Take(badge, "THE LUGGAGE GIVES");
            done(true);
        }
    }

    private sealed class DarkOption : Option
    {
        private readonly Item item;

        public DarkOption(Item item) => this.item = item;

        public override CardSpec Card() => DarkCard(item);

        public override void Take(Action<bool> done)
        {
            Grant(item);
            Hud.Banner("THE STATUE GIVES", Items.DisplayName(item), ChoiceScreen.Muted);
            done(true);
        }
    }

    private static CardSpec DarkCard(Item item)
    {
        var card = CrateCards.ForItem(item);
        card.Cursed = true;
        card.Footer = RoleTag("The statue remembers");
        return card;
    }

    private static bool Solo => !PhotonNetwork.InRoom || PhotonNetwork.OfflineMode || PhotonNetwork.CurrentRoom == null || PhotonNetwork.CurrentRoom.PlayerCount <= 1;

    /// <summary>Skull, Dagger and Book of Bones - alone, only the Book (the Dagger is banned in solo, the Skull needs a squad).</summary>
    private static List<Item> DarkItems()
    {
        var all = new List<Item>();
        void One(Func<Item, bool> what) { if (Items.Database.FirstOrDefault(i => i != null && what(i)) is { } it) all.Add(it); }
        if (!Solo) { One(Items.IsSkull); One(Items.IsDagger); }
        One(Items.IsBookOfBones);
        return all;
    }

    // --- the statue remembers: one revived scout per run may take a dark item ---------------------

    private const string DarkKey = "prl.rdo"; // room: the actor a revive offered the dark items to

    private static bool darkTaken, darkLater;
    private static float darkFlat = -1f;

    /// <summary>Host, as a statue revives the fallen: once per run, one of them may be offered the dark items.</summary>
    internal static void HostReviveRoll()
    {
        if (!On || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
        if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(DarkKey)) return;
        var fallen = Character.AllCharacters.Where(c => c != null && (c.data.dead || c.data.fullyPassedOut)
            && c.photonView?.Owner != null && Modless.HasMod(c.photonView.Owner)).ToList();
        if (fallen.Count == 0) return;
        float roll = UnityEngine.Random.value;
        bool hit = DevRevive || roll < Mathf.Clamp01(ModConfig.ReviveDarkChance.Value);
        DevRevive = false;
        Plugin.Log.LogInfo($"Revive statue: dark offer roll {roll:0.00} - {(hit ? "hit" : "miss")}.");
        if (!hit) return;
        var who = fallen[UnityEngine.Random.Range(0, fallen.Count)];
        PhotonNetwork.CurrentRoom.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { [DarkKey] = who.photonView.Owner.ActorNumber });
        Plugin.Log.LogInfo($"Revive statue: {who.characterName} is offered the dark items (once per run).");
    }

    /// <summary>F8: the next revive's dark offer is a hit (and clears this run's "already offered").</summary>
    internal static bool DevRevive;

    internal static void DevForceRevive()
    {
        DevRevive = true;
        if (PhotonNetwork.IsMasterClient && PhotonNetwork.CurrentRoom != null)
            PhotonNetwork.CurrentRoom.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { [DarkKey] = null });
        darkTaken = darkLater = false;
    }

    private static bool OfferedToMe =>
        !darkTaken && PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null && PhotonNetwork.LocalPlayer != null
        && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(DarkKey, out object raw) && raw is int a && a == PhotonNetwork.LocalPlayer.ActorNumber;

    /// <summary>The dark offer was put off ("Later"): the HUD's waiting pill shows it.</summary>
    internal static bool DarkWaiting => darkLater && OfferedToMe;

    private static void DarkTick()
    {
        if (!OfferedToMe || ChoiceScreen.IsOpen) { darkFlat = -1f; return; }
        Character me = Character.localCharacter;
        if (me == null || me.data.dead || me.data.passedOut) { darkFlat = -1f; return; }
        if (darkLater)
        {
            // Put off: the relic key reopens it (a waiting shared relic goes first).
            darkFlat = -1f;
            if (Relics.Waiting == null && UnityEngine.InputSystem.Keyboard.current is { } kb && kb[ModConfig.RelicKey.Value].wasPressedThisFrame)
                ShowDark();
            return;
        }
        if (!Xp.OnFlatGround(me)) { darkFlat = -1f; return; }
        if (darkFlat < 0f) darkFlat = Time.time;
        if (Time.time - darkFlat >= 1f) { darkFlat = -1f; ShowDark(); }
    }

    private static void ShowDark()
    {
        var items = DarkItems();
        if (items.Count == 0) { darkTaken = true; return; }
        bool shown = ChoiceScreen.Show(new ScreenSpec
        {
            Title = "THE STATUE REMEMBERS",
            Subtitle = "Choose one",
            Confirm = "TAKE",
            BackHint = $"Later ({ModConfig.RelicKey.Value} to reopen)",
            Cards = items.Select(DarkCard).ToList(),
            OnConfirm = i =>
            {
                darkTaken = true;
                darkLater = false;
                Grant(items[i]);
                Hud.Banner("THE STATUE GIVES", Items.DisplayName(items[i]), ChoiceScreen.Muted);
                Plugin.Log.LogInfo($"Revive statue: took {items[i].gameObject.name}.");
            },
            OnBack = () => darkLater = true,
            OnCancelled = () => { },
        });
        if (shown) Plugin.Log.LogInfo("Revive statue: dark offer shown.");
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
        if (Ascents.canReviveDead && Character.PlayerIsDeadOrDown())
        {
            // Not re-opened from its spawn history: this is the revive itself.
            if (!(__instance.HasSpawnTracking(out var tracker) && tracker.HasSpawnHistory))
            {
                try { Blessing.HostReviveRoll(); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Revive dark offer roll failed: {e.Message}"); }
                // Every scout gets a random badge relic for the revive.
                Net.Bless(__instance.photonView.ViewID, revive: true);
                Plugin.Log.LogInfo("Revive statue: a random badge for every scout.");
            }
            return true;
        }
        __result = new List<PhotonView>();
        Net.Bless(__instance.photonView.ViewID);
        Plugin.Log.LogInfo("Revive statue: nobody to revive - blessing the team.");
        return false;
    }
}
