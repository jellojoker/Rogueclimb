using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PeakRoguelike;

/// <summary>
/// Trade-ups: a full stack (3) of a tree item and a 4th copy in your pockets trade for one item two
/// levels higher. The offer pops up once you stand on the ground (a crate offers it as a card instead);
/// "Keep them" leaves it be until your pockets change.
/// (XP level-ups raise the whole stack instead.)
/// </summary>
internal class StackMerge : MonoBehaviour
{
    private static StackMerge? instance;
    private string snoozed = "";
    private float nextScan, flatSince = -1f;
    private Offer? ready;

    /// <summary>A full stack is waiting to level up (the HUD pill).</summary>
    public static bool Ready => false; // no pill: the trade is offered when the 4th copy arrives, "Keep them" lets it be

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Stack Merge");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        instance = go.AddComponent<StackMerge>();
    }

    /// <summary>A crate already asked about this item's trade (the scout took the plain 4th copy).</summary>
    private static ushort declinedItem;
    private static float declinedAt = -99f;

    /// <summary>Crates: the plain copy was picked over the trade - the pop-up won't ask again for it.</summary>
    internal static void Declined(ushort itemID)
    {
        declinedItem = itemID;
        declinedAt = Time.time;
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
        // The crate's plain copy was the answer: snooze this stack the way "Keep them" would (once it has arrived).
        if (ready.Carried == declinedItem && Time.time - declinedAt < 10f) { snoozed = Key(ready); declinedItem = 0; }
        if (Key(ready) == snoozed) { flatSince = -1f; return; }
        // Standing on the ground: ask now. Climbing / falling: wait for a second on flat ground.
        bool standing = me.data.isGrounded && !me.data.isClimbingAnything;
        if (!standing && !Xp.OnFlatGround(me)) { flatSince = -1f; return; }
        if (flatSince < 0f) flatSince = Time.time;
        if (!standing && Time.time - flatSince < 1f) return;
        flatSince = -1f;
        Show(ready);
    }

    private void Show(Offer o)
    {
        var tree = o.Tree!;
        string key = Key(o);
        string name = Player.localPlayer?.GetItemSlot(o.Slot)?.prefab is { } prefab ? Items.DisplayName(prefab) : tree.Name;
        if (o.Kind == OfferKind.Fork)
        {
            var options = Offers.Paths(tree, o.Level);
            ChoiceScreen.Show(new ScreenSpec
            {
                Title = "TRADE UP YOUR STACK",
                TitleColor = ChoiceScreen.TradeSilver,
                Subtitle = $"{Stacks.Limit + 1}× {name} → Lv {o.Level} · choose your path",
                Confirm = "TRADE UP",
                Cards = options.Select(x => Trade(CrateCards.ForPath(tree, o.Level, x, o.CrateItem))).ToList(),
                OnConfirm = i => Merge(o, options[i].Branch, Offers.Grant(options[i], o.CrateItem)),
                OnBack = () => Save(key, name),
                OnSkip = () => snoozed = key,
                SkipFree = true, // your stack stays as it is
                OnCancelled = () => snoozed = key,
            });
            return;
        }
        ChoiceScreen.Show(new ScreenSpec
        {
            Title = "TRADE UP YOUR STACK",
            TitleColor = ChoiceScreen.TradeSilver,
            Subtitle = $"{Stacks.Limit + 1}× {name} → Lv {o.Level}",
            Confirm = "TRADE UP",
            Cards = new() { Trade(CrateCards.ForStack(o)) },
            OnConfirm = _ => Merge(o, o.Branch, o.Grant),
            OnBack = () => Save(key, name),
            OnSkip = () => snoozed = key,
            SkipFree = true,
            OnCancelled = () => snoozed = key,
        });
    }

    private static CardSpec Trade(CardSpec card) { card.TradeUp = true; return card; }

    /// <summary>Saved for later: it stops asking, and the relic key (or the stack key) brings it back while the stack is unchanged.</summary>
    private void Save(string key, string name)
    {
        snoozed = key;
        SavedPicks.Save($"stack:{key}", $"{name} trade-up", () => { if (Offers.ForStack() is { } o) Show(o); },
            valid: () => Offers.ForStack() is { } o && Key(o) == key);
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
