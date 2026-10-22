using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Peak;
using Peak.Afflictions;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;
using Zorro.Core;

namespace PeakRoguelike;

public enum RelicGem { Tenacity, Initiative, Generosity, Ambition }
public enum RelicTrigger { Food, Healing, Movement }

/// <summary>Cursed relics (the statue amulets) raise blood moons and count toward their tier; Normal ones never touch the moon.</summary>
public enum RelicClass { Cursed, Normal }

internal static class RelicTriggerNames
{
    /// <summary>What the cards call the class (Movement shows as Tools: ropes, pitons and the rest count).</summary>
    public static string Label(this RelicTrigger t) => t == RelicTrigger.Movement ? "Tools" : t.ToString();
}

public class RelicDef
{
    public RelicGem Gem;
    public RelicTrigger Trigger;
    public string Name = "";
    public string Desc = "";
}

/// <summary>
/// Statue amulets become permanent relics: picking one up offers three variants (food /
/// healing / movement trigger). Each trigger fires the real amulet's own effect, and turns you
/// a little more to glass. Generosity is the exception: a crate pick of its kind earns a second pick.
/// Names and colours follow the game: red Initiative (team super jump), yellow Ambition
/// (infinite-stamina bubble), blue Tenacity (heal + invincibility), green Generosity (clone).
/// </summary>
internal class Relics : MonoBehaviour
{
    public static readonly List<RelicDef> All = new()
    {
        R(RelicGem.Tenacity, RelicTrigger.Food, "Gourmet Shield", "Eating any food heals you and\nmakes you invincible."),
        R(RelicGem.Tenacity, RelicTrigger.Healing, "Medic Shield", "Using any healing item heals you\nmore and makes you invincible."),
        R(RelicGem.Tenacity, RelicTrigger.Movement, "Kinetic Shield", "Using a tool heals you\nand makes you invincible."),
        R(RelicGem.Initiative, RelicTrigger.Food, "Sugar Launch", "Eating any food launches you and\nnearby scouts into a super jump."),
        R(RelicGem.Initiative, RelicTrigger.Healing, "Purge Rocket", "Using any healing item launches\nyou and nearby scouts skyward."),
        R(RelicGem.Initiative, RelicTrigger.Movement, "Overdrive Leap", "Using a tool launches\nyou and nearby scouts skyward."),
        R(RelicGem.Generosity, RelicTrigger.Food, "Second Helping", "Take food from a crate\nand you may take another card."),
        R(RelicGem.Generosity, RelicTrigger.Healing, "Spare Supplies", "Take a healing item from a crate\nand you may take another card."),
        R(RelicGem.Generosity, RelicTrigger.Movement, "Twin Gear", "Take a tool from a crate\nand you may take another card."),
        R(RelicGem.Ambition, RelicTrigger.Food, "Glucose Engine", "Eating any food radiates\ninfinite stamina around you."),
        R(RelicGem.Ambition, RelicTrigger.Healing, "Adrenaline Rush", "Using any healing item radiates\ninfinite stamina around you."),
        R(RelicGem.Ambition, RelicTrigger.Movement, "Momentum Flow", "Using a tool radiates\ninfinite stamina around you."),
    };

    static RelicDef R(RelicGem g, RelicTrigger t, string n, string d) => new() { Gem = g, Trigger = t, Name = n, Desc = d };

    /// <summary>Every statue relic is cursed. Normal relics (the crash-site curios, later) will say so here.</summary>
    public static RelicClass ClassOf(RelicGem g) => RelicClass.Cursed;

    public static bool Cursed(RelicGem g) => ClassOf(g) == RelicClass.Cursed;

    public static RelicDef Get(RelicGem g, RelicTrigger t) => All.First(r => r.Gem == g && r.Trigger == t);

    public static Color GemColor(RelicGem g) => g switch
    {
        RelicGem.Generosity => new Color32(0x6F, 0xE0, 0x7C, 0xFF),
        RelicGem.Initiative => new Color32(0xFF, 0x63, 0x45, 0xFF),
        RelicGem.Tenacity => new Color32(0x74, 0xB4, 0xFF, 0xFF),
        _ => new Color32(0xFF, 0xD2, 0x4A, 0xFF),
    };

    /// <summary>The in-game amulet behind a relic (its icon and its effect).</summary>
    public static Item? Amulet(RelicGem g) => Items.Database.FirstOrDefault(i => Items.IsAmulet(i) && Items.AmuletGem(i) == g);

    public static string GemName(RelicGem g) => $"Relic of {g}";

    private static Relics? instance;
    /// <summary>Amulets you chose to keep (by instance). Forgotten once the amulet leaves your pockets.</summary>
    private readonly HashSet<System.Guid> declined = new();
    private readonly HashSet<System.Guid> warnedTaken = new();
    /// <summary>When each of my relics last fired on each scout (by actor number): the cooldown is per scout.</summary>
    private readonly Dictionary<(RelicGem, int), float> lastTrigger = new();
    private float nextScan;
    private bool wasDead;

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Relics");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        instance = go.AddComponent<Relics>();
        // Not GlobalEvents.OnItemConsumed: the game wipes that delegate at the start of every run.
        // RelicPatches.ItemConsumed hooks the trigger itself instead.
    }

    // --- amulet pickup -> relic choice ------------------------------------------------------

    private void Update()
    {
        if (!ModConfig.Relics.Value) return;
        Character me = Character.localCharacter;
        Player p = Player.localPlayer;
        if (me == null || p == null) { wasDead = false; offerFlatSince = -1f; return; }
        // The Void: no relics at all - nothing binds, nothing waits to be offered.
        if (VoidRun.Active)
        {
            laterShared.Clear();
            randomOffers.Clear();
            while (sharedOffers.Count > 0) { sharedOffers.Dequeue(); Net.SharedDone(); }
            offerFlatSince = -1f;
            return;
        }

        // The relic key reopens a shared relic left for later - anywhere, any time.
        laterShared.RemoveAll(g => RunState.Relics.ContainsKey(g) && !RunState.IsCursed(g));
        if (laterShared.Count > 0 && !ChoiceScreen.IsOpen && !me.data.dead && !me.data.passedOut
            && Keyboard.current != null && Keyboard.current[ModConfig.RelicKey.Value].wasPressedThisFrame)
        {
            var gem = laterShared[0];
            laterShared.RemoveAt(0);
            OfferShared(gem, later: true);
            return;
        }

        if (Time.time < nextScan) return;
        nextScan = Time.time + 0.5f;

        if (me.data.dead && !wasDead) { deathAt = Time.time; DropOnDeath(me); }
        // Raised by the Book of Bones after dying to a dark rite (your own skull, or someone's dagger):
        // the bones bring back a gift.
        if (!me.data.dead && wasDead && me.data.isSkeleton && Mathf.Abs(riteDeathAt - deathAt) < 6f)
        {
            Plugin.Log.LogInfo("Raised as bones after a dark rite - a random relic.");
            QueueRandom("RAISED FROM THE BONES", "The rite remembers you");
            riteDeathAt = -999f;
        }
        wasDead = me.data.dead;
        // Testing: the dead don't choose shared relics - let the host's moon go on without them.
        if (me.data.dead) while (sharedOffers.Count > 0) { sharedOffers.Dequeue(); Net.SharedDone(); }
        if (ChoiceScreen.IsOpen || me.data.dead || me.data.passedOut) { offerFlatSince = -1f; return; }
        // Others' relics never pop up mid-climb: they wait for 1s of flat ground.
        if (sharedOffers.Count > 0 || randomOffers.Count > 0)
        {
            if (!Xp.OnFlatGround(me)) offerFlatSince = -1f;
            else if (offerFlatSince < 0f) offerFlatSince = Time.time;
            else if (Time.time - offerFlatSince >= 1f)
            {
                offerFlatSince = -1f;
                if (sharedOffers.Count > 0) OfferShared(sharedOffers.Dequeue());
                else OfferRandom(randomOffers.Dequeue());
                return;
            }
        }
        else offerFlatSince = -1f;

        // With all three pockets full, a picked-up item rides in your hand (tempFullSlot) - an
        // amulet there never offered its relic until you juggled it into a pocket.
        var amulets = Slots(p)
            .Where(s => s != null && !s.IsEmpty() && s.prefab != null && Items.IsAmulet(s.prefab) && Items.AmuletGem(s.prefab) != null)
            .ToList();
        // Drop an amulet and pick it up again, and it asks again.
        var inPockets = amulets.Select(s => s.data?.guid ?? System.Guid.Empty).ToList();
        declined.IntersectWith(inPockets);
        warnedTaken.IntersectWith(inPockets);
        Explain(p);

        foreach (ItemSlot slot in amulets)
        {
            RelicGem gem = Items.AmuletGem(slot.prefab)!.Value;
            var guid = slot.data?.guid ?? System.Guid.Empty;
            if (RunState.Relics.ContainsKey(gem) && !RunState.IsCursed(gem))
            {
                // Already have this relic: it stays a normal amulet. (A cursed copy can be traded up for the real one.)
                warnedTaken.Add(guid);
                continue;
            }
            // A fallen scout's relic: you get exactly that one, no choice.
            if (RelicDrops.TryGet(gem, out var dropped))
            {
                Bind(slot.itemSlotID, slot.prefab.itemID, Get(gem, dropped), recovered: true);
                return;
            }
            if (declined.Contains(guid)) continue;
            // One of each colour per scout: someone else holding it doesn't stop you.
            Offer(slot, gem, guid);
            return;
        }
    }

    private readonly HashSet<System.Guid> logged = new();

    /// <summary>Log every amulet-like item that lands in your pockets, and what the relic scan makes of it.</summary>
    private static IEnumerable<ItemSlot> Slots(Player p) =>
        p.tempFullSlot != null ? p.itemSlots.Append(p.tempFullSlot) : p.itemSlots;

    private void Explain(Player p)
    {
        foreach (var s in Slots(p))
        {
            if (s == null || s.IsEmpty() || s.prefab == null || !s.prefab.itemTags.HasFlag(Item.ItemTags.ScoutAmulet)) continue;
            var guid = s.data?.guid ?? System.Guid.Empty;
            if (!logged.Add(guid)) continue;
            var gem = Items.AmuletGem(s.prefab);
            string verdict = !Items.IsAmulet(s.prefab) ? "not a relic amulet"
                : RunState.Relics.ContainsKey(gem!.Value) && !RunState.IsCursed(gem.Value) ? "relic already held"
                : RelicDrops.TryGet(gem.Value, out _) ? "recovering a dropped relic"
                : declined.Contains(guid) ? "declined earlier"
                : "offering the relic choice";
            Plugin.Log.LogInfo($"Amulet in pockets: {s.prefab.gameObject.name} -> {(gem?.ToString() ?? "no gem")}: {verdict}.");
        }
    }

    /// <summary>Dying drops every relic you hold as its amulet, right where you fell. Cursed copies (and dagger stacks) shatter.</summary>
    private static void DropOnDeath(Character me)
    {
        foreach (var gem in RunState.Relics.Keys.Where(RunState.IsCursed).ToList())
        {
            RunState.RemoveRelic(gem);
            Plugin.Log.LogInfo($"Cursed {gem} relic shattered.");
        }
        if (!ModConfig.RelicDrops.Value || RunState.Relics.Count == 0) return;
        // The host lays the amulets where we last stood on solid ground (not off a ledge or mid-air).
        var gems = new List<int>();
        foreach (var kv in RunState.Relics.ToList())
        {
            gems.Add((int)kv.Key);
            RelicDrops.Add(kv.Key, kv.Value);
            RunState.RemoveRelic(kv.Key);
            Plugin.Log.LogInfo($"Dropped relic {Get(kv.Key, kv.Value).Name} on death.");
        }
        if (gems.Count > 0) Net.DeathDrop(gems.ToArray());
    }

    private void Offer(ItemSlot slot, RelicGem gem, System.Guid guid)
    {
        var options = All.Where(r => r.Gem == gem).ToList();
        byte slotId = slot.itemSlotID;
        ushort amuletId = slot.prefab.itemID;
        ChoiceScreen.Show(new ScreenSpec
        {
            Title = "CHOOSE YOUR RELIC",
            Subtitle = GemName(gem),
            Warning = ModConfig.BloodMoon.Value && ModConfig.BmWarning.Value ? "The moon remembers who takes it." : "",
            Confirm = "BIND",
            BackHint = "Keep the amulet",
            Cards = options.Select(o => new CardSpec
            {
                Style = CardStyle.Relic,
                Gem = gem,
                Title = o.Name,
                Heading = GemName(gem),
                Body = o.Desc,
                Tag = o.Trigger.Label(),
                Icon = Items.Icon(slot.prefab),
            }).ToList(),
            StillValid = () => Player.localPlayer != null && !slot.IsEmpty() && slot.prefab != null && slot.prefab.itemID == amuletId,
            OnConfirm = i => Bind(slotId, amuletId, options[i], recovered: false),
            OnBack = () =>
            {
                declined.Add(guid);
            },
            // Dropped it, got knocked out...: not a choice. It asks again next scan.
            OnCancelled = () => { },
        });
    }

    // --- testing: shared relic pickups ------------------------------------------------------

    private static readonly Queue<RelicGem> sharedOffers = new();
    /// <summary>Shared relics closed with "Later": the relic key brings them back.</summary>
    private static readonly List<RelicGem> laterShared = new();
    private static float offerFlatSince = -1f;

    /// <summary>A shared relic is waiting for the relic key (the HUD pill), and its colour.</summary>
    internal static RelicGem? Waiting => laterShared.Count > 0 ? laterShared[0] : null;

    internal static void ResetOffers()
    {
        sharedOffers.Clear();
        laterShared.Clear();
        randomOffers.Clear();
        offerFlatSince = -1f;
    }

    /// <summary>Host (testing option): another scout bound a relic - everyone else gets that colour's choice.</summary>
    internal static void ReceiveSharedOffer(RelicGem gem, int binderActor)
    {
        if (!ModConfig.Relics.Value || PhotonNetwork.LocalPlayer == null || PhotonNetwork.LocalPlayer.ActorNumber == binderActor) return;
        sharedOffers.Enqueue(gem);
    }

    // --- random relics from the dark rites: one card, a colour you don't carry, a cursed copy ---

    private static float deathAt = -999f, riteDeathAt = -999f;
    private static readonly Queue<(RelicDef relic, string title, string subtitle)> randomOffers = new();

    /// <summary>This scout is about to die to a dark rite (their own skull, or the dagger fed to them).</summary>
    internal static void MarkRiteDeath() => riteDeathAt = Time.time;

    /// <summary>Queue a random relic for the local scout: any colour they don't carry yet. Nothing if they carry all four.</summary>
    internal static void QueueRandom(string title, string subtitle)
    {
        if (!ModConfig.Relics.Value) return;
        var pool = All.Where(r => !RunState.Relics.ContainsKey(r.Gem) && !randomOffers.Any(o => o.relic.Gem == r.Gem)).ToList();
        if (pool.Count == 0) { Plugin.Log.LogInfo($"{title}: no colour left to give."); return; }
        var relic = pool[Random.Range(0, pool.Count)];
        randomOffers.Enqueue((relic, title, subtitle));
        Plugin.Log.LogInfo($"{title}: random relic {relic.Name} queued.");
    }

    private void OfferRandom((RelicDef relic, string title, string subtitle) offer)
    {
        var relic = offer.relic;
        // Picked up that colour since: roll again.
        if (RunState.Relics.ContainsKey(relic.Gem)) { QueueRandom(offer.title, offer.subtitle); return; }
        var amulet = Amulet(relic.Gem);
        ChoiceScreen.Show(new ScreenSpec
        {
            Title = offer.title,
            Subtitle = offer.subtitle,
            Warning = ModConfig.BloodMoon.Value && ModConfig.BmWarning.Value ? "The moon remembers who takes it." : "",
            Confirm = "BIND",
            BackHint = "Not yet",
            Cards = new List<CardSpec>
            {
                new CardSpec
                {
                    Style = CardStyle.Relic,
                    Gem = relic.Gem,
                    Title = relic.Name,
                    Heading = "Cursed copy",
                    Cursed = true,
                    Body = relic.Desc + "\nShatters if you die.",
                    Tag = relic.Trigger.Label(),
                    Icon = amulet != null ? Items.Icon(amulet) : null,
                },
            },
            StillValid = () => Player.localPlayer != null,
            OnConfirm = _ =>
            {
                if (RunState.Relics.ContainsKey(relic.Gem)) { QueueRandom(offer.title, offer.subtitle); return; }
                RunState.SetRelic(relic.Gem, relic.Trigger, isCursed: true);
                Hud.Banner(GemName(relic.Gem).ToUpperInvariant(), relic.Name, GemColor(relic.Gem));
                Plugin.Log.LogInfo($"{offer.title}: bound cursed {relic.Name}.");
            },
            // There's no refusing it: it asks again.
            OnBack = () => randomOffers.Enqueue(offer),
            OnCancelled = () => randomOffers.Enqueue(offer),
        });
    }

    /// <param name="later">Reopened with the relic key: the host's moon already counted this scout as answered.</param>
    private void OfferShared(RelicGem gem, bool later = false)
    {
        // Already carrying this colour (a cursed copy can still be traded up).
        if (RunState.Relics.ContainsKey(gem) && !RunState.IsCursed(gem)) { if (!later) Net.SharedDone(); return; }
        var options = All.Where(r => r.Gem == gem).ToList();
        var amulet = Amulet(gem);
        ChoiceScreen.Show(new ScreenSpec
        {
            Title = "CHOOSE YOUR RELIC",
            Subtitle = GemName(gem),
            Warning = ModConfig.BloodMoon.Value && ModConfig.BmWarning.Value ? "The moon remembers who takes it." : "",
            Confirm = "BIND",
            BackHint = $"Later ({ModConfig.RelicKey.Value} to reopen)",
            Cards = options.Select(o => new CardSpec
            {
                Style = CardStyle.Relic,
                Gem = gem,
                Title = o.Name,
                Heading = GemName(gem),
                Body = o.Desc,
                Tag = o.Trigger.Label(),
                Icon = amulet != null ? Items.Icon(amulet) : null,
            }).ToList(),
            StillValid = () => Player.localPlayer != null,
            OnConfirm = i =>
            {
                var relic = options[i];
                RunState.SetRelic(relic.Gem, relic.Trigger, isShared: true);
                Hud.Banner(GemName(relic.Gem).ToUpperInvariant(), relic.Name, GemColor(relic.Gem));
                Plugin.Log.LogInfo($"Bound shared relic {relic.Name}{(later ? " (reopened)" : "")}.");
                if (!later) Net.SharedDone();
            },
            // "Later": the moon goes on without waiting; the relic key brings the choice back.
            OnBack = () =>
            {
                if (!laterShared.Contains(gem)) laterShared.Add(gem);
                Plugin.Log.LogInfo($"Shared relic {GemName(gem)} left for later.");
                if (!later) Net.SharedDone();
            },
            // Knocked out, screen closed by the game...: not a choice. It asks again on flat ground.
            OnCancelled = () => { if (later) laterShared.Insert(0, gem); else sharedOffers.Enqueue(gem); },
        });
    }

    private static void Bind(byte slotId, ushort amuletId, RelicDef relic, bool recovered)
    {
        Character me = Character.localCharacter;
        Player p = Player.localPlayer;
        if (me == null || p == null) return;
        var slot = p.GetItemSlot(slotId);
        if (slot == null || slot.IsEmpty() || slot.prefab.itemID != amuletId) return;

        bool held = me.refs.items.currentSelectedSlot.IsSome && me.refs.items.currentSelectedSlot.Value == slotId;
        p.EmptySlot(Optionable<byte>.Some(slotId));
        if (held) me.refs.items.EquipSlot(Optionable<byte>.None);

        RunState.SetRelic(relic.Gem, relic.Trigger);
        if (recovered)
        {
            RelicDrops.Take(relic.Gem);
            Hud.Banner("RELIC RECOVERED", relic.Name, GemColor(relic.Gem));
            Plugin.Log.LogInfo($"Recovered dropped relic {relic.Name}.");
            return;
        }
        Hud.Banner(GemName(relic.Gem).ToUpperInvariant(), relic.Name, GemColor(relic.Gem));
        Plugin.Log.LogInfo($"Bound relic {relic.Name}.");
        // The price of power: a blood moon rises (or restarts at the new tier). Without blood
        // moons it's the old minute of the Scoutmaster hunting the whole team.
        if (Cursed(relic.Gem)) Net.RelicTaken(ModConfig.HuntSeconds.Value, me.characterName ?? "A scout", relic.Gem);
    }

    // --- triggers -----------------------------------------------------------------------

    internal static void OnConsumed(Item item, Character character)
    {
        if (character == null || !character.IsLocal || item == null) return;
        // The item's extra effects count as the item (the suns cap them like the item itself).
        SunCaps.Push(item);
        try
        {
            try { TreePatches.AfterConsumed(item, character); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Tree consume effect failed: {e.Message}"); }
            try { CraftPatches.AfterConsumed(item, character); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Craft consume effect failed: {e.Message}"); }
            try { Stacking.Apply(item, character); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Stacked effects failed: {e.Message}"); }
        }
        finally { SunCaps.Pop(); }

        Stove.Eaten(item, character);
        Cookies.Eaten(item, character);
        DarkRite(item);

        try { Xp.Used(item, character); }
        catch (System.Exception e) { Plugin.Log.LogWarning($"XP use hook failed: {e.Message}"); }

        FireConsumeRelics(item, "Consumed");
    }

    private static float lastRite = -999f;

    /// <summary>
    /// The local scout used the Cursed Skull or the Ritual Dagger: unlocks the dark rites, and
    /// the host raises a blood moon at once. One use can reach here more than once (cast + consume).
    /// </summary>
    internal static void DarkRite(Item item)
    {
        bool skull = Items.IsSkull(item), dagger = Items.IsDagger(item);
        if (!skull && !dagger) return;
        if (skull) { RunState.UsedSkull = true; MarkRiteDeath(); } // the skull takes its user
        if (dagger) RunState.UsedDagger = true;
        if (Time.time - lastRite < 5f) return;
        lastRite = Time.time;
        string who = Character.localCharacter?.characterName ?? "A scout";
        // The skull's sacrifice: everyone else gets a cursed copy of each of your real relics,
        // and a random relic on top (sent even when you carry none).
        if (skull && ModConfig.CursedCopies.Value && ModConfig.Relics.Value)
        {
            string list = string.Join(";", RunState.Relics.Where(kv => !RunState.IsCursed(kv.Key)).Select(kv => $"{kv.Key}:{kv.Value}"));
            Net.SkullCopies(who, list);
        }
        Net.DarkRite(who);
    }

    // --- cursed copies ------------------------------------------------------------------------

    /// <summary>Someone's skull: take a cursed copy of each of their relics you don't hold yet.</summary>
    internal static void ReceiveSkullCopies(string who, string list)
    {
        Character me = Character.localCharacter;
        if (!ModConfig.Relics.Value || me == null || me.data.dead) return;
        int got = 0;
        foreach (string part in list.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            var bits = part.Split(':');
            if (bits.Length < 2 || !System.Enum.TryParse(bits[0], out RelicGem g) || !System.Enum.TryParse(bits[1], out RelicTrigger t)) continue;
            if (RunState.Relics.ContainsKey(g)) continue;
            RunState.SetRelic(g, t, isCursed: true);
            got++;
            Plugin.Log.LogInfo($"{who}'s skull: cursed copy of {Get(g, t).Name}.");
        }
        if (got > 0) Hud.Banner(who.ToUpperInvariant() + "'S SACRIFICE", got == 1 ? "A CURSED RELIC" : "CURSED RELICS", new Color(0.55f, 0.35f, 0.75f));
        QueueRandom(who.ToUpperInvariant() + "'S SACRIFICE", "The skull gives");
    }

    /// <summary>
    /// The dagger's kill: copy one relic from your victim (a cursed copy) or stack one of your
    /// own (it recharges faster). Stacks and copies shatter when you die.
    /// </summary>
    internal static void DaggerKill(Character victim)
    {
        if (!ModConfig.Relics.Value || !ModConfig.CursedCopies.Value || victim == null || ChoiceScreen.IsOpen) return;
        var options = new List<(RelicGem gem, RelicTrigger trigger, bool stack)>();
        if (victim.photonView?.Owner is { } owner)
            foreach (var r in RunState.RelicsOf(owner).Where(r => !r.cursed))
                options.Add((r.gem, r.trigger, RunState.Relics.ContainsKey(r.gem)));
        foreach (var kv in RunState.Relics.Where(kv => !RunState.IsCursed(kv.Key)))
            if (!options.Any(o => o.gem == kv.Key)) options.Add((kv.Key, kv.Value, true));
        string name = victim.characterName ?? "your victim";
        // Nothing to take, nothing to feed: the dagger gives one of its own.
        if (options.Count == 0) { QueueRandom("THE DAGGER DRINKS", $"{name} carried nothing"); return; }
        ChoiceScreen.Show(new ScreenSpec
        {
            Title = "THE DAGGER DRINKS",
            Subtitle = $"Take from {name}, or feed your own",
            Confirm = "TAKE",
            BackHint = "Leave it",
            Cards = options.Select(o => new CardSpec
            {
                Style = CardStyle.Relic,
                Gem = o.gem,
                Title = Get(o.gem, o.trigger).Name,
                Heading = o.stack ? "Stack it" : "Cursed copy",
                Cursed = !o.stack,
                Body = o.stack ? $"Your {GemName(o.gem)} grows:\nit recharges faster." : $"A cursed copy of {name}'s\nrelic. Shatters if you die.",
                Tag = o.trigger.Label(),
                Icon = Items.Icon(Amulet(o.gem)),
            }).ToList(),
            OnConfirm = i =>
            {
                var o = options[i];
                if (o.stack && RunState.Relics.ContainsKey(o.gem)) RunState.AddStack(o.gem);
                else RunState.SetRelic(o.gem, o.trigger, isCursed: true);
                Plugin.Log.LogInfo($"Dagger: {(o.stack ? "stacked" : "copied")} {o.gem} (stacks {RunState.Stacks(o.gem)}).");
            },
            OnBack = () => { },
        });
    }

    /// <summary>
    /// One bite / sip of a multi-use item (a box of cookies has 4). The game only reports the
    /// last one as "consumed", so the bites before it fire the relics here.
    /// </summary>
    internal static void OnUsedOnce(Item item)
    {
        if (item == null) return;
        FireConsumeRelics(item, "Used");
    }

    private static void FireConsumeRelics(Item item, string verb)
    {
        // Something can be both (a berry that also cures poison): both kinds of relic fire.
        bool food = IsFood(item), healing = IsHealing(item);
        // Fed (or bandaged) by another scout: our own relics stay quiet - the feeder's fire on both of us.
        var feeder = item.trueHolderCharacter;
        if (feeder != null && feeder != item.holderCharacter && !feeder.IsLocal)
        {
            Plugin.Log.LogInfo($"{verb} {item.gameObject.name}, fed by {feeder.characterName}: food={food} healing={healing}");
            if ((food || healing) && feeder.photonView?.Owner is { } owner) Net.RelicFed(owner.ActorNumber, food, healing);
            return;
        }
        Plugin.Log.LogInfo($"{verb} {item.gameObject.name}: food={food} healing={healing}");
        if (food) Trigger(RelicTrigger.Food, item);
        if (healing) Trigger(RelicTrigger.Healing, item);
    }

    /// <summary>We fed or healed another scout: our food / healing relics fire on both of us.</summary>
    internal static void ReceiveFed(int fedActor, bool food, bool healing)
    {
        var fed = ScoutOf(fedActor);
        if (fed == null) return;
        if (food) Fire(RelicTrigger.Food, fed);
        if (healing) Fire(RelicTrigger.Healing, fed);
    }

    /// <summary>Someone let go of a tool we placed: our movement relics fire on them.</summary>
    internal static void ReceiveToolUsed(int userActor)
    {
        var user = ScoutOf(userActor);
        if (user != null) Fire(RelicTrigger.Movement, user, onMe: false);
    }

    internal static Character? ScoutOf(int actor) =>
        Character.AllCharacters.FirstOrDefault(c => c != null && !c.isBot && c.photonView?.Owner?.ActorNumber == actor);

    /// <summary>Another scout's relic fired on us: its effect, and its glass.</summary>
    internal static void ReceiveShare(int ownerActor, RelicGem gem)
    {
        Character me = Character.localCharacter;
        if (!ModConfig.Relics.Value || me == null || me.data.dead) return;
        SunCaps.RelicFiring++;
        try { Apply(me, gem); }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Shared relic {gem} failed: {e.Message}"); return; }
        finally { SunCaps.RelicFiring--; }
        me.refs.afflictions.AddPetrify(ModConfig.GlassPerTrigger.Value);
        if (BloodMoon.Active) Net.Veil(ModConfig.RelicVeil.Value);
        Plugin.Log.LogInfo($"{ScoutOf(ownerActor)?.characterName ?? "A scout"}'s {GemName(gem)} fired on us.");
    }

    /// <summary>Lanterns count as healing (they thaw frost); the Faerie Lantern fires every kind of relic.</summary>
    internal static bool IsLantern(Item item) => item.GetComponent<Lantern>() != null;

    internal static bool IsHealing(Item item) =>
        item.GetComponents<Action_ModifyStatus>().Any(a => a.changeAmount < 0f && a.statusType is CharacterAfflictions.STATUSTYPE.Injury or CharacterAfflictions.STATUSTYPE.Poison)
        || item.GetComponent<Action_ClearAllStatus>()
        || item.GetComponents<Action_ApplyAffliction>().Any(a => a.affliction is Affliction_HealAll or Affliction_AdjustStatusOverTime)
        || Trees.Classify(item) == TreeId.Bandage
        || IsLantern(item)
        || IsRemedy(item);

    /// <summary>Remedy Fungus: thrown, it bursts into a healing cloud (the game builds it on the Shelf Fungus's parts).</summary>
    internal static bool IsRemedy(Item item) => item.gameObject.name.StartsWith("HealingPuffShroom");

    /// <summary>
    /// Food = anything you eat or drink that isn't healing: real food, drinks (sports / energy
    /// drinks) and any other consumable that would otherwise belong to no relic class.
    /// </summary>
    internal static bool IsFood(Item item)
    {
        if (Items.IsSunscreen(item)) return false; // you wear it, you don't eat it
        if (item.GetComponent<Action_RestoreHunger>()
            || item.GetComponent<Action_RandomMushroomEffect>()
            || item.GetComponents<Action_ModifyStatus>().Any(a => a.changeAmount < 0f && a.statusType == CharacterAfflictions.STATUSTYPE.Hunger))
            return true;
        if (Trees.Classify(item) is TreeId.SportsDrink or TreeId.Food) return true;
        return IsConsumable(item) && !IsHealing(item) && !IsMovementItem(item) && !Items.IsSkull(item) && !Items.IsDagger(item);
    }

    /// <summary>Gets used up when eaten / drunk (one go, or the last of several uses).</summary>
    internal static bool IsConsumable(Item item) =>
        item.GetComponent<Action_Consume>() != null
        || item.GetComponents<Action_ReduceUses>().Any(a => a.consumeOnFullyUsed);

    internal static bool IsMovementItem(Item item)
    {
        if (IsRemedy(item)) return false;
        var tree = Trees.Classify(item);
        // The Tools class: anti-ropes, the Chain Launcher and Rescue Claw are tools too, tree or not.
        return tree is TreeId.Rope or TreeId.Piton or TreeId.Glider or TreeId.Balloons
            || item.GetComponent<RopeSpool>() || item.GetComponent<RopeShooter>() || item.GetComponent<VineShooter>()
            || item.GetComponent<RescueHook>() || item.GetComponent<CloudFungus>()
            || item.GetComponent<MagicBean>() || item.GetComponent<ShelfShroom>() || item.GetComponent<JetpackItem>()
            || item.GetComponent<Action_LaunchPlayer>() || item.GetComponent<Parasol>() || item.GetComponent<Antigrav>();
    }

    internal static void Trigger(RelicTrigger trigger) => Fire(trigger, null);

    /// <summary>What set the relics off: an item and/or a tool you got off ("rope", "glide"...) - badges spare some of them the glass.</summary>
    private static Item? firingItem;
    private static string firingKind = "";

    internal static void Trigger(RelicTrigger trigger, Item? item, string kind = "")
    {
        firingItem = item;
        firingKind = kind;
        try { Fire(trigger, null); }
        finally { firingItem = null; firingKind = ""; }
    }

    /// <summary>
    /// Fire my relics of this kind: on me, and/or shared onto another scout (they get the
    /// effect and the glass on their side). I pay the glass whenever one of mine goes off.
    /// The cooldown runs per scout, so one relic can lift a whole team in turn.
    /// </summary>
    internal static void Fire(RelicTrigger trigger, Character? other, bool onMe = true)
    {
        if (!ModConfig.Relics.Value || instance == null) return;
        Character me = Character.localCharacter;
        if (me == null || me.data.dead) return;
        int myActor = PhotonNetwork.LocalPlayer?.ActorNumber ?? -1;
        int otherActor = other != null && other != me && other.photonView?.Owner is { } o ? o.ActorNumber : 0;

        foreach (var kv in RunState.Relics.ToList())
        {
            if (kv.Value != trigger) continue;
            RelicGem gem = kv.Key;
            if (gem == RelicGem.Generosity) continue; // Generosity works at the crate (Relics.Generosity)
            float cooldown = Mathf.Max(0.5f, ModConfig.RelicCooldown.Value * Mathf.Pow(Mathf.Clamp(ModConfig.DaggerStackCooldown.Value, 0.1f, 1f), RunState.Stacks(gem)));
            bool fired = false;

            if (onMe && Ready(gem, myActor, cooldown))
            {
                // A relic always beats its sun: nothing it does is capped.
                SunCaps.RelicFiring++;
                try { Apply(me, gem); fired = true; }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Relic {gem} failed: {e.Message}"); }
                finally { SunCaps.RelicFiring--; }
                // Under a blood moon, a relic going off hides you from the hunters for a moment.
                if (fired && BloodMoon.Active) Net.Veil(ModConfig.RelicVeil.Value);
            }
            if (otherActor != 0 && Ready(gem, otherActor, cooldown))
            {
                Net.RelicShare(otherActor, gem);
                fired = true;
                Plugin.Log.LogInfo($"Our {GemName(gem)} fired on {other!.characterName}.");
            }
            if (fired) Glass(me, gem);
        }
    }

    private static bool Ready(RelicGem gem, int actor, float cooldown)
    {
        var key = (gem, actor);
        if (instance!.lastTrigger.TryGetValue(key, out float last) && Time.time - last < cooldown) return false;
        instance.lastTrigger[key] = Time.time;
        return true;
    }

    private static void Glass(Character me, RelicGem gem)
    {
        int glass = Badges.GlassFor(ModConfig.GlassPerTrigger.Value, firingItem, firingKind);
        if (glass > 0) me.refs.afflictions.AddPetrify(glass);
        Hud.Pulse(gem);
    }

    /// <summary>Fire the real amulet's effect (copied from the amulet prefab, so it matches the game exactly).</summary>
    private static void Apply(Character me, RelicGem gem)
    {
        var aff = me.refs.afflictions;
        Item? amulet = Amulet(gem);
        switch (gem)
        {
            case RelicGem.Tenacity:
                float injury = aff.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Injury), hot = aff.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Hot);
                float shield = 5f;
                if (amulet?.GetComponent<Action_HealingGem>() is { } heal)
                {
                    aff.AddAffliction(Copy(heal.healingAffliction));
                    aff.AddAffliction(Copy(heal.invincibilityAffliction));
                    shield = heal.invincibilityAffliction.totalTime;
                }
                else aff.AddAffliction(new Affliction_Invincibility { totalTime = 5f, isFromAmulet = true });
                Plugin.Log.LogInfo($"Tenacity fired: injury {injury:0.00}->{aff.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Injury):0.00}, heat {hot:0.00}->{aff.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Hot):0.00}, invincible {shield:0.#}s (now {me.data.isInvincible}), sun {(Suns.Current is { } s ? Suns.ColorName(s) : "none")}.");
                break;

            case RelicGem.Initiative:
                if (amulet?.GetComponent<Action_SuperJumpAmulet>() is { affliction: not null } jump) aff.AddAffliction(Copy(jump.affliction));
                else me.AddForce(Vector3.up * 400f);
                break;

            case RelicGem.Ambition:
                var radiate = amulet?.GetComponents<Action_ApplyAffliction>().FirstOrDefault(a => a.affliction is Affliction_RadiateInfiniteStam);
                if (radiate != null) aff.AddAffliction(Copy(radiate.affliction!));
                else GiveInfiniteStamina(me, 8f);
                break;
        }
    }

    private static readonly System.Reflection.MethodInfo Clone =
        typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

    /// <summary>A fresh copy of a prefab's affliction (never hand out the prefab's own instance).</summary>
    internal static T Copy<T>(T affliction) where T : Affliction
    {
        var copy = (T)Clone.Invoke(affliction, null);
        copy.timeElapsed = 0f;
        return copy;
    }

    /// <summary>
    /// Generosity: a crate pick of the bound kind (food / healing / movement) earns a second pick
    /// from that crate. Returns true when the second pick should be offered.
    /// </summary>
    internal static bool Generosity(Item granted)
    {
        if (!ModConfig.Relics.Value || !RunState.Relics.TryGetValue(RelicGem.Generosity, out var trigger)) return false;
        bool matches = trigger switch
        {
            RelicTrigger.Food => IsFood(granted),
            RelicTrigger.Healing => IsHealing(granted),
            _ => IsMovementItem(granted),
        };
        Character me = Character.localCharacter;
        if (!matches || me == null) return false;
        Glass(me, RelicGem.Generosity);
        return true;
    }

    /// <summary>Infinite stamina that safely stacks with an existing sugar rush.</summary>
    internal static void GiveInfiniteStamina(Character me, float seconds)
    {
        var aff = me.refs.afflictions;
        if (aff.HasAfflictionType(Affliction.AfflictionType.InfiniteStamina, out Affliction existing))
        {
            float left = existing.totalTime - existing.timeElapsed;
            if (left < seconds) { existing.timeElapsed = 0f; existing.totalTime = seconds; }
        }
        else aff.AddAffliction(new Affliction_InfiniteStamina(seconds));
    }
}

/// <summary>
/// Relics that fell with a dead scout, shared in a room property ("Gem:Trigger;...").
/// There's one of each colour per team, so the next amulet of that colour picked up is it.
/// </summary>
internal static class RelicDrops
{
    private const string RoomKey = "prl.drop";

    private static Dictionary<RelicGem, RelicTrigger> Read()
    {
        var d = new Dictionary<RelicGem, RelicTrigger>();
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return d;
        if (!PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(RoomKey, out object raw) || raw is not string s) return d;
        foreach (string part in s.Split(';'))
        {
            var bits = part.Split(':');
            if (bits.Length == 2 && System.Enum.TryParse(bits[0], out RelicGem g) && System.Enum.TryParse(bits[1], out RelicTrigger t)) d[g] = t;
        }
        return d;
    }

    private static void Write(Dictionary<RelicGem, RelicTrigger> d)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
        try
        {
            PhotonNetwork.CurrentRoom.SetCustomProperties(new ExitGames.Client.Photon.Hashtable
            {
                [RoomKey] = string.Join(";", d.Select(kv => $"{kv.Key}:{kv.Value}")),
            });
        }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't share dropped relics: {e.Message}"); }
    }

    public static bool TryGet(RelicGem gem, out RelicTrigger trigger) => Read().TryGetValue(gem, out trigger);

    public static void Add(RelicGem gem, RelicTrigger trigger)
    {
        var d = Read();
        d[gem] = trigger;
        Write(d);
    }

    public static void Take(RelicGem gem)
    {
        var d = Read();
        if (d.Remove(gem)) Write(d);
    }

    /// <summary>New run: the host wipes the list.</summary>
    public static void Clear()
    {
        if (PhotonNetwork.IsMasterClient && Read().Count > 0) Write(new Dictionary<RelicGem, RelicTrigger>());
    }
}

[HarmonyPatch]
internal static class RelicPatches
{
    [HarmonyPostfix, HarmonyPatch(typeof(GlobalEvents), nameof(GlobalEvents.TriggerItemConsumed))]
    private static void ItemConsumed(Item item, Character character)
    {
        try { Relics.OnConsumed(item, character); }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Consume hook failed: {e.Message}"); }
    }

    // Movement-tool triggers ------------------------------------------------------------------

    [HarmonyPostfix, HarmonyPatch(typeof(Item), "FinishCastPrimary")]
    private static void CastFinished(Item __instance)
    {
        var holder = __instance.holderCharacter;
        if (holder == null || !holder.IsLocal) return;
        Relics.DarkRite(__instance);
        // Tools you ride (ropes, pitons, beanstalks, gliders, balloons, jetpacks) fire when you
        // get off them (ToolRelics); lanterns when lit (below); the rest when the use completes.
        if (Relics.IsMovementItem(__instance) && !ToolRelics.FiresOnDismount(__instance)) Relics.Trigger(RelicTrigger.Movement, __instance);
        // Leave No Trace: every item you use costs glass.
        if (Badges.Has(BadgeId.LeaveNoTrace)) holder.refs.afflictions.AddPetrify(ModConfig.GlassPerTrigger.Value);
    }

    // Thrown fungi: they fire their thrower's relics when they sprout, not when they leave the hand
    // (a soft drop that doesn't sprout costs nothing). Break runs on the item's owner - the thrower.

    private static readonly AccessTools.FieldRef<ShelfShroom, bool> ShelfBroke = AccessTools.FieldRefAccess<ShelfShroom, bool>("alreadyBroke");
    private static readonly AccessTools.FieldRef<CloudFungus, bool> CloudBroke = AccessTools.FieldRefAccess<CloudFungus, bool>("alreadyBroke");

    /// <summary>Shelf Fungus grows a ledge (tool relics); Remedy Fungus bursts into its healing cloud (healing relics).</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(ShelfShroom), nameof(ShelfShroom.Break))]
    private static void ShelfSprouts(ShelfShroom __instance)
    {
        try
        {
            if (ShelfBroke(__instance)) return;
            var item = __instance.GetComponent<Item>();
            Sprouted(item, item != null && Relics.IsRemedy(item) ? RelicTrigger.Healing : RelicTrigger.Movement);
        }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Shelf fungus relic hook failed: {e.Message}"); }
    }

    /// <summary>Cloud fungus pops into its cloud: tool relics.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(CloudFungus), nameof(CloudFungus.Break))]
    private static void CloudSprouts(CloudFungus __instance)
    {
        try
        {
            if (!CloudBroke(__instance)) Sprouted(__instance.GetComponent<Item>(), RelicTrigger.Movement);
        }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Cloud fungus relic hook failed: {e.Message}"); }
    }

    private static void Sprouted(Item? item, RelicTrigger trigger)
    {
        // Ours if we threw it lately (a fungus someone picked up and set down again counts for nobody).
        if (item == null || item.lastThrownCharacter is not { IsLocal: true } || Time.time - item.lastThrownTime > 30f) return;
        Plugin.Log.LogInfo($"Our thrown {item.gameObject.name} sprouted: {trigger} relics.");
        Relics.Trigger(trigger, item);
    }

    /// <summary>Lighting a lantern (not snuffing it) fires healing relics; a Faerie Lantern fires food, healing and tool relics.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(Action_LightLantern), nameof(Action_LightLantern.RunAction))]
    private static void LanternUsed(Action_LightLantern __instance)
    {
        try
        {
            var lantern = __instance.GetComponent<Lantern>();
            var item = __instance.GetComponent<Item>();
            var holder = item?.holderCharacter;
            if (lantern == null || holder == null || !holder.IsLocal) return;
            if (lantern.GetData<BoolItemData>(DataEntryKey.FlareActive).Value) return; // about to snuff
            Relics.Trigger(RelicTrigger.Healing, item);
            if (!Items.IsFaerie(item!)) return;
            Relics.Trigger(RelicTrigger.Food, item);
            Relics.Trigger(RelicTrigger.Movement, item);
        }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Lantern relic hook failed: {e.Message}"); }
    }

    /// <summary>Every bite of a multi-use item except the last (that one reaches ItemConsumed).</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(Action_ReduceUses), nameof(Action_ReduceUses.RunAction))]
    private static void UsedOnce(Action_ReduceUses __instance)
    {
        try
        {
            var item = __instance.GetComponent<Item>();
            var holder = item?.holderCharacter;
            if (item == null || holder == null || !holder.IsLocal) return;
            var uses = item.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
            if (__instance.consumeOnFullyUsed && uses.HasData && uses.Value == 0) return;
            Stove.Eaten(item, holder);
            Cookies.Eaten(item, holder);
            Relics.OnUsedOnce(item);
        }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Multi-use relic hook failed: {e.Message}"); }
    }

    /// <summary>The dagger takes a life: its wielder may copy or stack a relic.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(RitualDaggerFeedBehavior), nameof(RitualDaggerFeedBehavior.FeederAction))]
    private static void DaggerKilled(Character feeder, Character fedCharacter)
    {
        try { if (feeder != null && feeder.IsLocal) Relics.DaggerKill(fedCharacter); }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Dagger relic choice failed: {e.Message}"); }
    }

    /// <summary>
    /// The dagger's blessing reaches every client. Its victim remembers dying to a rite (for the Book
    /// of Bones); everyone else gets the vanilla heal at full strength - no sun cuts it.
    /// </summary>
    [HarmonyPrefix, HarmonyPatch(typeof(RitualDaggerFeedBehavior), "RPC_RitualDaggerBuff")]
    private static void DaggerBuffBegin(int excludeID)
    {
        var me = Character.localCharacter;
        if (me != null && me.photonView != null && me.photonView.ViewID == excludeID) Relics.MarkRiteDeath();
        SunCaps.RelicFiring++;
    }

    [HarmonyFinalizer, HarmonyPatch(typeof(RitualDaggerFeedBehavior), "RPC_RitualDaggerBuff")]
    private static System.Exception? DaggerBuffEnd(System.Exception? __exception)
    {
        SunCaps.RelicFiring = Mathf.Max(0, SunCaps.RelicFiring - 1);
        return __exception;
    }

    /// <summary>The dagger is used on someone else (secondary use) - still counts as "used" for the dark rites.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(Item), nameof(Item.FinishCastSecondary))]
    private static void SecondaryFinished(Item __instance)
    {
        var holder = __instance.trueHolderCharacter;
        if (holder == null || !holder.IsLocal) return;
        Relics.DarkRite(__instance);
    }

}
