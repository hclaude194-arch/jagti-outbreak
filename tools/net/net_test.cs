// Tests Assets/Scripts/Net outside Unity: a host and several clients on this computer, with packets
// thrown away on purpose. Build and run (mono):
//   mcs -out:/tmp/net_test.exe tools/net/net_test.cs Assets/Scripts/Net/NetBuffer.cs Assets/Scripts/Net/NetPeer.cs && mono /tmp/net_test.exe
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Jagti.Net;

static class NetTest
{
    static int failures;
    static int seen;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) failures++;
    }

    static Stopwatch clock = Stopwatch.StartNew();
    static double Now { get { return clock.Elapsed.TotalSeconds; } }

    static void Run(IList<NetPeer> peers, double seconds, Func<bool> done = null)
    {
        double end = Now + seconds;
        while (Now < end)
        {
            foreach (var p in peers) p.Update(Now);
            if (done != null && done()) return;
            Thread.Sleep(1);
        }
    }

    static int Main()
    {
        Console.WriteLine("buffer round trip");
        var w = new NetWriter(4);
        w.Byte(200); w.Bool(true); w.Short(-1234); w.UShort(65000); w.Int(-123456789); w.Float(3.25f);
        w.Packed(12.5f, -1000f, 1000f); w.Angle(-90f); w.String("Rahul ठाकुर");
        var r = new NetReader(w.data, 0, w.Length);
        Check(r.Byte() == 200 && r.Bool() && r.Short() == -1234 && r.UShort() == 65000 && r.Int() == -123456789
              && r.Float() == 3.25f, "integers and floats");
        Check(Math.Abs(r.Packed(-1000f, 1000f) - 12.5f) < 0.02f, "packed position within 2 cm");
        Check(Math.Abs(r.Angle() - 270f) < 1.5f, "packed angle");
        Check(r.String() == "Rahul ठाकुर" && !r.Failed, "unicode name");
        r.Int();
        Check(r.Failed, "reading past the end is caught");

        Console.WriteLine("host and three clients, 30% of packets lost each way");
        var host = new NetPeer { dropRate = 0.3f };
        Check(host.StartHost(17777, "Test host"), "host opens its port");
        var clients = new List<NetPeer>();
        var all = new List<NetPeer> { host };
        for (int i = 0; i < 3; i++)
        {
            var c = new NetPeer { dropRate = 0.3f };
            Check(c.StartClient("127.0.0.1", 17777, "Client " + i), "client " + i + " starts");
            clients.Add(c);
            all.Add(c);
        }
        Run(all, 6, () => host.ConnectedCount == 3 && clients.TrueForAll(c => c.LocalId != 0));
        Check(host.ConnectedCount == 3, "all three joined (" + host.ConnectedCount + ")");
        var ids = new HashSet<int>();
        foreach (var c in clients) ids.Add(c.LocalId);
        Check(ids.Count == 3 && !ids.Contains(1) && !ids.Contains(0), "each got its own player number");
        Check(host.connections.Exists(c => c.name == "Client 2"), "host knows their names");

        // Reliable, ordered: each client sends 400 numbers; the host sends 400 to everyone.
        var gotAtHost = new Dictionary<int, List<int>>();
        var gotAtClient = new List<int>[3];
        host.Message += (c, m) =>
        {
            if (m.Byte() != 1) return;
            List<int> l;
            if (!gotAtHost.TryGetValue(c.id, out l)) gotAtHost[c.id] = l = new List<int>();
            l.Add(m.Int());
        };
        for (int i = 0; i < 3; i++)
        {
            int k = i;
            gotAtClient[k] = new List<int>();
            clients[k].Message += (c, m) =>
            {
                byte kind = m.Byte();
                if (kind == 1) gotAtClient[k].Add(m.Int());
                else if (kind == 2 && k == 2) seen++;
            };
        }
        const int N = 400;
        for (int n = 0; n < N; n++)
        {
            var b = host.BeginMessage(); b.Byte(1); b.Int(n);
            host.Broadcast(b, true);
            foreach (var c in clients) { var cb = c.BeginMessage(); cb.Byte(1); cb.Int(n); c.Broadcast(cb, true); }
            if (n % 20 == 0) Run(all, 0.01);
        }
        Run(all, 15, () =>
        {
            foreach (var l in gotAtClient) if (l.Count < N) return false;
            if (gotAtHost.Count < 3) return false;
            foreach (var l in gotAtHost.Values) if (l.Count < N) return false;
            return true;
        });
        bool inOrder = true;
        foreach (var l in gotAtClient) { if (l.Count != N) inOrder = false; else for (int n = 0; n < N; n++) if (l[n] != n) inOrder = false; }
        Check(inOrder, "every client got all " + N + " host messages once, in order");
        inOrder = gotAtHost.Count == 3;
        foreach (var l in gotAtHost.Values) { if (l.Count != N) inOrder = false; else for (int n = 0; n < N; n++) if (l[n] != n) inOrder = false; }
        Check(inOrder, "the host got all " + N + " from each client once, in order");

        // Unreliable: some arrive, none repeat.
        // (One Message handler per peer: handlers share the reader, as NetSession does.)
        for (int n = 0; n < 200; n++) { var b = host.BeginMessage(); b.Byte(2); host.Broadcast(b, false); }
        Run(all, 0.5);
        Check(seen > 100 && seen < 190, "unreliable messages: " + seen + " of 200 arrived (about 70% expected)");

        // Pings measure a round trip.
        Run(all, 2.5);
        Check(clients[0].Server.rtt < 0.2f, "round trip measured (" + (clients[0].Server.rtt * 1000).ToString("0") + " ms)");

        // Full game and closed lobby.
        host.acceptingPlayers = () => false;
        var late = new NetPeer();
        string why = null;
        late.Disconnected += (c, reason) => why = reason;
        late.StartClient("127.0.0.1", 17777, "Late");
        var withLate = new List<NetPeer>(all) { late };
        Run(withLate, 4, () => why != null);
        Check(why == "The match has already started", "a late join is turned away (" + why + ")");
        late.Stop();

        // Finding the host on this computer.
        var finder = new NetPeer();
        finder.StartSearch();
        string foundName = null;
        int foundPlayers = 0;
        finder.HostFound += (name, players, at) => { foundName = name; foundPlayers = players; };
        host.dropRate = 0f;
        finder.Search(17777);
        Run(new List<NetPeer>(all) { finder }, 2, () => foundName != null);
        Check(foundName == "Test host" && foundPlayers == 4, "search finds the host (" + foundName + ", " + foundPlayers + " players)");
        finder.Stop();

        // Leaving and timing out.
        string hostSaw = null;
        host.Disconnected += (c, reason) => hostSaw = c.name + ": " + reason;
        clients[0].dropRate = 0f;
        clients[0].Stop();
        all.Remove(clients[0]);
        Run(all, 3, () => hostSaw != null);
        Check(hostSaw == "Client 0: Left the game" || host.ConnectedCount == 2, "a client leaving is noticed (" + hostSaw + ")");

        string clientSaw = null;
        clients[1].Disconnected += (c, reason) => clientSaw = reason;
        host.dropRate = 0f;
        host.Stop();
        all.Remove(host);
        Run(all, 3, () => clientSaw != null);
        Check(clientSaw == "The host closed the game" || clientSaw == "Connection lost", "clients hear the host close (" + clientSaw + ")");

        var nobody = new NetPeer();
        string noAnswer = null;
        nobody.Disconnected += (c, reason) => noAnswer = reason;
        nobody.StartClient("127.0.0.1", 17999, "Nobody");
        Run(new List<NetPeer> { nobody }, 10, () => noAnswer != null);
        Check(noAnswer == "No answer from the host", "joining an address with no game gives up (" + noAnswer + ")");

        foreach (var c in clients) c.Stop();
        Console.WriteLine(failures == 0 ? "all passed" : failures + " failed");
        return failures == 0 ? 0 : 1;
    }
}
