using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PeakRoguelike;

/// <summary>
/// Three copies of a tree item in one pocket level up together: once you're on flat ground a
/// pop-up asks to turn the stack into one item a level higher. "Later" leaves a STACK READY
/// pill (and the stack key) until the pocket changes.
/// </summary>
internal class StackMerge : MonoBehaviour
{
    private static StackMerge? instance;
    private string snoozed = "";
    private float nextScan, flatSince = -1f;
    private Offer? ready;

    /// <summary>A full stack is waiting to level up (the HUD pill).</summary>
    public static bool Ready => instance != null && instance.ready != null;

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Stack Merge");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        instance = go.AddComponent<StackMerge>();
    }

    private static string Key(Offer o) =>
        Player.localPlayer?.GetItemSlot(o.Slot) is { } s && s.data != null ? $"{o.Slot}:{s.data.guid}:{Stacks.Count(s.data)}:{o.Level}" : "";

    private void Update()
    {
        Character me = Character.localCharacter;
        if (!Stacks.On || !ModConfig.Leveling.Value || me == null || me.data.dead || me.data.passedOut) { ready = null; return; }

        bool keyPressed = Keyboard.current != null && Keyboard.current[ModConfig.StackKey.Value].wasPressedThisFrame;
        if (keyPressed || Time.time >= nextScan)
        {
            nextScan = Time.time + 0.5f;
            ready = Offers.ForStack();
        }
        if (ready == null || ChoiceScreen.IsOpen) { flatSince = -1f; return; }

        if (keyPressed) { flatSince = -1f; Show(ready); return; }
        if (Key(ready) == snoozed || !Xp.OnFlatGround(me)) { flatSince = -1f; return; }
        if (flatSince < 0f) flatSince = Time.time;
        if (Time.time - flatSince < 1f) return;
        flatSince = -1f;
        Show(ready);
    }

    private void Show(Offer o)
    {
        var tree = o.Tree!;
        string key = Key(o);
        string name = Player.localPlayer?.GetItemSlot(o.Slot)?.prefab is { } prefab ? Items.DisplayName(prefab) : tree.Name;
        string later = $"Later ({ModConfig.StackKey.Value} to reopen)";
        if (o.Kind == OfferKind.Fork)
        {
            var options = Offers.Paths(tree, o.Level);
            ChoiceScreen.Show(new ScreenSpec
            {
                Title = "LEVEL UP YOUR STACK",
                Subtitle = $"{Stacks.Limit}× {name} → Lv {o.Level} · choose your path",
                Confirm = "LEVEL UP",
                BackHint = later,
                Cards = options.Select(x => CrateCards.ForPath(tree, o.Level, x, o.CrateItem)).ToList(),
                OnConfirm = i => Merge(o, options[i].Branch, Offers.Grant(options[i], o.CrateItem)),
                OnBack = () => snoozed = key,
                OnCancelled = () => snoozed = key,
            });
            return;
        }
        ChoiceScreen.Show(new ScreenSpec
        {
            Title = "LEVEL UP YOUR STACK",
            Subtitle = $"{Stacks.Limit}× {name} → Lv {o.Level}",
            Confirm = "LEVEL UP",
            BackHint = later,
            Cards = new() { CrateCards.ForStack(o) },
            OnConfirm = _ => Merge(o, o.Branch, o.Grant),
            OnBack = () => snoozed = key,
            OnCancelled = () => snoozed = key,
        });
    }

    private void Merge(Offer o, int branch, Item grant)
    {
        if (Character.localCharacter is not { } me) return;
        if (!Offers.ConsumeMerge(o)) { Hud.Toast("The stack is gone", Color.white); return; }
        LuggagePatches.LevelUp(o.Tree!, o.Level, branch);
        Hud.PickChime();
        Xp.ExpectCrateItem(grant);
        GameUtils.instance.InstantiateAndGrab(grant, me);
        ready = null;
        nextScan = Time.time + 1f;
    }
}
