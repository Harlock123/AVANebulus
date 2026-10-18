namespace AVANebulus.Engine;

/// <summary>Left or right (round the tower), jump, up (into a doorway), and fire.</summary>
public readonly record struct Controls(int Dx = 0, bool Jump = false, bool Up = false, bool Fire = false);

/// <summary>The screen, and a tower: squares round it and squares up it, unrolled flat (the columns wrap round).</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    /// <summary>The strip along the bottom of the screen.</summary>
    public const double Panel = 76;
    public const double Tile = 40;
    /// <summary>Squares round the tower.</summary>
    public const int Cols = 24;
    public const double Round = Cols * Tile;
}

/// <summary>What sticks out of the tower at a square: nothing, a ledge, a ledge that crumbles, or a ledge at a doorway.</summary>
public enum Tile : byte
{
    Empty,
    Ledge,
    /// <summary>Gives way a moment after Pogo stands on it.</summary>
    Crumble,
    /// <summary>A ledge in front of a doorway right through the tower: in at one side, out at the other.</summary>
    Door,
}

/// <summary>A ball rolling to and fro along a ledge (between two points round the tower).</summary>
public readonly record struct BallDef(int Row, double Min, double Max, double Speed);

/// <summary>A flying eye going round and round the tower at one height, bobbing.</summary>
public readonly record struct EyeDef(double Y, double Speed, double Phase);

/// <summary>A tower: its ledges and doorways, where Pogo starts, the top, what's on it, and the time allowed.</summary>
public sealed class TowerDef
{
    public int Index, Rows;
    public string Name = "";
    public Tile[,] Grid = new Tile[0, 0];
    public int StartCol;
    /// <summary>The ring of ledge round the very top: standing on it, the tower is climbed.</summary>
    public int TopRow = 2;
    public List<BallDef> Balls = [];
    public List<EyeDef> Eyes = [];
    public double Time;
    public uint Colour;
    public double Height => Rows * Field.Tile;

    public static int Wrap(int c) => ((c % Field.Cols) + Field.Cols) % Field.Cols;
    public static double WrapX(double x) => ((x % Field.Round) + Field.Round) % Field.Round;
    /// <summary>The shortest way round from one point to another (negative is to the left).</summary>
    public static double Delta(double from, double to)
    {
        double d = WrapX(to - from);
        return d > Field.Round / 2 ? d - Field.Round : d;
    }

    public Tile At(int c, int r) => r < 0 || r >= Rows ? Tile.Empty : Grid[Wrap(c), r];
    public static bool Standable(Tile t) => t != Tile.Empty;
}

/// <summary>The towers, each taller, with more on it and less time to spare.</summary>
public static class TowerBook
{
    private static readonly (string Name, int Rows, int Balls, int Eyes, double Time, uint Colour)[] Plans =
    [
        ("THE TOWER OF EYES", 34, 2, 1, 140, 0xFF4080D0),
        ("THE SLIPPERY SLOPE", 40, 3, 2, 160, 0xFFC06040),
        ("THE GREEN SPIRE", 46, 4, 2, 180, 0xFF40A060),
        ("THE PINK PALACE", 52, 5, 3, 200, 0xFFC050A0),
        ("THE GOLDEN STAIR", 58, 6, 3, 220, 0xFFC0A030),
        ("THE BLACK TOWER", 64, 7, 4, 240, 0xFF606078),
    ];

    public static int Count => Plans.Length;

    private static readonly Dictionary<int, TowerDef> Built = [];

    /// <summary>A tower (built once, then kept: proving it climbable takes a moment).</summary>
    public static TowerDef Build(int index)
    {
        index %= Plans.Length;
        lock (Built)
        {
            if (!Built.TryGetValue(index, out var t)) Built[index] = t = Make(index);
            return t;
        }
    }

    private static TowerDef Make(int index)
    {
        var plan = Plans[index % Plans.Length];
        for (int attempt = 0; ; attempt++)
        {
            var t = Try(index, plan, 7000 + index * 191 + attempt * 7919);
            if (t != null) return t;
        }
    }

    private static TowerDef? Try(int index, (string Name, int Rows, int Balls, int Eyes, double Time, uint Colour) plan, int seed)
    {
        var d = new Dice(seed);
        int rows = plan.Rows, cols = Field.Cols;
        var t = new TowerDef { Index = index, Name = plan.Name, Rows = rows, Time = plan.Time, Colour = plan.Colour, Grid = new Tile[cols, rows] };
        // The ring round the foot of the tower, and the one round the top.
        for (int c = 0; c < cols; c++) { t.Grid[c, rows - 1] = Tile.Ledge; t.Grid[c, t.TopRow] = Tile.Ledge; }
        t.StartCol = 0;
        // The way up: runs of ledge round the tower, a step up at the end of each, now and then a doorway through.
        int col = 0, row = rows - 1, dir = 1;
        var runs = new List<(int Row, int C0, int C1)>();
        int sinceDoor = 0;
        while (row > t.TopRow)
        {
            int len = 2 + d.Next(4);
            int c0 = col, c1 = col + dir * (len - 1);
            for (int k = 0; k < len; k++)
            {
                int c = col + dir * k;
                if (t.At(c, row) == Tile.Empty) t.Grid[TowerDef.Wrap(c), row] = k > 0 && k < len - 1 && d.Next(6) == 0 && index > 0 ? Tile.Crumble : Tile.Ledge;
            }
            if (row < rows - 1) runs.Add((row, Math.Min(c0, c1), Math.Max(c0, c1)));
            col = c1;
            sinceDoor++;
            if (sinceDoor > 3 && d.Next(5) == 0 && row < rows - 2)
            {
                // A doorway through to the far side, and on from there.
                int far = col + Field.Cols / 2;
                if (t.At(far, row) == Tile.Empty && t.At(far - 1, row) == Tile.Empty && t.At(far + 1, row) == Tile.Empty)
                {
                    t.Grid[TowerDef.Wrap(col), row] = Tile.Door;
                    t.Grid[TowerDef.Wrap(far), row] = Tile.Door;
                    col = far;
                    sinceDoor = 0;
                    if (d.Next(2) == 0) dir = -dir;
                    t.Grid[TowerDef.Wrap(col + dir), row] = Tile.Ledge;
                    col += dir;
                }
            }
            if (d.Next(7) == 0) dir = -dir;
            // Up a step: one or two squares on round, one row up.
            col += dir * (1 + d.Next(2));
            row--;
        }
        // A few odd ledges off the way, for show (and the odd short cut).
        for (int k = 0; k < rows / 2; k++)
        {
            int c = d.Next(cols), r = t.TopRow + 2 + d.Next(rows - t.TopRow - 4);
            if (t.At(c, r) == Tile.Empty && t.At(c - 1, r) == Tile.Empty && t.At(c + 1, r) == Tile.Empty) t.Grid[c, r] = Tile.Ledge;
        }
        // Proof it can be climbed.
        var graph = Graph.Build(t);
        var reach = graph.Reach(new Spot(t.StartCol, rows - 1));
        if (!reach.Any(s => s.Row == t.TopRow)) return null;
        // Balls on the longer runs; eyes at heights up the tower.
        var long_ = runs.Where(r => r.C1 - r.C0 >= 3).OrderBy(_ => d.Next()).Take(plan.Balls).ToList();
        foreach (var (r, c0, c1) in long_)
            t.Balls.Add(new BallDef(r, (c0 + 0.5) * Field.Tile, (c1 + 0.5) * Field.Tile, 50 + d.Next(30) + index * 6));
        for (int k = 0; k < plan.Eyes; k++)
            t.Eyes.Add(new EyeDef((t.TopRow + 4 + (rows - t.TopRow - 10) * (k + 0.5) / plan.Eyes) * Field.Tile, (d.Next(2) == 0 ? -1 : 1) * (60 + d.Next(40) + index * 5), d.Next()));
        return t;
    }
}
