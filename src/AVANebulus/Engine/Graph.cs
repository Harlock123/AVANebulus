namespace AVANebulus.Engine;

/// <summary>A move from one standing place to another: a walk, a jump (left, up or right), or into a doorway.</summary>
public readonly record struct Move(int Dx, bool Jump, bool Door = false);

/// <summary>Where Pogo stands: on the ledge at a square.</summary>
public readonly record struct Spot(int Col, int Row)
{
    public double X => (Col + 0.5) * Field.Tile;
    public double Feet => Row * Field.Tile;
    public static Spot Of(Pogo p) => new((int)Math.Floor(TowerDef.WrapX(p.X) / Field.Tile), (int)Math.Round(p.Y / Field.Tile));
}

/// <summary>
/// Every ledge Pogo can stand on, and where each move takes him from there, found by trying the moves out (with
/// nothing about to knock him off) in the game's own physics. The towers are kept only if this shows the top can be
/// reached; the computer player plans its climb by it.
/// </summary>
public sealed class Graph
{
    public static readonly Move[] Moves = [new(-1, false), new(1, false), new(-1, true), new(0, true), new(1, true), new(0, false, true)];
    public readonly Dictionary<Spot, List<(Move Move, Spot To, int Ticks)>> Edges = [];

    public static Graph Build(TowerDef t, World? state = null)
    {
        var g = new Graph();
        var probe = new World { Probe = true };
        Sim.Start(probe, t, things: false);
        if (state != null) probe.Grid = (Tile[,])state.Grid.Clone();
        var grid = probe.Grid;
        for (int c = 0; c < Field.Cols; c++)
        for (int r = 0; r < t.Rows; r++)
        {
            if (!TowerDef.Standable(grid[c, r])) continue;
            var s = new Spot(c, r);
            var list = new List<(Move, Spot, int)>();
            foreach (var mv in Moves)
            {
                if (mv.Door && grid[c, r] != Tile.Door) continue;
                if (Try(probe, grid, s, mv) is { } to) list.Add((mv, to.Spot, to.Ticks));
            }
            g.Edges[s] = list;
        }
        return g;
    }

    /// <summary>A move tried from a spot: where it ends, standing (and alive), and how long it took.</summary>
    public static (Spot Spot, int Ticks)? Try(World probe, Tile[,] grid, Spot s, Move mv)
    {
        var w = probe;
        w.Grid = (Tile[,])grid.Clone();
        w.Pogo = new Pogo { X = s.X, Y = s.Feet };
        int ticks = 0;
        foreach (var c in Script(w, mv))
        {
            Sim.Step(w, c);
            ticks++;
            if (w.Pogo.Dead || ticks > 300) return null;
        }
        if (w.Pogo.Airborne) return null;
        var end = Spot.Of(w.Pogo);
        if (end == s) return null;
        return (end, ticks);
    }

    /// <summary>
    /// The controls for a move, one step at a time, stopping once he's standing again: a walk goes on to the middle of
    /// the next square (or over the edge and down); a jump goes until he lands; a doorway is one press of up.
    /// </summary>
    public static IEnumerable<Controls> Script(World w, Move mv)
    {
        var p = w.Pogo;
        if (mv.Door)
        {
            yield return new Controls(Up: true);
            yield return default;
            yield break;
        }
        if (mv.Jump)
        {
            yield return new Controls(mv.Dx, true);
            for (int i = 0; i < 300 && p.Airborne && !p.Dead; i++) yield return new Controls(mv.Dx);
            yield break;
        }
        double target = (Math.Floor(TowerDef.WrapX(p.X) / Field.Tile) + mv.Dx + 0.5) * Field.Tile;
        for (int i = 0; i < 300 && !p.Dead; i++)
        {
            if (p.Airborne) { yield return default; continue; }
            double left = TowerDef.Delta(p.X, target);
            if (mv.Dx > 0 ? left <= 1.3 : left >= -1.3) yield break;
            yield return new Controls(mv.Dx);
        }
    }

    public HashSet<Spot> Reach(Spot from)
    {
        var seen = new HashSet<Spot> { from };
        var q = new Queue<Spot>(seen);
        while (q.Count > 0)
        {
            var s = q.Dequeue();
            if (!Edges.TryGetValue(s, out var list)) continue;
            foreach (var (_, to, _) in list)
                if (seen.Add(to)) q.Enqueue(to);
        }
        return seen;
    }
}
