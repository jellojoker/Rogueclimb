using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace PeakRoguelike;

/// <summary>
/// Fusion luck along the run - never announced, it just feels lucky:
///   ingredient pull - crates roll the one ingredient a maxed item of yours is missing more often
///                     (a little early on, a lot once the squad is past the third biome);
///   statues         - the missing-ingredient / finished-fusion odds rise at the same point;
///   lucky fusion    - reaching the second biome's campfire, about one squad in ten finds a
///                     finished fusion on someone's next level-up;
///   guarantee       - reaching the campfire before the Kiln with no fusion made by anyone, one scout (the
///                     closest to a fusion) is offered one. Leaving it passes it to the next scout.
/// </summary>
internal class Luck : MonoBehaviourPunCallbacks
{
    private const string FusedKey = "prl.fz";     // room: someone in the squad has a fusion
    private const string ChosenKey = "prl.fg";    // room: actor offered the guarantee (-1 = nobody left)
    private const string DeclinedKey = "prl.fd";  // room: actors who left it, ",3,5,"
    private const string KilnKey = "prl.kc";      // player: reached the Kiln's campfire
    private const string ScoreKey = "prl.fs";     // player: how close to a fusion

    private static Luck? instance;
    /// <summary>The lucky fusion waiting on our next level-up (null = none).</summary>
    private static Recipe? lucky;
    private float nextTick, retryAt, flatSince = -1f;
    private int lastScore = -1;

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Luck");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        instance = go.AddComponent<Luck>();
    }

    internal static void Reset()
    {
        lucky = null;
        if (instance != null) { instance.lastScore = -1; instance.retryAt = 0f; }
        if (!PhotonNetwork.InRoom) return;
        try
        {
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { [KilnKey] = null, [ScoreKey] = null });
            if (PhotonNetwork.IsMasterClient)
                PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { [FusedKey] = null, [ChosenKey] = null, [DeclinedKey] = null });
        }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Luck: couldn't reset: {e.Message}"); }
    }

    // --- the ramp ---------------------------------------------------------------------------------

    /// <summary>Past the third biome's campfire (the squad's map segment).</summary>
    internal static bool Late
    {
        get
        {
            try { return Singleton<MapHandler>.Instance is { } map && map.currentSegment >= ModConfig.LuckLateFrom.Value; }
            catch { return false; }
        }
    }

    internal static float StatueIngredientChance => Late ? ModConfig.LuckStatueIngredientLate.Value : ModConfig.StatueIngredientChance.Value;
    private static float Pull => Mathf.Max(1f, Late ? ModConfig.LuckPullLate.Value : ModConfig.LuckPullEarly.Value);

    /// <summary>
    /// Ingredients a maxed item in our pockets is waiting for: every tree ingredient of the recipe
    /// is in our pockets and good enough, exactly one other ingredient is missing.
    /// </summary>
    private static List<Ingredient> Wanted()
    {
        var list = new List<Ingredient>();
        var p = Player.localPlayer;
        if (p == null || !ModConfig.Fusion.Value) return list;
        var pockets = p.itemSlots.Append(p.tempFullSlot).Where(s => s != null && !s.IsEmpty() && s.prefab != null).ToList();
        foreach (var r in Fusion.Recipes)
        {
            if (!r.Unlocked()) continue;
            var trees = r.Ingredients.Where(i => i.FromTree).ToList();
            if (trees.Count == 0) continue;
            if (!trees.All(i => pockets.Any(s => i.Match(s.prefab) && (i.SlotReady == null ? i.Ready() : i.SlotReady(s))))) continue;
            var missing = r.Ingredients.Where(i => !i.FromTree && !pockets.Any(s => i.Match(s.prefab))).ToList();
            if (missing.Count == 1) list.Add(missing[0]);
        }
        return list;
    }

    /// <summary>LootCurve, on a crate pool it just reweighted: pull what our maxed items are missing.</summary>
    internal static void PullIngredients(Dictionary<ushort, int> pool, int even)
    {
        var wanted = Wanted();
        if (wanted.Count == 0) return;
        float pull = Pull;
        var pulled = new List<string>();
        foreach (var ing in wanted)
        {
            bool found = false;
            foreach (ushort id in pool.Keys.ToList())
            {
                if (!Items.TryGet(id, out var item) || !ing.Match(item)) continue;
                pool[id] = Mathf.Max(1, Mathf.RoundToInt(pool[id] * pull));
                found = true;
            }
            // Not in this crate at all: it turns up now and then (never an earned-only item).
            if (!found && ing.Gift != null && Items.Find(ing.Gift) is { } gift && !Items.IsEarnedOnly(gift))
            {
                pool[gift.itemID] = Mathf.Max(1, Mathf.RoundToInt(even * 10f * 0.25f * pull));
                found = true;
            }
            if (found) pulled.Add(ing.Name);
        }
        if (pulled.Count > 0) Plugin.Log.LogInfo($"Luck: crate pulls {string.Join(", ", pulled)} x{pull:0.#}.");
    }

    // --- fusions to hand out ---------------------------------------------------------------------

    /// <summary>A finished fusion that fits what you carry (shares a tree with a pocket item), else any.</summary>
    internal static Recipe? FittingFusion()
    {
        var all = Fusion.Recipes.Where(r => r.Result != null && r.Unlocked() && Items.Find(r.Result) != null).ToList();
        if (all.Count == 0) return null;
        var p = Player.localPlayer;
        var pockets = p == null ? new List<Item>()
            : p.itemSlots.Where(s => s != null && !s.IsEmpty() && s.prefab != null).Select(s => s.prefab).ToList();
        int Fit(Recipe r) => r.Ingredients.Count(i => pockets.Any(i.Match)) + r.Trees.Count(t => pockets.Any(it => Trees.Classify(it) == t));
        int best = all.Max(Fit);
        return all.Where(r => Fit(r) == best).OrderBy(_ => Random.value).First();
    }

    /// <summary>Someone in the squad now has a fusion (made, gifted or claimed): no guarantee needed.</summary>
    internal static void MarkFused(string what)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(FusedKey)) return;
        try { PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { [FusedKey] = 1 }); }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Luck: couldn't mark a fusion: {e.Message}"); }
        Plugin.Log.LogInfo($"Luck: the squad has its first fusion ({what}).");
    }

    // --- campfires ------------------------------------------------------------------------------

    /// <summary>Xp: we reached biome b's campfire.</summary>
    internal static void BiomeCleared(int b)
    {
        if (b == (int)Segment.Tropics && ModConfig.Fusion.Value && lucky == null)
        {
            int squad = PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null ? Mathf.Max(1, PhotonNetwork.CurrentRoom.PlayerCount) : 1;
            float chance = Mathf.Clamp01(ModConfig.LuckFusionChance.Value) / squad;
            float roll = Random.value;
            if (roll < chance) lucky = FittingFusion();
            Plugin.Log.LogInfo($"Luck: second campfire - lucky fusion roll {roll:0.000} vs {chance:0.000}" + (lucky != null ? $" -> {lucky.Name} on the next level-up." : "."));
        }
        // The Kiln has no campfire of its own: the guarantee comes as it starts (the Caldera / Gloom campfire).
        if (b == (int)Segment.Caldera && PhotonNetwork.InRoom)
        {
            try { PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { [KilnKey] = 1 }); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Luck: couldn't share the campfire: {e.Message}"); }
            Plugin.Log.LogInfo("Luck: reached the campfire before the Kiln.");
        }
    }

    /// <summary>The lucky fusion for this level-up's cards (stays until claimed).</summary>
    internal static Recipe? LuckyCard => lucky;

    internal static void LuckyClaimed()
    {
        Plugin.Log.LogInfo($"Luck: lucky fusion {lucky?.Name} claimed.");
        lucky = null;
    }

    // --- the squad guarantee --------------------------------------------------------------------

    private static object? Room(string key) =>
        PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(key, out object v) ? v : null;

    /// <summary>How close we are to a fusion: a maxed item one ingredient short wins, then item levels.</summary>
    private static int Score() =>
        (Blessing.MissingIngredient() != null || Wanted().Count > 0 ? 1000 : 0) + Trees.All.Keys.Sum(RunState.Level);

    private void Update()
    {
        if (!PhotonNetwork.InRoom || !MapHandler.Exists || !ModConfig.LuckGuarantee.Value || !ModConfig.Fusion.Value) return;
        if (Time.time >= nextTick)
        {
            nextTick = Time.time + 1f;
            Publish();
            if (PhotonNetwork.IsMasterClient) Choose();
        }
        Offer();
    }

    private void Publish()
    {
        int s = Score();
        if (s == lastScore) return;
        lastScore = s;
        try { PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { [ScoreKey] = s }); }
        catch { /* next tick */ }
    }

    /// <summary>Host: once a scout reaches the campfire before the Kiln and nobody has a fusion, pick who's offered one.</summary>
    private static void Choose()
    {
        if (Room(FusedKey) != null || Room(ChosenKey) is int) return;
        var players = PhotonNetwork.PlayerList;
        if (!players.Any(p => p.CustomProperties.ContainsKey(KilnKey))) return;
        string declined = Room(DeclinedKey) as string ?? ",";
        var pick = players.Where(p => !declined.Contains("," + p.ActorNumber + ","))
            .OrderByDescending(p => p.CustomProperties.TryGetValue(ScoreKey, out object v) && v is int i ? i : 0)
            .ThenBy(_ => Random.value)
            .FirstOrDefault();
        int actor = pick?.ActorNumber ?? -1;
        try { PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { [ChosenKey] = actor }); }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Luck: couldn't choose: {e.Message}"); return; }
        Plugin.Log.LogInfo(actor < 0 ? "Luck: everyone left the guaranteed fusion." : $"Luck: no fusion in the squad - {pick!.NickName} is offered one.");
    }

    /// <summary>The chosen scout: the offer opens once they stand on flat ground.</summary>
    private void Offer()
    {
        if (Room(FusedKey) != null || Room(ChosenKey) is not int chosen || chosen != PhotonNetwork.LocalPlayer.ActorNumber) return;
        var me = Character.localCharacter;
        if (me == null || ChoiceScreen.IsOpen || Time.time < retryAt || !Xp.OnFlatGround(me)) { flatSince = -1f; return; }
        if (flatSince < 0f) flatSince = Time.time;
        if (Time.time - flatSince < 1f) return;
        flatSince = -1f;

        var missing = Blessing.MissingIngredient();
        var recipe = missing?.recipe ?? FittingFusion();
        if (recipe == null) { retryAt = Time.time + 30f; return; }
        Item? gift = missing is { } m ? Items.Find(m.ingredient.Gift!) : null;
        CardSpec card;
        if (gift != null)
        {
            card = ItemPerkInfo.From(gift).ToCard();
            card.Title = Items.DisplayName(gift);
            card.Tag = "Gift";
            card.TagColor = Trees.Gold;
            card.Body = $"The last piece of the\n{recipe.Name}.";
        }
        else card = Fusion.ItemCard(recipe, "The mountain's gift");

        bool shown = ChoiceScreen.Show(new ScreenSpec
        {
            Title = "THE MOUNTAIN PROVIDES",
            Subtitle = recipe.Name,
            Confirm = "ACCEPT",
            BackHint = "Leave it",
            Cards = new List<CardSpec> { card },
            StillValid = () => Character.localCharacter is { } c && !c.data.dead && !c.data.passedOut,
            OnConfirm = _ =>
            {
                MarkFused("guaranteed");
                Hud.PickChime();
                if (gift != null)
                {
                    Xp.ExpectCrateItem(gift);
                    GameUtils.instance.InstantiateAndGrab(gift, Character.localCharacter);
                    Hud.Banner("THE MOUNTAIN GIVES", missing!.Value.ingredient.Name, Trees.Gold);
                }
                else
                {
                    Fusion.Give(recipe);
                    Hud.Banner("THE MOUNTAIN GIVES", recipe.Name, ChoiceScreen.FusionGlow);
                }
                Plugin.Log.LogInfo($"Luck: took the guaranteed {(gift != null ? missing!.Value.ingredient.Name + " for the " : "")}{recipe.Name}.");
            },
            OnBack = Decline,
            OnCancelled = () => retryAt = Time.time + 20f,
        });
        if (!shown) retryAt = Time.time + 5f;
    }

    private static void Decline()
    {
        string declined = Room(DeclinedKey) as string ?? ",";
        declined += PhotonNetwork.LocalPlayer.ActorNumber + ",";
        try { PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { [DeclinedKey] = declined, [ChosenKey] = null }); }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Luck: couldn't pass the fusion on: {e.Message}"); }
        Plugin.Log.LogInfo("Luck: left the guaranteed fusion - it passes on.");
    }
}
