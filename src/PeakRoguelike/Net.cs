using System;
using ExitGames.Client.Photon;
using Peak.Afflictions;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace PeakRoguelike;

public enum AuraKind : byte
{
    Siphon,     // Contaminated / Burden-Share Wrap: your injuries/poison are pulled into the bandager
    TraumaKit,  // (unused since the Saint path became the Med Kit)
    Purge,      // Cathartic Purge: clear every ailment nearby
}

/// <summary>
/// Mod-wide network messages (Photon events), for effects on things that have no
/// PhotonView of ours: auras that affect other players, pitons cracking and ropes snapping.
/// </summary>
internal class Net : MonoBehaviourPunCallbacks, IOnEventCallback
{
    private const byte RescueHealEvent = 168;
    private const byte BingBongEvent = 169;
    private const byte SupplyDropEvent = 170;
    private const byte AuraEvent = 171;
    private const byte PitonBrittleEvent = 172;
    private const byte PitonBreakEvent = 173;
    private const byte PitonStartEvent = 174;
    private const byte PitonCrackEvent = 175;
    private const byte RopeSnapEvent = 176;
    private const byte SpawnItemEvent = 177;
    private const byte BlessingEvent = 178;
    private const byte HuntEvent = 179;
    private const byte TornadoTargetEvent = 180;
    private const byte MeteorEvent = 181;
    private const byte BloodMoonDevEvent = 182;
    private const byte MealShareEvent = 183;
    private const byte DarkRiteEvent = 184;
    private const byte VeilEvent = 185;
    private const byte SkullCopiesEvent = 186;
    private const byte RelicFedEvent = 187;
    private const byte RelicShareEvent = 188;
    private const byte ToolUsedEvent = 189;
    private const byte PlacedEvent = 190;
    private const byte StackSpentEvent = 191;
    private const byte SharedRelicEvent = 192;
    private const byte SharedDoneEvent = 193;
    private const byte CureAllSplashEvent = 194;
    private const byte PandoraRollEvent = 195;
    private const byte XpFedEvent = 196;
    private const byte DeathDropEvent = 197;

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Net");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<Net>();
    }

    private static void Send(byte code, object[] content, ReceiverGroup to = ReceiverGroup.All)
    {
        if (!PhotonNetwork.InRoom) return;
        PhotonNetwork.RaiseEvent(code, content, new RaiseEventOptions { Receivers = to }, SendOptions.SendReliable);
    }

    public static void Aura(AuraKind kind, Vector3 origin, float radius) =>
        Send(AuraEvent, new object[] { (byte)kind, origin, radius }, ReceiverGroup.Others);

    public static void MarkBrittlePiton(Vector3 pos, float hangSeconds) =>
        Send(PitonBrittleEvent, new object[] { pos, hangSeconds });

    public static void BreakPiton(Vector3 pos) => Send(PitonBreakEvent, new object[] { pos });
    public static void StartBreakPiton(Vector3 pos) => Send(PitonStartEvent, new object[] { pos });
    public static void CrackPiton(Vector3 pos) => Send(PitonCrackEvent, new object[] { pos });
    public static void SnapRope(int ropeViewId) => Send(RopeSnapEvent, new object[] { ropeViewId });

    /// <summary>A copy of a stacked item was used up (the item stays as the next copy).</summary>
    public static void StackSpent(int itemViewId, int consumerViewId) => Send(StackSpentEvent, new object[] { itemViewId, consumerViewId });

    /// <summary>I died carrying these relics: the host drops their amulets where I last stood.</summary>
    public static void DeathDrop(int[] gems)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient) { DeathDrops.Received(Me, gems); return; }
        Send(DeathDropEvent, new object[] { Me, gems }, ReceiverGroup.MasterClient);
    }

    /// <summary>World items are room objects, so the host spawns them.</summary>
    public static void SpawnItem(string prefabName, Vector3 pos) =>
        Send(SpawnItemEvent, new object[] { prefabName, pos }, ReceiverGroup.MasterClient);

    /// <summary>Host: a revive statue was touched with nobody to revive - everyone gets a blessing.</summary>
    public static void SupplyDrop(Vector3 at, Vector3 look, string caller) => Send(SupplyDropEvent, new object[] { at, look, caller }, ReceiverGroup.MasterClient);

    /// <summary>A statue blessed the team (revive = it revived someone: everyone gets a random badge instead).</summary>
    public static void Bless(int statueViewId, bool revive = false) => Send(BlessingEvent, new object[] { statueViewId, revive });

    /// <summary>A fresh relic was bound: the host raises a blood moon (or, with blood moons off, everyone gets the Scoutmaster hunt).</summary>
    public static void RelicTaken(float huntSeconds, string binder, RelicGem gem) =>
        Send(HuntEvent, new object[] { huntSeconds, binder, (int)gem, Me });

    /// <summary>Host (testing): everyone but the binder gets this colour's relic choice.</summary>
    public static void SharedRelic(RelicGem gem, int binderActor) => Send(SharedRelicEvent, new object[] { (int)gem, binderActor });

    /// <summary>I chose (or skipped) my shared relic: the host may raise the pickup's moon once everyone has.</summary>
    public static void SharedDone()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient) { BloodMoon.SharedDone(Me); return; }
        Send(SharedDoneEvent, new object[] { Me }, ReceiverGroup.MasterClient);
    }

    /// <summary>Host: where a blood moon tornado heads next.</summary>
    public static void TornadoTarget(int viewId, Vector3 pos) => Send(TornadoTargetEvent, new object[] { viewId, pos });

    /// <summary>Host: a batch of meteors (9 floats each), everyone draws them and checks their own hits.</summary>
    public static void Meteors(float[] pack) => Send(MeteorEvent, new object[] { pack });

    /// <summary>Dev panel: ask the host to start (tier 1-4) or end (0) a blood moon now.</summary>
    public static void DevBloodMoon(int tier) => Send(BloodMoonDevEvent, new object[] { tier }, ReceiverGroup.MasterClient);

    /// <summary>Generosity: a meal shared with the scouts around you.</summary>
    public static void ShareMeal(Vector3 origin, float radius, float amount)
    {
        if (amount > 0f) Send(MealShareEvent, new object[] { origin, radius, amount }, ReceiverGroup.Others);
    }

    /// <summary>A thrown Cure-All broke here: everyone this close gets a dose (us too).</summary>
    public static void CureAllSplash(Vector3 at, float radius)
    {
        if (PhotonNetwork.InRoom) Send(CureAllSplashEvent, new object[] { at, radius });
        else Medicine.ReceiveSplash(at, radius);
    }

    /// <summary>We opened Pandora's Lunchbox: everyone this close takes the same roll.</summary>
    public static void PandoraRoll(Vector3 at, float radius, float[] roll, float stamina) =>
        Send(PandoraRollEvent, new object[] { at, radius, roll, stamina }, ReceiverGroup.Others);

    /// <summary>The Cursed Skull or the Ritual Dagger was used: the host raises a blood moon now.</summary>
    public static void DarkRite(string who)
    {
        if (PhotonNetwork.IsMasterClient) BloodMoon.DarkRite(who);
        else Send(DarkRiteEvent, new object[] { who }, ReceiverGroup.MasterClient);
    }

    /// <summary>My relic just fired under a blood moon: the host hides me from the hunters for a moment.</summary>
    public static void Veil(float seconds)
    {
        if (seconds <= 0f) return;
        if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient) { Squad.Veil(PhotonNetwork.LocalPlayer?.ActorNumber ?? 0, seconds); return; }
        Send(VeilEvent, new object[] { PhotonNetwork.LocalPlayer.ActorNumber, seconds }, ReceiverGroup.MasterClient);
    }

    /// <summary>A skull's sacrifice: every other scout gets cursed copies of these relics ("Gem:Trigger;...").</summary>
    public static void SkullCopies(string who, string relics) => Send(SkullCopiesEvent, new object[] { who, relics }, ReceiverGroup.Others);

    private static int Me => PhotonNetwork.LocalPlayer?.ActorNumber ?? -1;

    private static void SendTo(int actor, byte code, object[] content)
    {
        if (!PhotonNetwork.InRoom) return;
        PhotonNetwork.RaiseEvent(code, content, new RaiseEventOptions { TargetActors = new[] { actor } }, SendOptions.SendReliable);
    }

    /// <summary>We were fed / healed by this scout: their food or healing relics fire on both of us.</summary>
    /// <summary>Bing Bong's swaps: the other scout's half (see BingBongPranks.Receive).</summary>
    public static void Bing(int actor, int kind, object payload) => SendTo(actor, BingBongEvent, new object[] { Me, kind, payload });

    public static void RelicFed(int feederActor, bool food, bool healing) => SendTo(feederActor, RelicFedEvent, new object[] { Me, food, healing });
    /// <summary>XP for the scout who fed or bandaged us with a crate item.</summary>
    public static void XpFed(int feederActor, int xp) => SendTo(feederActor, XpFedEvent, new object[] { Me, xp });

    /// <summary>One of our relics fires on this scout (effect + glass on their side).</summary>
    public static void RelicShare(int actor, RelicGem gem) => SendTo(actor, RelicShareEvent, new object[] { Me, (int)gem });

    /// <summary>We let go of a tool this scout placed: their movement relics fire on us.</summary>
    public static void ToolUsed(int placerActor) => SendTo(placerActor, ToolUsedEvent, new object[] { Me });

    /// <summary>Disaster Response: our Rescue Claw pulled this scout - their game heals them.</summary>
    public static void RescueHeal(int targetActor) => SendTo(targetActor, RescueHealEvent, new object[] { Me });

    /// <summary>A piton or beanstalk was placed here by this scout (pitons and vines are room objects - no creator of their own).</summary>
    public static void Placed(Vector3 pos, int actor)
    {
        ToolRelics.AddPlacement(pos, actor);
        Send(PlacedEvent, new object[] { pos, actor }, ReceiverGroup.Others);
    }

    public override void OnJoinedRoom()
    {
        RunState.Republish();
        SugarRush.LoadFromRoom();
    }

    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable changed) => SugarRush.LoadFromRoom();

    private float nextPoll;

    private void Update()
    {
        if (Time.time < nextPoll) return;
        nextPoll = Time.time + 0.25f;
        BrittlePiton.Poll();
        SugarRush.PollExpected();
    }

    public void OnEvent(EventData e)
    {
        try
        {
            switch (e.Code)
            {
                case AuraEvent when e.CustomData is object[] a:
                    ReceiveAura((AuraKind)(byte)a[0], (Vector3)a[1], (float)a[2]);
                    break;
                case PitonBrittleEvent when e.CustomData is object[] a:
                    BrittlePiton.Mark((Vector3)a[0], (float)a[1]);
                    break;
                case PitonBreakEvent when e.CustomData is object[] a:
                    BrittlePiton.Break((Vector3)a[0]);
                    break;
                case PitonStartEvent when e.CustomData is object[] a:
                    BrittlePiton.StartBreaking((Vector3)a[0]);
                    break;
                case PitonCrackEvent when e.CustomData is object[] a:
                    BrittlePiton.Crack((Vector3)a[0]);
                    break;
                case RopeSnapEvent when e.CustomData is object[] a:
                    RopeSnapFx.Play((int)a[0]);
                    break;
                case SpawnItemEvent when e.CustomData is object[] a && PhotonNetwork.IsMasterClient:
                    PhotonNetwork.InstantiateItemRoom((string)a[0], (Vector3)a[1], Quaternion.identity);
                    break;
                case BingBongEvent when e.CustomData is object[] a:
                    BingBongPranks.Receive((int)a[0], (int)a[1], a[2]);
                    break;
                case SupplyDropEvent when e.CustomData is object[] a && PhotonNetwork.IsMasterClient:
                    PeakRoguelike.SupplyDrop.FlareDrop((Vector3)a[0], (Vector3)a[1], (string)a[2]);
                    break;
                case BlessingEvent when e.CustomData is object[] a:
                    Blessing.FromStatue((int)a[0], a.Length > 1 && a[1] is bool r && r);
                    break;
                case HuntEvent when e.CustomData is object[] a:
                    if (a.Length > 3) BloodMoon.ShareRelic((RelicGem)(int)a[2], (int)a[3]);
                    if (ModConfig.BloodMoon.Value) BloodMoon.RelicTaken((string)a[1]);
                    else if ((float)a[0] > 0f) ScoutmasterHunt.Begin((float)a[0], (string)a[1]);
                    break;
                case TornadoTargetEvent when e.CustomData is object[] a:
                    BloodTornado.SetTarget((int)a[0], (Vector3)a[1]);
                    break;
                case MeteorEvent when e.CustomData is object[] a:
                    PeakRoguelike.Meteors.Receive((float[])a[0]);
                    break;
                case BloodMoonDevEvent when e.CustomData is object[] a && PhotonNetwork.IsMasterClient:
                    BloodMoon.DevStart((int)a[0]);
                    break;
                case DarkRiteEvent when e.CustomData is object[] a && PhotonNetwork.IsMasterClient:
                    BloodMoon.DarkRite((string)a[0]);
                    break;
                case VeilEvent when e.CustomData is object[] a && PhotonNetwork.IsMasterClient:
                    Squad.Veil((int)a[0], (float)a[1]);
                    break;
                case SkullCopiesEvent when e.CustomData is object[] a:
                    Relics.ReceiveSkullCopies((string)a[0], (string)a[1]);
                    break;
                case DeathDropEvent when e.CustomData is object[] a && PhotonNetwork.IsMasterClient:
                    DeathDrops.Received((int)a[0], (int[])a[1]);
                    break;
                case XpFedEvent when e.CustomData is object[] a:
                    Xp.ReceiveFed((int)a[1]);
                    break;
                case RelicFedEvent when e.CustomData is object[] a:
                    Relics.ReceiveFed((int)a[0], (bool)a[1], (bool)a[2]);
                    break;
                case RelicShareEvent when e.CustomData is object[] a:
                    Relics.ReceiveShare((int)a[0], (RelicGem)(int)a[1]);
                    break;
                case RescueHealEvent when e.CustomData is object[] a:
                    Badges.ReceiveRescueHeal((int)a[0]);
                    break;
                case ToolUsedEvent when e.CustomData is object[] a:
                    Relics.ReceiveToolUsed((int)a[0]);
                    break;
                case SharedRelicEvent when e.CustomData is object[] a:
                    Relics.ReceiveSharedOffer((RelicGem)(int)a[0], (int)a[1]);
                    break;
                case SharedDoneEvent when e.CustomData is object[] a && PhotonNetwork.IsMasterClient:
                    BloodMoon.SharedDone((int)a[0]);
                    break;
                case StackSpentEvent when e.CustomData is object[] a:
                    Stacks.Spent((int)a[0], (int)a[1]);
                    break;
                case PlacedEvent when e.CustomData is object[] a:
                    ToolRelics.AddPlacement((Vector3)a[0], (int)a[1]);
                    break;
                case CureAllSplashEvent when e.CustomData is object[] a:
                    Medicine.ReceiveSplash((Vector3)a[0], (float)a[1]);
                    break;
                case PandoraRollEvent when e.CustomData is object[] a:
                    Medicine.ReceiveRoll((Vector3)a[0], (float)a[1], (float[])a[2], (float)a[3]);
                    break;
                case MealShareEvent when e.CustomData is object[] a:
                    Suns.ReceiveShare((Vector3)a[0], (float)a[1], (float)a[2]);
                    break;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Bad network event {e.Code}: {ex.Message}");
        }
    }

    private static void ReceiveAura(AuraKind kind, Vector3 origin, float radius)
    {
        Character me = Character.localCharacter;
        if (me == null || me.data.dead || Vector3.Distance(me.Center, origin) > radius) return;
        var aff = me.refs.afflictions;
        switch (kind)
        {
            case AuraKind.Siphon:
                aff.SubtractStatus(CharacterAfflictions.STATUSTYPE.Injury, 0.25f);
                aff.SubtractStatus(CharacterAfflictions.STATUSTYPE.Poison, 0.25f);
                break;
            case AuraKind.TraumaKit:
                aff.SubtractStatus(CharacterAfflictions.STATUSTYPE.Injury, 0.35f);
                break;
            case AuraKind.Purge:
                aff.ClearAllStatus(excludeCurse: true);
                break;
        }
    }
}
