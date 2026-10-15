using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;

namespace PeakRoguelike;

/// <summary>
/// Game objects whose effects we borrow: the jungle rope bridge (creak, shake, snap) for
/// snapping ropes, and Alpine's crumbling pitons (crack, sounds, burst) for brittle pitons.
/// They are found once loaded (scene copies or prefabs in memory); if not, we fall back to shakes.
/// </summary>
internal static class Templates
{
    private static BreakableBridge? bridge;
    private static ShittyPiton? piton;
    private static float nextBridgeLook, nextPitonLook;

    public static BreakableBridge? Bridge
    {
        get
        {
            if (bridge != null || Time.time < nextBridgeLook) return bridge;
            nextBridgeLook = Time.time + 15f;
            bridge = Resources.FindObjectsOfTypeAll<BreakableBridge>().FirstOrDefault(b => b != null);
            if (bridge != null) Plugin.Log.LogInfo($"Borrowing bridge break effects from {bridge.name}.");
            return bridge;
        }
    }

    public static ShittyPiton? Piton
    {
        get
        {
            if (piton != null || Time.time < nextPitonLook) return piton;
            nextPitonLook = Time.time + 15f;
            piton = Resources.FindObjectsOfTypeAll<ShittyPiton>().FirstOrDefault(p => p != null && p.crack != null);
            if (piton != null) Plugin.Log.LogInfo($"Borrowing crumbling piton effects from {piton.name}.");
            return piton;
        }
    }

    public static void PlayAll(SFX_Instance[]? sounds, Vector3 at)
    {
        if (sounds == null) return;
        foreach (var s in sounds) if (s != null) s.Play(at);
    }
}

// ==========================================================================================
// Frayed Cord / Knotted Line: a few seconds of climbing, then it snaps
// ==========================================================================================

/// <summary>
/// Lives on a low-level rope on the client that tied it (the rope's owner). Every second
/// anyone spends climbing it counts (two climbers wear it twice as fast); the creak starts
/// so that it snaps right as the time runs out. ClimbSeconds 0 = the blue sun's rule: one
/// climb, then it snaps (a second climber or a re-grab snaps it at once).
/// </summary>
public class FrayedRope : MonoBehaviour
{
    public float ClimbSeconds;
    private Rope rope = null!;
    private bool occupied, usedOnce, snapping;
    private float worn;

    private void Awake() => rope = GetComponent<Rope>();

    private void Update()
    {
        if (snapping || rope == null || !rope.photonView.IsMine || rope.attachmenState != Rope.ATTACHMENT.anchored) return;
        int climbers = rope.charactersClimbing.Where(c => c != null).Distinct().Count();
        if (ClimbSeconds > 0f)
        {
            worn += climbers * Time.deltaTime;
            // The creak (RopeSnapFx) runs 1.4s before the snap.
            if (worn >= Mathf.Max(0.3f, ClimbSeconds - RopeSnapFx.CreakSeconds)) Snap();
            return;
        }
        if (climbers >= 2 || (climbers == 1 && usedOnce)) { Snap(); return; }
        if (climbers == 1) occupied = true;
        else if (occupied) { occupied = false; usedOnce = true; }
    }

    private void Snap()
    {
        snapping = true;
        Net.SnapRope(rope.photonView.ViewID);
    }
}

/// <summary>The bridge-break sequence on a rope: creak and shake, then it snaps and falls.</summary>
public class RopeSnapFx : MonoBehaviour
{
    internal const float CreakSeconds = 1.4f;
    private const float Linger = 6f;

    private Rope rope = null!;
    private AudioSource? creak;
    private float start, nextShake, nextLetGo;
    private bool snapped;
    private float climbShake = 6f;

    internal static void Play(int viewId)
    {
        var view = PhotonView.Find(viewId);
        var rope = view != null ? view.GetComponent<Rope>() : null;
        if (rope == null || rope.GetComponent<RopeSnapFx>()) return;
        rope.gameObject.AddComponent<RopeSnapFx>(); // dies with the rope
    }

    private void Awake()
    {
        rope = GetComponent<Rope>();
        start = Time.time;
        var bridge = Templates.Bridge;
        if (bridge != null)
        {
            climbShake = Mathf.Max(1f, bridge.climbingScreenShake);
            if (bridge.GetComponent<AudioSource>() is { clip: not null } src)
            {
                var go = new GameObject("Creak");
                go.transform.SetParent(transform, false);
                creak = go.AddComponent<AudioSource>();
                creak.clip = src.clip;
                creak.outputAudioMixerGroup = src.outputAudioMixerGroup;
                creak.spatialBlend = 1f;
                creak.rolloffMode = src.rolloffMode;
                creak.minDistance = src.minDistance;
                creak.maxDistance = src.maxDistance;
                creak.loop = true;
                creak.volume = 0.2f;
                creak.pitch = 0.9f;
                creak.transform.position = Middle();
                creak.Play();
            }
            GamefeelHandler.instance?.AddPerlinShakeProximity(Middle(), Mathf.Max(1f, bridge.startShakeAmount) * 0.5f, 0.3f, 15f, bridge.startShakeDistance);
        }
        else GamefeelHandler.instance?.AddPerlinShakeProximity(Middle(), 3f);
    }

    private List<Transform> Segments() => rope != null ? rope.GetRopeSegments() : new List<Transform>();

    private Vector3 Top()
    {
        var segs = Segments();
        return segs.Count > 0 && segs[0] != null ? segs[0].position : transform.position;
    }

    private Vector3 Middle()
    {
        var segs = Segments();
        return segs.Count > 0 && segs[segs.Count / 2] != null ? segs[segs.Count / 2].position : transform.position;
    }

    private void Update()
    {
        if (rope == null) return;
        float t = Time.time - start;
        Character me = Character.localCharacter;
        bool onIt = me != null && me.data.heldRope == rope;

        if (!snapped)
        {
            float k = Mathf.Clamp01(t / CreakSeconds);
            if (creak != null)
            {
                creak.transform.position = Middle();
                creak.pitch = Mathf.Lerp(0.9f, 1.3f, k);
                creak.volume = Mathf.Lerp(0.2f, 0.7f, k);
            }
            if (onIt && Time.time >= nextShake)
            {
                nextShake = Time.time + 0.2f;
                GamefeelHandler.instance?.AddPerlinShake(climbShake);
            }
            if (t >= CreakSeconds) Snap();
            return;
        }

        // Nobody holds on to a snapped rope.
        if (onIt && Time.time >= nextLetGo)
        {
            nextLetGo = Time.time + 0.3f;
            me!.refs.ropeHandling.view.RPC("StopRopeClimbingRpc", RpcTarget.All, false);
        }
        if (t > CreakSeconds + Linger && rope.photonView.IsMine)
        {
            PhotonNetwork.Destroy(rope.photonView);
            enabled = false;
        }
    }

    private void FixedUpdate()
    {
        if (snapped || rope == null || !rope.photonView.IsMine) return;
        // The owner simulates the rope; everyone else sees the shudder through rope sync.
        foreach (var seg in Segments())
            if (seg != null && seg.TryGetComponent(out Rigidbody rb))
                rb.AddForce(Random.insideUnitSphere * 30f, ForceMode.Acceleration);
    }

    private void Snap()
    {
        snapped = true;
        Vector3 at = Top();
        if (creak != null) creak.Stop();

        var bridge = Templates.Bridge;
        Templates.PlayAll(bridge?.breakSfx, at);
        if (bridge?.breakParticles != null) FibrePuff(bridge.breakParticles.gameObject, at);
        GamefeelHandler.instance?.AddPerlinShakeProximity(at, 4f, 0.3f);
        SnapTop();
    }

    private static readonly Color Fibre = new(0.78f, 0.66f, 0.48f, 1f);

    /// <summary>
    /// A small puff of rope fibres where it snapped. Borrowed from the bridge's break effect, but
    /// only its dust: every plank and debris piece (mesh particles, meshes) is switched off.
    /// </summary>
    private static void FibrePuff(GameObject template, Vector3 at)
    {
        var go = Instantiate(template, at, Quaternion.identity);
        go.transform.localScale = Vector3.one * 0.4f;
        int kept = 0;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            if (r is not ParticleSystemRenderer) r.enabled = false; // planks / rope pieces
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var pr = ps.GetComponent<ParticleSystemRenderer>();
            if (pr == null || pr.renderMode == ParticleSystemRenderMode.Mesh)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (pr != null) pr.enabled = false;
                continue;
            }
            var main = ps.main;
            main.maxParticles = 20;
            main.startColor = Fibre;
            main.startLifetime = 1f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.25f;
            ps.Play();
            kept++;
        }
        if (!loggedPuff) { loggedPuff = true; Plugin.Log.LogInfo($"Rope snap: fibre puff from {kept} dust effect(s) of the bridge break (planks hidden)."); }
        Destroy(go, 4f);
    }

    private static bool loggedPuff;

    private void SnapTop()
    {
        // Let the top end go: the rope drops from its anchor.
        if (rope.ropeBoneVisualizer != null) rope.ropeBoneVisualizer.StartTransform = null;
        if (rope.photonView.IsMine)
        {
            var segs = Segments();
            if (segs.Count > 0 && segs[0] != null)
                foreach (var joint in segs[0].GetComponents<ConfigurableJoint>())
                    if (joint.connectedBody == null) Destroy(joint);
        }
    }
}

// ==========================================================================================
// Brittle pitons: Alpine's crumbling-piton sequence on a piton you hammered in
// ==========================================================================================

/// <summary>
/// A low-level piton. It holds for its seconds of hanging in total: the first 40% quietly,
/// then four cracks (crack mesh grows, crack sounds, shakes) while someone hangs on, then it
/// bursts and lets go. The scout hanging on drives the timing; every step is broadcast.
/// </summary>
public class BrittlePiton : MonoBehaviour
{
    private const int CracksToBreak = 4;

    private static readonly List<(Vector3 pos, float seconds, float until)> pending = new();
    private ClimbHandle handle = null!;
    private float remaining, sinceCrack, crackInterval = 1.2f;
    private bool breaking, broken, startSent, breakSent;
    private int cracks, cracksSent;
    private GameObject? crack;
    private float crackScale, crackUnit = 1f;

    internal static void Mark(Vector3 pos, float seconds) => pending.Add((pos, seconds, Time.time + 10f));

    /// <summary>Called from Net's Update loop; attaches marks once the networked piton exists.</summary>
    internal static void Poll()
    {
        if (pending.Count == 0) return;
        ClimbHandle[] handles = FindObjectsByType<ClimbHandle>(FindObjectsSortMode.None);
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            var (pos, seconds, until) = pending[i];
            ClimbHandle? best = null;
            float bestD = 0.75f;
            foreach (var h in handles)
            {
                if (h == null || h.isPickaxe || h.GetComponent<BrittlePiton>() || h.GetComponent<ShittyPiton>()) continue;
                float d = Vector3.Distance(h.transform.position, pos);
                if (d < bestD) { bestD = d; best = h; }
            }
            if (best != null)
            {
                var b = best.gameObject.AddComponent<BrittlePiton>();
                b.handle = best;
                b.remaining = seconds * 0.4f;
                b.crackInterval = seconds * 0.6f / CracksToBreak;
                pending.RemoveAt(i);
            }
            else if (Time.time > until) pending.RemoveAt(i);
        }
    }

    private static BrittlePiton? At(Vector3 pos)
    {
        BrittlePiton? best = null;
        float bestD = 0.75f;
        foreach (var b in FindObjectsByType<BrittlePiton>(FindObjectsSortMode.None))
        {
            float d = Vector3.Distance(b.transform.position, pos);
            if (!b.broken && d < bestD) { bestD = d; best = b; }
        }
        return best;
    }

    private bool LocalHanging => handle != null && handle.hanger != null && handle.hanger.IsLocal;

    // --- network steps ------------------------------------------------------------------

    internal static void StartBreaking(Vector3 pos)
    {
        var b = At(pos);
        if (b == null || b.breaking) return;
        b.breaking = true;
        b.sinceCrack = 0f;
        b.SpawnCrack();
    }

    internal static void Crack(Vector3 pos)
    {
        var b = At(pos);
        if (b == null) return;
        b.cracks++;
        b.crackScale += 0.75f;
        GamefeelHandler.instance?.AddPerlinShakeProximity(b.transform.position, 2f + b.crackScale);
        var t = Templates.Piton;
        Templates.PlayAll(t?.cracKSFX, b.transform.position);
    }

    internal static void Break(Vector3 pos)
    {
        var b = At(pos);
        if (b == null) return;
        b.broken = true;
        var t = Templates.Piton;
        if (t != null && t.vfx != null)
        {
            var (relPos, relRot) = Relative(t.transform, t.vfx.transform);
            var vfx = Instantiate(t.vfx, b.transform.TransformPoint(relPos), b.transform.rotation * relRot);
            vfx.SetActive(true);
            Destroy(vfx, 8f);
        }
        Templates.PlayAll(t?.breakSFX, b.transform.position);
        GamefeelHandler.instance?.AddPerlinShakeProximity(b.transform.position, 6f);
        if (b.crack != null)
        {
            b.crack.transform.SetParent(null, true); // the crack stays in the rock
            Destroy(b.crack, 20f);
        }
        b.handle.Break();
    }

    // --- local --------------------------------------------------------------------------

    private static (Vector3 pos, Quaternion rot) Relative(Transform root, Transform child) =>
        (root.InverseTransformPoint(child.position), Quaternion.Inverse(root.rotation) * child.rotation);

    private void SpawnCrack()
    {
        var t = Templates.Piton;
        if (t == null || t.crack == null) return;
        var (relPos, relRot) = Relative(t.transform, t.crack.transform);
        crack = Instantiate(t.crack, transform);
        crack.transform.localPosition = relPos;
        crack.transform.localRotation = relRot;
        // Keep the crack the same world size as on Alpine's pitons whatever our piton's scale.
        float templateUnit = t.crack.transform.parent != null ? t.crack.transform.parent.lossyScale.x : 1f;
        crackUnit = templateUnit / Mathf.Max(0.0001f, transform.lossyScale.x);
        crack.transform.localScale = Vector3.zero;
        crack.SetActive(true);
    }

    private void Update()
    {
        if (crack != null && !broken)
            crack.transform.localScale = Vector3.Lerp(crack.transform.localScale, Vector3.one * crackScale * crackUnit, Time.deltaTime * 15f);

        if (broken || !LocalHanging) return;
        if (!breaking)
        {
            remaining -= Time.deltaTime;
            if (remaining <= 0f && !startSent)
            {
                startSent = true;
                Net.StartBreakPiton(transform.position);
            }
            return;
        }

        // Cracks only advance while someone hangs on (like Alpine's).
        sinceCrack += Time.deltaTime;
        if (sinceCrack < crackInterval || breakSent) return;
        sinceCrack = 0f;
        cracksSent = Mathf.Max(cracksSent, cracks) + 1;
        Net.CrackPiton(transform.position);
        if (cracksSent >= CracksToBreak)
        {
            breakSent = true;
            Net.BreakPiton(transform.position);
        }
    }
}
