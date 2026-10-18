namespace AVANebulus.Engine;

/// <summary>
/// The computer Pogo, for the demonstration and the tests. It knows every ledge it can stand on and where each walk,
/// jump or doorway takes it (the tower's <see cref="Graph"/>), and plans the quickest climb to the top. Before each
/// move it plays the move out on a copy of the tower, balls and eyes and all, and if it would be knocked off it
/// waits for the moment it wouldn't (or tries another way). It throws a snowball at a ball in its way.
/// </summary>
public sealed class Pilot
{
    private readonly Queue<Controls> _plan = new();
    private Graph? _full;
    private TowerDef? _for;
    private int _crumbled = -1;
    private HashSet<Spot> _beats = [];
    private bool _firePrev;
    /// <summary>What a step onto a ball's run counts as, against going round.</summary>
    public const int BeatCost = 200;
    public const int WaitStep = 6, MaxWait = 300, Settle = 30;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _plan.Clear();
        _full = null;
        _for = null;
        Doing = "";
    }

    public Controls Decide(GameEngine g) => g.Phase != Phase.Play ? default : Decide(g.World);

    public Controls Decide(World w)
    {
        var p = w.Pogo;
        if (p.Dead || w.Up) { _plan.Clear(); return default; }
        if (_plan.Count > 0) return _plan.Dequeue();
        if (p.Airborne || p.Knocked > 0) return default;
        int crumbled = 0;
        foreach (var t in w.Grid) if (t == Tile.Empty) crumbled++;
        if (_full == null || _for != w.Tower || crumbled != _crumbled)
        {
            _beats = [];
            foreach (var b in w.Tower.Balls)
                for (double x = b.Min; x <= b.Max + 1; x += Field.Tile)
                    _beats.Add(new Spot(TowerDef.Wrap((int)Math.Floor(x / Field.Tile)), b.Row));
            _full = Graph.Build(w.Tower, w);
            _for = w.Tower;
            _crumbled = crumbled;
        }
        // A ball close in front, on this ledge: a snowball at it.
        if (Shoot(w, p)) return new Controls(Fire: true);
        _firePrev = false;
        var here = Spot.Of(p);
        if (!_full.Edges.ContainsKey(here))
        {
            // Off the map (one foot on the end of a ledge): onto the middle of a square underfoot.
            var near = new[] { here with { Col = TowerDef.Wrap(here.Col - 1) }, here with { Col = TowerDef.Wrap(here.Col + 1) } }
                .Where(_full.Edges.ContainsKey).OrderBy(s => Math.Abs(TowerDef.Delta(p.X, s.X))).FirstOrDefault();
            if (near != default) return Step(p, near, "stepping on");
        }
        var targets = _full.Edges.Keys.Where(s => s.Row == w.Tower.TopRow).ToList();
        var far = Distances(_full, targets, _beats);
        int now = far.TryGetValue(here, out int h) ? h : int.MaxValue;
        if (Math.Abs(TowerDef.Delta(p.X, here.X)) > 2) return Step(p, here, "lining up");
        var options = new List<(Move Move, int Cost)>();
        if (_full.Edges.TryGetValue(here, out var edges))
            foreach (var (mv, to, ticks) in edges)
                if (far.TryGetValue(to, out int d) && (d < now || now == int.MaxValue)) options.Add((mv, ticks + d + (_beats.Contains(to) ? BeatCost : 0)));
        options.Sort((a, b) => a.Cost.CompareTo(b.Cost));
        int best = int.MaxValue;
        List<Controls>? pick = null;
        int pickWait = 0;
        foreach (var (mv, cost) in options.Take(3))
            for (int wait = 0; wait <= MaxWait && wait + cost < best; wait += WaitStep)
            {
                var run = Rehearse(w, mv, wait);
                if (run == null) continue;
                if (wait + cost < best) { best = wait + cost; pick = run; pickWait = wait; }
                break;
            }
        if (pick == null)
        {
            Doing = "stuck";
            for (int i = 0; i < WaitStep; i++) _plan.Enqueue(default);
            return _plan.Dequeue();
        }
        Doing = pickWait > 0 ? "waiting" : "climbing";
        if (pickWait > 0)
        {
            for (int i = 0; i < Math.Min(pickWait, WaitStep); i++) _plan.Enqueue(default);
            return _plan.Dequeue();
        }
        foreach (var c in pick) _plan.Enqueue(c);
        return _plan.Count > 0 ? _plan.Dequeue() : default;
    }

    private Controls Step(Pogo p, Spot to, string doing)
    {
        Doing = doing;
        double off = TowerDef.Delta(p.X, to.X);
        int dx = Math.Sign(off);
        int steps = Math.Clamp((int)Math.Round(Math.Abs(off) / (Pogo.Speed * Field.Step)), 1, 16);
        for (int i = 0; i < steps; i++) _plan.Enqueue(new Controls(dx));
        return _plan.Dequeue();
    }

    private bool Shoot(World w, Pogo p)
    {
        foreach (var b in w.Balls)
        {
            if (b.Dead) continue;
            var (bx, by) = Sim.BallAt(b);
            double dx = TowerDef.Delta(p.X, bx);
            if (Math.Sign(dx) == p.Facing && Math.Abs(dx) < 170 && Math.Abs(by - (p.Y - 24)) < 18 && w.Snowballs.Count == 0)
            {
                _firePrev = !_firePrev;
                return _firePrev;
            }
        }
        return false;
    }

    /// <summary>A move played out on a copy after a wait, then a moment standing: its controls if he isn't knocked off.</summary>
    private static List<Controls>? Rehearse(World start, Move mv, int wait)
    {
        var w = start.Copy();
        for (int i = 0; i < wait; i++)
        {
            Sim.Step(w, default);
            if (w.Pogo.Dead || w.Pogo.Knocked > 0 || w.Pogo.Airborne) return null;
        }
        var controls = new List<Controls>();
        foreach (var c in Graph.Script(w, mv))
        {
            controls.Add(c);
            Sim.Step(w, c);
            if (w.Pogo.Dead || w.Pogo.Knocked > 0) return null;
            if (w.Up) return controls;
        }
        for (int i = 0; i < Settle; i++)
        {
            Sim.Step(w, default);
            if (w.Pogo.Dead || w.Pogo.Knocked > 0) return null;
            if (w.Up) break;
        }
        return controls;
    }

    /// <summary>How long (in steps) from every spot to the nearest of the targets, going backwards from them.</summary>
    private static Dictionary<Spot, int> Distances(Graph g, List<Spot> targets, HashSet<Spot> beats)
    {
        var back = new Dictionary<Spot, List<(Spot From, int Ticks)>>();
        foreach (var (from, list) in g.Edges)
            foreach (var (_, to, ticks) in list)
            {
                if (!back.TryGetValue(to, out var l)) back[to] = l = [];
                l.Add((from, ticks));
            }
        var dist = new Dictionary<Spot, int>();
        var pq = new PriorityQueue<Spot, int>();
        foreach (var t in targets) { dist[t] = 0; pq.Enqueue(t, 0); }
        while (pq.TryDequeue(out var s, out int d))
        {
            if (d > dist[s]) continue;
            if (!back.TryGetValue(s, out var l)) continue;
            foreach (var (from, ticks) in l)
            {
                int nd = d + ticks + (beats.Contains(s) ? BeatCost : 0);
                if (dist.TryGetValue(from, out int od) && od <= nd) continue;
                dist[from] = nd;
                pq.Enqueue(from, nd);
            }
        }
        return dist;
    }
}
