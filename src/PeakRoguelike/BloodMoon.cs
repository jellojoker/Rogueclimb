using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Zorro.Core;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace PeakRoguelike;

/// <summary>
/// Blood moons: binding a relic (not recovering a dropped one) raises one at once - a blood
/// eclipse by day, a blood moon by night - and nothing else does until the next relic (or a
/// dark rite); nightfall alone stays calm. Each relic held adds a hazard; all of them rise with
/// the moon and last until it sets (60 / 80 / 100 / 120s by tier). Which hazard each tier adds
/// follows a shuffled order (see <see cref="RelicShuffle.MoonOrder"/>) of:
///   zombies beside every scout (more beside relic / skull / dagger carriers),
///   the Scoutmaster, a tornado (sand on Mesa, snow on Alpine),
///   a meteor shower (purple glass on Gloom runs, fire on Caldera runs).
/// Binding another relic mid-moon restarts it at the new tier. In daylight it's a blood eclipse
/// (red sun and sky, the clock isn't touched). The host publishes "tier|start time|binder" as a room
/// property and the order beside it; everyone draws the sky from that, so late joiners see it too.
/// </summary>
internal class BloodMoon : MonoBehaviour
{
    internal const string RoomKey = "prl.bm";
    private const string OrderKey = "prl.bmo";

    internal enum Hazard { Zombies, Scoutmaster, Tornado, Meteors }
    /// <summary>The order before the shuffle (and for hosts on older builds).</summary>
    internal static readonly Hazard[] DefaultOrder = { Hazard.Zombies, Hazard.Scoutmaster, Hazard.Tornado, Hazard.Meteors };

    // Every wave the tier unlocks rises with the moon and lasts until it sets. The moon's length
    // still grows with the tier: WaveLength at tier 1, WaveGap more per tier (60 / 80 / 100 / 120s).
    private static float Gap => Mathf.Max(0f, ModConfig.BmWaveGap.Value);
    internal static float WaveFor => Mathf.Max(5f, ModConfig.BmWaveLength.Value);
    /// <summary>No nightfall moon this soon after the last one ended.</summary>
    internal const float SafetySeconds = 30f;

    internal static readonly Color Blood = new(1f, 0.16f, 0.12f);
    /// <summary>The orange of the blood moon lettering.</summary>
    internal static readonly Color Ember = new(1f, 0.47f, 0.08f);

    private static BloodMoon? instance;

    // --- shared state (everyone, from the room property) --------------------------------------
    internal static bool Active { get; private set; }
    internal static int Tier { get; private set; }
    /// <summary>This moon's hazards in the order the tiers add them.</summary>
    internal static Hazard[] Order { get; private set; } = DefaultOrder;
    /// <summary>A hazard's own tier this moon: 1 on the moon that first brings it, 0 or less = not here.</summary>
    internal static int TierOf(Hazard h)
    {
        int i = System.Array.IndexOf(Order, h);
        return i < 0 ? 0 : Tier - i;
    }
    /// <summary>Counted from his first tier: 1 = hunter, 2 = watcher (stare charge), 3+ = he only watches.</summary>
    internal static int ScoutmasterTier => TierOf(Hazard.Scoutmaster);
    internal static string OrderLabel => string.Join(" > ", Order);
    /// <summary>Dev panel (host): this hazard comes first, the rest follow the shuffle. Null = the shuffle.</summary>
    internal static Hazard? DevFirst;
    internal static double StartTime { get; private set; }
    /// <summary>Who bound the relic that raised this moon ("" for a nightfall moon).</summary>
    internal static string Binder { get; private set; } = "";
    /// <summary>0..1 fade of the red sky / filter / music.</summary>
    internal static float Blend { get; private set; }
    internal static float Elapsed => Active ? (float)(PhotonNetwork.Time - StartTime) : 0f;

    private string? seenId;
    private float nextPoll;

    // --- host state -----------------------------------------------------------------------------
    private DayNightManager? lastDnm;
    private bool wasNight;
    private bool nightfallPending;
    private double safeUntil;
    private double expectStart = -1, endedFor = -1;
    private double stagesFor = -1;
    private bool huntDone, zombiesDone, zombiesCleared, tornadoDone, finalDone;

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Blood Moon");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        instance = go.AddComponent<BloodMoon>();
        go.AddComponent<Meteors>();
        instance.StartCoroutine(instance.LoadMusic());
    }

    /// <summary>Gloom variant runs (Gloom, then Citadel) get glass meteors as the 4th wave, everything else fire.</summary>
    internal static bool GloomRun => MapHandler.Exists && Singleton<MapHandler>.Instance.BiomeIsPresent(Biome.BiomeType.Swamp);
    internal static bool MesaRun => MapHandler.Exists && Singleton<MapHandler>.Instance.BiomeIsPresent(Biome.BiomeType.Mesa);

    private static bool InRun =>
        DayNightManager.instance != null && GameUtils.instance != null && !GameUtils.instance.m_inAirport && MapHandler.Exists;

    private static bool IsNight => InRun && DayNightManager.instance.isDay < 0.5f;

    /// <summary>Until the last wave of this tier is over.</summary>
    internal static float EffectsEnd(int tier) => (Mathf.Clamp(tier, 1, 4) - 1) * Gap + WaveFor;

    private void Update()
    {
        Blend = Mathf.MoveTowards(Blend, Active ? 1f : 0f, Time.deltaTime / 6f);
        Visuals.Tick();
        TickMusic();

        if (Time.time < nextPoll) return;
        nextPoll = Time.time + 0.25f;
        ReadRoom();
        if (Active) Announce();
        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient) HostTick();
    }

    // --- everyone: read the room property ----------------------------------------------------------

    private void ReadRoom()
    {
        string? raw = null;
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null
            && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(RoomKey, out object v) && v is string s)
            raw = s;
        if (raw != null && !InRun) raw = null;

        if (raw == null)
        {
            if (Active) Plugin.Log.LogInfo("Blood moon over.");
            Active = false;
            seenId = null;
            return;
        }
        var bits = raw.Split(new[] { '|' }, 3);
        if (bits.Length < 2 || !int.TryParse(bits[0], out int tier)
            || !double.TryParse(bits[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double start)) return;
        bool restart = Active;
        Tier = tier;
        string? rawOrder = PhotonNetwork.CurrentRoom!.CustomProperties.TryGetValue(OrderKey, out object o) ? o as string : null;
        Order = ParseOrder(rawOrder);
        if (seenId != raw && string.IsNullOrEmpty(rawOrder))
            Plugin.Log.LogWarning("Blood moon: no hazard order from the host (an older build?) - using the default order.");
        StartTime = start;
        Binder = bits.Length > 2 ? bits[2] : "";
        Active = true;
        if (seenId != raw)
        {
            seenId = raw;
            Begin(restart);
        }
    }

    private void Begin(bool restart)
    {
        Plugin.Log.LogInfo($"Blood moon rises: tier {Tier}{(restart ? " (restarted)" : "")}, order {OrderLabel}.");
        // Name the event, nothing more: what it brings is for the scouts to find out.
        Hud.BloodBanner("", "BLOOD MOON", Blood); // day or night: the sky shows which
        StartMusic(restart);
    }

    /// <summary>Waves arrive unannounced - the zombies, tornado and meteors speak for themselves.</summary>
    private void Announce() { }

    // --- host -------------------------------------------------------------------------------------

    private void HostTick()
    {
        var dnm = DayNightManager.instance;
        if (dnm != lastDnm)
        {
            // New level: don't treat "already night" as nightfall.
            lastDnm = dnm;
            wasNight = IsNight;
        }
        bool night = IsNight;

        if (!InRun)
        {
            if (Active) SetRoom(null);
            wasNight = false;
            nightfallPending = false;
            skipNextNight = false;
            sharedWaiting.Clear();
            return;
        }

        // A daytime pickup moon spares the night that follows it - even if it's still up at nightfall.
        if (night && !wasNight && Active) skipNextNight = false;
        if (!Active)
        {
            // A nightfall during a moon is swallowed by it; one just after a moon waits out the safety window.
            if (night && !wasNight) nightfallPending = true;
            if (!night) nightfallPending = false;
            if (nightfallPending && PhotonNetwork.Time >= safeUntil)
            {
                nightfallPending = false;
                Nightfall();
            }
        }
        else if (StartTime == endedFor)
        {
            // Ended; waiting for the room property to clear.
        }
        else if (expectStart >= 0 && StartTime != expectStart && PhotonNetwork.Time - expectStart < 5.0)
        {
            // Restarted; until the new start time comes back from the room, the old moon's clock is stale.
        }
        else
        {
            expectStart = -1;
            RunWaves();
            if (Elapsed >= EffectsEnd(Tier)) End();
        }
        wasNight = night;
    }

    private void Rise(int tier, string binder)
    {
        if (Active) StopEffects(); // a new relic mid-moon: start over at the new tier
        string now = PhotonNetwork.Time.ToString("R", CultureInfo.InvariantCulture);
        expectStart = double.Parse(now, CultureInfo.InvariantCulture);
        endedFor = -1;
        var order = RelicShuffle.MoonOrder(out string how);
        if (DevFirst is { } first && order.Remove(first)) { order.Insert(0, first); how += $", F8 put {first} first"; }
        Plugin.Log.LogInfo($"Blood moon order (host): {string.Join(" > ", order)} - shuffled from {how}.");
        SetRoom($"{tier}|{now}|{binder}", string.Join(",", order));
        huntDone = zombiesDone = zombiesCleared = tornadoDone = finalDone = false;
        stagesFor = -2; // ours from the start: claimed on the first wave tick
        ReadRoom();
        Plugin.Log.LogInfo(binder.Length > 0
            ? $"{binder} bound a relic - blood moon tier {tier}."
            : $"Nightfall with {tier} relic(s) held - blood moon.");
    }

    private void End()
    {
        endedFor = StartTime;
        SetRoom(null);
        StopEffects();
        safeUntil = PhotonNetwork.Time + SafetySeconds;
        nightfallPending = false;
        Plugin.Log.LogInfo("Blood moon sets.");
    }

    private static void StopEffects()
    {
        ScoutmasterHunt.Stop();
        BloodZombies.Clear();
        Meteors.Stop();
        BloodTornado.KillAll();
    }

    /// <summary>
    /// Host: someone bound a fresh relic. Waits a moment so their relic shows up in the squad
    /// count, then raises (or restarts) the moon at that tier.
    /// </summary>
    internal static void RelicTaken(string binder)
    {
        if (instance == null || !PhotonNetwork.IsMasterClient) return;
        if (!ModConfig.DevMoonOnPickup.Value)
        {
            Plugin.Log.LogInfo($"{binder} took a relic - no moon on pickup (BloodMoonOnPickup is off).");
            return;
        }
        // A second pickup while the first one's moon is still on its way joins it.
        instance.riseBinder = binder;
        if (!instance.risePending) instance.StartCoroutine(instance.RiseForRelic());
    }

    private bool risePending;
    private string riseBinder = "";

    private IEnumerator RiseForRelic()
    {
        risePending = true;
        yield return new WaitForSeconds(1.5f);
        // Testing: scouts choosing a shared relic get to finish first.
        while (sharedWaiting.Count > 0 && Time.time < sharedUntil)
        {
            if (PhotonNetwork.CurrentRoom != null) sharedWaiting.RemoveWhere(a => PhotonNetwork.CurrentRoom.GetPlayer(a) == null);
            yield return new WaitForSeconds(0.25f);
        }
        if (sharedWaiting.Count > 0) Plugin.Log.LogInfo($"Shared relics: stopped waiting for {sharedWaiting.Count} scout(s).");
        sharedWaiting.Clear();
        risePending = false;
        if (!InRun) yield break;
        // Every freshly taken relic raises one at once: by day a blood eclipse, by night a blood moon.
        nightfallPending = false;
        // A morning pickup still owes tonight's moon; from the afternoon on (the game's 11:30) its eclipse stands in for it.
        bool afternoon = !IsNight && DayNightManager.instance.timeOfDay >= 11.5f;
        if (afternoon && ModConfig.DevMoonEveryNight.Value) skipNextNight = true;
        Plugin.Log.LogInfo($"{riseBinder} took a relic - blood {(IsNight ? "moon" : "eclipse")} now"
            + (IsNight ? "." : afternoon ? " (afternoon: tonight stays calm)." : " (morning: tonight's moon still rises)."));
        Rise(Mathf.Clamp(RunState.SquadRelics(), 1, 4), riseBinder);
    }

    // --- testing options -----------------------------------------------------------------------------

    private bool skipNextNight;
    private readonly System.Collections.Generic.HashSet<int> sharedWaiting = new();
    private float sharedUntil;

    /// <summary>
    /// Nightfall. Normally calm - only a freshly taken relic (or a dark rite) raises a moon. With
    /// the every-night option, any night while the squad holds a relic, except the one
    /// right after an afternoon / evening pickup moon.
    /// </summary>
    private void Nightfall()
    {
        bool skip = skipNextNight;
        skipNextNight = false;
        if (!ModConfig.BloodMoon.Value || !ModConfig.Relics.Value) return;
        int tier = RunState.SquadRelics();
        if (!ModConfig.DevMoonEveryNight.Value || tier <= 0) { Plugin.Log.LogInfo("Nightfall - a calm night."); return; }
        if (skip) { Plugin.Log.LogInfo("Nightfall - calm: the day's pickup moon stands in for tonight's."); return; }
        Rise(Mathf.Clamp(tier, 1, 4), "");
    }

    /// <summary>
    /// Host (testing option): a scout bound a relic from an amulet - everyone else gets that
    /// colour's choice, and the pickup's moon waits until they've all chosen (or the wait runs out).
    /// </summary>
    internal static void ShareRelic(RelicGem gem, int binderActor)
    {
        if (instance == null || !PhotonNetwork.IsMasterClient || !ModConfig.DevShareRelics.Value || !ModConfig.Relics.Value) return;
        if (PhotonNetwork.PlayerList.All(p => p.ActorNumber == binderActor)) return; // solo: nobody to share with
        foreach (var p in PhotonNetwork.PlayerList)
            if (p.ActorNumber != binderActor && Modless.HasMod(p)) instance.sharedWaiting.Add(p.ActorNumber);
        instance.sharedUntil = Time.time + Mathf.Max(5f, ModConfig.DevShareWait.Value);
        Plugin.Log.LogInfo($"Shared relics: {gem} offered to {instance.sharedWaiting.Count} scout(s).");
        Net.SharedRelic(gem, binderActor);
    }

    /// <summary>Host: this scout chose or skipped their shared relic.</summary>
    internal static void SharedDone(int actor)
    {
        if (instance == null || !PhotonNetwork.IsMasterClient) return;
        instance.sharedWaiting.Remove(actor);
    }

    /// <summary>
    /// Host: the Cursed Skull or the Ritual Dagger was used. No waiting for night - the moon rises
    /// now (by day that's the blood eclipse), at least tier 1 even with no relics held.
    /// </summary>
    internal static void DarkRite(string who)
    {
        if (instance == null || !PhotonNetwork.IsMasterClient || !InRun) return;
        if (!ModConfig.BloodMoon.Value || !ModConfig.BmDarkRites.Value) return;
        int tier = Mathf.Clamp(RunState.SquadRelics(), 1, 4);
        Plugin.Log.LogInfo($"{who} performed a dark rite - blood {(IsNight ? "moon" : "eclipse")} at tier {tier}.");
        instance.nightfallPending = false;
        instance.Rise(tier, who);
    }

    private void RunWaves()
    {
        float t = Elapsed;
        if (stagesFor != StartTime)
        {
            // We became host mid-moon (or just started it): waves already past stay done.
            bool fresh = stagesFor == -2;
            stagesFor = StartTime;
            if (!fresh)
            {
                huntDone = zombiesDone = tornadoDone = finalDone = t > 1f;
                zombiesCleared = false;
            }
        }

        // Everything the tier unlocks rises together and lasts the whole moon.
        float left = EffectsEnd(Tier) - t;
        if (TierOf(Hazard.Scoutmaster) >= 1 && !huntDone)
        {
            huntDone = true;
            if (ModConfig.BmScoutmaster.Value) ScoutmasterHunt.Steer(left);
        }
        if (TierOf(Hazard.Zombies) >= 1 && !zombiesDone)
        {
            zombiesDone = true;
            if (ModConfig.BmZombies.Value) BloodZombies.Spawn(left);
        }
        if (TierOf(Hazard.Tornado) >= 1 && !tornadoDone)
        {
            tornadoDone = true;
            if (ModConfig.BmTornado.Value) BloodTornado.Spawn(left, alone: Tier <= 1);
        }
        if (TierOf(Hazard.Meteors) >= 1 && !finalDone)
        {
            finalDone = true;
            if (GloomRun ? ModConfig.BmGlassMeteors.Value : ModConfig.BmMeteors.Value) Meteors.Begin(left);
        }
        BloodZombies.HostTick();
    }

    private static void SetRoom(string? value, string? order = null)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
        var props = new Hashtable { [RoomKey] = value };
        if (order != null) props[OrderKey] = order;
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    /// <summary>"Tornado,Zombies,..." from the room; anything missing or unreadable keeps its place in the default order.</summary>
    private static Hazard[] ParseOrder(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return DefaultOrder;
        var list = new System.Collections.Generic.List<Hazard>();
        foreach (var name in raw!.Split(','))
            if (System.Enum.TryParse(name, out Hazard h) && !list.Contains(h)) list.Add(h);
        foreach (var h in DefaultOrder) if (!list.Contains(h)) list.Add(h);
        return list.ToArray();
    }

    /// <summary>Dev panel (host): a moon with only this hazard (tier 1), right now. Null = back to the shuffle.</summary>
    internal static void DevAlone(Hazard? h)
    {
        DevFirst = h;
        if (h == null) { Hud.Toast("Blood moon order: shuffled", Blood); return; }
        DevStart(1);
    }

    /// <summary>Dev panel (host): tier 1-4 = make it night and start a blood moon now; 0 = end it.</summary>
    internal static void DevStart(int tier)
    {
        if (instance == null || !PhotonNetwork.IsMasterClient) return;
        if (tier <= 0)
        {
            SetRoom(null);
            StopEffects();
            Hud.Toast("Blood moon ended (dev)", Blood);
            return;
        }
        if (!InRun) { Hud.Toast("Start a run first", Blood); return; }
        var dnm = DayNightManager.instance;
        float night = dnm.dayEnd + 0.05f;
        dnm.setTimeOfDay(night);
        dnm.photonView.RPC("RPCA_SyncTime", RpcTarget.All, dnm.dayCount, night);
        instance.wasNight = true;
        instance.nightfallPending = false;
        instance.Rise(Mathf.Clamp(tier, 1, 4), "");
    }

    // --- music ----------------------------------------------------------------------------------------

    private AudioSource? music;
    private AudioClip? musicClip;

    private IEnumerator LoadMusic()
    {
        // BloodMoon.ogg/.wav/.mp3 if it's there, otherwise any song in the mod's folder.
        string dir = Path.GetDirectoryName(typeof(BloodMoon).Assembly.Location) ?? "";
        var types = new System.Collections.Generic.Dictionary<string, AudioType>
        {
            [".ogg"] = AudioType.OGGVORBIS, [".wav"] = AudioType.WAV, [".mp3"] = AudioType.MPEG,
        };
        var songs = Directory.Exists(dir)
            ? Directory.GetFiles(dir).Where(f => types.ContainsKey(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => Path.GetFileNameWithoutExtension(f).Replace(" ", "").ToLowerInvariant() == "bloodmoon" ? 0 : 1)
                .ThenBy(f => f)
                .ToList()
            : new System.Collections.Generic.List<string>();
        foreach (string path in songs.Take(1))
        {
            var type = types[Path.GetExtension(path).ToLowerInvariant()];
            using var req = UnityWebRequestMultimedia.GetAudioClip(new System.Uri(path).AbsoluteUri, type);
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Plugin.Log.LogWarning($"Couldn't load blood moon music {path}: {req.error}");
                yield break;
            }
            musicClip = DownloadHandlerAudioClip.GetContent(req);
            Plugin.Log.LogInfo($"Blood moon music loaded: {Path.GetFileName(path)} ({musicClip.length:0}s).");
            yield break;
        }
        Plugin.Log.LogInfo("No .ogg/.wav/.mp3 next to the DLL - blood moons play without music.");
    }

    // Fades: the moon's Blend (6s) fades the music in and out with the sky; musicGain dips it
    // for a restart (quick fade out, back to the top of the song, fade in).
    private float musicGain = 1f;
    private bool musicRestart;

    private void EnsureMusicSource()
    {
        if (music == null)
        {
            music = gameObject.AddComponent<AudioSource>();
            music.spatialBlend = 0f;
            music.playOnAwake = false;
        }
        // Through the game's Music channel, so the in-game music slider turns it down too.
        // (Retried each time: the mixer may not be loaded yet the first time.)
        if (music.outputAudioMixerGroup == null)
        {
            music.outputAudioMixerGroup = Meteors.MixerGroup("Music");
            Plugin.Log.LogInfo(music.outputAudioMixerGroup != null
                ? $"Blood moon music plays through the game's '{music.outputAudioMixerGroup.name}' channel."
                : "Blood moon music: game Music channel not found - the in-game music slider won't affect it.");
        }
    }

    private float previewUntil;

    /// <summary>Dev panel: play the song for a few seconds at the current volume.</summary>
    internal static void DevPreviewMusic()
    {
        if (instance == null || instance.musicClip == null) { Hud.Toast("No blood moon music loaded", ChoiceScreen.Cream); return; }
        instance.EnsureMusicSource();
        if (!instance.music!.isPlaying) { instance.musicGain = 1f; instance.PlayFromTop(); }
        instance.previewUntil = Time.time + 10f;
    }

    private void StartMusic(bool restart)
    {
        if (musicClip == null) return;
        EnsureMusicSource();
        if (restart && music!.isPlaying)
        {
            musicRestart = true;
            return;
        }
        musicRestart = false;
        musicGain = 1f;
        PlayFromTop();
    }

    private void PlayFromTop()
    {
        music!.clip = musicClip;
        music.loop = ModConfig.BmMusicLoop.Value;
        music.volume = 0f;
        music.Play();
    }

    // --- main menu: the blood moon song instead of the game's menu music ---------------------------

    private float menuBlend, nextMuteScan;
    private readonly System.Collections.Generic.HashSet<AudioSource> muted = new();

    private void TickMenuMusic()
    {
        bool want = Ambiance.InMenu && ModConfig.MenuMusic.Value && musicClip != null;
        menuBlend = Mathf.MoveTowards(menuBlend, want ? 1f : 0f, Time.deltaTime / (want ? 2.5f : 1f));
        if (want)
        {
            EnsureMusicSource();
            if (!music!.isPlaying)
            {
                musicGain = 1f;
                PlayFromTop();
                music.loop = true;
            }
            if (Time.time >= nextMuteScan)
            {
                nextMuteScan = Time.time + 0.5f;
                MuteGameMusic();
            }
        }
        else if (muted.Count > 0)
        {
            foreach (var s in muted) if (s != null) s.mute = false;
            muted.Clear();
        }
    }

    /// <summary>Silence the menu's own music (anything playing through the game's Music channel).</summary>
    private void MuteGameMusic()
    {
        foreach (var s in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
        {
            if (s == null || s == music || !s.isPlaying || s.mute) continue;
            var group = s.outputAudioMixerGroup;
            if (group == null || group.name.IndexOf("music", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            s.mute = true;
            if (muted.Add(s)) Plugin.Log.LogInfo($"Menu: muted the game's music '{s.name}' for the blood moon song.");
        }
    }

    private float safeGain = 1f;

    private void TickMusic()
    {
        TickMenuMusic();
        if (music == null) return;
        if (musicRestart)
        {
            musicGain = Mathf.MoveTowards(musicGain, 0f, Time.deltaTime / 0.75f);
            if (musicGain <= 0f)
            {
                musicRestart = false;
                PlayFromTop();
            }
        }
        else musicGain = Mathf.MoveTowards(musicGain, 1f, Time.deltaTime / 1.5f);

        // On safe ground the moon's song fades out; it comes back when you step out.
        safeGain = Mathf.MoveTowards(safeGain, Sanctuary.LocalSafe && menuBlend <= 0f ? 0f : 1f, Time.deltaTime / 2.5f);
        if (!music.isPlaying) return;
        bool preview = Time.time < previewUntil;
        music.volume = ModConfig.BmMusicVolume.Value * (preview ? 1f : Mathf.Max(Blend, menuBlend) * safeGain) * musicGain;
        if (!Active && Blend <= 0f && !preview && menuBlend <= 0f) music.Stop();
    }

    // --- sky, moon, filter --------------------------------------------------------------------------

    /// <summary>Red screen filter, red full moon disc, red moonlight (via the DayNightManager patch).</summary>
    [HarmonyPatch]
    internal static class Visuals
    {
        private static GameObject? disc;
        private static Material? discMat;
        private static Light? movedMoon;
        private static Quaternion moonLocal;

        internal static void Tick()
        {
            // No screen filter: the moon lights the world red, the way a red sun does by day.
            if (Blend <= 0f && movedMoon != null)
            {
                movedMoon.transform.localRotation = moonLocal;
                movedMoon = null;
            }
        }

        // A blood moon night is lit like a red-sun day, only redder: bright saturated sky (it also
        // feeds the ambient light) and a light red moonlight - you can see where you're going.
        private static readonly Color NightTop = new(0.75f, 0.06f, 0.04f), NightMid = new(1f, 0.18f, 0.08f), NightBottom = new(1f, 0.3f, 0.15f);
        private static readonly Color MoonLightColour = new(1f, 0.5f, 0.4f);

        /// <summary>After the game lights the sky: red moonlight, a red sky, and the moon's direction raised.</summary>
        [HarmonyPostfix, HarmonyPatch(typeof(DayNightManager), nameof(DayNightManager.UpdateCycle))]
        private static void Sky(DayNightManager __instance)
        {
            if (Application.isPlaying && Ambiance.SkyStrength > 0f)
            {
                try { AirportSky(__instance, Ambiance.SkyStrength); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Airport sky failed: {e.Message}"); }
                return;
            }
            float b = Blend;
            if (b <= 0f || !Application.isPlaying) { HideDisc(); RestoreMoon(); return; }
            try
            {
                var moon = __instance.moon;
                var sun = __instance.sun;
                if (moon == null || !moon.enabled)
                {
                    Eclipse(sun, b);
                    return;
                }

                Shader.SetGlobalColor("SkyTopColor", Color.Lerp(Shader.GetGlobalColor("SkyTopColor"), NightTop, b));
                Shader.SetGlobalColor("SkyMidColor", Color.Lerp(Shader.GetGlobalColor("SkyMidColor"), NightMid, b));
                Shader.SetGlobalColor("SkyBottomColor", Color.Lerp(Shader.GetGlobalColor("SkyBottomColor"), NightBottom, b));

                // Keep the moon high in the sky so it lights the mountain from above.
                Vector3 dir = -moon.transform.forward;
                if (dir.y < 0.45f)
                {
                    Vector3 flat = new Vector3(dir.x, 0f, dir.z);
                    if (flat.sqrMagnitude < 0.01f) flat = Vector3.forward;
                    dir = (flat.normalized * Mathf.Cos(35f * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(35f * Mathf.Deg2Rad)).normalized;
                }
                if (movedMoon == null)
                {
                    movedMoon = moon;
                    moonLocal = moon.transform.localRotation;
                }
                moon.transform.localRotation = moonLocal;
                Vector3 vanillaDir = -moon.transform.forward;
                moon.transform.rotation = Quaternion.LookRotation(-Vector3.Slerp(vanillaDir, dir, b));

                moon.color = Color.Lerp(moon.color, MoonLightColour, b);
                moon.intensity = Mathf.Lerp(moon.intensity, Mathf.Max(moon.intensity, ModConfig.BmMoonLight.Value), b);
                ShowNightDisc(__instance, dir, b);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Blood sky failed: {e.Message}");
            }
        }

        /// <summary>
        /// The airport: a blood moon night outside the windows. Only the sky changes - the sun and
        /// moon lights (which light the scouts) stay vanilla. The red moon hangs high in the sky.
        /// </summary>
        private static void AirportSky(DayNightManager dnm, float k)
        {
            // Brighter, more saturated reds than the in-run blood moon: the dark ones read as grey
            // through the menu/airport haze.
            Shader.SetGlobalColor("SkyTopColor", Color.Lerp(Shader.GetGlobalColor("SkyTopColor"), Ambiance.SkyTop, k));
            Shader.SetGlobalColor("SkyMidColor", Color.Lerp(Shader.GetGlobalColor("SkyMidColor"), Ambiance.SkyMid, k));
            Shader.SetGlobalColor("SkyBottomColor", Color.Lerp(Shader.GetGlobalColor("SkyBottomColor"), Ambiance.SkyBottom, k));
            // Red sunlight: the clouds are lit by it, so they stop being grey (it tints the scene a bit too).
            float l = Ambiance.LightStrength;
            var redLight = new Color(1f, 0.3f, 0.2f);
            if (dnm.sun != null) dnm.sun.color = Color.Lerp(dnm.sun.color, redLight * dnm.sun.color.maxColorComponent, l);
            if (dnm.moon != null) dnm.moon.color = Color.Lerp(dnm.moon.color, redLight * dnm.moon.color.maxColorComponent, l);
            SunFlare.Tint(dnm, Ambiance.SkyMid, k);
            var light = dnm.moon != null && dnm.moon.enabled ? dnm.moon : dnm.sun;
            Vector3 dir = light != null ? -light.transform.forward : Vector3.up;
            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude < 0.01f) flat = Vector3.forward;
            if (dir.y < 0.42f) dir = (flat.normalized * Mathf.Cos(28f * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(28f * Mathf.Deg2Rad)).normalized;
            ShowDisc(dir, k);
        }

        /// <summary>A blood moon in daylight: dimmed blood-orange sun, a red sky, and the red moon over the sun.</summary>
        private static void Eclipse(Light? sun, float b)
        {
            Shader.SetGlobalColor("SkyTopColor", Color.Lerp(Shader.GetGlobalColor("SkyTopColor"), new Color(0.28f, 0.03f, 0.03f), b * 0.8f));
            Shader.SetGlobalColor("SkyMidColor", Color.Lerp(Shader.GetGlobalColor("SkyMidColor"), new Color(0.62f, 0.12f, 0.06f), b * 0.8f));
            Shader.SetGlobalColor("SkyBottomColor", Color.Lerp(Shader.GetGlobalColor("SkyBottomColor"), new Color(0.85f, 0.28f, 0.10f), b * 0.8f));
            if (sun == null || !sun.enabled) { HideDisc(); return; }
            sun.color = Color.Lerp(sun.color, new Color(1f, 0.34f, 0.2f), b * 0.85f);
            sun.intensity *= Mathf.Lerp(1f, 0.7f, b);
            Vector3 dir = -sun.transform.forward;
            if (dir.y > 0f) ShowDisc(dir, b, eclipse: true);
            else HideDisc();
        }

        // The sun's disc is ~1.77x our moon (playtest screenshot: a thick white ring around it).
        // Covering 95% of it leaves a thin burning rim - a real eclipse.
        private const float EclipseScale = 1.68f;

        private static void HideDisc()
        {
            if (disc != null && disc.activeSelf) disc.SetActive(false);
        }

        private static DayNightManager? moonFor;
        private static Renderer? gameMoon;

        private static readonly string[] MoonColorProps = { "_Color", "_BaseColor", "_TintColor", "_EmissionColor", "_MainColor" };
        private static readonly System.Collections.Generic.Dictionary<string, Color> moonOriginal = new();
        private static Material? moonMat;

        /// <summary>
        /// Night: the game's own moon turns blood red (our drawn full moon looked janky). Its
        /// material's colours are lerped toward red by the moon's blend and put back afterwards.
        /// </summary>
        private static void ShowNightDisc(DayNightManager dnm, Vector3 lightDir, float b)
        {
            HideDisc();
            if (moonFor != dnm)
            {
                moonFor = dnm;
                RestoreMoon();
                var root = dnm.earth != null ? dnm.earth.transform : dnm.transform;
                gameMoon = root.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r.name.IndexOf("moon", System.StringComparison.OrdinalIgnoreCase) >= 0 && r is not ParticleSystemRenderer)
                    .OrderByDescending(r => r.bounds.extents.sqrMagnitude)
                    .FirstOrDefault();
                moonMat = gameMoon != null ? gameMoon.material : null; // its own instance
                moonOriginal.Clear();
                if (moonMat != null)
                    foreach (var p in MoonColorProps)
                        if (moonMat.HasProperty(p)) moonOriginal[p] = moonMat.GetColor(p);
                Plugin.Log.LogInfo(gameMoon != null
                    ? $"Blood moon reddens the game's moon '{gameMoon.name}' ({moonMat?.shader?.name}; colours: {string.Join(", ", moonOriginal.Keys)})."
                    : "Blood moon: game moon object not found - only the light turns red.");
            }
            TintMoon(b);
        }

        private static void TintMoon(float b)
        {
            if (moonMat == null) return;
            foreach (var kv in moonOriginal)
            {
                var o = kv.Value;
                // Keep its brightness, take the blood moon's colour.
                var red = new Color(Blood.r, Blood.g * 0.35f, Blood.b * 0.3f, o.a) * Mathf.Max(0.6f, o.maxColorComponent);
                red.a = o.a;
                moonMat.SetColor(kv.Key, Color.Lerp(o, red, b));
            }
        }

        private static void RestoreMoon()
        {
            if (moonMat == null) return;
            foreach (var kv in moonOriginal) moonMat.SetColor(kv.Key, kv.Value);
        }

        internal static void ShowDisc(Vector3 dir, float b, bool eclipse = false, float size = 1f, float distance = 0f)
        {
            var cam = Meteors.Eye();
            if (cam == null) { HideDisc(); return; }
            if (disc == null)
            {
                var sh = Shader.Find("Hidden/Internal-Colored");
                if (sh == null) return;
                disc = new GameObject("PEAK Roguelike - Blood Moon Disc");
                Object.DontDestroyOnLoad(disc);
                disc.AddComponent<MeshFilter>().sharedMesh = MoonMesh();
                discMat = new Material(sh);
                discMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                discMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                discMat.SetInt("_ZWrite", 0);
                discMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
                discMat.SetInt("_Cull", 0);
                discMat.renderQueue = 2990;
                var r = disc.AddComponent<MeshRenderer>();
                r.sharedMaterial = discMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            if (!disc.activeSelf) disc.SetActive(true);
            float dist = Mathf.Min(cam.farClipPlane * 0.85f, distance > 0f ? distance : 2500f);
            disc.transform.position = cam.transform.position + dir * dist;
            disc.transform.rotation = Quaternion.LookRotation(-dir);
            disc.transform.localScale = Vector3.one * dist * 0.075f * (eclipse ? EclipseScale : size);
            // Eclipse: the moon's face goes dark blood-red against the sun behind it.
            discMat!.SetColor("_Color", eclipse ? new Color(0.42f, 0.1f, 0.08f, b) : new Color(1f, 1f, 1f, b));
        }

        /// <summary>A full moon as a vertex-coloured disc: mottled red face, darker limb, soft halo.</summary>
        private static Mesh MoonMesh()
        {
            const int rings = 22, segs = 72;
            var verts = new System.Collections.Generic.List<Vector3> { Vector3.zero };
            var cols = new System.Collections.Generic.List<Color> { Face(0f, 0f) };
            var tris = new System.Collections.Generic.List<int>();
            for (int r = 1; r <= rings + 6; r++)
            {
                // rings 1..rings = the face (radius 1), then a halo out to radius ~2.2
                float rad = r <= rings ? (float)r / rings : 1f + (r - rings) / 6f * 1.2f;
                for (int s = 0; s < segs; s++)
                {
                    float ang = s / (float)segs * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad, 0f));
                    cols.Add(r <= rings ? Face(rad, ang) : new Color(1f, 0.12f, 0.08f, 0.28f * Mathf.Pow(1f - (rad - 1f) / 1.2f, 2f)));
                }
            }
            for (int s = 0; s < segs; s++) { tris.Add(0); tris.Add(1 + (s + 1) % segs); tris.Add(1 + s); }
            for (int r = 1; r < rings + 6; r++)
            {
                int a0 = 1 + (r - 1) * segs, b0 = 1 + r * segs;
                for (int s = 0; s < segs; s++)
                {
                    int s1 = (s + 1) % segs;
                    tris.AddRange(new[] { a0 + s, a0 + s1, b0 + s, a0 + s1, b0 + s1, b0 + s });
                }
            }
            var m = new Mesh { name = "BloodMoon" };
            m.SetVertices(verts);
            m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        private static Color Face(float rad, float ang)
        {
            float x = Mathf.Cos(ang) * rad, y = Mathf.Sin(ang) * rad;
            // "maria": large dark patches, plus fine mottling
            float maria = Mathf.PerlinNoise(x * 1.6f + 3.1f, y * 1.6f + 7.7f);
            float fine = Mathf.PerlinNoise(x * 6f + 11f, y * 6f + 5f);
            float shade = Mathf.Lerp(1f, 0.62f, Mathf.SmoothStep(0.45f, 0.7f, maria)) * Mathf.Lerp(0.9f, 1.05f, fine);
            float limb = Mathf.Lerp(1f, 0.72f, Mathf.Pow(rad, 3f));
            float v = shade * limb;
            return new Color(1.0f * v, 0.2f * v, 0.13f * v, 1f);
        }
    }
}
