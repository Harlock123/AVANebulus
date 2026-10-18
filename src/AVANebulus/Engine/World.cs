namespace AVANebulus.Engine;

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>Pogo: where round the tower (x) and how high (y, down the tower), which way he faces, in the air or not.</summary>
public sealed class Pogo
{
    public const double HalfWidth = 9, Height = 44, Speed = 150, JumpSpeed = 410, Gravity = 1500;
    public double X, Y, Vy, Walk, Knocked;
    public int Facing = 1, Jdx;
    public bool Airborne, Dead, JumpPrev, UpPrev, FirePrev;
    public Pogo Copy() => (Pogo)MemberwiseClone();
}

public sealed class Ball
{
    public BallDef Def;
    public double X;
    public int Dir = 1;
    public bool Dead;
    public Ball Copy() => (Ball)MemberwiseClone();
}

public sealed class Eye
{
    public EyeDef Def;
    public double X;
    public Eye Copy() => (Eye)MemberwiseClone();
}

public sealed class Snowball
{
    public double X, Y, Life;
    public int Dir;
    public Snowball Copy() => (Snowball)MemberwiseClone();
}

/// <summary>Everything on the tower that moves or changes, kept together so it can be copied whole.</summary>
public sealed class World
{
    public TowerDef Tower = null!;
    public Tile[,] Grid = new Tile[0, 0];
    /// <summary>How much each crumbling ledge has left (in seconds of standing on).</summary>
    public double[,] Crumble = new double[0, 0];
    public Pogo Pogo = new();
    public List<Ball> Balls = [];
    public List<Eye> Eyes = [];
    public List<Snowball> Snowballs = [];
    public double Clock, TimeLeft;
    public int Score;
    /// <summary>The highest row reached so far (climbing scores).</summary>
    public int Best;
    /// <summary>On the ring at the top: the tower is climbed.</summary>
    public bool Up;
    public string Cause = "";
    public List<Sfx> Heard = [];
    /// <summary>A world used only to try moves out: no time, nothing scored.</summary>
    public bool Probe;

    public World Copy() => new()
    {
        Tower = Tower, Grid = (Tile[,])Grid.Clone(), Crumble = (double[,])Crumble.Clone(), Pogo = Pogo.Copy(),
        Balls = Balls.Select(b => b.Copy()).ToList(), Eyes = Eyes.Select(e => e.Copy()).ToList(), Snowballs = Snowballs.Select(s => s.Copy()).ToList(),
        Clock = Clock, TimeLeft = TimeLeft, Score = Score, Best = Best, Up = Up, Cause = Cause, Probe = Probe,
    };

    public Tile At(int c, int r) => r < 0 || r >= Tower.Rows ? Tile.Empty : Grid[TowerDef.Wrap(c), r];
}
