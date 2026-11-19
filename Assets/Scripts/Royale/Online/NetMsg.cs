using Jagti.Net;
using UnityEngine;

namespace Jagti.Royale
{
    /// <summary>One person or bot in an online match, as the host lists them at the start.</summary>
    public class Entrant
    {
        public int id;            // humans 1 to 8 (the host is 1), bots from 100
        public string name;
        public bool human;
        public int skin, shirt, pants, canopy;   // indices into MatchDirector's look lists
    }

    /// <summary>
    /// The first byte of every game message, and helpers for the shapes they share. All positions are
    /// packed to 16 bits a coordinate (about 8 cm across the 5 km box the match can reach).
    /// </summary>
    public static class NetMsg
    {
        // Lobby
        public const byte LobbyList = 1;    // host -> all: count, then (id, name, ready)
        public const byte Ready = 2;        // client -> host: my scene is loaded and I'm in the lobby
        public const byte MatchStart = 3;   // host -> all: seed, difficulty, roster
        public const byte BackToLobby = 4;  // host -> all: reload for the next match

        // Match
        public const byte State = 10;       // client -> host: my player (unreliable, 20 a second)
        public const byte Snapshot = 11;    // host -> all: everyone (unreliable, 15 a second)
        public const byte Hit = 12;         // attacker's machine -> victim's machine (through the host)
        public const byte Result = 13;      // victim's machine -> everyone: what a hit did
        public const byte Shot = 14;        // shooter's machine -> everyone: tracers and sound
        public const byte Throw = 15;       // thrower's machine -> everyone
        public const byte LootAdd = 16;     // dropper's machine -> everyone
        public const byte LootSet = 17;     // taker's machine -> everyone: new count (0 = gone)
        public const byte Supply = 18;      // host -> all: a supply drop
        public const byte Haze = 19;        // host -> all: the haze timer
        public const byte Left = 20;        // host -> all: someone left mid-match
        public const byte Stash = 21;       // opener's machine -> everyone: a stash opened (index)
        public const byte Glass = 22;       // breaker's machine -> everyone: a window pane broke (building glass id, pane)

        public const float Extent = 2500f;

        public static void Position(NetWriter w, Vector3 p)
        {
            w.Packed(p.x, -Extent, Extent);
            w.Packed(p.y, -300f, 1200f);
            w.Packed(p.z, -Extent, Extent);
        }

        public static Vector3 Position(NetReader r)
        {
            float x = r.Packed(-Extent, Extent);
            float y = r.Packed(-300f, 1200f);
            float z = r.Packed(-Extent, Extent);
            return new Vector3(x, y, z);
        }

        public static void Vector(NetWriter w, Vector3 v)
        {
            w.Float(v.x);
            w.Float(v.y);
            w.Float(v.z);
        }

        public static Vector3 Vector(NetReader r)
        {
            float x = r.Float();
            float y = r.Float();
            float z = r.Float();
            return new Vector3(x, y, z);
        }
    }
}
