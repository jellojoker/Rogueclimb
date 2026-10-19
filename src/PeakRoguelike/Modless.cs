using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PeakRoguelike;

/// <summary>
/// Scouts without Rogue Climb in the lobby. They're told about (every modded scout gets a toast
/// naming the Thunderstore package), left out of crate drafts, relic sharing and zombie spawns, and
/// their vanilla crate opens are ignored - otherwise their crates would spill loot for everyone.
/// A scout counts as modded once their build player property shows up.
/// </summary>
[HarmonyPatch]
internal class Modless : MonoBehaviourPunCallbacks
{
    public const string Package = "TheJelloJokers-Rogue_Climb";

    private static readonly Dictionary<int, float> firstSeen = new();
    private static readonly HashSet<int> warned = new();
    private float nextLook;

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Modless");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<Modless>();
    }

    internal static bool HasMod(Photon.Realtime.Player? p) =>
        p == null || p.IsLocal || !PhotonNetwork.InRoom || p.CustomProperties.ContainsKey(DevPanel.BuildKey);

    internal static bool HasMod(Character? c) =>
        c == null || c.photonView == null || HasMod(c.photonView.Owner);

    private void Update()
    {
        if (Time.time < nextLook || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
        nextLook = Time.time + 2f;
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (!firstSeen.ContainsKey(p.ActorNumber)) firstSeen[p.ActorNumber] = Time.time;
            // Give their properties time to arrive before calling them out.
            if (HasMod(p) || warned.Contains(p.ActorNumber) || Time.time - firstSeen[p.ActorNumber] < 15f) continue;
            warned.Add(p.ActorNumber);
            string who = string.IsNullOrEmpty(p.NickName) ? "A scout" : p.NickName;
            Plugin.Log.LogWarning($"{who} (actor {p.ActorNumber}) doesn't have Rogue Climb.");
            // The host gets a card to close; everyone else the toast.
            if (PhotonNetwork.IsMasterClient) ModlessPopup.Add(who);
            else Hud.Toast($"{who} doesn't have Rogue Climb\n{Package} on Thunderstore", ChoiceScreen.Muted);
        }
    }

    public override void OnJoinedRoom() { firstSeen.Clear(); warned.Clear(); }
    public override void OnLeftRoom() { firstSeen.Clear(); warned.Clear(); }

    /// <summary>
    /// A vanilla crate open: modded scouts never send it for a crate with loot (they draft instead),
    /// so it came from someone without the mod - nothing spawns, and the crate stays shut for us.
    /// </summary>
    [HarmonyPrefix, HarmonyPatch(typeof(Luggage), "OpenLuggageRPC")]
    private static bool IgnoreVanillaOpen(Luggage __instance, bool spawnItems)
    {
        if (!spawnItems || __instance is RespawnChest || LuggagePatches.OpeningVanilla || __instance.GetComponent<PerkLuggage>() == null) return true;
        if ((__instance.GetSpawnSpots()?.Count ?? 0) == 0) return true;
        Plugin.Log.LogInfo($"{__instance.displayName}: ignored a vanilla open (a scout without the mod).");
        return false;
    }
}

/// <summary>
/// The host's card when a scout joins without Rogue Climb: who, the package name, OK. Scouts
/// who turn up while it's open join its list. A different version of the mod doesn't count (F8
/// marks that as a MISMATCH).
/// </summary>
internal class ModlessPopup : MonoBehaviour
{
    private static ModlessPopup? instance;
    internal static bool IsOpen => instance != null;

    private readonly List<string> names = new();
    private TextMeshProUGUI body = null!;
    private CanvasGroup group = null!;
    private bool closing;

    internal static void Add(string who)
    {
        if (instance == null)
        {
            var go = new GameObject("PEAK Roguelike - Modless Popup");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<ModlessPopup>();
            instance.Build();
        }
        instance.names.Add(who);
        instance.Refresh();
    }

    internal static void Close()
    {
        if (instance != null) instance.closing = true;
    }

    private void Build()
    {
        if (EventSystem.current == null)
        {
            var es = new GameObject("EventSystem (PEAK Roguelike)");
            es.AddComponent<EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 950;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        var root = (RectTransform)transform;

        var backdrop = Ui.Image(root, "Backdrop", null, new Color(0.03f, 0.04f, 0.06f, 0.55f));
        Ui.Stretch(backdrop.rectTransform);
        backdrop.raycastTarget = true;

        var panel = Ui.Image(root, "Panel", null, new Color(ChoiceScreen.InkBrown.r, ChoiceScreen.InkBrown.g, ChoiceScreen.InkBrown.b, 0.94f));
        var pr = panel.rectTransform;
        pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
        pr.sizeDelta = new Vector2(820, 400);

        var title = Ui.Text(pr, "Title", "NOT COMPATIBLE", UiArt.TitleFont, 52, ChoiceScreen.Cream, TextAlignmentOptions.Center);
        title.rectTransform.anchoredPosition = new Vector2(0, 140);
        title.rectTransform.sizeDelta = new Vector2(780, 70);
        title.characterSpacing = 4;

        body = Ui.Text(pr, "Body", "", UiArt.BodyFont, 26, ChoiceScreen.Cream, TextAlignmentOptions.Center);
        body.rectTransform.anchoredPosition = new Vector2(0, 10);
        body.rectTransform.sizeDelta = new Vector2(760, 180);
        body.enableAutoSizing = true;
        body.fontSizeMin = 16;
        body.fontSizeMax = 26;

        var ok = MenuButtonView.Create(pr, "OK", UiArt.Embedded("button_confirm"), 1.1f, "OK", Close);
        ok.Rect.anchoredPosition = new Vector2(0, -140);
    }

    private void Refresh()
    {
        string who = string.Join("\n", names.Select(n => $"<b><noparse>{n}</noparse></b> isn't running Rogue Climb"));
        body.text = $"{who}\n\n<size=80%><color=#D9C9A3>{Modless.Package} on Thunderstore</color></size>";
        closing = false;
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) closing = true;
        group.alpha = Mathf.MoveTowards(group.alpha, closing ? 0f : 1f, Time.unscaledDeltaTime / 0.2f);
        if (closing && group.alpha <= 0f) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
