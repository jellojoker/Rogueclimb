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
/// Blood moons: binding a relic (not recovering a dropped one) and every nightfall while the
/// squad holds at least one relic turn the moon full and red. Each relic held adds a wave,
/// 15s apart, and the moon lasts one minute per relic:
///   1 - the Scoutmaster hunts (60s)
///   2 - zombies rise above and below the squad (until 2:00)
///   3 - a tornado (until 3:00; sand on Mesa, snow on Alpine)
///   4 - a meteor shower (until 4:00): purple glass on Gloom runs, fire on Caldera runs
/// Binding another relic mid-moon restarts it at the new tier. In daylight it's a blood eclipse
/// (red sun and sky, the clock isn't touched). A nightfall within 30s of a moon ending waits out
/// the rest of those 30s. The host publishes "tier|start time|binder" as a room property;
/// everyone draws the sky from that, so late joiners see it too.
/// </summary>
internal class BloodMoon : MonoBehaviour
{
    internal const string RoomKey = "prl.bm";

    // Wave timing (seconds after the moon rises) and lengths: every wave ends on a whole minute.
    // A relay: each wave lasts WaveLength and the next arrives WaveGap later, so at most three
    // overlap and a tier 4 moon is over in (3 x 20) + 60 = 2 minutes.
    private static float Gap => Mathf.Max(0f, ModConfig.BmWaveGap.Value);
    internal static float WaveFor => Mathf.Max(5f, ModConfig.BmWaveLength.Value);
    internal static float ZombiesAt => Gap;
    internal static float TornadoAt => Gap * 2f;
    internal static float FinalAt => Gap * 3f;
    /// <summary>No nightfall moon this soon after the last one ended.</summary>
    internal const float SafetySeconds = 30f;

    internal static readonly Color Blood = new(1f, 0.16f, 0.12f);
    /// <summary>The orange of the blood moon lettering.</summary>
    internal static readonly Color Ember = new(1f, 0.47f, 0.08f);

    private static BloodMoon? instance;

    // --- shared state (everyone, from the room property) --------------------------------------
    internal static bool Active { get; private set; }
    internal static int Tier { get; private set; }
    internal static double StartTime { get; private set; }
    /// <summary>Who bound the relic that raised this moon ("" for a nightfall moon).</summary>
    internal static string Binder { get; private set; } = "";
    /// <summary>0..1 fade of the red sky / filter / music.</summary>
    internal static float Blend { get; private set; }
    internal static float Elapsed => Active ? (float)(PhotonNetwork.Time - StartTime) : 0f;

    private string? seenId;
    private int toastsShown;
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
        StartTime = start;
        Binder = bits.Length > 2 ? bits[2] : "";
        Active = true;
        if (seenId != raw)
        {
            seenId = raw;
            toastsShown = 0;
            Begin(restart);
        }
    }

    private void Begin(bool restart)
    {
        Plugin.Log.LogInfo($"Blood moon rises: tier {Tier}{(restart ? " (restarted)" : "")}.");
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
            return;
        }

        if (!Active)
        {
            // A nightfall during a moon is swallowed by it; one just after a moon waits out the safety window.
            if (night && !wasNight) nightfallPending = true;
            if (!night) nightfallPending = false;
            if (nightfallPending && PhotonNetwork.Time >= safeUntil)
            {
                nightfallPending = false;
                if (ModConfig.BloodMoon.Value && ModConfig.Relics.Value)
                {
                    int tier = Mathf.Clamp(RunState.SquadRelics(), 0, 4);
                    if (tier > 0 && Suns.NightsUnlocked) Rise(tier, "");
                    else Plugin.Log.LogInfo(tier > 0 ? "Nightfall - this biome's relic isn't taken, a calm night." : "Nightfall - nobody holds a relic, a normal night.");
                }
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
        SetRoom($"{tier}|{now}|{binder}");
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
        instance.StartCoroutine(instance.RiseForRelic(binder));
    }

    private IEnumerator RiseForRelic(string binder)
    {
        yield return new WaitForSeconds(1.5f);
        if (!InRun) yield break;
        // With suns: taking the relic clears the day; the night it unlocks comes at nightfall
        // (taken at night, the moon rises now).
        if (ModConfig.Suns.Value && (!IsNight || !Suns.NightsUnlocked))
        {
            Plugin.Log.LogInfo($"{binder} took a relic by day - the sun clears, the blood moon waits for night.");
            yield break;
        }
        Rise(Mathf.Clamp(RunState.SquadRelics(), 1, 4), binder);
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
                huntDone = t > 1f;
                zombiesDone = t > ZombiesAt + 1f;
                zombiesCleared = false;
                tornadoDone = t > TornadoAt + 1f;
                finalDone = t > FinalAt + 1f;
            }
        }

        if (!huntDone)
        {
            huntDone = true;
            if (ModConfig.BmScoutmaster.Value) ScoutmasterHunt.Steer(WaveFor - t);
        }
        if (Tier >= 2 && !zombiesDone && t >= ZombiesAt)
        {
            zombiesDone = true;
            if (ModConfig.BmZombies.Value) BloodZombies.Spawn(ModConfig.BmZombieCount.Value, ZombiesAt + WaveFor - t);
        }
        if (Tier >= 3 && !tornadoDone && t >= TornadoAt)
        {
            tornadoDone = true;
            if (ModConfig.BmTornado.Value) BloodTornado.Spawn(TornadoAt + WaveFor - t);
        }
        if (Tier >= 4 && !finalDone && t >= FinalAt)
        {
            finalDone = true;
            float left = FinalAt + WaveFor - t;
            if (GloomRun ? ModConfig.BmGlassMeteors.Value : ModConfig.BmMeteors.Value) Meteors.Begin(left);
        }
        BloodZombies.HostTick();
    }

    private static void SetRoom(string? value)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { [RoomKey] = value });
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
        private static Image? tint, vignette;
        private static GameObject? disc;
        private static Material? discMat;
        private static Light? movedMoon;
        private static Quaternion moonLocal;

        internal static void Tick()
        {
            float b = Blend;
            EnsureOverlay();
            float strength = Mathf.Clamp01(ModConfig.BmTint.Value) * b;
            if (tint != null)
            {
                tint.enabled = strength > 0.001f;
                tint.color = new Color(0.62f, 0.02f, 0.01f, strength);
            }
            if (vignette != null)
            {
                vignette.enabled = strength > 0.001f;
                vignette.color = new Color(0.45f, 0f, 0f, Mathf.Clamp01(strength * 3.5f));
            }
            if (b <= 0f && movedMoon != null)
            {
                movedMoon.transform.localRotation = moonLocal;
                movedMoon = null;
            }
        }

        private static void EnsureOverlay()
        {
            if (tint != null) return;
            var go = new GameObject("PEAK Roguelike - Blood Filter");
            Object.DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -50; // under the game's HUD
            tint = Full(go.transform, "Tint", null);
            vignette = Full(go.transform, "Vignette", VignetteSprite());
            tint.enabled = vignette.enabled = false;
        }

        private static Image Full(Transform parent, string name, Sprite? sprite)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            return img;
        }

        private static Sprite VignetteSprite()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, d));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }

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

                Shader.SetGlobalColor("SkyTopColor", Color.Lerp(Shader.GetGlobalColor("SkyTopColor"), new Color(0.10f, 0.0f, 0.01f), b * 0.85f));
                Shader.SetGlobalColor("SkyMidColor", Color.Lerp(Shader.GetGlobalColor("SkyMidColor"), new Color(0.32f, 0.02f, 0.02f), b * 0.85f));
                Shader.SetGlobalColor("SkyBottomColor", Color.Lerp(Shader.GetGlobalColor("SkyBottomColor"), new Color(0.45f, 0.05f, 0.03f), b * 0.85f));

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

                moon.color = Color.Lerp(moon.color, Blood, b);
                moon.intensity = Mathf.Lerp(moon.intensity, ModConfig.BmMoonLight.Value, b);
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
