using Jagti.Net;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>
    /// How one person or bot looks right now, as sent over the network: where they are, which way
    /// they face, their pose, gun and gear, and their health. 14 bytes on the wire.
    /// </summary>
    public struct FigureState
    {
        public Vector3 position;
        public float yaw, pitch;
        public bool landed, hidden, armed, aiming;
        public HumanBody.Pose pose;
        public float crouch, speed;
        public int gun;                   // -1 for none
        public int vest, helmet, pack;
        public float health;

        const byte Landed = 1, Hidden = 2, Armed = 4, Aiming = 8;

        public void Write(NetWriter w)
        {
            NetMsg.Position(w, position);
            w.Angle(yaw);
            w.SByte((sbyte)Mathf.Clamp(Mathf.RoundToInt(pitch), -90, 90));
            w.Byte((byte)((landed ? Landed : 0) | (hidden ? Hidden : 0) | (armed ? Armed : 0) | (aiming ? Aiming : 0)));
            w.Byte((byte)(((int)pose & 7) | (Mathf.Clamp(Mathf.RoundToInt(crouch * 31f), 0, 31) << 3)));
            w.Byte((byte)Mathf.Clamp(Mathf.RoundToInt(speed * 10f), 0, 255));
            w.Byte((byte)Mathf.Clamp(gun + 1, 0, 255));
            w.Byte((byte)((vest & 3) | ((helmet & 3) << 2) | ((pack & 3) << 4)));
            w.Byte((byte)Mathf.Clamp(Mathf.CeilToInt(health), 0, 255));
        }

        public static FigureState Read(NetReader r)
        {
            var s = new FigureState();
            s.position = NetMsg.Position(r);
            s.yaw = r.Angle();
            s.pitch = r.SByte();
            int flags = r.Byte();
            s.landed = (flags & Landed) != 0;
            s.hidden = (flags & Hidden) != 0;
            s.armed = (flags & Armed) != 0;
            s.aiming = (flags & Aiming) != 0;
            int pc = r.Byte();
            s.pose = (HumanBody.Pose)(pc & 7);
            s.crouch = (pc >> 3) / 31f;
            s.speed = r.Byte() / 10f;
            s.gun = r.Byte() - 1;
            int gear = r.Byte();
            s.vest = gear & 3;
            s.helmet = (gear >> 2) & 3;
            s.pack = (gear >> 4) & 3;
            s.health = r.Byte();
            return s;
        }

        /// <summary>A bot (or anyone drawn by a HumanBody that its own code poses), read off the body.</summary>
        public static FigureState OfBody(Combatant c, HumanBody body)
        {
            var l = c.loadout;
            var s = new FigureState
            {
                position = c.transform.position,
                yaw = c.transform.eulerAngles.y,
                landed = c.Landed,
                health = c.Health.current,
                vest = l.vest, helmet = l.helmet, pack = l.backpack,
                gun = l.Current != null ? l.Current.def.id : -1,
            };
            if (body != null)
            {
                s.hidden = !body.gameObject.activeSelf;
                s.pose = body.pose;
                s.pitch = body.aimPitch;
                s.armed = body.armed;
                s.aiming = body.aiming;
                s.crouch = body.crouch;
                s.speed = body.speed;
            }
            return s;
        }
    }
}
