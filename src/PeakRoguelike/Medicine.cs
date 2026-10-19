using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Peak.Afflictions;
using Photon.Pun;
using UnityEngine;
using STATUS = CharacterAfflictions.STATUSTYPE;

namespace PeakRoguelike;

/// <summary>
/// The health tree's medicine, on top of the game's own item actions:
///   Bandages (1-3)        also ease every ailment as much as they heal.
///   Holy health packs     heal injury and every ailment (half at level 4, all from 5); the
///                         Radiant pack (6) adds milk invincibility, and drops beside anyone who passes out.
///   Dark medkits          turn injury and glass into ailments that fade on their own: half into
///                         poison (4), all into poison (5), injury into sleep and glass shattered (6).
///   Antidote              eases every ailment a little, on top of its poison cure.
///   Cure-All (fused)      the game's Cure-All + a Radiant pack; thrown and broken it doses everyone near.
///   Pandora's Lunchbox    the game's reroll (glass wiped too), shared: everyone near gets the same roll.
/// The suns' caps still apply (blue: healing at 10%) - it all runs inside the item's own action.
/// </summary>
[HarmonyPatch]
internal class Medicine : MonoBehaviour
{
    /// <summary>"Every ailment" (spores go with poison - the game clears them together).</summary>
    private static readonly STATUS[] Ailments = { STATUS.Poison, STATUS.Cold, STATUS.Hot, STATUS.Drowsy };

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Medicine");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<Medicine>();
    }

    private static bool On => ModConfig.ItemTrees.Value;

    private static bool Local(Character? c) => c != null && c.photonView.IsMine && !c.data.dead;

    private static bool IsDarkKit(Item? item) =>
        item != null && Items.IsMedKit(item) && Stats.For(item, TreeId.Bandage).branch == 2;

    // --- the item's own actions -----------------------------------------------------------------

    /// <summary>Dark medkits never heal: the game's own injury / poison cure is skipped (we convert instead).</summary>
    [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(Action_ModifyStatus), nameof(Action_ModifyStatus.RunAction))]
    private static bool Before(Action_ModifyStatus __instance, out float __state)
    {
        __state = __instance.changeAmount; // before the tree scales it
        return !(On && __instance.changeAmount < 0f && IsDarkKit(__instance.item));
    }

    /// <summary>
    /// Each medicine does its extra once per use, on one of its own actions (the injury cure, or
    /// the Antidote's poison cure) - so feeding and bandaging other scouts work like the game's.
    /// </summary>
    [HarmonyPostfix, HarmonyPatch(typeof(Action_ModifyStatus), nameof(Action_ModifyStatus.RunAction))]
    private static void After(Action_ModifyStatus __instance, float __state)
    {
        try
        {
            var item = __instance.item;
            if (!On || item == null || __state >= 0f || __instance.ifSkeleton) return;
            var c = item.holderCharacter;
            if (!Local(c)) return;
            var aff = c.refs.afflictions;

            if (Items.IsAntidote(item))
            {
                if (__instance.statusType != STATUS.Poison) return;
                float ease = Mathf.Clamp01(ModConfig.AntidoteCleanse.Value);
                foreach (var s in new[] { STATUS.Injury, STATUS.Cold, STATUS.Drowsy }) aff.SubtractStatus(s, ease);
                return;
            }
            if (__instance.statusType != STATUS.Injury) return;

            if (Items.IsCureAll(item))
            {
                RadiantDose(c);
                return;
            }
            if (Trees.Classify(item) != TreeId.Bandage) return;
            var (lv, br) = Stats.For(item, TreeId.Bandage);
            if (Items.IsMedKit(item))
            {
                int step = Stats.KitStep(lv, br);
                if (br == 2) { DarkConvert(c, step); return; }
                float share = step <= 1 ? Stats.HolyFirstStep : 1f;
                foreach (var s in Ailments) aff.SubtractStatus(s, share);
                if (step >= 3) Shield(c);
                return;
            }
            // A bandage: every ailment eased as much as the injury it heals.
            float healed = -Stats.SnapStatus(__state, Stats.Potency(item, lv, br).status);
            foreach (var s in Ailments) aff.SubtractStatus(s, healed);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Medicine failed: {e.Message}"); }
    }

    /// <summary>A Radiant pack's worth: all injury and every ailment, then milk invincibility.</summary>
    private static void RadiantDose(Character c)
    {
        var aff = c.refs.afflictions;
        aff.SubtractStatus(STATUS.Injury, 1f);
        foreach (var s in Ailments) aff.SubtractStatus(s, 1f);
        Shield(c);
    }

    private static void Shield(Character c)
    {
        float t = ModConfig.HolyShieldSeconds.Value;
        if (t > 0f) c.refs.afflictions.AddAffliction(new Affliction_Invincibility { totalTime = t, isFromMilk = true });
    }

    /// <summary>
    /// Dark medkit: injury and glass become an ailment that fades by itself. Only what really
    /// came off is converted (a blue sun lets little come off - and so little is added).
    /// </summary>
    private static void DarkConvert(Character c, int step)
    {
        var aff = c.refs.afflictions;
        float share = step <= 1 ? Stats.HolyFirstStep : 1f;

        float injBefore = aff.GetCurrentStatus(STATUS.Injury);
        if (injBefore > 0f) aff.SubtractStatus(STATUS.Injury, injBefore * share);
        float injury = Mathf.Max(0f, injBefore - aff.GetCurrentStatus(STATUS.Injury));

        int glassBefore = c.data.petrifyAmount;
        if (glassBefore > 0) aff.SubtractStatus(STATUS.Petrify, glassBefore * 0.01f * share);
        float glass = Mathf.Max(0, glassBefore - c.data.petrifyAmount) * 0.01f;

        if (step >= 3)
        {
            // The deepest dark: wounds become sleep, the glass simply shatters.
            if (injury > 0f) aff.AddStatus(STATUS.Drowsy, injury, ignoreInvincibility: true);
        }
        else if (injury + glass > 0f) aff.AddStatus(STATUS.Poison, injury + glass, ignoreInvincibility: true);
        Plugin.Log.LogInfo($"Dark medkit (step {step}): {injury:0.##} injury + {glass:0.##} glass -> {(step >= 3 ? "drowsiness, glass shattered" : "poison")}.");
    }

    // --- Cure-All: thrown, it breaks over everyone near ------------------------------------------

    [HarmonyPrefix, HarmonyPatch(typeof(Breakable), nameof(Breakable.Break))]
    private static void Shatter(Breakable __instance)
    {
        try
        {
            if (!On || Traverse.Create(__instance).Field<bool>("alreadyBroke").Value) return;
            var item = __instance.GetComponent<Item>();
            if (item == null || !Items.IsCureAll(item) || !item.photonView.IsMine) return;
            Plugin.Log.LogInfo("A Cure-All shattered: everyone near gets a full dose.");
            Net.CureAllSplash(item.transform.position, ModConfig.MedicineRadius.Value);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Cure-All splash failed: {e.Message}"); }
    }

    /// <summary>Everyone (us included): a full Cure-All dose if we're close to where it broke.</summary>
    internal static void ReceiveSplash(Vector3 at, float radius)
    {
        var me = Character.localCharacter;
        if (!Local(me) || Vector3.Distance(me!.Center, at) > radius) return;
        var aff = me.refs.afflictions;
        // The game's Cure-All, once (hunger, curse, thorns), then the Radiant pack's dose.
        if (Items.Find("CureAll") is { } prefab)
        {
            foreach (var a in prefab.GetComponents<Action_ModifyStatus>())
                if (a.changeAmount < 0f && !a.ifSkeleton) aff.SubtractStatus(a.statusType, -a.changeAmount);
            foreach (var a in prefab.GetComponents<Action_AddOrRemoveThorns>())
                for (int i = a.thornCount; i < 0; i++) aff.RemoveRandomThornLinq();
        }
        RadiantDose(me);
    }

    // --- Pandora's Lunchbox: one roll, shared ------------------------------------------------------

    /// <summary>The reroll wipes your glass too (the game leaves it).</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(Affliction_Chaos), nameof(Affliction_Chaos.OnApplied))]
    private static void BeforeRoll(Affliction_Chaos __instance)
    {
        var c = __instance.character;
        if (!On || !Local(c) || BingBongPranks.Rolling || c.data.petrifyAmount <= 0) return;
        c.refs.afflictions.SubtractStatus(STATUS.Petrify, c.data.petrifyAmount * 0.01f);
    }

    /// <summary>What the roll gave us goes out to everyone near: they get exactly the same.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(Affliction_Chaos), nameof(Affliction_Chaos.OnApplied))]
    private static void AfterRoll(Affliction_Chaos __instance)
    {
        try
        {
            var c = __instance.character;
            if (!On || !Local(c) || BingBongPranks.Rolling) return; // Bing Bong's roll is yours alone
            var aff = c.refs.afflictions;
            int n = Enum.GetValues(typeof(STATUS)).Length;
            var roll = new float[n];
            for (int i = 0; i < n; i++)
                if ((STATUS)i != STATUS.Petrify) roll[i] = aff.GetCurrentStatus((STATUS)i);
            Plugin.Log.LogInfo($"Pandora's roll shared: {string.Join(", ", roll.Select((v, i) => (v, i)).Where(x => x.v > 0.001f).Select(x => $"{(STATUS)x.i} {x.v:0.##}"))}; stamina +{c.data.extraStamina:0.##}.");
            Net.PandoraRoll(c.Center, ModConfig.MedicineRadius.Value, roll, c.data.extraStamina);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Pandora share failed: {e.Message}"); }
    }

    /// <summary>Someone near opened the lunchbox: our ailments become theirs, exactly.</summary>
    internal static void ReceiveRoll(Vector3 at, float radius, float[] roll, float stamina)
    {
        var me = Character.localCharacter;
        if (!Local(me) || Vector3.Distance(me!.Center, at) > radius) return;
        var aff = me.refs.afflictions;
        aff.ClearAllStatus(excludeCurse: false, excludePetrify: false);
        for (int i = 0; i < roll.Length; i++)
            if (roll[i] > 0f && (STATUS)i != STATUS.Petrify)
                aff.AddStatus((STATUS)i, roll[i], playEffects: true, ignoreInvincibility: true);
        me.SetExtraStamina(stamina);
        Plugin.Log.LogInfo("Pandora's Lunchbox opened nearby: took the same roll.");
    }

    // --- the Radiant pack: help for whoever passes out (host) ----------------------------------------

    private readonly Dictionary<int, bool> wasDown = new();
    private readonly Dictionary<int, float> lastDrop = new();
    private float nextLook;

    private void Update()
    {
        if (!On || !PhotonNetwork.IsMasterClient || Time.time < nextLook) return;
        nextLook = Time.time + 0.25f;
        try
        {
            float cooldown = ModConfig.PassOutKitCooldown.Value;
            foreach (var c in Character.AllCharacters)
            {
                if (c == null || c.isBot || c.photonView == null) continue;
                int id = c.photonView.ViewID;
                bool down = c.data.passedOut && !c.data.dead;
                bool was = wasDown.TryGetValue(id, out bool w) && w;
                wasDown[id] = down;
                if (!down || was || cooldown <= 0f) continue;
                if (Time.time - lastDrop.GetValueOrDefault(id, -999f) < cooldown || !SquadHasRadiant()) continue;
                lastDrop[id] = Time.time;
                DropPack(c);
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Pass-out pack failed: {e.Message}"); }
    }

    /// <summary>Has any scout reached the top of the holy path?</summary>
    private static bool SquadHasRadiant() =>
        Character.AllCharacters.Any(c => c != null && !c.isBot && RunState.LevelOf(c, TreeId.Bandage) is var (lv, br)
            && br == 1 && Trees.Get(TreeId.Bandage) is { } t && lv >= t.MaxFor(1));

    private static void DropPack(Character c)
    {
        if (Items.Find("FirstAidKit") is not { } kit) return;
        string name = kit.gameObject.name.Replace("(Clone)", "").Trim();
        Vector3 pos = c.Center + Vector3.up * 0.75f + UnityEngine.Random.insideUnitSphere * 0.3f;
        var view = PhotonNetwork.InstantiateItemRoom(name, pos, UnityEngine.Random.rotation)?.GetComponent<PhotonView>();
        if (view == null) return;
        var data = new ItemInstanceData(Guid.NewGuid());
        ItemInstanceDataHandler.AddInstanceData(data);
        view.RPC("SetItemInstanceDataRPC", RpcTarget.All, data);
        int top = Trees.Get(TreeId.Bandage)?.MaxFor(1) ?? 6;
        ItemLevels.StampPath(data.guid, top, 1, $"Radiant Health Pack dropped beside {c.characterName}");
    }
}
