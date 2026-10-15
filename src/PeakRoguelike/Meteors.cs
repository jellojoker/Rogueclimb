using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Audio;

namespace PeakRoguelike;

/// <summary>
/// Blood moon meteor shower (4 relics, Caldera runs). Inspired by CptPICHU's True Final Ascent
/// eruption (go play it): glowing rocks fall from high above, a red mark shows where each lands
/// 4 seconds ahead, and a direct hit knocks you down, injures and burns you.
/// On Gloom runs (Gloom, then Citadel) it rains purple glass instead: crystal shards in the
/// game's petrify-glass material, purple marks and trails, glass sounds, and a direct hit adds
/// glass instead of heat.
/// The host rolls the rocks and sends them out; every client draws them and checks only its
/// own scout for hits.
/// </summary>
internal class Meteors : MonoBehaviour
{
    /// <summary>Glass shower on Gloom runs (every client works it out from the map).</summary>
    private static bool Glass => BloodMoon.GloomRun;
    private static readonly Color GlassGlow = new(0.68f, 0.32f, 1f);
    private const int GlassPerHit = 10;

    private const float U = 1f / 1.6f;           // metres -> game units
    private const float FallSeconds = 4f;
    private const float AroundUnits = 55f * U;
    private const int Stride = 9;

    private class Rock
    {
        public Vector3 Land, Start, Normal = Vector3.up;
        public float Size, SpawnAt, LandAt;
        public int Seed;
        public bool Spawned, Landed, Glass;
        public GameObject? Body, Mark;
        public Light? Glow;
        public Material? MarkMat;
        public Vector3 Spin;
        public float GoneAt;
    }

    private static Meteors? instance;
    private readonly List<Rock> rocks = new();

    // host
    private static float until;
    private float rolledTo;
    private readonly Dictionary<int, float> next = new();
    private readonly List<float> pack = new();

    private void Awake() => instance = this;

    public static void Begin(float seconds)
    {
        until = Time.time + seconds;
        if (instance != null) { instance.rolledTo = Time.time; instance.next.Clear(); }
        Plugin.Log.LogInfo($"Blood moon meteors for {seconds:0}s.");
    }

    public static void Stop() => until = 0f;

    public static Camera? Eye()
    {
        var cam = MainCamera.instance != null ? MainCamera.instance.GetComponent<Camera>() : null;
        return cam != null ? cam : Camera.main;
    }

    // --- host: roll the rocks -----------------------------------------------------------------

    private void Update()
    {
        if (until > 0f && Time.time < until && PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient) Roll();
        Animate();
    }

    private void Roll()
    {
        float now = Time.time;
        if (now < rolledTo - 0.5f) return;
        float from = Mathf.Max(rolledTo, now), to = from + 0.5f;
        rolledTo = to;
        // Scouts on safe ground aren't aimed at, and no rock lands inside a campfire's circle.
        var alive = Squad.Hunted();
        if (alive.Count == 0) return;
        pack.Clear();
        float rate = Mathf.Max(0.05f, ModConfig.BmMeteorRate.Value);
        foreach (var c in alive)
        {
            int id = c.photonView.ViewID;
            if (!next.TryGetValue(id, out float t) || t < from - 3f) t = from + Random.Range(0f, 1f);
            for (; t < to; t += Mathf.Clamp(-Mathf.Log(1f - Random.value * 0.999f) / rate, 0.08f, 3f))
            {
                Aim(c, t - now);
                if (Random.value < 0.25f)
                    for (int i = Random.Range(1, 3); i > 0; i--) Aim(alive[Random.Range(0, alive.Count)], t - now + Random.Range(0.1f, 0.4f));
            }
            next[id] = t;
        }
        if (pack.Count > 0) Net.Meteors(pack.ToArray());
    }

    private void Aim(Character target, float delay)
    {
        Vector2 off = Random.insideUnitCircle * AroundUnits;
        float probe = 170f * U;
        var c = target.Center;
        if (!Physics.Raycast(new Vector3(c.x + off.x, c.y + probe, c.z + off.y), Vector3.down, out var hit, probe * 2f,
                HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore)) return;
        Vector3 land = hit.point;
        if (Sanctuary.Safe(land)) return;
        float h =Random.Range(110f, 150f) * U;
        if (Physics.Raycast(land + Vector3.up * 0.3f, Vector3.up, h, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore)) return; // roofed over
        Vector3 slant = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
        Vector3 start = land + Vector3.up * h + slant * (h * Random.Range(0.08f, 0.3f));
        if (Physics.Linecast(start, land + Vector3.up * 5f, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
            start = land + Vector3.up * h;
        pack.AddRange(new[] { land.x, land.y, land.z, start.x, start.y, start.z, Random.Range(3f, 8f) * U, Mathf.Max(0f, delay), (float)Random.Range(0, 1 << 20) });
    }

    // --- everyone: draw, land, hit ----------------------------------------------------------------

    public static void Receive(float[] data)
    {
        if (instance == null || data == null || !BloodMoon.Active) return;
        float now = Time.time;
        for (int i = 0; i + Stride <= data.Length; i += Stride)
        {
            var r = new Rock
            {
                Land = new Vector3(data[i], data[i + 1], data[i + 2]),
                Start = new Vector3(data[i + 3], data[i + 4], data[i + 5]),
                Size = Mathf.Clamp(data[i + 6], 1f, 6f),
                SpawnAt = now + Mathf.Clamp(data[i + 7], 0f, 1.5f),
                Seed = (int)data[i + 8],
            };
            r.LandAt = r.SpawnAt + FallSeconds;
            instance.rocks.Add(r);
        }
    }

    private void Animate()
    {
        float now = Time.time;
        for (int i = rocks.Count - 1; i >= 0; i--)
        {
            var r = rocks[i];
            if (now < r.SpawnAt) continue;
            if (!r.Spawned) Spawn(r);
            if (!r.Landed)
            {
                float t = Mathf.Clamp01((now - r.SpawnAt) / FallSeconds);
                float e = t * t;                                         // falling: speeds up
                if (r.Body != null)
                {
                    r.Body.transform.position = Vector3.Lerp(r.Start, r.Land + Vector3.up * r.Size * 0.3f, e);
                    r.Body.transform.Rotate(r.Spin * Time.deltaTime, Space.Self);
                }
                if (r.MarkMat != null)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(now * Mathf.Lerp(6f, 22f, t));
                    Color mark = r.Glass ? new Color(0.62f, 0.2f, 1f) : new Color(1f, 0.1f, 0.05f);
                    mark.a = Mathf.Lerp(0.18f, 0.6f, t) * Mathf.Lerp(0.6f, 1f, pulse);
                    r.MarkMat.SetColor("_Color", mark);
                }
                if (now >= r.LandAt) Land(r);
            }
            else
            {
                if (r.Glow != null) r.Glow.intensity = Mathf.MoveTowards(r.Glow.intensity, 0f, Time.deltaTime * 12f);
                if (now >= r.GoneAt)
                {
                    if (r.Body != null) Destroy(r.Body);
                    if (r.Mark != null) Destroy(r.Mark);
                    rocks.RemoveAt(i);
                }
            }
        }
    }

    private void Spawn(Rock r)
    {
        r.Spawned = true;
        r.Glass = Glass;
        var rng = new System.Random(r.Seed);
        r.Spin = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 240f;

        r.Body = new GameObject(r.Glass ? "Blood Glass Meteor" : "Blood Meteor");
        r.Body.transform.position = r.Start;
        r.Body.transform.localScale = Vector3.one * r.Size;
        r.Body.AddComponent<MeshFilter>().sharedMesh = r.Glass ? CrystalMesh(r.Seed % RockVariants) : RockMesh(r.Seed % RockVariants);
        var mr = r.Body.AddComponent<MeshRenderer>();
        mr.sharedMaterial = r.Glass ? GlassMaterial() : RockMaterial();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        r.Glow = new GameObject("Glow").AddComponent<Light>();
        r.Glow.transform.SetParent(r.Body.transform, false);
        r.Glow.type = LightType.Point;
        r.Glow.color = r.Glass ? GlassGlow : new Color(1f, 0.38f, 0.12f);
        r.Glow.range = r.Size * 7f;
        r.Glow.intensity = 4f;

        if (TrailMaterial() is { } tm)
        {
            var trail = r.Body.AddComponent<TrailRenderer>();
            trail.sharedMaterial = tm;
            trail.time = 0.7f;
            trail.widthMultiplier = r.Size * 0.9f;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            var g = new Gradient();
            if (r.Glass)
                g.SetKeys(new[] { new GradientColorKey(new Color(0.92f, 0.82f, 1f), 0f), new GradientColorKey(new Color(0.6f, 0.22f, 1f), 0.5f), new GradientColorKey(new Color(0.16f, 0.05f, 0.3f), 1f) },
                          new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.4f, 0.5f), new GradientAlphaKey(0f, 1f) });
            else
                g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.75f, 0.35f), 0f), new GradientColorKey(new Color(0.9f, 0.12f, 0.05f), 0.5f), new GradientColorKey(new Color(0.2f, 0.05f, 0.05f), 1f) },
                          new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.4f, 0.5f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = g;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // The warning mark where it will land.
        if (Physics.Raycast(r.Land + Vector3.up * 2f, Vector3.down, out var hit, 6f, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
            r.Normal = hit.normal;
        if (MarkMaterial() is { } mm)
        {
            r.Mark = new GameObject("Blood Meteor Mark");
            r.Mark.transform.position = r.Land + r.Normal * 0.15f;
            r.Mark.transform.rotation = Quaternion.LookRotation(r.Normal) * Quaternion.Euler(0f, 0f, rng.Next(360));
            r.Mark.transform.localScale = Vector3.one * (r.Size * 0.5f + 0.6f);
            r.Mark.AddComponent<MeshFilter>().sharedMesh = MarkMesh();
            var markR = r.Mark.AddComponent<MeshRenderer>();
            r.MarkMat = new Material(mm);
            markR.sharedMaterial = r.MarkMat;
            markR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        var cam = Eye();
        if (cam != null && Vector3.Distance(cam.transform.position, r.Land) < 60f * U) PlayAt(WhooshClips(), r.Land + Vector3.up * 8f, 0.5f, 1f);
    }

    private void Land(Rock r)
    {
        r.Landed = true;
        r.GoneAt = Time.time + 1.2f;
        if (r.Body != null)
        {
            r.Body.GetComponent<MeshRenderer>().enabled = false;
            if (r.Body.GetComponent<TrailRenderer>() is { } tr) tr.emitting = false;
            r.Body.transform.position = r.Land + Vector3.up * 0.5f;
        }
        if (r.Glow != null) { r.Glow.intensity = 14f; r.Glow.range = r.Size * 10f; }
        if (r.Mark != null) r.Mark.SetActive(false);

        float big = Mathf.InverseLerp(3f * U, 8f * U, r.Size);
        if (GamefeelHandler.instance != null)
            GamefeelHandler.instance.AddPerlinShakeProximity(r.Land, Mathf.Lerp(3f, 5f, big), 0.45f, 18f, 30f * U);
        if (r.Glass)
        {
            PlayAt(GlassClips(), r.Land + Vector3.up, Mathf.Lerp(0.85f, 1f, big), Mathf.Lerp(1.1f, 0.85f, big));
            PlayAt(ImpactClips(), r.Land + Vector3.up, Mathf.Lerp(0.35f, 0.5f, big), Mathf.Lerp(1.3f, 1f, big)); // the thud under the shatter
        }
        else PlayAt(ImpactClips(), r.Land + Vector3.up, Mathf.Lerp(0.75f, 1f, big), Mathf.Lerp(1.15f, 0.8f, big));
        Shatter(r);
        Hit(r);
    }

    /// <summary>A few glowing chunks bounce away from the impact.</summary>
    private void Shatter(Rock r)
    {
        var mesh = r.Glass ? CrystalMesh((r.Seed + 1) % RockVariants) : RockMesh((r.Seed + 1) % RockVariants);
        for (int i = 0; i < (r.Glass ? 9 : 6); i++)
        {
            var chunk = new GameObject("Meteor Chunk");
            chunk.transform.position = r.Land + Vector3.up * (r.Size * 0.4f) + Random.insideUnitSphere * r.Size * 0.3f;
            chunk.transform.localScale = Vector3.one * r.Size * (r.Glass ? Random.Range(0.1f, 0.22f) : Random.Range(0.15f, 0.3f));
            chunk.AddComponent<MeshFilter>().sharedMesh = mesh;
            chunk.AddComponent<MeshRenderer>().sharedMaterial = r.Glass ? GlassMaterial() : RockMaterial();
            var col = chunk.AddComponent<SphereCollider>();
            col.radius = 0.4f;
            var rb = chunk.AddComponent<Rigidbody>();
            rb.mass = 0.5f;
            rb.linearVelocity = (Random.insideUnitSphere + Vector3.up * 1.4f).normalized * Random.Range(6f, 14f);
            rb.angularVelocity = Random.insideUnitSphere * 10f;
            // Only bounce off the mountain, not scouts.
            chunk.layer = LayerMask.NameToLayer("Ignore Raycast");
            Destroy(chunk, Random.Range(2.5f, 4f));
        }
    }

    private static void Hit(Rock r)
    {
        var me = Character.localCharacter;
        if (me == null || me.data == null || me.data.dead || me.refs == null || Sanctuary.LocalSafe) return;
        Vector3 c = me.Center;
        float dx = c.x - r.Land.x, dz = c.z - r.Land.z;
        float reach = r.Size * 0.5f + 1f * U;
        float dy = c.y - r.Land.y;
        if (dx * dx + dz * dz > reach * reach || dy < -1.5f * U || dy > r.Size + 1f * U) return;
        if (Physics.Linecast(r.Land + Vector3.up * r.Size * 0.5f, c + Vector3.up * 0.3f, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore)) return;
        me.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Injury, 0.15f);
        if (r.Glass) me.refs.afflictions.AddPetrify(GlassPerHit);
        else me.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Hot, 0.1f);
        me.refs.view.RPC("RPCA_Fall", RpcTarget.All, 2f, 0f);
        Plugin.Log.LogInfo(r.Glass ? "Hit by a glass meteor." : "Hit by a meteor.");
    }

    // --- looks & sounds ----------------------------------------------------------------------------

    private const int RockVariants = 4;
    private static Mesh?[]? rockMeshes;
    private static Material? rockMat, markMat, trailMat;
    private static bool triedTrail, triedMark;
    private static Mesh? markMesh;

    /// <summary>A lumpy rock: a sphere pushed in and out by noise (seamless: noise is by position).</summary>
    private static Mesh RockMesh(int variant)
    {
        rockMeshes ??= new Mesh?[RockVariants];
        if (rockMeshes[variant] != null) return rockMeshes[variant]!;
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var src = tmp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(tmp);
        var verts = src.vertices;
        float o = variant * 13.7f;
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 v = verts[i].normalized;
            float n = Mathf.PerlinNoise(v.x * 2.2f + o, v.y * 2.2f + v.z * 1.3f + o) * 0.55f
                      + Mathf.PerlinNoise(v.z * 5f + o, v.x * 5f - v.y * 2f) * 0.2f;
            verts[i] = new Vector3(v.x, v.y * (0.8f + variant * 0.06f), v.z) * (0.38f + n * 0.45f);
        }
        var m = new Mesh { name = "Blood Meteor " + variant, vertices = verts, triangles = src.triangles, uv = src.uv };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return rockMeshes[variant] = m;
    }

    // --- glass ---------------------------------------------------------------------------------

    private static Mesh?[]? crystalMeshes;
    private static List<Mesh>? gameCrystals;
    private static Material? glassMat;

    /// <summary>
    /// A glass shard: one of the game's crystal meshes when it has readable ones (recentred and
    /// sized like our rocks), otherwise a faceted cluster of pointed prisms.
    /// </summary>
    private static Mesh CrystalMesh(int variant)
    {
        crystalMeshes ??= new Mesh?[RockVariants];
        if (crystalMeshes[variant] != null) return crystalMeshes[variant]!;
        if (gameCrystals == null)
        {
            gameCrystals = new List<Mesh>();
            var named = Resources.FindObjectsOfTypeAll<Mesh>()
                .Where(m => m != null && m.name.Length > 0 && System.Text.RegularExpressions.Regex.IsMatch(m.name, "crystal|shard|glass", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                .GroupBy(m => m.name).Select(g => g.First()).ToList();
            Plugin.Log.LogInfo("Glass meteor: game crystal meshes: " + (named.Count == 0 ? "none" : string.Join(", ", named.Take(25).Select(m => $"{m.name} ({m.vertexCount}v{(m.isReadable ? "" : ", locked")})"))));
            gameCrystals = named.Where(m => m.isReadable && m.vertexCount >= 24 && m.vertexCount <= 6000 && m.name.IndexOf("glasses", System.StringComparison.OrdinalIgnoreCase) < 0).ToList();
        }
        Mesh mesh;
        if (gameCrystals.Count > 0)
        {
            var src = gameCrystals[variant % gameCrystals.Count];
            var b = src.bounds;
            float fit = 1.3f / Mathf.Max(0.001f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
            var verts = src.vertices;
            for (int i = 0; i < verts.Length; i++) verts[i] = (verts[i] - b.center) * fit;
            mesh = new Mesh { name = "Blood Glass " + src.name, vertices = verts, triangles = src.triangles, uv = src.uv };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (variant == 0) Plugin.Log.LogInfo($"Glass meteors use the game's '{src.name}' mesh.");
        }
        else mesh = ShardCluster(variant);
        return crystalMeshes[variant] = mesh;
    }

    private static Mesh ShardCluster(int variant)
    {
        var rng = new System.Random(variant * 7919 + 3);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        int spikes = 4 + variant % 3;
        for (int s = 0; s < spikes; s++)
        {
            Vector3 dir = s == 0 ? Vector3.up : Quaternion.Euler(R(25f, 70f), R(0f, 360f), 0f) * Vector3.up;
            float len = s == 0 ? R(1.05f, 1.25f) : R(0.55f, 0.9f);
            float rad = len * R(0.18f, 0.24f);
            Spike(verts, uvs, -dir * 0.18f, dir, len, rad, s % 2 == 0 ? 6 : 5, R(0f, 60f));
        }
        // Centre it so it spins about its middle.
        var b = new Bounds(verts[0], Vector3.zero);
        foreach (var v in verts) b.Encapsulate(v);
        float fit = 1.3f / Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        for (int i = 0; i < verts.Count; i++) verts[i] = (verts[i] - b.center) * fit;
        var m = new Mesh { name = "Blood Glass Cluster " + variant };
        m.SetVertices(verts);
        m.SetUVs(0, uvs);
        m.SetTriangles(Enumerable.Range(0, verts.Count).ToArray(), 0);
        m.RecalculateNormals(); // every triangle has its own corners: flat, faceted glass
        m.RecalculateBounds();
        return m;
    }

    /// <summary>A pointed prism: a base ring, a shoulder ring, then the tip (unshared corners).</summary>
    private static void Spike(List<Vector3> verts, List<Vector2> uvs, Vector3 root, Vector3 dir, float len, float rad, int sides, float twist)
    {
        var rot = Quaternion.FromToRotation(Vector3.up, dir) * Quaternion.Euler(0f, twist, 0f);
        Vector3 Ring(int i, float h, float r)
        {
            float a = i / (float)sides * Mathf.PI * 2f;
            return root + rot * new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r);
        }
        Vector3 bottom = root + rot * new Vector3(0f, -len * 0.05f, 0f), tip = root + rot * new Vector3(0f, len, 0f);
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            verts.Add(a); verts.Add(b); verts.Add(c);
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f)); uvs.Add(new Vector2(0.5f, 1f));
        }
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            Vector3 b0 = Ring(i, 0f, rad), b1 = Ring(j, 0f, rad);
            Vector3 s0 = Ring(i, len * 0.72f, rad * 0.92f), s1 = Ring(j, len * 0.72f, rad * 0.92f);
            Tri(bottom, b1, b0);           // base
            Tri(b0, b1, s1); Tri(b0, s1, s0); // side
            Tri(s0, s1, tip);              // point
        }
    }

    /// <summary>
    /// The game's own glass: the material scouts turn into when petrified (fully set), tinted
    /// purple and lit from within. A plain glossy purple if no scout's glass can be found.
    /// </summary>
    private static Material GlassMaterial()
    {
        if (glassMat != null) return glassMat;
        Material? src = null;
        foreach (var c in Character.AllCharacters)
        {
            var rs = c != null && c.refs.customization != null && c.refs.customization.refs != null ? c.refs.customization.refs.stoneRenderers : null;
            var r = rs?.FirstOrDefault(x => x != null && x.sharedMaterial != null);
            if (r != null) { src = r.sharedMaterial; break; }
        }
        Color purple = new(0.62f, 0.3f, 1f);
        if (src != null)
        {
            glassMat = new Material(src) { name = "Blood Glass (" + src.name + ")" };
            if (glassMat.HasProperty("_Petrify")) glassMat.SetFloat("_Petrify", 1f);
            foreach (var p in new[] { "_Tint", "_BaseColor", "_Color", "_MainColor", "_GlassColor" })
                if (glassMat.HasProperty(p)) glassMat.SetColor(p, purple);
            Plugin.Log.LogInfo($"Glass meteors use the petrify glass '{src.name}' (shader {src.shader.name}; colours: {string.Join(", ", glassMat.GetPropertyNames(MaterialPropertyType.Vector))}).");
        }
        else
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            glassMat = new Material(sh) { name = "Blood Glass" };
            glassMat.SetColor("_BaseColor", purple);
            glassMat.SetColor("_Color", purple);
            glassMat.SetFloat("_Smoothness", 0.95f);
            glassMat.SetFloat("_Metallic", 0.2f);
            Plugin.Log.LogInfo("Glass meteors: no petrify glass found - plain purple glass.");
        }
        glassMat.EnableKeyword("_EMISSION");
        glassMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        if (glassMat.HasProperty("_EmissionColor")) glassMat.SetColor("_EmissionColor", GlassGlow * 0.9f);
        return glassMat;
    }

    private static AudioClip[]? glassClips;

    /// <summary>The game's glass sounds: the petrify crackle / full-petrify shatter, plus any clip named glass/crystal/shatter.</summary>
    private static AudioClip[] GlassClips()
    {
        if (glassClips is { Length: > 0 }) return glassClips;
        var found = new List<AudioClip>();
        foreach (var c in Character.AllCharacters)
        {
            var aff = c != null ? c.refs.afflictions : null;
            if (aff == null) continue;
            foreach (var sfx in new[] { aff.fullPetrifySFX, aff.injuryPetrify })
                if (sfx != null && sfx.clips != null) found.AddRange(sfx.clips.Where(x => x != null));
            if (found.Count > 0) break;
        }
        Clips(); // fills the name cache
        found.AddRange(clips!.Values.Where(x => System.Text.RegularExpressions.Regex.IsMatch(x.name, "glass|crystal|shatter", System.Text.RegularExpressions.RegexOptions.IgnoreCase)));
        glassClips = found.Distinct().ToArray();
        foreach (var c in glassClips) if (c.loadState == AudioDataLoadState.Unloaded) c.LoadAudioData();
        Plugin.Log.LogInfo("Glass meteor sounds: " + (glassClips.Length == 0 ? "none found - rock impacts only" : string.Join(", ", glassClips.Select(x => x.name))));
        if (glassClips.Length == 0) glassClips = ImpactClips();
        return glassClips;
    }

    private static Material RockMaterial()
    {
        if (rockMat != null) return rockMat;
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        rockMat = new Material(sh) { name = "Blood Meteor Rock" };
        rockMat.SetColor("_BaseColor", new Color(0.13f, 0.09f, 0.08f));
        rockMat.SetColor("_Color", new Color(0.13f, 0.09f, 0.08f));
        rockMat.SetFloat("_Smoothness", 0.15f);
        rockMat.EnableKeyword("_EMISSION");
        rockMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        rockMat.SetColor("_EmissionColor", new Color(1f, 0.28f, 0.06f) * 1.6f);
        return rockMat;
    }

    private static Material? MarkMaterial()
    {
        if (markMat != null || triedMark) return markMat;
        triedMark = true;
        var sh = Shader.Find("Hidden/Internal-Colored");
        if (sh == null) return null;
        markMat = new Material(sh) { name = "Blood Meteor Mark" };
        markMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        markMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        markMat.SetInt("_ZWrite", 0);
        markMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
        markMat.SetInt("_Cull", 0);
        markMat.SetFloat("_ZBias", -1f);
        markMat.renderQueue = 3000;
        return markMat;
    }

    private static Material? TrailMaterial()
    {
        if (trailMat != null || triedTrail) return trailMat;
        triedTrail = true;
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
        if (sh == null) return null;
        trailMat = new Material(sh) { name = "Blood Meteor Trail" };
        trailMat.SetColor("_BaseColor", Color.white);
        trailMat.SetColor("_Color", Color.white);
        // Particles/Unlit: transparent, additive
        trailMat.SetFloat("_Surface", 1f);
        trailMat.SetFloat("_Blend", 2f);
        trailMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        trailMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        trailMat.SetInt("_ZWrite", 0);
        trailMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        trailMat.renderQueue = 3000;
        return trailMat;
    }

    /// <summary>A target ring: solid centre dot, a gap, then a ring (vertex colours carry the shape).</summary>
    private static Mesh MarkMesh()
    {
        if (markMesh != null) return markMesh;
        const int segs = 48;
        float[] radii = { 0f, 0.28f, 0.34f, 0.7f, 0.82f, 1f };
        float[] alpha = { 1f, 1f, 0f, 0f, 1f, 0.6f };
        var verts = new List<Vector3>();
        var cols = new List<Color>();
        var tris = new List<int>();
        for (int r = 0; r < radii.Length; r++)
            for (int s = 0; s < segs; s++)
            {
                float a = s / (float)segs * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(a) * radii[r], Mathf.Sin(a) * radii[r], 0f));
                cols.Add(new Color(1f, 1f, 1f, alpha[r]));
            }
        for (int r = 0; r < radii.Length - 1; r++)
        {
            int a0 = r * segs, b0 = (r + 1) * segs;
            for (int s = 0; s < segs; s++)
            {
                int s1 = (s + 1) % segs;
                tris.AddRange(new[] { a0 + s, a0 + s1, b0 + s, a0 + s1, b0 + s1, b0 + s });
            }
        }
        markMesh = new Mesh { name = "Blood Meteor Mark" };
        markMesh.SetVertices(verts);
        markMesh.SetColors(cols);
        markMesh.SetTriangles(tris, 0);
        markMesh.RecalculateBounds();
        return markMesh;
    }

    private static Dictionary<string, AudioClip>? clips;

    private static AudioClip[] Clips(params string[] names)
    {
        if (clips == null)
        {
            clips = new Dictionary<string, AudioClip>();
            foreach (var c in Resources.FindObjectsOfTypeAll<AudioClip>())
                if (c != null && !clips.ContainsKey(c.name)) clips[c.name] = c;
        }
        var found = names.Where(clips.ContainsKey).Select(n => clips[n]).ToArray();
        foreach (var c in found) if (c.loadState == AudioDataLoadState.Unloaded) c.LoadAudioData();
        return found;
    }

    private static AudioClip[]? impacts, whooshes;
    private static AudioClip[] ImpactClips() => impacts is { Length: > 0 } ? impacts
        : impacts = Clips("Au_Explosion_Deep", "Au_explosionRock1", "Au_ExplosionLayer1", "Au_Destroy_Stone2_1", "Au_Destroy_Stone2_2", "rocket BOOM");
    private static AudioClip[] WhooshClips() => whooshes is { Length: > 0 } ? whooshes
        : whooshes = Clips("Au_Passby_Debris1", "Au_Passby_Debris3");

    private readonly List<AudioSource> voices = new();
    private int voice;

    private void PlayAt(AudioClip[] options, Vector3 pos, float volume, float pitch)
    {
        if (options.Length == 0) return;
        if (voices.Count < 12)
        {
            var go = new GameObject("Meteor Voice");
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.dopplerLevel = 0f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 16f * U;
            src.maxDistance = 400f * U;
            src.outputAudioMixerGroup = MixerGroup("SFX");
            voices.Add(src);
        }
        var s = voices[voice++ % voices.Count];
        s.transform.position = pos;
        s.pitch = pitch * Random.Range(0.93f, 1.07f);
        s.PlayOneShot(options[Random.Range(0, options.Length)], volume);
    }

    private static readonly Dictionary<string, AudioMixerGroup?> groups = new();

    /// <summary>The game's mixer group whose name contains this (so the volume sliders apply).</summary>
    internal static AudioMixerGroup? MixerGroup(string contains)
    {
        if (groups.TryGetValue(contains, out var g) && g != null) return g;
        g = Resources.FindObjectsOfTypeAll<AudioMixerGroup>()
            .FirstOrDefault(x => x != null && x.name.IndexOf(contains, System.StringComparison.OrdinalIgnoreCase) >= 0);
        groups[contains] = g;
        return g;
    }
}
