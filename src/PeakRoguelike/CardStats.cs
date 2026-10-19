using System.Collections.Generic;
using System.Linq;
using Peak;
using Peak.Afflictions;
using UnityEngine;

namespace PeakRoguelike;

/// <summary>
/// How much of a status bar, in words a scout reads at a glance: easy fractions (¼ ⅓ ½ ⅔ ¾),
/// "a sliver" for tiny amounts, halves past a full bar. Levelled values are snapped to these
/// (Snap) so a card is exactly true; vanilla values that sit between them say "almost" / "over".
/// </summary>
internal static class Amount
{
    private static readonly float[] Easy = { 0.25f, 1f / 3f, 0.5f, 2f / 3f, 0.75f, 1f };
    /// <summary>Below this a share is "a sliver" (and isn't snapped).</summary>
    public const float Sliver = 0.19f;
    private const float Close = 0.02f;

    /// <summary>A share of a bar snapped to the nearest easy fraction (keeps the sign).</summary>
    public static float Snap(float v)
    {
        float a = Mathf.Abs(v);
        if (a < Sliver) return v;
        float s = a > 1.001f ? Mathf.Round(a * 2f) / 2f : Easy.OrderBy(e => Mathf.Abs(e - a)).First();
        return Mathf.Sign(v) * s;
    }

    /// <summary>Seconds snapped to what a card shows: halves under 10s, whole seconds above.</summary>
    public static float SnapSeconds(float s) => s < 10f ? Mathf.Max(0.5f, Mathf.Round(s * 2f) / 2f) : Mathf.Round(s);

    // --- words ---------------------------------------------------------------------------------

    private static readonly Dictionary<string, bool> hasGlyph = new();

    /// <summary>The fraction glyph if the game's font has it, else "1/3".</summary>
    private static string Glyph(string fancy, string plain)
    {
        if (hasGlyph.TryGetValue(fancy, out bool ok)) return ok ? fancy : plain;
        var font = UiArt.BodyFont;
        if (font == null) return plain; // not in a scene yet - ask again later
        try { ok = fancy.All(c => font.HasCharacter(c, true, true)); }
        catch { ok = false; }
        hasGlyph[fancy] = ok;
        if (!ok) Plugin.Log.LogInfo($"Card text: the game's font has no '{fancy}', writing '{plain}'.");
        return ok ? fancy : plain;
    }

    private static string Frac(float a)
    {
        if (Mathf.Abs(a - 0.25f) < 0.01f) return Glyph("¼", "1/4");
        if (Mathf.Abs(a - 1f / 3f) < 0.01f) return Glyph("⅓", "1/3");
        if (Mathf.Abs(a - 0.5f) < 0.01f) return Glyph("½", "1/2");
        if (Mathf.Abs(a - 2f / 3f) < 0.01f) return Glyph("⅔", "2/3");
        if (Mathf.Abs(a - 0.75f) < 0.01f) return Glyph("¾", "3/4");
        int whole = Mathf.FloorToInt(a + 0.01f);
        return Mathf.Abs(a - whole) < 0.01f ? whole.ToString() : whole + Glyph("½", ".5");
    }

    /// <summary>The nearest easy share, with "almost" / "over" when it isn't exact.</summary>
    private static string Near(float a, out float shown)
    {
        shown = Mathf.Abs(Snap(a));
        if (Mathf.Abs(shown - a) <= Close) return Frac(shown);
        return (a < shown ? "almost " : "over ") + Frac(shown);
    }

    /// <summary>"⅓ of your injury" / "a sliver of your hunger" / "all of your heat".</summary>
    public static string Of(float v, string noun)
    {
        float a = Mathf.Abs(v);
        if (a < Sliver) return $"a sliver of {noun}";
        if (a >= 1f - Close) return $"all of {noun}";
        return $"{Near(a, out _)} of {noun}";
    }

    /// <summary>"⅓ bar of bonus stamina" / "a full bar of ..." / "1½ bars of ...".</summary>
    public static string Bars(float v, string noun)
    {
        float a = Mathf.Abs(v);
        if (a < Sliver) return $"a sliver of {noun}";
        string n = Near(a, out float shown);
        if (Mathf.Abs(shown - 1f) < 0.01f)
            return $"{(n.StartsWith("almost") ? "almost " : n.StartsWith("over") ? "over " : "")}a full bar of {noun}";
        return shown > 1f ? $"{n} bars of {noun}" : $"{n} bar of {noun}";
    }

    public static string Seconds(float s)
    {
        if (s >= 60f && Mathf.Abs(s / 30f - Mathf.Round(s / 30f)) < 0.02f) return $"{Frac(Mathf.Round(s / 30f) / 2f)} min";
        return $"{Frac(SnapSeconds(s))}s";
    }

    public static string Metres(float m) => $"{Mathf.RoundToInt(m)}m";
}

/// <summary>
/// What item cards say: the real numbers of each level in metres, seconds and easy fractions,
/// read off the game's own prefabs and pushed through the same Stats the items use - so a card
/// can't drift from what the item does. Relic cards keep their mystery (not built here).
/// </summary>
internal static class CardStats
{
    /// <summary>A tree level's card text (crate cards, fork cards, statue blessings).</summary>
    public static string Tree(TreeDef tree, LevelDef def, int level, int branch)
    {
        try
        {
            var lines = Lines(tree.Id, def, level, branch);
            if (lines == null || lines.Count == 0) return def.Desc;
            // The level's own tags (written in Trees) ride along with the numbers.
            if (IsEvolution(def)) lines.Insert(0, EvolveLine(def));
            if (def.Desc.Contains("Fusion-ready")) lines.Add("Fusion-ready.");
            if (def.Desc.Contains("forks next")) lines.Add("The path forks next.");
            return string.Join("\n", lines);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"Card stats for {def.Name} failed: {e.Message}");
            return def.Desc;
        }
    }

    /// <summary>This level hands you a different item (Trees mark it "Evolves!").</summary>
    public static bool IsEvolution(LevelDef def) => def.Desc.StartsWith("Evolves!");

    /// <summary>"Evolves into a new item: Granola Bar" (the card's own title, so a medkit reads as its path).</summary>
    public static string EvolveLine(LevelDef def) => $"Evolves into a new item:\n{def.Name}";

    private static List<string>? Lines(TreeId id, LevelDef def, int lv, int br)
    {
        if (def.Grants == null || Items.Find(def.Grants) is not { } p) return null;
        br = def.Branch != 0 ? def.Branch : br;
        var l = new List<string>();
        switch (id)
        {
            case TreeId.Rope:
                if (p.GetComponent<RopeSpool>() is { } spool)
                {
                    float spacing = TreePatches.RopeSpacing(spool);
                    float? segs = Stats.RopeSegments(spool.isAntiRope, lv, br, spool.ropeStartFuel, spacing);
                    string what = spool.isAntiRope ? "floating rope" : "rope";
                    if (segs is { } s) l.Add($"{Amount.Metres(Stats.Metres(s * spacing))} of {what}");
                    else
                    {
                        l.Add($"{Amount.Metres(Stats.Metres(spool.ropeStartFuel * spacing))} of {what}");
                        l.Add($"Up to {Amount.Metres(Stats.Metres(Mathf.Min(spool.ropeStartFuel, Rope.MaxSegments) * spacing))} per rope.");
                    }
                    if (!spool.isAntiRope)
                        l.Add(Stats.RopeSnapSeconds(lv) is { } snap ? $"Snaps after {Amount.Seconds(snap)} of climbing." : "Holds for good.");
                    else if (lv < 4) l.Add("Floats upward.");
                }
                else if (p.GetComponent<RopeShooter>() is { } gun)
                {
                    float m = Stats.Metres(gun.maxLength * Stats.ShooterScale(p, lv, br, gun.maxLength));
                    bool anti = Items.IsAntiRopeShooter(p);
                    l.Add($"Fires a {Amount.Metres(m)} {(anti ? "floating" : "anchored")} rope.");
                    if (anti) l.Add("Still fuses as a cannon.");
                }
                break;

            case TreeId.Piton:
                l.Add(Stats.PitonSeconds(lv) is { } hold ? $"Holds you {Amount.Seconds(hold)}, then cracks." : "Holds for good.");
                break;

            case TreeId.Glider:
                if (p.GetComponentInChildren<Glider>(true) is { } g)
                {
                    float secs = Stats.GlideSeconds(lv, g.stamUse, g.extraOpeningCost);
                    l.Add($"Glides {Amount.Seconds(secs)} on a full stamina bar.");
                    l.Add(Lift(Stats.GliderLift(lv)) + " lift.");
                }
                break;

            case TreeId.Balloons:
            {
                bool bunch = Items.IsBalloonBunch(p);
                float fly = Stats.BalloonFlight(bunch, lv);
                string what = bunch ? "A whole bunch" : Stats.BalloonTies(lv) > 1 ? "Two balloons" : "One balloon";
                if (fly <= 0f) l.Add($"{what}: slows your fall for {Amount.Seconds(VanillaBalloonSeconds)}.");
                else l.Add($"{what}: flies you up to {Amount.Metres(Stats.Metres(Stats.BalloonRiseSpeed * fly))} over {Amount.Seconds(fly)}.");
                if (ModConfig.BalloonPopJump.Value > 0f) l.Add("Jump to pop one.");
                break;
            }

            case TreeId.Bandage:
                if (Items.IsMedKit(p)) Medkit(l, lv, br);
                else
                {
                    float scale = Stats.Potency(p, lv, br).status;
                    var heals = p.GetComponents<Action_ModifyStatus>().Where(a => a.changeAmount < 0f && !a.ifSkeleton)
                        .Select(a => (a.statusType, amount: Stats.SnapStatus(a.changeAmount, scale))).ToList();
                    foreach (var grp in heals.GroupBy(h => Mathf.Round(h.amount * 100f)))
                        l.Add($"Heals {Amount.Of(grp.First().amount, "your " + string.Join(" and ", grp.Select(h => StatusName(h.statusType))))}.");
                    if (heals.Count > 0) l.Add("Eases every ailment as much.");
                }
                float use = Stats.UseSeconds(p, lv, br, p.usingTimePrimary);
                if (use > 0.1f) l.Add($"{Amount.Seconds(use)} to apply.");
                break;

            case TreeId.SportsDrink:
                if (Items.IsEnergyDrink(p))
                {
                    var (_, time, crash) = Stats.Potency(p, lv, br);
                    var boost = Boosts(p).ToList();
                    if (boost.Count > 0)
                    {
                        float t = boost.Max(b => b.totalTime);
                        float d = boost.Max(b => b.drowsyOnEnd);
                        l.Add($"Speed boost for {Amount.Seconds(Stats.SnapTime(t, time))}.");
                        if (d > 0f) l.Add($"Then {Amount.Bars(Stats.SnapCrash(d, crash), "drowsiness")}.");
                    }
                    l.Add("+ a full Sports Drink.");
                }
                else
                {
                    float scale = Stats.Potency(p, lv, br).status;
                    float stam = p.GetComponents<Action_GiveExtraStamina>().Sum(a => a.amount);
                    if (stam > 0f) l.Add($"+{Amount.Bars(stam * Stats.StaminaFactor(p, scale), "bonus stamina")}.");
                    foreach (var a in p.GetComponents<Action_ModifyStatus>().Where(a => a.changeAmount < 0f && !a.ifSkeleton))
                        l.Add($"{Relieves(a.statusType)} {Amount.Of(Stats.SnapStatus(a.changeAmount, scale), "your " + StatusName(a.statusType))}.");
                }
                break;

            case TreeId.Food:
            {
                // Each snack also gives every snack below it (Stacking).
                var chain = new List<Item>();
                for (int i = 1; i <= lv; i++)
                    if (Trees.Get(TreeId.Food)?.At(i, 0) is { Grants: { } role } && Items.Find(role) is { } it) chain.Add(it);
                float hunger = chain.Sum(i => i.GetComponents<Action_RestoreHunger>().Sum(a => a.restorationAmount));
                float stam = chain.Sum(i => i.GetComponents<Action_GiveExtraStamina>().Sum(a => a.amount));
                if (hunger > 0f) l.Add($"Fills {Amount.Of(hunger, "your hunger")}.");
                if (stam > 0f) l.Add($"+{Amount.Bars(stam, "bonus stamina")}.");
                if (lv > 1) l.Add("Includes every snack before it.");
                break;
            }

            case TreeId.Light:
                if (p.GetComponent<Lantern>() is { } lantern)
                {
                    l.Add($"Burns {Amount.Seconds(Stats.LanternSeconds(lv, lantern.startingFuel))}.");
                    if (p.GetComponent<Peak.GloomSafeZone>() is { statusProtectionRadius: > 0f } glow)
                        l.Add($"Glows {Amount.Metres(Stats.Metres(glow.statusProtectionRadius * Stats.GlowScale(lv, glow.statusProtectionRadius)))} around you.");
                }
                break;
        }
        return l;
    }

    private static string Lift(float share) => share < 0.5f ? "Weak" : share < 0.95f ? "Steady" : "Full";

    private static IEnumerable<Affliction_FasterBoi> Boosts(Item p) =>
        p.GetComponents<Action_ApplyAffliction>().Select(a => a.affliction).OfType<Affliction_FasterBoi>();

    /// <summary>Holy packs heal now; dark medkits turn injury and glass into ailments that fade.</summary>
    private static void Medkit(List<string> l, int lv, int br)
    {
        int step = Stats.KitStep(lv, br);
        float share = step <= 1 ? Stats.HolyFirstStep : 1f;
        if (br == 2)
        {
            if (step >= 3)
            {
                l.Add("Turns all of your injury into drowsiness.");
                l.Add("Shatters your glass.");
            }
            else l.Add($"Turns {Amount.Of(share, "your injury and glass")} into poison.");
            return;
        }
        l.Add($"Heals {Amount.Of(share, "your injury")}.");
        l.Add($"Cures {Amount.Of(share, "every ailment")}.");
        if (step >= 3)
        {
            l.Add($"{Amount.Seconds(ModConfig.HolyShieldSeconds.Value)} of invincibility.");
            l.Add("A scout who passes out finds one beside them.");
        }
    }

    private static string StatusName(CharacterAfflictions.STATUSTYPE s) => s switch
    {
        CharacterAfflictions.STATUSTYPE.Hot => "heat",
        CharacterAfflictions.STATUSTYPE.Drowsy => "drowsiness",
        _ => s.ToString().ToLowerInvariant(),
    };

    private static string Relieves(CharacterAfflictions.STATUSTYPE s) => s switch
    {
        CharacterAfflictions.STATUSTYPE.Hot => "Cools",
        CharacterAfflictions.STATUSTYPE.Cold => "Warms",
        CharacterAfflictions.STATUSTYPE.Drowsy => "Clears",
        CharacterAfflictions.STATUSTYPE.Hunger => "Fills",
        _ => "Heals",
    };

    /// <summary>A tied balloon's vanilla float time (it's spawned from the TiedBalloon prefab).</summary>
    private static float VanillaBalloonSeconds
    {
        get
        {
            if (balloonSeconds > 0f) return balloonSeconds;
            try
            {
                var go = Resources.Load<GameObject>("TiedBalloon");
                balloonSeconds = go != null && go.GetComponent<TiedBalloon>() is { } t ? t.popTime : 10f;
            }
            catch { balloonSeconds = 10f; }
            return balloonSeconds;
        }
    }
    private static float balloonSeconds;

    // --- fusions -------------------------------------------------------------------------------

    /// <summary>A fusion card's text in real numbers (falls back to the recipe's own line).</summary>
    public static string Fusion(Recipe r)
    {
        try { return FusionText(r) ?? r.Desc; }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"Card stats for {r.Name} failed: {e.Message}");
            return r.Desc;
        }
    }

    private static string? FusionText(Recipe r)
    {
        Item? p = r.Result != null ? Items.Find(r.Result) : null;
        switch (r.Id)
        {
            case "jetpack":
            {
                if (p?.GetComponent<JetpackItem>() is not { } jet) return null;
                float secs = jet.startingFuel / Mathf.Max(0.1f, CraftPatches.VanillaJetpackDecay) * Crafts.Twist(CraftId.Jetpack);
                return $"{Amount.Seconds(secs)} of thrust per tank.";
            }
            case "lollipop":
            {
                if (p == null) return null;
                var rush = p.GetComponents<Action_ApplyAffliction>().Select(a => a.affliction).OfType<Affliction_InfiniteStamina>().ToList();
                float t = rush.Select(a => a.totalTime).DefaultIfEmpty(8f).Max();
                float sleepy = rush.Select(a => a.drowsyAffliction?.totalTime ?? 0f).DefaultIfEmpty(0f).Max();
                return $"Infinite stamina and invincible\nfor {Amount.Seconds(t)}."
                    + (sleepy > 0f ? $"\nThen {Amount.Seconds(sleepy)} of drowsiness." : "\nThen drowsy.");
            }
            case "sugarrush":
            {
                var drink = Items.Find("EnergyDrink");
                float boost = drink != null ? Boosts(drink).Select(b => b.totalTime).DefaultIfEmpty(0f).Max() : 0f;
                float t = ModConfig.SugarRushSeconds.Value;
                return (boost > 0f ? $"Speed boost for {Amount.Seconds(boost)}.\n" : "")
                    + $"Infinite stamina and invincible for {Amount.Seconds(t)}.\nThen you pass out - and wake healed.";
            }
            case "faerie":
            {
                if (p?.GetComponent<Lantern>() is not { } lantern) return null;
                float m = Crafts.Twist(CraftId.FaerieLantern);
                float burn = lantern.startingFuel * m;
                return p.GetComponent<StatusField>() is { } field
                    ? $"Heals and warms scouts within {Amount.Metres(Stats.Metres(field.radius * m))}.\nBurns {Amount.Seconds(burn)}."
                    : $"Heals and warms scouts around you.\nBurns {Amount.Seconds(burn)}.";
            }
            case "chain":
            {
                int uses = ModConfig.ChainLauncherUses.Value;
                return p?.GetComponent<VineShooter>() is { } vine
                    ? $"Fires a {Amount.Metres(Stats.Metres(vine.maxLength))} climbable chain.\n{uses} shots."
                    : $"Fires a climbable chain.\n{uses} shots.";
            }
            case "rescue":
            {
                int uses = ModConfig.RescueClawUses.Value;
                return p?.GetComponent<RescueHook>() is { } hook
                    ? $"Grabs a scout or a wall\nup to {Amount.Metres(Stats.Metres(hook.range))} away.\n{uses} uses."
                    : $"Grabs a scout or a wall and pulls.\n{uses} uses.";
            }
            case "antigrav":
            {
                int uses = ModConfig.AntiGravUses.Value;
                return p?.GetComponent<Action_RaycastSpawnSomething>() is { } ray
                    ? $"Fires a low-gravity bubble\nup to {Amount.Metres(Stats.Metres(ray.maxDistance))} away.\n{uses} shots."
                    : $"Fires a low-gravity bubble.\n{uses} shots.";
            }
            default:
                return null;
        }
    }
}
