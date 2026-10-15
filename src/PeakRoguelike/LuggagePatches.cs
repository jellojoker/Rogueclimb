using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace PeakRoguelike;

/// <summary>
/// Opening a crate no longer spills items into the world. The crate's loot is rolled once
/// (and shared with everyone, so backing out can't reroll it). It's a draft: every scout in
/// the lobby when it was first opened picks one card; the card taken is re-rolled for the
/// next scout, the others stay. The lid opens for you once you've picked, and for everyone
/// once all have. Picking an item that belongs to a tree levels that tree up.
/// </summary>
[HarmonyPatch]
internal static class LuggagePatches
{
    public const int MaxCards = 5;

    // Respawn chests revive people - leave them alone.
    private static bool Handles(Luggage luggage) => luggage is not RespawnChest;

    [HarmonyPostfix, HarmonyPatch(typeof(Luggage), "Awake")]
    private static void AddRpcTarget(Luggage __instance)
    {
        if (Handles(__instance) && !__instance.GetComponent<PerkLuggage>())
            __instance.gameObject.AddComponent<PerkLuggage>();
    }

    private static bool openVanilla;

    [HarmonyPrefix, HarmonyPatch(typeof(Luggage), nameof(Luggage.Interact_CastFinished))]
    private static bool OpenPerkScreen(Luggage __instance, Character interactor)
    {
        if (openVanilla || !Handles(__instance) || __instance.state != Luggage.LuggageState.Closed) return true;
        if (interactor == null || !interactor.IsLocal) return true;
        if (ChoiceScreen.IsOpen) return false;

        var perk = __instance.GetComponent<PerkLuggage>();
        if (perk == null) return true;
        if (!perk.CanPick) { perk.OpenForMe(); return false; }
        if (perk.LockedByOther(out string who))
        {
            Hud.Toast($"{who} is choosing from this crate", ChoiceScreen.Muted);
            return false;
        }
        // Only one scout at a time: the host hands out the lock, then the cards open.
        perk.RequestLock(() => Open(__instance, perk, interactor));
        return false;
    }

    private static void Open(Luggage luggage, PerkLuggage perk, Character interactor)
    {
        if (luggage == null || luggage.IsOpen || ChoiceScreen.IsOpen || !perk.CanPick) { perk.ReleaseLock(); return; }
        // Ancient Luggage holds a blessing instead of loot.
        if (Blessing.TryCrate(luggage, perk, interactor)) return;
        List<Item> choices = perk.GetOrRoll();
        if (choices.Count > 0)
        {
            ShowCrate(luggage, choices);
            return;
        }
        perk.ReleaseLock();
        if ((luggage.GetSpawnSpots()?.Count ?? 0) > 0)
        {
            // Everything in it was earned-only loot: the crate is a dud rather than spilling it.
            luggage.photonView.RPC(nameof(PerkLuggage.RPC_PerkOpen), RpcTarget.All);
            GlobalEvents.TriggerLuggageOpened(luggage, interactor);
            Hud.Toast("The crate was empty", ChoiceScreen.Muted);
            return;
        }
        Plugin.Log.LogInfo($"{luggage.displayName}: nothing to offer, opening normally.");
        openVanilla = true;
        try { luggage.Interact_CastFinished(interactor); }
        finally { openVanilla = false; }
    }

    /// <summary>No unclasp animation while someone else is choosing.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(Luggage), nameof(Luggage.Interact))]
    private static bool BlockWhileLocked(Luggage __instance) =>
        !(__instance.GetComponent<PerkLuggage>() is { } perk && perk.LockedByOther(out _));

    [HarmonyPostfix, HarmonyPatch(typeof(Luggage), nameof(Luggage.GetInteractionText))]
    private static void LockedText(Luggage __instance, ref string __result)
    {
        if (__instance.GetComponent<PerkLuggage>() is { } perk && perk.LockedByOther(out string who))
            __result = $"{who} is choosing...";
    }

    private static void ShowCrate(Luggage luggage, List<Item> items)
    {
        string crateName = "";
        try { crateName = luggage.GetName(); } catch { /* not localised */ }
        if (string.IsNullOrEmpty(crateName)) crateName = "Supply crate";
        var perk = luggage.GetComponent<PerkLuggage>();
        // Fusions you've made come along with their ingredients' cards.
        var fused = Offers.UnlockedFusions(items);
        var cards = items.Select(CrateCards.ForItem).ToList();
        cards.AddRange(fused.Select(r => Fusion.ItemCard(r, "Fusion unlocked")));

        bool shown = ChoiceScreen.Show(new ScreenSpec
        {
            Title = "CHOOSE YOUR PERK",
            Subtitle = crateName,
            Confirm = "CLAIM",
            Cards = cards,
            StillValid = () => luggage != null && !luggage.IsOpen && Character.localCharacter != null
                               && Vector3.Distance(Character.localCharacter.Center, luggage.Center()) <= 8f,
            OnConfirm = i =>
            {
                if (i < items.Count) Picked(luggage, items, i);
                else ClaimFusion(luggage, fused[i - items.Count]);
            },
            OnBack = () => perk?.ReleaseLock(),
        });
        if (!shown) perk?.ReleaseLock();
    }

    /// <summary>A card was chosen. Tree items may first ask for a path.</summary>
    private static void Picked(Luggage luggage, List<Item> items, int index)
    {
        Item item = items[index];
        var offer = Offers.Resolve(item);
        switch (offer.Kind)
        {
            case OfferKind.TreeLevel:
            case OfferKind.TreeSpare:
                LevelUp(offer.Tree!, offer.Level, offer.Branch);
                Claim(luggage, offer.Grant, index);
                return;
            case OfferKind.Fork:
                break;
            default:
                Claim(luggage, item, index);
                return;
        }

        // Fork: pick a path. Backing out returns to the crate (nothing is claimed yet).
        var tree = offer.Tree!;
        int next = offer.Level;
        var options = Offers.Paths(tree, next);
        ChoiceScreen.Show(new ScreenSpec
        {
            Title = "CHOOSE YOUR PATH",
            Subtitle = $"{tree.Name} · level {next}",
            Confirm = "CHOOSE",
            Cards = options.Select(o => CrateCards.ForPath(tree, next, o, item)).ToList(),
            StillValid = () => luggage != null && !luggage.IsOpen,
            OnConfirm = i =>
            {
                LevelUp(tree, next, options[i].Branch);
                Claim(luggage, Offers.Grant(options[i], item), index);
            },
            OnBack = () => ShowCrate(luggage, items),
        });
    }

    private static void LevelUp(TreeDef tree, int level, int branch)
    {
        bool wasMax = RunState.Level(tree.Id) >= tree.MaxFor(RunState.Branch(tree.Id)) && RunState.Level(tree.Id) > 0;
        RunState.SetLevel(tree.Id, level, branch);
        var def = tree.At(level, branch);
        if (wasMax) Hud.Toast($"{tree.Name} is already maxed", Trees.Gold);
        else Hud.PickBanner($"{tree.Name.ToUpperInvariant()} · LEVEL {level}", def.Name, Trees.LevelColor(tree.Id, level, branch));
    }

    /// <summary>An unlocked fusion's card: the crate opens and hands you the finished item.</summary>
    private static void ClaimFusion(Luggage luggage, Recipe recipe)
    {
        if (luggage == null || luggage.IsOpen || Character.localCharacter is not { } me) return;
        var perk = luggage.GetComponent<PerkLuggage>();
        perk?.Taken(-1, 0); // your own extra card: the crate's cards stay as they are
        perk?.ReleaseLock();
        GlobalEvents.TriggerLuggageOpened(luggage, me);
        Fusion.Give(recipe);
        Hud.Banner("FUSION", recipe.Name, ChoiceScreen.FusionGlow);
        Plugin.Log.LogInfo($"Claimed fusion {recipe.Name} from {luggage.displayName}.");
    }

    /// <summary>Same roll the host would do when the crate opens.</summary>
    internal static List<Item> RollChoices(Luggage luggage)
    {
        var result = new List<Item>();
        try
        {
            List<Transform> spots = luggage.GetSpawnSpots();
            int count = Mathf.Min(spots?.Count ?? 0, MaxCards);
            if (count == 0) return result;
            // Earned-only items (fusions, level unlocks) are thrown back and the crate rolls again.
            for (int attempt = 0; attempt < 8 && result.Count < count; attempt++)
            {
                List<GameObject> objects = luggage.GetObjectsToSpawn(count, luggage.canRepeatSpawns);
                if (objects == null) break;
                foreach (var item in objects.Where(o => o != null).Select(o => o.GetComponent<Item>()))
                {
                    if (item == null || result.Count >= count) continue;
                    if (ModConfig.Leveling.Value && Items.IsEarnedOnly(item)) continue;
                    if (attempt > 0 && !luggage.canRepeatSpawns && result.Any(r => r.itemID == item.itemID)) continue;
                    result.Add(item);
                }
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"Failed to roll crate loot: {e}");
        }
        return result;
    }

    /// <summary>One more item from the crate's pool, for the card someone just took (0 = nothing).</summary>
    internal static ushort RollOne(Luggage luggage)
    {
        try
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var objects = luggage.GetObjectsToSpawn(1, true);
                var item = objects?.Where(o => o != null).Select(o => o.GetComponent<Item>()).FirstOrDefault(i => i != null);
                if (item == null) continue;
                if (ModConfig.Leveling.Value && Items.IsEarnedOnly(item)) continue;
                return item.itemID;
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"Failed to re-roll a crate card: {e}");
        }
        return 0;
    }

    /// <summary>Your pick: the crate opens for you, the card you took re-rolls for the next scout, and the item is yours.</summary>
    internal static void Claim(Luggage luggage, Item item, int slot = -1)
    {
        if (luggage == null || luggage.IsOpen) return;
        Character me = Character.localCharacter;
        if (me == null) return;
        Hud.PickChime();

        var perk = luggage.GetComponent<PerkLuggage>();
        if (perk != null)
        {
            perk.Taken(slot, slot >= 0 && perk.OthersLeft ? RollOne(luggage) : (ushort)0);
            perk.ReleaseLock();
        }
        else luggage.photonView.RPC(nameof(PerkLuggage.RPC_PerkOpen), RpcTarget.All);
        GlobalEvents.TriggerLuggageOpened(luggage, me);
        // Host spawns the item at our hip and immediately picks it up for us
        // (if our pockets are full it drops at our feet, same as the game's own give-item path).
        GameUtils.instance.InstantiateAndGrab(item, me);
        // Generosity relic: a pick of its kind comes with a spare.
        if (Relics.Generosity(item)) GameUtils.instance.InstantiateAndGrab(item, me);
        Plugin.Log.LogInfo($"Claimed {item.gameObject.name} from {luggage.displayName}.");
    }

    /// <summary>
    /// Every spawner in the world (crates, ground spawns, ...) asks this for its items.
    /// Earned-only items (fusions, evolved items, level unlocks) become their base ingredient.
    /// </summary>
    [HarmonyPostfix, HarmonyPatch(typeof(Spawner), "GetObjectsToSpawn")]
    private static void SwapEarned(Spawner __instance, List<GameObject> __result)
    {
        if (!ModConfig.Leveling.Value || __result == null) return;
        for (int i = 0; i < __result.Count; i++)
        {
            var item = __result[i] != null ? __result[i].GetComponent<Item>() : null;
            if (item == null) continue;
            if (__instance is not Luggage && Items.IsCrateOnly(item))
            {
                // Food-tree snacks only come out of crates; the spot stays empty instead.
                Plugin.Log.LogInfo($"Spawn: {item.gameObject.name} is crate-only -> nothing.");
                __result[i] = null!;
                continue;
            }
            if (!Items.IsEarnedOnly(item)) continue;
            var swap = Items.BaseIngredient(item);
            Plugin.Log.LogInfo($"Spawn: {item.gameObject.name} is earned-only -> {(swap != null ? swap.gameObject.name : "nothing")}.");
            __result[i] = swap != null ? swap.gameObject : null!;
        }
    }
}

/// <summary>
/// Lives on every crate: syncs the one-time loot roll, the draft (who may pick, who has, and
/// the re-rolled cards), the "opened without spawning" state, and who is currently choosing
/// (the host arbitrates so two scouts can't open it at once).
/// </summary>
public class PerkLuggage : MonoBehaviourPun
{
    private Luggage luggage = null!;
    private ushort[]? rolled;
    /// <summary>The scouts in the lobby when the crate was first opened: one pick each.</summary>
    private int[]? pickers;
    private readonly HashSet<int> picked = new();
    private int lockedBy;              // actor number of the scout choosing, 0 = free
    private System.Action? pendingOpen;

    private void Awake() => luggage = GetComponent<Luggage>();

    private static int LocalActor => PhotonNetwork.LocalPlayer?.ActorNumber ?? -1;

    private static bool InRoom(int actor) => !PhotonNetwork.InRoom ? actor == LocalActor : PhotonNetwork.CurrentRoom?.GetPlayer(actor) != null;

    /// <summary>Not rolled yet (you'd be first), or you're one of its pickers and haven't picked.</summary>
    public bool CanPick => pickers == null || (pickers.Contains(LocalActor) && !picked.Contains(LocalActor));

    /// <summary>Someone else in the lobby still has a pick coming (worth re-rolling the card you took).</summary>
    public bool OthersLeft => pickers != null && pickers.Any(a => a != LocalActor && !picked.Contains(a) && InRoom(a));

    private bool Spent => pickers != null && pickers.All(a => picked.Contains(a) || !InRoom(a));

    /// <summary>You took a card (slot -1 = your own extra card): tell everyone, with the card's re-roll.</summary>
    public void Taken(int slot, ushort reroll)
    {
        if (PhotonNetwork.InRoom) photonView.RPC(nameof(RPC_Taken), RpcTarget.All, LocalActor, slot, (int)reroll);
        else RPC_Taken(LocalActor, slot, reroll);
    }

    [PunRPC]
    public void RPC_Taken(int actor, int slot, int reroll)
    {
        picked.Add(actor);
        if (rolled != null && slot >= 0 && slot < rolled.Length)
        {
            var list = rolled.ToList();
            if (reroll > 0) list[slot] = (ushort)reroll;
            else list.RemoveAt(slot);
            rolled = list.ToArray();
        }
        Plugin.Log.LogInfo($"{luggage?.displayName}: actor {actor} picked ({picked.Count}/{pickers?.Length ?? 1}){(reroll > 0 && Items.TryGet((ushort)reroll, out var it) ? $", card re-rolled to {it.gameObject.name}" : "")}.");
        if (Spent) RPC_PerkOpen();
        else if (actor == LocalActor) OpenForMe();
    }

    /// <summary>The lid opens for this scout only (the crate stays closed for those still to pick).</summary>
    public void OpenForMe()
    {
        if (luggage == null || luggage.state == Luggage.LuggageState.Open) return;
        luggage.state = Luggage.LuggageState.Open;
        if (luggage.anim != null) luggage.anim.Play("Luggage_Open");
        if (luggage.activateOnOpen != null) luggage.activateOnOpen.SetActive(true);
    }

    public bool LockedByOther(out string who)
    {
        who = "";
        if (lockedBy == 0 || !PhotonNetwork.InRoom || lockedBy == LocalActor) return false;
        var p = PhotonNetwork.CurrentRoom.GetPlayer(lockedBy);
        if (p == null) { lockedBy = 0; return false; } // they left mid-choice
        who = string.IsNullOrEmpty(p.NickName) ? "Someone" : p.NickName;
        return true;
    }

    public void RequestLock(System.Action onGranted)
    {
        if (!PhotonNetwork.InRoom) { onGranted(); return; }
        pendingOpen = onGranted;
        photonView.RPC(nameof(RPC_RequestLock), RpcTarget.MasterClient, LocalActor);
    }

    public void ReleaseLock()
    {
        if (!PhotonNetwork.InRoom) { lockedBy = 0; return; }
        photonView.RPC(nameof(RPC_ReleaseLock), RpcTarget.All, LocalActor);
    }

    [PunRPC]
    public void RPC_RequestLock(int actor)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        bool takenByOther = lockedBy != 0 && lockedBy != actor && InRoom(lockedBy);
        photonView.RPC(nameof(RPC_SetLock), RpcTarget.All, takenByOther ? lockedBy : actor);
    }

    [PunRPC]
    public void RPC_SetLock(int actor)
    {
        lockedBy = actor;
        if (pendingOpen == null) return;
        var open = pendingOpen;
        pendingOpen = null;
        if (actor == LocalActor) open();
        else if (LockedByOther(out string who)) Hud.Toast($"{who} is choosing from this crate", ChoiceScreen.Muted);
    }

    [PunRPC]
    public void RPC_ReleaseLock(int actor)
    {
        if (lockedBy == actor) lockedBy = 0;
    }

    /// <summary>The crate's cards. Rolled once, then the same for every reopen and every player.</summary>
    public List<Item> GetOrRoll()
    {
        if (rolled == null)
        {
            var items = LuggagePatches.RollChoices(luggage);
            rolled = items.Select(i => i.itemID).ToArray();
            pickers = PhotonNetwork.InRoom ? PhotonNetwork.PlayerList.Select(p => p.ActorNumber).ToArray() : new[] { LocalActor };
            if (rolled.Length > 0 && PhotonNetwork.InRoom)
                photonView.RPC(nameof(RPC_SetRoll), RpcTarget.Others, rolled.Select(x => (int)x).ToArray(), pickers);
        }
        var list = new List<Item>();
        foreach (ushort id in rolled)
            if (Items.TryGet(id, out var item)) list.Add(item);
        return list;
    }

    [PunRPC]
    public void RPC_SetRoll(int[] ids, int[] actors)
    {
        // First roll wins; ignore a late one if we already rolled ourselves.
        if (rolled != null) return;
        rolled = ids.Select(x => (ushort)x).ToArray();
        pickers = actors;
        // Joined after it was first opened: not one of its pickers.
        if (!pickers.Contains(LocalActor)) OpenForMe();
    }

    /// <summary>The crate is spent: open for everyone.</summary>
    [PunRPC]
    public void RPC_PerkOpen()
    {
        Luggage.ALL_LUGGAGE.Remove(luggage);
        OpenForMe();
    }
}
