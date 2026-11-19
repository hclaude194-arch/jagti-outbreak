using System.Collections.Generic;
using Jagti.Net;
using Jagti.Player;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// Keeps an online match in step. Every machine runs the same match from the host's seed; this
    /// passes on what can't be worked out locally:
    ///  - where everyone is (each player sends their own state; the host sends everyone's, bots too),
    ///  - hits (the attacker's machine sends them to the victim's, which applies armour and health
    ///    and tells everyone the result, so deaths, the feed and placements follow everywhere),
    ///  - shots and throws (so others see the tracers and grenades and hear the gunfire),
    ///  - loot dropped and taken, supply drops, and the haze clock.
    /// Clients talk only to the host, which passes their messages on to the others.
    /// </summary>
    public class NetMatch : MonoBehaviour
    {
        /// <summary>The "weapon" for someone who left mid-match (the kill feed says so).</summary>
        public const string LeftWeapon = "left";

        public float stateRate = 20f;      // client -> host
        public float snapshotRate = 15f;   // host -> clients

        NetSession session;
        NetPeer peer;
        MatchDirector director;
        LootDirector loot;
        Combatant me;
        FirstPersonController fpc;
        SkyDiver diver;
        PlayerArsenal arsenal;
        PlayerMover mover;

        readonly Dictionary<int, NetPuppet> puppets = new Dictionary<int, NetPuppet>();
        readonly List<KeyValuePair<Combatant, HumanBody>> ownedBots = new List<KeyValuePair<Combatant, HumanBody>>();
        readonly NetWriter relay = new NetWriter(NetPeer.MaxPacket);
        readonly List<Vector3> ends = new List<Vector3>();
        float sendTimer, hazeTimer;

        bool Host { get { return session.mode == NetSession.Mode.Host; } }

        public void Init(NetSession s, MatchDirector d)
        {
            session = s;
            peer = s.peer;
            director = d;
            loot = d.loot;
            me = d.player;
            if (me != null)
            {
                fpc = me.GetComponent<FirstPersonController>();
                diver = me.GetComponent<SkyDiver>();
                arsenal = me.GetComponent<PlayerArsenal>();
                mover = me.GetComponent<PlayerMover>();
            }
            foreach (var c in Combatant.All)
            {
                var p = c.GetComponent<NetPuppet>();
                if (p != null) puppets[c.netId] = p;
                else if (c.owned && !c.isPlayer)
                {
                    var brain = c.GetComponent<BotBrain>();
                    ownedBots.Add(new KeyValuePair<Combatant, HumanBody>(c, brain != null ? brain.body : null));
                }
            }
            if (loot != null)
            {
                loot.idBase = session.LocalId << 20;
                loot.Added += OnLootAdded;
                loot.Changed += OnLootChanged;
                loot.StashOpened += OnStashOpened;
            }
            Combatant.ForwardHit = OnForwardHit;
            Combatant.Damaged += OnDamaged;
            Ballistics.Fired = OnFired;
            Grenade.Launched = OnLaunched;
            director.SupplyDropSent = OnSupplyDrop;
        }

        void OnDestroy()
        {
            if (loot != null)
            {
                loot.Added -= OnLootAdded;
                loot.Changed -= OnLootChanged;
                loot.StashOpened -= OnStashOpened;
            }
            if (Combatant.ForwardHit == (System.Action<Combatant, float, bool, Combatant, string, Vector3, bool>)OnForwardHit) Combatant.ForwardHit = null;
            Combatant.Damaged -= OnDamaged;
            Ballistics.Fired = null;
            Grenade.Launched = null;
            if (session != null && session.match == this) session.match = null;
        }

        // ------------------------------------------------------------------ the regular sends

        void Update()
        {
            if (session == null || !NetSession.Online) return;
            sendTimer -= Time.unscaledDeltaTime;
            if (sendTimer <= 0f)
            {
                sendTimer = 1f / (Host ? snapshotRate : stateRate);
                if (Host) SendSnapshot(); else SendState();
            }
            if (Host && director.haze != null && director.haze.Running)
            {
                hazeTimer -= Time.unscaledDeltaTime;
                if (hazeTimer <= 0f)
                {
                    hazeTimer = 2f;
                    var h = director.haze;
                    var w = peer.BeginMessage();
                    w.Byte(NetMsg.Haze);
                    w.Byte((byte)h.RoundIndex);
                    w.Bool(h.Closing);
                    w.Float(h.TimeLeft);
                    peer.Broadcast(w, false);
                }
            }
        }

        /// <summary>This machine's player as others should see them.</summary>
        FigureState Mine()
        {
            var l = me.loadout;
            var s = new FigureState
            {
                position = me.transform.position,
                yaw = me.transform.eulerAngles.y,
                landed = me.Landed,
                health = me.Health.current,
                vest = l.vest, helmet = l.helmet, pack = l.backpack,
                gun = l.Current != null ? l.Current.def.id : -1,
                pose = HumanBody.Pose.Stand,
            };
            s.armed = s.gun >= 0;
            if (fpc != null)
            {
                s.pitch = -fpc.Pitch;
                s.speed = fpc.HorizontalSpeed;
                s.crouch = fpc.CrouchAmount;
            }
            var body = diver != null ? diver.body : null;
            s.aiming = s.armed && ((arsenal != null && arsenal.Aiming) || (body != null && body.aiming));
            if (diver != null)
            {
                switch (diver.Current)
                {
                    case SkyDiver.State.Waiting:
                    case SkyDiver.State.Aboard: s.hidden = true; break;
                    case SkyDiver.State.FreeFall: s.pose = HumanBody.Pose.FreeFall; s.armed = false; break;
                    case SkyDiver.State.Glide: s.pose = HumanBody.Pose.Glide; s.armed = false; break;
                }
            }
            if (mover != null && mover.State == PlayerMover.Mode.Zipping)
            {
                s.pose = HumanBody.Pose.Hang;
                s.armed = false;
                if (body != null) s.yaw = body.transform.eulerAngles.y;
            }
            return s;
        }

        void SendState()
        {
            if (me == null) return;
            var w = peer.BeginMessage();
            w.Byte(NetMsg.State);
            w.Float(Time.unscaledTime);
            Mine().Write(w);
            peer.SendUnreliable(peer.Server, w);
        }

        void SendSnapshot()
        {
            if (peer.ConnectedCount == 0) return;
            var w = peer.BeginMessage();
            w.Byte(NetMsg.Snapshot);
            w.Float(Time.unscaledTime);
            int countAt = w.Length;
            w.Byte(0);
            int n = 0;
            if (me != null)
            {
                w.Byte((byte)me.netId);
                Mine().Write(w);
                n++;
            }
            foreach (var kv in ownedBots)
            {
                if (kv.Key == null) continue;
                w.Byte((byte)kv.Key.netId);
                FigureState.OfBody(kv.Key, kv.Value).Write(w);
                n++;
            }
            foreach (var kv in puppets)
            {
                if (kv.Value == null || !kv.Value.HasState) continue;
                w.Byte((byte)kv.Key);
                kv.Value.Latest.Write(w);
                n++;
            }
            w.data[countAt] = (byte)n;
            peer.Broadcast(w, false);
        }

        // ------------------------------------------------------------------ things that happen here

        void OnForwardHit(Combatant victim, float damage, bool head, Combatant attacker, string weapon, Vector3 from, bool ignoreArmour)
        {
            var w = peer.BeginMessage();
            w.Byte(NetMsg.Hit);
            w.Byte((byte)victim.netId);
            w.Byte((byte)attacker.netId);
            w.Float(damage);
            w.Bool(head);
            w.String(weapon);
            NetMsg.Position(w, from);
            w.Bool(ignoreArmour);
            if (Host) peer.SendReliable(peer.Find(victim.netId), w);   // to that player's machine
            else peer.SendReliable(peer.Server, w);
        }

        void OnDamaged(Combatant.DamageInfo d)
        {
            if (d.victim == null || !d.victim.owned) return;   // results are told by the victim's machine only
            float after = d.killing ? 0f : Mathf.Max(0f, d.victim.Health.current - d.amount);
            var w = peer.BeginMessage();
            w.Byte(NetMsg.Result);
            w.Byte((byte)d.victim.netId);
            w.Byte((byte)(d.attacker != null ? d.attacker.netId : 0));
            w.Float(d.amount);
            w.Float(d.absorbed);
            w.Byte((byte)((d.head ? 1 : 0) | (d.armourBroke ? 2 : 0) | (d.killing ? 4 : 0)));
            w.Byte((byte)d.armourLevel);
            w.String(d.weapon);
            w.Float(after);
            NetMsg.Position(w, d.victim.LastHitFrom);
            peer.Broadcast(w, true);
        }

        void OnFired(Combatant shooter, GunItem gun, Vector3 origin, List<Vector3> shotEnds, int worldMask)
        {
            var w = peer.BeginMessage();
            w.Byte(NetMsg.Shot);
            w.Byte((byte)shooter.netId);
            w.Byte((byte)gun.def.id);
            w.Bool(gun.Suppressed);
            NetMsg.Position(w, origin);
            int n = Mathf.Min(shotEnds.Count, 16);
            w.Byte((byte)n);
            for (int i = 0; i < n; i++) NetMsg.Position(w, shotEnds[i]);
            w.UShort((ushort)(worldMask & 0xffff));
            peer.Broadcast(w, false);
        }

        void OnLaunched(Combatant thrower, bool smoke, Vector3 from, Vector3 velocity)
        {
            var w = peer.BeginMessage();
            w.Byte(NetMsg.Throw);
            w.Byte((byte)thrower.netId);
            w.Bool(smoke);
            NetMsg.Vector(w, from);
            NetMsg.Vector(w, velocity);
            peer.Broadcast(w, true);
        }

        void OnLootAdded(LootDrop d)
        {
            var w = peer.BeginMessage();
            w.Byte(NetMsg.LootAdd);
            w.Int(d.id);
            NetMsg.Vector(w, d.position);
            w.Angle(d.yaw);
            w.Byte((byte)d.kind);
            w.Byte((byte)d.value);
            w.Byte((byte)d.tier);
            w.UShort((ushort)Mathf.Clamp(d.count, 0, 65535));
            w.Bool(d.indoor);
            peer.Broadcast(w, true);
        }

        void OnLootChanged(LootDrop d)
        {
            var w = peer.BeginMessage();
            w.Byte(NetMsg.LootSet);
            w.Int(d.id);
            w.UShort((ushort)Mathf.Clamp(d.taken ? 0 : d.count, 0, 65535));
            peer.Broadcast(w, true);
        }

        void OnStashOpened(LootStash s)
        {
            var w = peer.BeginMessage();
            w.Byte(NetMsg.Stash);
            w.UShort((ushort)s.index);
            peer.Broadcast(w, true);
        }

        void OnSupplyDrop(Vector3 at, int seed)
        {
            if (!Host) return;
            var w = peer.BeginMessage();
            w.Byte(NetMsg.Supply);
            NetMsg.Vector(w, at);
            w.Int(seed);
            peer.Broadcast(w, true);
        }

        /// <summary>Host: someone's link dropped mid-match. They count as out.</summary>
        public void OnLeft(int id)
        {
            var c = Combatant.ById(id);
            if (c == null || !c.Alive) return;
            c.owned = true;   // nobody else will decide for them now
            c.ReceiveHit(9999f, false, null, LeftWeapon, c.transform.position, true);
        }

        // ------------------------------------------------------------------ things that happened elsewhere

        /// <summary>Passes a client's message on to the other clients (host only).</summary>
        void Relay(NetConnection from, NetReader r, bool reliable)
        {
            if (!Host) return;
            r.CopyWhole(relay);
            peer.Broadcast(relay, reliable, from);
        }

        public void Handle(NetConnection from, byte type, NetReader r)
        {
            switch (type)
            {
                case NetMsg.State:
                    {
                        if (!Host) return;
                        float t = r.Float();
                        var s = FigureState.Read(r);
                        NetPuppet p;
                        if (!r.Failed && puppets.TryGetValue(from.id, out p) && p != null) p.Push(t, s);
                    }
                    break;

                case NetMsg.Snapshot:
                    {
                        if (Host) return;
                        float t = r.Float();
                        int n = r.Byte();
                        for (int i = 0; i < n; i++)
                        {
                            int id = r.Byte();
                            var s = FigureState.Read(r);
                            if (r.Failed) break;
                            NetPuppet p;
                            if (id != session.LocalId && puppets.TryGetValue(id, out p) && p != null) p.Push(t, s);
                        }
                    }
                    break;

                case NetMsg.Hit:
                    {
                        int victimId = r.Byte();
                        var victim = Combatant.ById(victimId);
                        if (victim == null) return;
                        if (!victim.owned)
                        {
                            // Host: it's another player's; pass it to their machine.
                            if (Host)
                            {
                                r.CopyWhole(relay);
                                peer.SendReliable(peer.Find(victimId), relay);
                            }
                            return;
                        }
                        var attacker = Combatant.ById(r.Byte());
                        float damage = Mathf.Clamp(r.Float(), 0f, 400f);
                        bool head = r.Bool();
                        string weapon = r.String();
                        Vector3 at = NetMsg.Position(r);
                        bool ignore = r.Bool();
                        if (!r.Failed) victim.ApplyHit(damage, head, attacker, weapon, at, ignore);
                    }
                    break;

                case NetMsg.Result:
                    {
                        Relay(from, r, true);
                        var victim = Combatant.ById(r.Byte());
                        var info = new Combatant.DamageInfo();
                        info.attacker = Combatant.ById(r.Byte());
                        info.amount = r.Float();
                        info.absorbed = r.Float();
                        int flags = r.Byte();
                        info.head = (flags & 1) != 0;
                        info.armourBroke = (flags & 2) != 0;
                        info.killing = (flags & 4) != 0;
                        info.armourLevel = r.Byte();
                        info.weapon = r.String();
                        float after = r.Float();
                        Vector3 at = NetMsg.Position(r);
                        if (r.Failed || victim == null || victim.owned) return;
                        victim.ApplyRemoteResult(info, at, after);
                    }
                    break;

                case NetMsg.Shot:
                    {
                        Relay(from, r, false);
                        var shooter = Combatant.ById(r.Byte());
                        int gunId = r.Byte();
                        bool suppressed = r.Bool();
                        Vector3 origin = NetMsg.Position(r);
                        int n = r.Byte();
                        ends.Clear();
                        for (int i = 0; i < n; i++) ends.Add(NetMsg.Position(r));
                        int mask = r.UShort();
                        if (r.Failed || shooter == null || shooter.owned) return;
                        Ballistics.ReplayShot(shooter, gunId, suppressed, origin, ends, mask);
                        var p = shooter.GetComponent<NetPuppet>();
                        if (p != null) p.Kick();
                    }
                    break;

                case NetMsg.Throw:
                    {
                        Relay(from, r, true);
                        var thrower = Combatant.ById(r.Byte());
                        bool smoke = r.Bool();
                        Vector3 at = NetMsg.Vector(r);
                        Vector3 v = NetMsg.Vector(r);
                        // Without its thrower the copy would hurt people here as if no one threw it.
                        if (r.Failed || thrower == null || thrower.owned) return;
                        Grenade.Launch(smoke ? director.smokeTemplate : director.grenadeTemplate, at, v, thrower);
                    }
                    break;

                case NetMsg.LootAdd:
                    {
                        Relay(from, r, true);
                        int id = r.Int();
                        Vector3 p = NetMsg.Vector(r);
                        float yaw = r.Angle();
                        var kind = (ItemKind)r.Byte();
                        int value = r.Byte();
                        var tier = (Tier)r.Byte();
                        int count = r.UShort();
                        bool indoor = r.Bool();
                        if (!r.Failed && loot != null) loot.AddRemote(id, p, yaw, kind, value, tier, count, indoor);
                    }
                    break;

                case NetMsg.LootSet:
                    {
                        Relay(from, r, true);
                        int id = r.Int();
                        int count = r.UShort();
                        if (!r.Failed && loot != null) loot.SetRemote(id, count);
                    }
                    break;

                case NetMsg.Stash:
                    {
                        Relay(from, r, true);
                        int index = r.UShort();
                        if (!r.Failed && loot != null) loot.OpenRemote(index);
                    }
                    break;

                case NetMsg.Supply:
                    {
                        if (Host) return;
                        Vector3 p = NetMsg.Vector(r);
                        int seed = r.Int();
                        if (!r.Failed) director.ArriveSupplyDrop(p, seed);
                    }
                    break;

                case NetMsg.Haze:
                    {
                        if (Host || director.haze == null) return;
                        int round = r.Byte();
                        bool closing = r.Bool();
                        float left = r.Float();
                        if (!r.Failed) director.haze.Sync(round, closing, left);
                    }
                    break;
            }
        }
    }
}
