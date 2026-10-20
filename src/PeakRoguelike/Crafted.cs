using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Peak;
using Peak.Afflictions;
using Peak.Network;
using Photon.Pun;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace PeakRoguelike;

public enum CraftId { None, Jetpack, SugarRush, FaerieLantern }

/// <summary>
/// A fused item that carries a twist of its own on top of the vanilla item (fusions have no
/// levels: they are the end of a tree).
/// </summary>
public class CraftDef
{
    public CraftId Id;
    public string Name = "";
    /// <summary>Items.Find role of the prefab handed out.</summary>
    public string Role = "";
    public PerkTheme Theme;
    public string[] Names = new string[0];
    public string[] Descs = new string[0];

    public int Max => Names.Length;
    public string NameAt(int lv) => Names[Mathf.Clamp(lv, 1, Max) - 1];
    public string DescAt(int lv) => Descs[Mathf.Clamp(lv, 1, Max) - 1];
}

internal static class Crafts
{
    public static readonly Dictionary<CraftId, CraftDef> All = new()
    {
        [CraftId.Jetpack] = new CraftDef
        {
            Id = CraftId.Jetpack, Name = "Jetpack", Role = "Jetpack", Theme = PerkTheme.Climber,
            Names = new[] { "Jetpack" },
            Descs = new[] { "Fused: more fuel." },
        },
        [CraftId.SugarRush] = new CraftDef
        {
            Id = CraftId.SugarRush, Name = "Sugar-Rush", Role = "EnergyDrink", Theme = PerkTheme.Mountaineer,
            Names = new[] { "Sugar-Rush Cocktail" },
            Descs = new[] { "A full energy drink and lollipop.\nThe crash is a healing sleep." },
        },
        [CraftId.FaerieLantern] = new CraftDef
        {
            Id = CraftId.FaerieLantern, Name = "Faerie Lantern", Role = "FaerieLantern", Theme = PerkTheme.Explorer,
            Names = new[] { "Faerie Lantern" },
            Descs = new[] { "Fused: wider, stronger\nand longer-burning." },
        },
    };

    public static bool On => ModConfig.Leveling.Value;

    public static CraftDef? Get(CraftId id) => All.TryGetValue(id, out var d) ? d : null;

    /// <summary>Which craft an item (prefab or instance) is.</summary>
    public static CraftId Classify(Item? item)
    {
        if (item == null) return CraftId.None;
        if (item.GetComponent<JetpackItem>()) return CraftId.Jetpack;
        if (item.GetComponent<Lantern>() && Items.IsFaerie(item)) return CraftId.FaerieLantern;
        if (SugarRush.IsMarked(item)) return CraftId.SugarRush;
        return CraftId.None;
    }

    /// <summary>A fusion's twist on its vanilla item (Jetpack fuel, Faerie range and burn).</summary>
    public static float Twist(CraftId id) => !ModConfig.ItemTrees.Value ? 1f : id switch
    {
        CraftId.Jetpack => ModConfig.JetpackFuel.Value,
        CraftId.FaerieLantern => ModConfig.FaerieBoost.Value,
        _ => 1f,
    };
}

/// <summary>Recolours an item's materials (multiplies their _Tint).</summary>
internal class ItemTint
{
    private readonly List<(Material mat, Color original)> tints = new();
    private bool collected;

    public void Apply(GameObject go, Color mul)
    {
        if (!collected)
        {
            collected = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || r.GetComponentInParent<SugarRushLook.Deco>()) continue;
                foreach (var m in r.materials)
                    if (m != null && m.HasProperty("_Tint")) tints.Add((m, m.GetColor("_Tint")));
            }
        }
        foreach (var (mat, original) in tints)
            if (mat != null) mat.SetColor("_Tint", new Color(original.r * mul.r, original.g * mul.g, original.b * mul.b, original.a));
    }

    public static Color Multiplier(Color c)
    {
        if (c == Trees.Plain) return Color.white;
        if (c == Trees.Worn) return new Color(0.72f, 0.62f, 0.52f);
        if (c == Trees.Steel) return new Color(0.78f, 0.95f, 1.25f);
        if (c == Trees.Gold) return new Color(1.25f, 1.02f, 0.55f);
        if (c == Trees.Dark) return new Color(0.78f, 0.45f, 1.2f);
        if (c == Trees.Saint) return new Color(1.15f, 1.12f, 0.95f);
        return Color.white;
    }
}

/// <summary>Lives on fused items: applies their twist (Faerie range and healing) and a gold tint.</summary>
public class CraftedItem : MonoBehaviour
{
    public Item Item = null!;
    public CraftId Craft;

    private Lantern? lantern;
    private StatusField? field;
    private GloomSafeZone? zone;
    private DispelFogField? fog;
    private float baseRadius, baseAmount, baseZone, baseZoneVisual, baseFogIn, baseFogOut;
    private float[] baseExtra = new float[0];
    private bool tinted;
    private readonly ItemTint tint = new();

    private void Awake()
    {
        Item = GetComponent<Item>();
        Craft = Crafts.Classify(Item);
        lantern = GetComponent<Lantern>();
        field = GetComponent<StatusField>();
        zone = GetComponent<GloomSafeZone>();
        fog = GetComponent<DispelFogField>();
        if (field)
        {
            baseRadius = field!.radius;
            baseAmount = field.statusAmountPerSecond;
            baseExtra = field.additionalStatuses?.Select(s => s.statusAmountPerSecond).ToArray() ?? new float[0];
        }
        if (zone) { baseZone = zone!.statusProtectionRadius; baseZoneVisual = zone.visualRadius; }
        if (fog) { baseFogIn = fog!.innerRadius; baseFogOut = fog.outerRadius; }
    }

    private void Update()
    {
        if (Craft == CraftId.None || Craft == CraftId.SugarRush) return;
        float m = Crafts.Twist(Craft);

        if (Craft == CraftId.FaerieLantern)
        {
            if (field)
            {
                field!.radius = baseRadius * m;
                field.statusAmountPerSecond = baseAmount * m;
                if (field.additionalStatuses != null)
                    for (int i = 0; i < field.additionalStatuses.Count && i < baseExtra.Length; i++)
                        field.additionalStatuses[i].statusAmountPerSecond = baseExtra[i] * m;
            }
            if (zone) { zone!.statusProtectionRadius = baseZone * m; zone.visualRadius = baseZoneVisual * m; }
            if (fog) { fog!.innerRadius = baseFogIn * m; fog.outerRadius = baseFogOut * m; }
        }

        if (!tinted)
        {
            tinted = true;
            tint.Apply(gameObject, ItemTint.Multiplier(Crafts.On && ModConfig.TintItems.Value ? Trees.Gold : Trees.Plain));
        }
    }
}

[HarmonyPatch]
internal static class CraftPatches
{
    [HarmonyPostfix, HarmonyPatch(typeof(Item), "Start")]
    private static void Attach(Item __instance)
    {
        if (__instance.GetComponent<CraftedItem>()) return;
        var id = Crafts.Classify(__instance);
        if (id != CraftId.None && id != CraftId.SugarRush) __instance.gameObject.AddComponent<CraftedItem>();
    }

    /// <summary>The cocktail is a marked Energy Drink: it shows its own name.</summary>
    [HarmonyPostfix, HarmonyPriority(Priority.Low), HarmonyPatch(typeof(Item), nameof(Item.GetName))]
    private static void CraftName(Item __instance, ref string __result)
    {
        if (Crafts.On && SugarRush.IsMarked(__instance)) __result = Crafts.All[CraftId.SugarRush].NameAt(1);
    }

    // --- jetpack: the fusion's extra fuel -------------------------------------------------------

    private static readonly Dictionary<CharacterMovement, float> baseDecay = new();

    /// <summary>The game's jetpack fuel burn per second (for the card), before our twist.</summary>
    internal static float VanillaJetpackDecay => baseDecay.Count > 0 ? baseDecay.Values.First() : 20f;

    [HarmonyPrefix, HarmonyPatch(typeof(CharacterMovement), "TryJetpack")]
    private static void JetpackFuel(CharacterMovement __instance)
    {
        if (__instance.character == null || !__instance.character.IsLocal) return;
        if (!baseDecay.TryGetValue(__instance, out float decay)) baseDecay[__instance] = decay = __instance.jetpackFuelDecaySpeed;
        __instance.jetpackFuelDecaySpeed = decay / Mathf.Max(0.1f, Crafts.Twist(CraftId.Jetpack));
        // Red sun: the tank burns down faster - a full one flies a third as long.
        if (Suns.Red) __instance.jetpackFuelDecaySpeed *= Mathf.Max(1f, ModConfig.SunRedJetpackBurn.Value);
        if (Badges.Has(BadgeId.Aeronautics)) __instance.jetpackFuelDecaySpeed /= 1f + Badges.Buff;
    }

    // --- faerie lantern: the fusion's longer burn ---------------------------------------------

    [HarmonyPostfix, HarmonyPatch(typeof(Lantern), "Update")]
    private static void FaerieBurn(Lantern __instance)
    {
        if (!Crafts.On || !__instance.lit || !__instance.HasAuthority() || __instance.fuel <= 0f || !Items.IsFaerie(__instance.item)) return;
        float m = Mathf.Max(0.1f, Crafts.Twist(CraftId.FaerieLantern));
        // Vanilla burns 1 fuel per second; refund or charge so it lasts m times as long.
        __instance.fuel = Mathf.Max(0.01f, __instance.fuel + Time.deltaTime * (1f - 1f / m));
        __instance.GetData<FloatItemData>(DataEntryKey.Fuel).Value = __instance.fuel;
    }

    /// <summary>
    /// Extra effects after the local player uses a fused item: the cocktail's rush, and the Big
    /// Lollipop's milk - invincible for exactly as long as its infinite stamina lasts.
    /// </summary>
    internal static void AfterConsumed(Item item, Character consumer)
    {
        if (!Crafts.On || consumer == null || !consumer.IsLocal) return;
        if (SugarRush.IsMarked(item))
        {
            SugarRush.Begin(consumer);
            return;
        }
        if (Items.IsLollipop(item) && ModConfig.ItemTrees.Value)
        {
            float t = item.GetComponents<Action_ApplyInfiniteStamina>().Select(a => a.buffTime)
                .Concat(item.GetComponents<Action_ApplyAffliction>().Where(a => a.affliction is Affliction_InfiniteStamina).Select(a => a.affliction.totalTime))
                .DefaultIfEmpty(8f).Max();
            consumer.refs.afflictions.AddAffliction(new Affliction_Invincibility { totalTime = t, isFromMilk = true });
            Plugin.Log.LogInfo($"Big Lollipop: invincible for {t:0.#}s.");
        }
    }
}

/// <summary>
/// Sugar-Rush Cocktail: an Energy Drink marked as the fused item (by its instance guid, shared
/// through room properties so everyone sees it). Drinking it is a full energy drink + sports
/// drink + Big Lollipop (milk shield included); the crash at the end is a healing sleep.
/// </summary>
internal class SugarRush : MonoBehaviour
{
    private const string RoomKey = "prl.sr";
    private static readonly HashSet<Guid> marked = new();
    private static float expectUntil;
    private static HashSet<Guid> expectBefore = new();

    public static bool Active => instance != null;
    private static SugarRush? instance;
    private float endsAt;

    // --- marking -------------------------------------------------------------------------

    public static bool IsMarked(Item? item) =>
        item != null && IsMarked(item.data) && Trees.Classify(item) == TreeId.SportsDrink;

    public static bool IsMarked(ItemInstanceData? data) => data != null && marked.Count > 0 && marked.Contains(data.guid);

    /// <summary>The next new Sports Drink to land in our pockets is the cocktail.</summary>
    public static void ExpectGrant()
    {
        expectUntil = Time.time + 8f;
        expectBefore = PocketDrinks().ToHashSet();
    }

    private static IEnumerable<Guid> PocketDrinks()
    {
        var p = Player.localPlayer;
        if (p == null) yield break;
        foreach (var s in p.itemSlots)
            if (s != null && !s.IsEmpty() && s.prefab != null && s.data != null && Trees.Classify(s.prefab) == TreeId.SportsDrink)
                yield return s.data.guid;
    }

    internal static void PollExpected()
    {
        if (expectUntil <= 0f) return;
        if (Time.time > expectUntil) { expectUntil = 0f; return; }
        foreach (var g in PocketDrinks())
        {
            if (expectBefore.Contains(g) || marked.Contains(g)) continue;
            Mark(g);
            expectUntil = 0f;
            return;
        }
    }

    internal static void Mark(Guid g)
    {
        marked.Add(g);
        if (!PhotonNetwork.InRoom) return;
        try
        {
            string list = string.Join(",", marked.Select(x => x.ToString("N")));
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { [RoomKey] = list });
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Couldn't share the sugar-rush mark: {e.Message}"); }
    }

    internal static void LoadFromRoom()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
        if (!PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(RoomKey, out object raw) || raw is not string s) return;
        foreach (string part in s.Split(','))
            if (Guid.TryParseExact(part, "N", out Guid g)) marked.Add(g);
    }

    // --- the rush ------------------------------------------------------------------------

    /// <summary>
    /// The drink itself gives the energy drink's boost, and Stacking adds the sports drink and the
    /// lollipop's infinite stamina. Here: the milk's shield for the same time, then the crash.
    /// </summary>
    public static void Begin(Character me)
    {
        float t = ModConfig.SugarRushSeconds.Value;
        Relics.GiveInfiniteStamina(me, t);
        me.refs.afflictions.AddAffliction(new Affliction_Invincibility { totalTime = t, isFromMilk = true });
        if (instance == null)
        {
            var go = new GameObject("PEAK Roguelike - Sugar Rush");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SugarRush>();
        }
        instance.endsAt = Time.time + t;
        Hud.Banner("", "SUGAR RUSH", new Color(1f, 0.6f, 0.9f));
    }

    private void Update()
    {
        Character me = Character.localCharacter;
        if (me == null) { Destroy(gameObject); return; }

        // Unlimited jetpack fuel while the rush lasts (the jetpack is worn in the backpack slot).
        var data = me.player?.backpackSlot?.data;
        if (data != null && data.TryGetDataEntry<FloatItemData>(DataEntryKey.Fuel, out var fuel)) fuel.Value = Mathf.Max(fuel.Value, 100f);

        if (Time.time < endsAt) return;
        // The crash: you fall asleep wherever you are (on a wall, you fall) - and wake healed.
        var aff = me.refs.afflictions;
        aff.ClearAllStatus(excludeCurse: true);
        Hud.Toast("Sugar crash...", new Color(0.85f, 0.75f, 1f));
        aff.AddStatus(CharacterAfflictions.STATUSTYPE.Drowsy, 1f);
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}

/// <summary>The cocktail's look: a rainbow-cycling can with a lollipop stuck in the top.</summary>
public class SugarRushLook : MonoBehaviour
{
    /// <summary>Marks the decoration so tints skip it.</summary>
    public class Deco : MonoBehaviour { }

    private readonly ItemTint tint = new();

    private void Start()
    {
        try { AddLollipop(); }
        catch (Exception e) { Plugin.Log.LogWarning($"Sugar-rush lollipop failed: {e.Message}"); }
    }

    private void AddLollipop()
    {
        var lolli = Items.Find("Lollipop");
        if (lolli == null) return;

        // Top of the can, from its biggest mesh (mesh space is stable whatever the pose).
        var canMesh = GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null && !f.GetComponentInParent<Deco>())
            .OrderByDescending(f => f.sharedMesh.bounds.size.sqrMagnitude).FirstOrDefault();
        if (canMesh == null) return;
        Bounds cb = canMesh.sharedMesh.bounds;
        Vector3 top = transform.InverseTransformPoint(canMesh.transform.TransformPoint(cb.center + Vector3.up * cb.extents.y));
        float canHeight = canMesh.transform.TransformVector(Vector3.up * cb.size.y).magnitude / Mathf.Max(0.0001f, transform.lossyScale.y);

        var root = new GameObject("Sugar-Rush Lollipop", typeof(Deco));
        root.transform.SetParent(transform, false);
        root.transform.localPosition = top;
        root.transform.localRotation = Quaternion.Euler(0f, 0f, 18f);

        float lolliHeight = 0f;
        foreach (var mr in lolli.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var go = new GameObject(mr.name, typeof(Deco));
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = lolli.transform.InverseTransformPoint(mr.transform.position);
            go.transform.localRotation = Quaternion.Inverse(lolli.transform.rotation) * mr.transform.rotation;
            go.transform.localScale = Div(mr.transform.lossyScale, lolli.transform.lossyScale);
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
            lolliHeight = Mathf.Max(lolliHeight, mr.bounds.size.y / Mathf.Max(0.0001f, lolli.transform.lossyScale.y));
        }
        // Stick out about two thirds of the can's height.
        float scale = lolliHeight > 0.0001f ? Mathf.Clamp(canHeight * 0.65f / lolliHeight, 0.05f, 2f) : 0.5f;
        root.transform.localScale = Vector3.one * scale;
    }

    private static Vector3 Div(Vector3 a, Vector3 b) =>
        new(a.x / Mathf.Max(0.0001f, b.x), a.y / Mathf.Max(0.0001f, b.y), a.z / Mathf.Max(0.0001f, b.z));

    private void Update()
    {
        Color hue = Color.HSVToRGB(Mathf.Repeat(Time.time * 0.25f, 1f), 0.75f, 1f);
        tint.Apply(gameObject, Color.Lerp(Color.white, hue, 0.85f) * 1.15f);
    }
}

/// <summary>
/// Host: the Sugar-Rush drink is marked before it is picked up, so it can never join a stack of
/// plain Sports Drinks (a fresh item has no instance data, so it is given one here).
/// </summary>
[HarmonyPatch]
internal static class SugarRushGrant
{
    internal const int Marker = -7717;

    [HarmonyPrefix, HarmonyPatch(typeof(GameUtils), "InstantiateAndGrabRPC")]
    private static bool Grant(string itemPrefabName, PhotonView characterView, int cookedAmount)
    {
        if (cookedAmount != Marker || !PhotonNetwork.IsMasterClient) return true;
        try
        {
            var character = characterView.GetComponent<Character>();
            character.refs.items.lastEquippedSlotTime = 0f;
            var hip = character.GetBodypart(BodypartType.Hip);
            var item = PhotonNetwork.InstantiateItemRoom(itemPrefabName, hip.transform.position + hip.transform.forward * 0.5f, Quaternion.identity).GetComponent<Item>();
            if (item.data == null)
            {
                item.data = new ItemInstanceData(Guid.NewGuid());
                ItemInstanceDataHandler.AddInstanceData(item.data);
            }
            SugarRush.Mark(item.data.guid);
            item.Interact(character);
            Plugin.Log.LogInfo("Sugar-Rush: marked before pickup (never stacks).");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Sugar-Rush grant failed: {e.Message}"); }
        return false;
    }
}
