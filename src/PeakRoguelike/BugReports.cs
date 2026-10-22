using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.InputSystem;
using Zorro.Core;

namespace PeakRoguelike;

/// <summary>
/// Bug reports. F7 (anyone) saves a folder on every modded player's PC at once:
/// screenshot.jpg, log.txt (the last few minutes of log), state.txt (a snapshot of the run) and
/// config.cfg; whoever pressed it can type what happened (note.txt). The first mod error of a run saves one by itself. Nothing is sent while playing:
/// the host collects everyone's unsent reports from the F8 panel (anywhere, one player at a
/// time, in small chunks), and other players can send theirs from the airport.
/// Folders: BepInEx/BugReports/&lt;stamp&gt;/&lt;player&gt;/ - received reports land in the same layout
/// on the host, so one F7 press groups every player's view of that moment.
/// </summary>
internal class BugReports : MonoBehaviourPunCallbacks, IOnEventCallback
{
    private const byte ControlEvent = 198;
    private const byte ChunkEvent = 199;
    private const int ChunkBytes = 16 * 1024;
    private const float ChunkGap = 0.1f;   // 10 chunks a second, ~160 KB/s

    private enum Op : byte { Snap, Count, Collect, Stop, Done, AskCount, File, Ack }

    private const string MineMark = ".mine", SentMark = ".sent";

    private static BugReports? instance;
    private static readonly LogRing ring = new();
    private static volatile string? pendingError;
    private static bool autoSaved;
    private static string? lastLevel;

    internal static string Root => Path.Combine(Paths.BepInExRootPath, "BugReports");

    // Host side: unsent counts per player, who is sending now, who is waiting.
    private readonly Dictionary<int, int> counts = new();
    private readonly Queue<int> queue = new();
    private readonly Dictionary<string, Incoming> incoming = new();
    private int sendingActor = -1;
    private float sendingSince;
    internal static string Status { get; private set; } = "";

    // Sender side.
    private Coroutine? sending;

    // The "what happened?" box after F7.
    private static string? noteDir;
    private static string noteText = "";
    private static bool noteFocused;
    private static GUIStyle? boxStyle, fieldStyle, hintStyle;

    /// <summary>The note box is up: free the cursor and keep keys away from the game.</summary>
    internal static bool Typing => noteDir != null;

    private class Incoming
    {
        public string Stamp = "", Name = "", Player = "";
        public int Chunks;
        public readonly Dictionary<int, byte[]> Parts = new();
    }

    internal static void Install()
    {
        BepInEx.Logging.Logger.Listeners.Add(ring);
        var go = new GameObject("PEAK Roguelike - Bug Reports");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        instance = go.AddComponent<BugReports>();
    }

    // --- panel hooks ------------------------------------------------------------------------------

    internal static bool InAirport => GameUtils.instance != null && GameUtils.instance.m_inAirport;
    internal static bool IsHost => PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient;
    internal static bool Collecting => instance != null && instance.sendingActor >= 0;
    internal static bool Sending => instance != null && instance.sending != null;

    /// <summary>The panel needs the cursor: the host's collect buttons, or a guest's send button in the airport.</summary>
    internal static bool PanelClickable => PhotonNetwork.InRoom && (PhotonNetwork.IsMasterClient || InAirport);

    /// <summary>Changes whenever the buttons the panel shows should change.</summary>
    internal static string PanelKey => $"{PhotonNetwork.InRoom}{IsHost}{InAirport}{Collecting}{Sending}";

    internal static int Waiting(int actor) => instance != null && instance.counts.TryGetValue(actor, out int n) ? n : 0;
    internal static int TotalWaiting => instance == null ? 0 : instance.counts.Where(kv => kv.Key != Me).Sum(kv => kv.Value);
    internal static int MyUnsent => Unsent().Count;

    internal static void Collect() => instance?.StartCollect();
    internal static void StopCollect() => instance?.StopCollecting();
    internal static void SendMine() => instance?.SendToHost();

    internal static void AskCounts()
    {
        if (IsHost) Raise(Op.AskCount, Array.Empty<object>(), ReceiverGroup.Others);
    }

    // --- capture ---------------------------------------------------------------------------------

    private static int Me => PhotonNetwork.LocalPlayer?.ActorNumber ?? -1;

    private static string MyName => Safe(string.IsNullOrEmpty(PhotonNetwork.NickName) ? "me" : PhotonNetwork.NickName);

    private static string Safe(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Trim().Length == 0 ? "player" : s.Trim();
    }

    private static string NewStamp() => DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && !Typing && kb[ModConfig.BugReportKey.Value].wasPressedThisFrame)
        {
            string stamp = NewStamp();
            Raise(Op.Snap, new object[] { stamp, MyName }, ReceiverGroup.Others);
            StartCoroutine(Save(stamp, $"{ModConfig.BugReportKey.Value} pressed by {MyName}", toast: true));
        }

        // A new level = a new run: the next mod error may save a report again.
        string level = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (level != lastLevel)
        {
            lastLevel = level;
            if (level.StartsWith("Level_")) autoSaved = false;
        }
        if (pendingError != null)
        {
            string why = pendingError;
            pendingError = null;
            if (!autoSaved && InRun)
            {
                autoSaved = true;
                StartCoroutine(Save(NewStamp(), "automatic - first mod error this run:\n    " + why, toast: false));
            }
        }

        // A sender that went quiet for a minute is skipped.
        if (sendingActor >= 0 && Time.unscaledTime - sendingSince > 60f)
        {
            Plugin.Log.LogWarning($"Bug reports: player {sendingActor} stopped sending - skipping them");
            NextSender();
        }
    }

    private static bool InRun => MapHandler.Exists && !InAirport;

    /// <summary>Called by the log listener for an error that points at our mod (any thread).</summary>
    internal static void ModError(string text)
    {
        if (ModConfig.BugReportOnError.Value && pendingError == null) pendingError = text.Split('\n')[0];
    }

    private IEnumerator Save(string stamp, string why, bool toast)
    {
        yield return new WaitForEndOfFrame(); // the frame as it was, before any toast
        string dir = Path.Combine(Root, stamp, MyName);
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, MineMark), "");
            try
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(dir, "screenshot.jpg"), ImageConversion.EncodeToJPG(tex, 92));
                Destroy(tex);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Bug report: no screenshot ({e.Message})"); }
            File.WriteAllText(Path.Combine(dir, "log.txt"), ring.Dump(ModConfig.BugReportLogMinutes.Value));
            File.WriteAllText(Path.Combine(dir, "state.txt"), ReportState.Describe(why));
            string cfg = Plugin.ConfigPath;
            if (File.Exists(cfg)) File.Copy(cfg, Path.Combine(dir, "config.cfg"), true);
            Plugin.Log.LogInfo($"Bug report saved: {dir}");
            if (toast)
            {
                Hud.Toast("Report saved", ChoiceScreen.Cream);
                noteDir = dir;
                noteText = "";
                noteFocused = false;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Bug report failed: {e}");
            if (toast) Hud.Toast("Report failed - see the log", BloodMoon.Blood);
        }
        AnnounceCount();
    }

    // --- note box --------------------------------------------------------------------------------

    private void OnGUI()
    {
        if (noteDir == null) return;
        var e = Event.current;
        if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) { CloseNote(save: true); e.Use(); return; }
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { CloseNote(save: false); e.Use(); return; }

        float scale = Screen.height / 1080f;
        if (boxStyle == null)
        {
            boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.normal.background = Solid(new Color(0.03f, 0.04f, 0.06f, 0.92f));
            fieldStyle = new GUIStyle(GUI.skin.textField) { padding = new RectOffset(10, 10, 8, 8) };
            fieldStyle.normal.background = fieldStyle.focused.background = fieldStyle.hover.background = Solid(new Color(0.12f, 0.13f, 0.16f, 1f));
            fieldStyle.normal.textColor = fieldStyle.focused.textColor = fieldStyle.hover.textColor = Color.white;
            hintStyle = new GUIStyle(GUI.skin.label);
            hintStyle.normal.textColor = ChoiceScreen.Cream;
        }
        fieldStyle!.fontSize = Mathf.RoundToInt(24 * scale);
        hintStyle!.fontSize = Mathf.RoundToInt(22 * scale);

        float w = 900f * scale, h = 150f * scale;
        var box = new Rect((Screen.width - w) / 2f, Screen.height * 0.62f, w, h);
        GUI.Box(box, GUIContent.none, boxStyle);
        GUI.Label(new Rect(box.x + 20 * scale, box.y + 14 * scale, w - 40 * scale, 34 * scale), "What happened?   (Enter = save, Esc = skip)", hintStyle);
        GUI.SetNextControlName("prl.bugnote");
        noteText = GUI.TextField(new Rect(box.x + 20 * scale, box.y + 60 * scale, w - 40 * scale, 64 * scale), noteText, 400, fieldStyle);
        if (!noteFocused) { GUI.FocusControl("prl.bugnote"); noteFocused = true; }
    }

    private static Texture2D Solid(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    private static void CloseNote(bool save)
    {
        string? dir = noteDir;
        noteDir = null;
        if (!save || dir == null || noteText.Trim().Length == 0) return;
        try { File.WriteAllText(Path.Combine(dir, "note.txt"), noteText.Trim() + "\n"); }
        catch (Exception e) { Plugin.Log.LogWarning($"Bug report note failed: {e.Message}"); }
        Plugin.Log.LogInfo($"Bug report note: {noteText.Trim()}");
    }

    /// <summary>My saved reports that haven't reached a host yet (folder, stamp).</summary>
    private static List<(string dir, string stamp)> Unsent()
    {
        var list = new List<(string, string)>();
        if (!Directory.Exists(Root)) return list;
        foreach (string stampDir in Directory.GetDirectories(Root))
            foreach (string dir in Directory.GetDirectories(stampDir))
                if (File.Exists(Path.Combine(dir, MineMark)) && !File.Exists(Path.Combine(dir, SentMark)))
                    list.Add((dir, Path.GetFileName(stampDir)));
        return list.OrderBy(x => x.Item2).ToList();
    }

    // --- network ---------------------------------------------------------------------------------

    private static void Raise(Op op, object[] args, ReceiverGroup to) =>
        RaiseRaw(ControlEvent, Prepend((byte)op, args), new RaiseEventOptions { Receivers = to });

    private static void RaiseTo(int actor, Op op, object[] args) =>
        RaiseRaw(ControlEvent, Prepend((byte)op, args), new RaiseEventOptions { TargetActors = new[] { actor } });

    private static object[] Prepend(byte op, object[] args)
    {
        var all = new object[args.Length + 1];
        all[0] = op;
        Array.Copy(args, 0, all, 1, args.Length);
        return all;
    }

    private static void RaiseRaw(byte code, object[] content, RaiseEventOptions options)
    {
        if (PhotonNetwork.InRoom) PhotonNetwork.RaiseEvent(code, content, options, SendOptions.SendReliable);
    }

    private static void AnnounceCount()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient) return;
        RaiseRaw(ControlEvent, new object[] { (byte)Op.Count, MyUnsent }, new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient });
    }

    public override void OnJoinedRoom() => AnnounceCount();
    public override void OnMasterClientSwitched(Photon.Realtime.Player newMaster) { counts.Clear(); AskCounts(); AnnounceCount(); }

    public override void OnPlayerLeftRoom(Photon.Realtime.Player other)
    {
        counts.Remove(other.ActorNumber);
        foreach (var key in incoming.Keys.Where(k => k.StartsWith(other.ActorNumber + "|")).ToList()) incoming.Remove(key);
        if (other.ActorNumber == sendingActor) NextSender();
    }

    public void OnEvent(EventData e)
    {
        if (e.Code != ControlEvent && e.Code != ChunkEvent) return;
        try
        {
            if (e.CustomData is not object[] a) return;
            int from = e.Sender;
            if (e.Code == ChunkEvent) { ReceiveChunk(from, a); return; }
            switch ((Op)(byte)a[0])
            {
                case Op.Snap:
                    StartCoroutine(Save((string)a[1], $"{ModConfig.BugReportKey.Value} pressed by {(string)a[2]}", toast: false));
                    break;
                case Op.Count when IsHost:
                    counts[from] = (int)a[1];
                    break;
                case Op.AskCount:
                    AnnounceCount();
                    break;
                case Op.Collect:
                    if (sending == null) sending = StartCoroutine(SendAll(PhotonNetwork.MasterClient.ActorNumber));
                    break;
                case Op.Stop:
                    if (sending != null) { StopCoroutine(sending); sending = null; Plugin.Log.LogInfo("Bug reports: host stopped the transfer"); }
                    AnnounceCount();
                    break;
                case Op.Done when IsHost:
                    counts[from] = 0;
                    if (from == sendingActor) NextSender();
                    break;
                case Op.File when IsHost:
                    BeginFile(from, (string)a[1], (string)a[2], (string)a[3], (int)a[4]);
                    break;
                case Op.Ack:
                    MarkSent((string)a[1], (string)a[2]);
                    break;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Bad bug report event: {ex.Message}");
        }
    }

    // --- host: collecting ------------------------------------------------------------------------

    private void StartCollect()
    {
        if (!IsHost) return;
        queue.Clear();
        foreach (var p in PhotonNetwork.CurrentRoom.Players.Values.Where(p => !p.IsLocal).OrderBy(p => p.ActorNumber))
            if (Waiting(p.ActorNumber) > 0) queue.Enqueue(p.ActorNumber);
        if (queue.Count == 0) { Hud.Toast("No reports waiting", ChoiceScreen.Cream); AskCounts(); return; }
        NextSender();
    }

    private void NextSender()
    {
        sendingActor = -1;
        while (queue.Count > 0)
        {
            int actor = queue.Dequeue();
            if (PhotonNetwork.CurrentRoom?.GetPlayer(actor) == null) continue;
            sendingActor = actor;
            sendingSince = Time.unscaledTime;
            RaiseTo(actor, Op.Collect, Array.Empty<object>());
            Status = $"Collecting from {NameOf(actor)}...";
            return;
        }
        Status = "";
    }

    private void StopCollecting()
    {
        if (sendingActor >= 0) RaiseTo(sendingActor, Op.Stop, Array.Empty<object>());
        foreach (var key in incoming.Keys.Where(k => k.StartsWith(sendingActor + "|")).ToList()) incoming.Remove(key);
        queue.Clear();
        sendingActor = -1;
        Status = "";
        Hud.Toast("Collecting stopped", ChoiceScreen.Cream);
        AskCounts();
    }

    private static string NameOf(int actor)
    {
        var p = PhotonNetwork.CurrentRoom?.GetPlayer(actor);
        return p == null || string.IsNullOrEmpty(p.NickName) ? $"Player {actor}" : p.NickName;
    }

    private void BeginFile(int from, string stamp, string player, string name, int chunks)
    {
        if (sendingActor < 0) { sendingActor = from; sendingSince = Time.unscaledTime; } // a guest sending on their own
        incoming[$"{from}|{stamp}|{name}"] = new Incoming { Stamp = Safe(stamp), Player = Safe(player), Name = Safe(name), Chunks = chunks };
        if (chunks == 0) Finish(from, $"{from}|{stamp}|{name}");
    }

    private void ReceiveChunk(int from, object[] a)
    {
        if (!IsHost) return;
        string key = $"{from}|{(string)a[0]}|{(string)a[1]}";
        if (!incoming.TryGetValue(key, out var file)) return;
        file.Parts[(int)a[2]] = (byte[])a[3];
        sendingSince = Time.unscaledTime;
        Status = $"Collecting from {NameOf(from)}: {file.Name} {file.Parts.Count}/{file.Chunks}";
        if (file.Parts.Count >= file.Chunks) Finish(from, key);
    }

    private void Finish(int from, string key)
    {
        var file = incoming[key];
        incoming.Remove(key);
        string dir = Path.Combine(Root, file.Stamp, file.Player);
        Directory.CreateDirectory(dir);
        using (var fs = File.Create(Path.Combine(dir, file.Name)))
            for (int i = 0; i < file.Chunks; i++) fs.Write(file.Parts[i], 0, file.Parts[i].Length);
        // state.txt goes last: once it's here the whole report is.
        if (file.Name == "state.txt")
        {
            RaiseTo(from, Op.Ack, new object[] { file.Stamp, file.Player });
            Hud.Toast($"Report from {NameOf(from)} received", ChoiceScreen.Cream);
            Plugin.Log.LogInfo($"Bug report received: {dir}");
        }
    }

    // --- guest: sending --------------------------------------------------------------------------

    private void SendToHost()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient || sending != null) return;
        if (!InAirport) { Hud.Toast("Send reports from the airport", ChoiceScreen.Cream); return; }
        if (MyUnsent == 0) { Hud.Toast("No reports to send", ChoiceScreen.Cream); return; }
        sending = StartCoroutine(SendAll(PhotonNetwork.MasterClient.ActorNumber));
    }

    private static readonly string[] Order = { "screenshot.jpg", "log.txt", "config.cfg", "note.txt", "state.txt" };

    private IEnumerator SendAll(int host)
    {
        var reports = Unsent();
        Hud.Toast(reports.Count == 0 ? "No reports to send" : $"Sending {reports.Count} report{(reports.Count == 1 ? "" : "s")} to the host", ChoiceScreen.Cream);
        foreach (var (dir, stamp) in reports)
        {
            string player = Path.GetFileName(dir);
            foreach (string name in Order)
            {
                string path = Path.Combine(dir, name);
                if (!File.Exists(path)) continue;
                byte[] bytes;
                try { bytes = File.ReadAllBytes(path); }
                catch (Exception e) { Plugin.Log.LogWarning($"Bug reports: can't read {path}: {e.Message}"); continue; }
                int chunks = (bytes.Length + ChunkBytes - 1) / ChunkBytes;
                RaiseTo(host, Op.File, new object[] { stamp, player, name, chunks });
                for (int i = 0; i < chunks; i++)
                {
                    int len = Math.Min(ChunkBytes, bytes.Length - i * ChunkBytes);
                    var part = new byte[len];
                    Buffer.BlockCopy(bytes, i * ChunkBytes, part, 0, len);
                    RaiseRaw(ChunkEvent, new object[] { stamp, name, i, part }, new RaiseEventOptions { TargetActors = new[] { host } });
                    yield return new WaitForSecondsRealtime(ChunkGap);
                }
            }
        }
        RaiseTo(host, Op.Done, Array.Empty<object>());
        sending = null;
    }

    private static void MarkSent(string stamp, string player)
    {
        string dir = Path.Combine(Root, stamp, player);
        if (Directory.Exists(dir)) File.WriteAllText(Path.Combine(dir, SentMark), DateTime.Now.ToString("u"));
        AnnounceCount();
        if (MyUnsent == 0 && instance?.sending == null) Hud.Toast("Reports sent", ChoiceScreen.Cream);
    }

    // --- log capture -----------------------------------------------------------------------------

    /// <summary>Keeps the last few minutes of every log line (game and mods) in memory.</summary>
    private class LogRing : ILogListener
    {
        private readonly Queue<(DateTime at, string line)> lines = new();
        private readonly object gate = new();

        public void LogEvent(object sender, LogEventArgs e)
        {
            string text = e.Data?.ToString() ?? "";
            string line = $"[{DateTime.Now:HH:mm:ss}] [{e.Level,-7}] [{e.Source?.SourceName}] {text}";
            if ((e.Level & (LogLevel.Error | LogLevel.Fatal)) != 0)
            {
                line = "!!! " + line;
                if (e.Source?.SourceName == Plugin.Name || text.Contains("PeakRoguelike")) ModError(text);
            }
            lock (gate)
            {
                lines.Enqueue((DateTime.Now, line));
                var cut = DateTime.Now.AddMinutes(-15);
                while (lines.Count > 20000 || (lines.Count > 0 && lines.Peek().at < cut)) lines.Dequeue();
            }
        }

        public string Dump(float minutes)
        {
            var from = DateTime.Now.AddMinutes(-minutes);
            var sb = new StringBuilder();
            lock (gate)
                foreach (var (at, line) in lines)
                    if (at >= from) sb.AppendLine(line);
            return sb.ToString();
        }

        public void Dispose() { }
    }
}

/// <summary>state.txt: what the run looked like when the report was saved. Each part is
/// written on its own, so one broken part never loses the rest.</summary>
internal static class ReportState
{
    internal static string Describe(string why)
    {
        var sb = new StringBuilder();
        void Part(string title, Action write)
        {
            sb.Append("\n== ").Append(title).Append(" ==\n");
            try { write(); }
            catch (Exception e) { sb.Append($"  (couldn't read: {e.Message})\n"); }
        }
        void L(string s) => sb.Append("  ").Append(s).Append('\n');

        sb.Append($"Rogue Climb bug report\nWhy: {why}\n");
        Part("Build", () =>
        {
            L($"Mod: {DevPanel.Build}");
            L($"PEAK: {Application.version}");
            L($"Local time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            L($"Scene: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            if (PhotonNetwork.InRoom)
                L($"Lobby: {PhotonNetwork.CurrentRoom.PlayerCount} players, {(PhotonNetwork.IsMasterClient ? "I am host" : "I am a guest")}, ping {PhotonNetwork.GetPing()} ms, network time {PhotonNetwork.Time:0.0}");
            else L("Not in a lobby");
        });
        Part("World", () =>
        {
            if (!MapHandler.Exists) { L("No map (menu or airport)"); return; }
            L($"Section: {MapHandler.CurrentSegmentNumber}, biome {Singleton<MapHandler>.Instance.GetCurrentBiome()}");
            var dnm = DayNightManager.instance;
            if (dnm != null) L($"Day {dnm.dayCount} {dnm.TimeOfDayString()} ({(dnm.isDay > 0.5f ? "day" : "night")})");
            L($"Sun: {(Suns.Current is { } sun ? Suns.ColorName(sun) : "normal")}");
            L(BloodMoon.Active ? $"Blood moon: tier {BloodMoon.Tier} {(BloodMoon.Relay ? $"relay ({BloodMoon.RelaySlot:0}s each)" : "stacked")}, {BloodMoon.Elapsed:0}s in, order {BloodMoon.OrderLabel}" : "Blood moon: none");
            L($"Squad relics: {RunState.SquadRelics()}");
        });
        Part("Me", () =>
        {
            var me = Character.localCharacter;
            if (me == null) { L("No character"); return; }
            var d = me.data;
            L($"Scout: {me.characterName} at {me.Center} (height {me.Center.y:0.0})");
            L($"Dead {d.dead}, passed out {d.fullyPassedOut}, grounded {d.isGrounded}, climbing {d.isClimbing}, rope {d.isRopeClimbing}, vine {d.isVineClimbing}, since grounded {d.sinceGrounded:0.00}s");
            L($"Stamina {d.currentStamina:0.00} + extra {d.extraStamina:0.00}, safe ground {Sanctuary.LocalSafe}");
            var st = me.refs.afflictions.currentStatuses;
            var shown = new List<string>();
            for (int i = 0; i < st.Length; i++)
                if (st[i] > 0.001f) shown.Add($"{(CharacterAfflictions.STATUSTYPE)i} {st[i]:0.000}");
            L("Statuses: " + (shown.Count > 0 ? string.Join(", ", shown) : "none"));
            L($"XP: {Xp.Summary}");
        });
        Part("Pockets", () =>
        {
            var p = Player.localPlayer;
            if (p == null) { L("No player"); return; }
            for (int i = 0; i < p.itemSlots.Length; i++) L($"[{i}] {Slot(p.itemSlots[i])}");
            L($"[hand] {Slot(p.tempFullSlot)}");
            L($"[back] {(p.backpackSlot == null || p.backpackSlot.IsEmpty() ? "empty" : p.backpackSlot.GetPrefabName())}");
        });
        Part("Trees and relics", () =>
        {
            foreach (var tree in Trees.All.Values)
            {
                int lv = RunState.Level(tree.Id), br = RunState.Branch(tree.Id);
                if (lv > 0) L($"{tree.Name}: level {lv}{(br > 0 ? $" path {br}" : "")} ({tree.At(lv, br).Name})");
            }
            if (RunState.Fused.Count > 0) L("Fused: " + string.Join(", ", RunState.Fused));
            foreach (var kv in RunState.Relics)
                L($"Relic {kv.Key}: {kv.Value.Label()}{(RunState.IsCursed(kv.Key) ? " (cursed)" : "")}{(RunState.Stacks(kv.Key) > 0 ? $" x{RunState.Stacks(kv.Key) + 1}" : "")}");
            if (RunState.Relics.Count == 0) L("No relics");
        });
        Part("Players", () =>
        {
            if (!PhotonNetwork.InRoom) { L("Solo"); return; }
            var me = Character.localCharacter;
            foreach (var p in PhotonNetwork.CurrentRoom.Players.Values.OrderBy(x => x.ActorNumber))
            {
                string build = p.CustomProperties.TryGetValue(DevPanel.BuildKey, out object raw) && raw is string s ? s : "no mod";
                var c = Character.AllCharacters.FirstOrDefault(x => x != null && !x.isBot && x.photonView != null && x.photonView.OwnerActorNr == p.ActorNumber);
                string where = c == null ? "no scout" : $"at {c.Center}{(me != null && !p.IsLocal ? $", {Vector3.Distance(me.Center, c.Center):0}u from me" : "")}{(c.data.dead ? ", dead" : "")}";
                string relics = string.Join(" ", RunState.RelicsOf(p).Select(r => $"{r.gem}{(r.cursed ? "*" : "")}"));
                var curio = CurioRelics.Of(p);
                L($"{p.NickName} (#{p.ActorNumber}{(p.IsMasterClient ? ", host" : "")}): {build}; {where}{(relics.Length > 0 ? $"; relics {relics}" : "")}"
                    + $"; starting relic {(curio == Curio.None ? "none" : CurioRelics.Name(curio))}");
            }
        });
        Part("Threats", () =>
        {
            var me = Character.localCharacter;
            foreach (var sm in UnityEngine.Object.FindObjectsByType<Scoutmaster>(FindObjectsSortMode.None))
            {
                var c = sm.GetComponent<Character>();
                Vector3 at = c != null ? c.Center : sm.transform.position;
                var target = sm.currentTarget;
                L($"Scoutmaster at {at}{(me != null ? $" ({Vector3.Distance(me.Center, at):0}u from me)" : "")}, target {(target != null ? target.characterName : "none")}, throwing {sm.isThrowing}, holding {(c != null && c.data.grabbedPlayer != null ? c.data.grabbedPlayer.characterName : "no one")}");
            }
            L($"Zombies: {UnityEngine.Object.FindObjectsByType<MushroomZombie>(FindObjectsSortMode.None).Length}");
            foreach (var t in UnityEngine.Object.FindObjectsByType<Tornado>(FindObjectsSortMode.None))
                L($"Tornado at {t.transform.position}");
        });
        return sb.ToString();
    }

    private static string Slot(ItemSlot? slot)
    {
        if (slot == null || slot.IsEmpty()) return "empty";
        string name = slot.prefab != null ? Items.DisplayName(slot.prefab) : slot.GetPrefabName();
        int lv = ItemLevels.LevelOf(slot);
        return lv > 0 ? $"{name} (level {lv})" : name;
    }
}
