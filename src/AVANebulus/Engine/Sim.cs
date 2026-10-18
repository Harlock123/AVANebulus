namespace AVANebulus.Engine;

/// <summary>
/// The tower, step by step: Pogo walking round it and jumping (a fixed arc), dropping off ledges, crumbling ledges,
/// doorways through the tower, the balls and flying eyes that knock him off, his snowballs, the sea at the foot,
/// and the clock.
/// </summary>
public static class Sim
{
    private const double Dt = Field.Step, T = Field.Tile;
    public const double CrumbleTime = 0.5, KnockTime = 0.6, SnowSpeed = 420, SnowLife = 0.5, DoorReach = 10;
    public const int RowPoints = 10, BallPoints = 100;

    public static void Start(World w, TowerDef t, bool things = true)
    {
        w.Tower = t;
        w.Grid = (Tile[,])t.Grid.Clone();
        w.Crumble = new double[Field.Cols, t.Rows];
        for (int c = 0; c < Field.Cols; c++)
        for (int r = 0; r < t.Rows; r++)
            w.Crumble[c, r] = CrumbleTime;
        w.Clock = 0;
        w.TimeLeft = t.Time;
        w.Up = false;
        w.Cause = "";
        w.Best = t.Rows - 1;
        w.Pogo = new Pogo { X = (t.StartCol + 0.5) * T, Y = (t.Rows - 1) * T };
        w.Balls = things ? t.Balls.Select(b => new Ball { Def = b, X = (b.Min + b.Max) / 2 }).ToList() : [];
        w.Eyes = things ? t.Eyes.Select(e => new Eye { Def = e, X = e.Phase * Field.Round }).ToList() : [];
        w.Snowballs.Clear();
    }

    /// <summary>After a fall into the sea: back on the lowest ledge below where he fell, or at the foot of the tower.</summary>
    public static void Respawn(World w)
    {
        var t = w.Tower;
        w.Pogo = new Pogo { X = (t.StartCol + 0.5) * T, Y = (t.Rows - 1) * T };
        w.Cause = "";
        w.Snowballs.Clear();
    }

    public static void Step(World w, Controls c)
    {
        w.Clock += Dt;
        foreach (var b in w.Balls)
        {
            if (b.Dead) continue;
            b.X += b.Dir * b.Def.Speed * Dt;
            if (b.X > b.Def.Max) { b.X = b.Def.Max; b.Dir = -1; }
            if (b.X < b.Def.Min) { b.X = b.Def.Min; b.Dir = 1; }
        }
        foreach (var e in w.Eyes) e.X = TowerDef.WrapX(e.X + e.Def.Speed * Dt);
        Snow(w);
        var p = w.Pogo;
        if (p.Dead || w.Up) return;
        if (!w.Probe)
        {
            w.TimeLeft -= Dt;
            if (w.TimeLeft <= 0) { w.TimeLeft = 0; Die(w, "OUT OF TIME"); return; }
        }
        if (p.Knocked > 0) { p.Knocked -= Dt; c = default; }
        if (!p.Airborne) Ground(w, p, c);
        if (p.Airborne) Air(w, p);
        p.X = TowerDef.WrapX(p.X);
        p.JumpPrev = c.Jump;
        p.UpPrev = c.Up;
        if (c.Fire && !p.FirePrev && w.Snowballs.Count < 2 && !w.Probe)
        {
            w.Snowballs.Add(new Snowball { X = p.X + p.Facing * 12, Y = p.Y - 24, Dir = p.Facing, Life = SnowLife });
            w.Heard.Add(Sfx.Throw);
        }
        p.FirePrev = c.Fire;
        if (p.Dead) return;
        Touch(w, p);
        // Height gained scores.
        if (!p.Airborne && !w.Probe)
        {
            int row = (int)Math.Round(p.Y / T);
            if (row < w.Best) { w.Score += (w.Best - row) * RowPoints; w.Best = row; }
            if (row == w.Tower.TopRow) { w.Up = true; w.Heard.Add(Sfx.Top); }
        }
    }

    /// <summary>The square under a foot.</summary>
    private static (int C, int R) Under(double x, double feet) => ((int)Math.Floor(TowerDef.WrapX(x) / T), (int)Math.Floor((feet + 1) / T));

    /// <summary>Whether there's a ledge under either foot, exactly at foot level.</summary>
    public static bool Footing(World w, double x, double feet)
    {
        if (Math.Abs(feet / T - Math.Round(feet / T)) > 0.001) return false;
        foreach (double fx in new[] { x - 6, x + 6 })
        {
            var (c, r) = Under(fx, feet);
            if (TowerDef.Standable(w.At(c, r))) return true;
        }
        return false;
    }

    private static void Ground(World w, Pogo p, Controls c)
    {
        if (!Footing(w, p.X, p.Y))
        {
            // Off the edge: straight down.
            p.Airborne = true;
            p.Vy = 0;
            p.Jdx = 0;
            return;
        }
        // Into a doorway: out at the far side of the tower.
        var (uc, ur) = Under(p.X, p.Y);
        if (c.Up && !p.UpPrev && w.At(uc, ur) == Tile.Door && Math.Abs(TowerDef.Delta((uc + 0.5) * T, p.X)) < DoorReach)
        {
            p.X = TowerDef.WrapX((uc + 0.5) * T + Field.Round / 2);
            w.Heard.Add(Sfx.Door);
            return;
        }
        if (c.Jump && !p.JumpPrev)
        {
            p.Airborne = true;
            p.Vy = -Pogo.JumpSpeed;
            p.Jdx = c.Dx;
            if (c.Dx != 0) p.Facing = c.Dx;
            w.Heard.Add(Sfx.Jump);
            return;
        }
        if (c.Dx != 0)
        {
            p.Facing = c.Dx;
            p.X += c.Dx * Pogo.Speed * Dt;
            p.Walk += Dt;
        }
        // Crumbling ledges give way under him.
        foreach (double fx in new[] { p.X - 6, p.X + 6 })
        {
            var (fc, fr) = Under(fx, p.Y);
            if (fr < 0 || fr >= w.Tower.Rows || w.Grid[fc, fr] != Tile.Crumble) continue;
            w.Crumble[fc, fr] -= Dt / 2;
            if (w.Crumble[fc, fr] <= 0) { w.Grid[fc, fr] = Tile.Empty; w.Heard.Add(Sfx.Crumble); }
        }
    }

    private static void Air(World w, Pogo p)
    {
        p.X += p.Jdx * Pogo.Speed * Dt;
        double vy = p.Vy + Pogo.Gravity * Dt, ny = p.Y + vy * Dt;
        if (vy > 0)
        {
            // Down onto the first ledge between where his feet were and where they'd be.
            for (int r = (int)Math.Ceiling(p.Y / T - 0.001); r <= (int)Math.Floor(ny / T); r++)
            {
                double top = r * T;
                if (top < p.Y - 0.001 || !Footing(w, p.X, top)) continue;
                p.Y = top;
                p.Vy = 0;
                p.Airborne = false;
                p.Knocked = 0;
                w.Heard.Add(Sfx.Land);
                return;
            }
        }
        p.Vy = vy;
        p.Y = ny;
        if (p.Y > w.Tower.Height + 30) Die(w, "INTO THE SEA");
    }

    /// <summary>Where a ball is: round the tower, and its middle's height.</summary>
    public static (double X, double Y) BallAt(Ball b) => (TowerDef.WrapX(b.X), b.Def.Row * T - 14);

    /// <summary>Where an eye is.</summary>
    public static (double X, double Y) EyeAt(World w, Eye e) => (e.X, e.Def.Y + Math.Sin(w.Clock * 2 + e.Def.Phase * 6) * 20);

    private static void Touch(World w, Pogo p)
    {
        if (p.Knocked > 0) return;
        double py = p.Y - Pogo.Height / 2;
        foreach (var b in w.Balls)
        {
            if (b.Dead) continue;
            var (bx, by) = BallAt(b);
            double dx = TowerDef.Delta(bx, p.X);
            if (Math.Abs(dx) < 20 && Math.Abs(by - py) < 30) { Knock(w, p, dx >= 0 ? 1 : -1); return; }
        }
        foreach (var e in w.Eyes)
        {
            var (ex, ey) = EyeAt(w, e);
            double dx = TowerDef.Delta(ex, p.X);
            if (Math.Abs(dx) < 20 && Math.Abs(ey - py) < 30) { Knock(w, p, dx >= 0 ? 1 : -1); return; }
        }
    }

    /// <summary>Knocked off his feet: up a little, away, and down to wherever he lands.</summary>
    private static void Knock(World w, Pogo p, int away)
    {
        p.Knocked = KnockTime;
        p.Airborne = true;
        p.Vy = -160;
        p.Jdx = away;
        p.Y -= 1;
        w.Heard.Add(Sfx.Knocked);
        if (w.Probe) { p.Dead = true; w.Cause = "KNOCKED"; }
    }

    private static void Snow(World w)
    {
        foreach (var s in w.Snowballs)
        {
            s.X = TowerDef.WrapX(s.X + s.Dir * SnowSpeed * Dt);
            s.Life -= Dt;
            foreach (var b in w.Balls)
            {
                if (b.Dead) continue;
                var (bx, by) = BallAt(b);
                if (Math.Abs(TowerDef.Delta(bx, s.X)) < 16 && Math.Abs(by - s.Y) < 22)
                {
                    b.Dead = true;
                    s.Life = 0;
                    w.Score += BallPoints;
                    w.Heard.Add(Sfx.BallDown);
                }
            }
        }
        w.Snowballs.RemoveAll(s => s.Life <= 0);
    }

    public static void Die(World w, string cause)
    {
        if (w.Pogo.Dead) return;
        w.Pogo.Dead = true;
        w.Cause = cause;
        w.Heard.Add(cause == "INTO THE SEA" ? Sfx.Splash : Sfx.Die);
    }
}
