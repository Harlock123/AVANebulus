using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVANebulus.Engine;

namespace AVANebulus.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVANebulus/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused;

    private const double T = Field.Tile, ViewH = Field.Height - Field.Panel, Radius = 230, Out = 34, Cx = Field.Width / 2;
    private double _camY, _turn;

    private double Sy(double y) => y - _camY + ViewH * 0.55;

    /// <summary>The angle of a point round the tower, with Pogo's place at the front (0).</summary>
    private double Angle(double x) => TowerDef.Delta(_turn, x) / Field.Round * 2 * Math.PI;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, Field.Height));
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            case GameState.GameOver: DrawGameOver(dc); break;
            default:
                DrawTower(dc, g.World);
                DrawPanel(dc);
                if (g.Message is { } msg)
                {
                    double wdt = msg.Length * 22 + 40;
                    dc.FillRectangle(Brush(0xB0000000), new Rect(Cx - wdt / 2, 70, wdt, 64), 8);
                    Text(dc, msg, new Point(Cx, 102), 48, g.Phase == Phase.Dying ? Red : Yellow, center: true);
                }
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, 22), 22, Yellow, center: true);
    }

    // ───────────────────────────── the tower ─────────────────────────────

    private static uint Shade(uint c, double k)
    {
        uint Ch(int sh) => (uint)Math.Clamp(((c >> sh) & 0xFF) * k, 0, 255);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    private void DrawTower(DrawingContext dc, World w)
    {
        var t = w.Tower;
        var p = w.Pogo;
        _turn = p.X;
        _camY = Math.Clamp(p.Y - 30, t.TopRow * T - 120, t.Height - ViewH * 0.3);
        using var clip = dc.PushClip(new Rect(0, 0, Field.Width, ViewH));
        DrawSky(dc, t);
        // The tower's face, column by column, lit from the front.
        double top = Sy(t.TopRow * T - 24), foot = Sy(t.Height + 20);
        int r0 = Math.Max(t.TopRow, (int)((_camY - ViewH) / T)), r1 = Math.Min(t.Rows, (int)((_camY + ViewH) / T) + 2);
        for (int c = 0; c < Field.Cols; c++)
        {
            double a0 = Angle(c * T), a1 = a0 + 2 * Math.PI / Field.Cols, mid = (a0 + a1) / 2;
            if (Math.Cos(mid) <= -0.05) continue;
            a0 = Math.Clamp(a0, -Math.PI / 2, Math.PI / 2);
            a1 = Math.Clamp(a1, -Math.PI / 2, Math.PI / 2);
            double x0 = Cx + Radius * Math.Sin(a0), x1 = Cx + Radius * Math.Sin(a1);
            double lit = 0.3 + 0.7 * Math.Max(0, Math.Cos(mid));
            dc.FillRectangle(Brush(Shade(t.Colour, lit)), new Rect(x0, top, Math.Max(0.5, x1 - x0 + 0.5), foot - top));
            // Brick joints: down each column on even rows, across the middle on odd ones.
            uint joint = Shade(t.Colour, lit * 0.55);
            for (int r = r0; r < r1; r++)
            {
                double y0 = Sy(r * T), y1 = Sy((r + 1) * T);
                double jx = r % 2 == 0 ? x0 : Cx + Radius * Math.Sin(Math.Clamp(mid, -Math.PI / 2, Math.PI / 2));
                dc.DrawLine(Pen(joint, 1.5), new Point(jx, y0), new Point(jx, y1));
            }
        }
        for (int r = r0; r <= r1; r++)
        {
            double y = Sy(r * T);
            dc.DrawLine(Pen(Shade(t.Colour, 0.35), 1.5), new Point(Cx - Radius, y), new Point(Cx + Radius, y));
        }
        // The battlements round the top.
        for (int k = -6; k <= 6; k++)
        {
            double a = Angle(k * 2 * T + (_turn - _turn % (2 * T)));
            if (Math.Cos(a) <= 0) continue;
            double x = Cx + Radius * Math.Sin(a);
            dc.FillRectangle(Brush(Shade(t.Colour, 0.4 + 0.6 * Math.Cos(a))), new Rect(x - 12 * Math.Cos(a), top - 26, 24 * Math.Cos(a), 28));
        }
        // Ledges and doorways.
        for (int r = r0; r < r1; r++)
        for (int c = 0; c < Field.Cols; c++)
        {
            var tile = w.Grid[c, r];
            if (tile == Tile.Empty) continue;
            double a0 = Angle(c * T), a1 = a0 + 2 * Math.PI / Field.Cols, mid = (a0 + a1) / 2;
            if (Math.Cos(mid) <= 0.02) continue;
            a0 = Math.Clamp(a0, -Math.PI / 2, Math.PI / 2);
            a1 = Math.Clamp(a1, -Math.PI / 2, Math.PI / 2);
            double y = Sy(r * T), lit = 0.35 + 0.65 * Math.Cos(mid);
            if (tile == Tile.Door)
            {
                // A dark arch into the tower above the ledge.
                double dx0 = Cx + Radius * Math.Sin(a0) + 3, dx1 = Cx + Radius * Math.Sin(a1) - 3;
                dc.FillRectangle(Brush(0xFF100808), new Rect(dx0, y - 46, Math.Max(1, dx1 - dx0), 46));
                dc.DrawEllipse(Brush(0xFF100808), null, new Point((dx0 + dx1) / 2, y - 46), Math.Max(1, (dx1 - dx0) / 2), 10);
            }
            double ox0 = Cx + (Radius + Out) * Math.Sin(a0), ox1 = Cx + (Radius + Out) * Math.Sin(a1);
            double ix0 = Cx + Radius * Math.Sin(a0), ix1 = Cx + Radius * Math.Sin(a1);
            uint body = tile == Tile.Crumble ? 0xFFA07040 : 0xFFD0D0E0;
            double left = tile == Tile.Crumble ? Math.Max(0.2, w.Crumble[c, r] / Sim.CrumbleTime) : 1;
            // Its top (seen just from above), its front, and the side that faces us.
            Poly(dc, Shade(body, lit * 1.1), null, 0, new Point(ix0, y - 5), new Point(ix1, y - 5), new Point(ox1, y), new Point(ox0, y));
            dc.FillRectangle(Brush(Shade(body, lit * 0.8)), new Rect(ox0, y, Math.Max(1, ox1 - ox0), 12 * left));
            if (mid > 0.15) Poly(dc, Shade(body, lit * 0.55), null, 0, new Point(ix0, y - 5), new Point(ox0, y), new Point(ox0, y + 12 * left), new Point(ix0, y + 7));
            if (mid < -0.15) Poly(dc, Shade(body, lit * 0.55), null, 0, new Point(ix1, y - 5), new Point(ox1, y), new Point(ox1, y + 12 * left), new Point(ix1, y + 7));
            if (tile == Tile.Crumble)
                for (int k = 0; k < 3; k++) dc.DrawLine(Pen(0xFF4A2A10, 1.5), new Point(ox0 + (ox1 - ox0) * (0.2 + k * 0.3), y), new Point(ox0 + (ox1 - ox0) * (0.3 + k * 0.3), y + 10));
        }
        // Balls, eyes, snowballs.
        foreach (var b in w.Balls)
        {
            if (b.Dead) continue;
            var (bx, by) = Sim.BallAt(b);
            double a = Angle(bx);
            if (Math.Cos(a) <= 0) continue;
            double x = Cx + (Radius + Out / 2) * Math.Sin(a), k = 0.6 + 0.4 * Math.Cos(a);
            dc.DrawEllipse(Brush(0xFFE04040), Pen(0xFF601010, 2), new Point(x, Sy(by)), 14 * k, 14 * k);
            dc.FillEllipse(x - 4 * k, Sy(by) - 5 * k, 4 * k, 0xFFFFB0B0);
        }
        foreach (var e in w.Eyes)
        {
            var (ex, ey) = Sim.EyeAt(w, e);
            double a = Angle(ex);
            if (Math.Cos(a) <= 0) continue;
            double x = Cx + (Radius + 70) * Math.Sin(a), k = 0.6 + 0.4 * Math.Cos(a), y = Sy(ey);
            double flap = Math.Sin(_clock * 12 + e.Def.Phase * 9) * 8 * k;
            Poly(dc, 0xFF8060C0, null, 0, new Point(x - 6 * k, y), new Point(x - 26 * k, y - 10 - flap), new Point(x - 10 * k, y + 4));
            Poly(dc, 0xFF8060C0, null, 0, new Point(x + 6 * k, y), new Point(x + 26 * k, y - 10 - flap), new Point(x + 10 * k, y + 4));
            dc.DrawEllipse(Brush(0xFFFFFFFF), Pen(0xFF8060C0, 2), new Point(x, y), 12 * k, 12 * k);
            dc.FillEllipse(x + Math.Sign(e.Def.Speed) * 3 * k, y, 5 * k, 0xFF2040C0);
        }
        foreach (var s in w.Snowballs)
        {
            double a = Angle(s.X);
            if (Math.Cos(a) <= 0) continue;
            dc.FillEllipse(Cx + (Radius + Out / 2) * Math.Sin(a), Sy(s.Y), 6, 0xFFFFFFFF);
        }
        if (!p.Dead || (int)(Engine.PhaseTime * 10) % 2 == 0) DrawPogo(dc, Cx, Sy(p.Y), p.Facing, p.Airborne ? 0 : p.Walk, p.Knocked > 0 ? _clock * 12 : 0);
        DrawSea(dc, t);
    }

    private void DrawSky(DrawingContext dc, TowerDef t)
    {
        dc.FillRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromUInt32(0xFF060818), 0), new GradientStop(Color.FromUInt32(0xFF201848), 1) },
        }, new Rect(0, 0, Field.Width, ViewH));
        // Stars that drift with the tower's turning, slower (they're far off).
        for (int k = 0; k < 80; k++)
        {
            double x = (Hash(k, 1, 3) % 2000 - _turn * 0.25) % 1024;
            if (x < 0) x += 1024;
            double y = (Hash(k, 2, 3) % 1400 - _camY * 0.1) % ViewH;
            if (y < 0) y += ViewH;
            dc.FillEllipse(x, y, 1 + Hash(k, 3, 3) % 2 * 0.6, 0xFFB0B0E0);
        }
    }

    private void DrawSea(DrawingContext dc, TowerDef t)
    {
        double y = Sy(t.Height + 14);
        if (y > ViewH) return;
        dc.FillRectangle(Brush(0xE0103060), new Rect(0, y, Field.Width, ViewH - y + 10));
        for (int k = 0; k < 40; k++)
        {
            double x = (k * 31 + _clock * 40 - _turn * 0.6) % 1100 - 40;
            if (x < -40) x += 1100;
            dc.DrawLine(Pen(0xFF60A0E0, 2), new Point(x, y + 6 + k % 4 * 14), new Point(x + 18, y + 4 + k % 4 * 14));
        }
        dc.DrawLine(Pen(0xFF80C0FF, 2), new Point(0, y), new Point(Field.Width, y));
    }

    /// <summary>Pogo: a round green creature with big eyes, on two little feet.</summary>
    private void DrawPogo(DrawingContext dc, double x, double feet, int facing, double walk, double spin)
    {
        double step = Math.Sin(walk * 14) * 4;
        using (dc.PushTransform(Matrix.CreateTranslation(-x, -(feet - 20)) * Matrix.CreateRotation(spin) * Matrix.CreateTranslation(x, feet - 20)))
        {
            dc.FillRectangle(Brush(0xFF206020), new Rect(x - 9 + step, feet - 8, 8, 8), 3);
            dc.FillRectangle(Brush(0xFF206020), new Rect(x + 1 - step, feet - 8, 8, 8), 3);
            dc.DrawEllipse(Brush(0xFF40C040), Pen(0xFF206020, 2), new Point(x, feet - 24), 16, 17);
            dc.FillEllipse(x + facing * 4 - 6, feet - 32, 6, 0xFFFFFFFF);
            dc.FillEllipse(x + facing * 4 + 6, feet - 32, 6, 0xFFFFFFFF);
            dc.FillEllipse(x + facing * 6 - 6, feet - 31, 3, 0xFF101010);
            dc.FillEllipse(x + facing * 6 + 6, feet - 31, 3, 0xFF101010);
            dc.DrawLine(Pen(0xFF206020, 2), new Point(x + facing * 2 - 6, feet - 18), new Point(x + facing * 2 + 6, feet - 18));
        }
    }

    // ───────────────────────────── the panel ─────────────────────────────

    private void DrawPanel(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        double y = ViewH;
        dc.FillRectangle(Brush(0xFF100C20), new Rect(0, y, Field.Width, Field.Panel));
        dc.FillRectangle(Brush(w.Tower.Colour), new Rect(0, y, Field.Width, 3));
        Text(dc, $"SCORE {g.Score:000000}", new Point(24, y + 24), 28, Yellow);
        Text(dc, $"HIGH  {Math.Max(g.HighScores.TopScore, g.Score):000000}", new Point(24, y + 54), 22, 0xFFB0A0D0);
        Text(dc, w.Tower.Name, new Point(Cx, y + 24), 26, Shade(w.Tower.Colour, 1.4), center: true);
        // How high: a bar from the foot to the top.
        double frac = 1 - (w.Pogo.Y / Field.Tile - w.Tower.TopRow) / (w.Tower.Rows - 1 - w.Tower.TopRow);
        dc.FillRectangle(Brush(0xFF2A2440), new Rect(Cx - 150, y + 46, 300, 12), 4);
        dc.FillRectangle(Brush(0xFF40C040), new Rect(Cx - 150, y + 46, 300 * Math.Clamp(frac, 0, 1), 12), 4);
        Text(dc, $"TIME {(int)Math.Ceiling(w.TimeLeft)}", new Point(Field.Width - 24, y + 24), 28, w.TimeLeft < 20 && (int)(_clock * 3) % 2 == 0 ? Red : White, right: true);
        for (int i = 0; i < Math.Min(g.Lives - 1, 6); i++)
            using (dc.PushTransform(Matrix.CreateScale(0.6, 0.6) * Matrix.CreateTranslation(Field.Width - 30 - i * 28, y + 70)))
                DrawPogo(dc, 0, 0, 1, 0, 0);
    }

    // ───────────────────────────── title, game over and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        // A tower turning behind the title.
        var t = _titleTower ??= TowerBook.Build(0);
        var w = _titleWorld ??= MakeTitleWorld(t);
        w.Pogo.X = TowerDef.WrapX(_clock * 60);
        w.Pogo.Y = (t.Rows - 6) * T;
        DrawTower(dc, w);
        dc.FillRectangle(Brush(0xB0000000), new Rect(0, 30, Field.Width, 150));
        string title = "AVA NEBULUS";
        for (int i = 0; i < title.Length; i++)
        {
            double tx = cx - title.Length * 32 + 32 + i * 64, ty = 92 + Math.Sin(_clock * 2.4 + i * 0.5) * 5;
            Text(dc, title[i].ToString(), new Point(tx, ty), 110, i % 2 == 0 ? 0xFF60E060 : 0xFFE0E060, center: true);
        }
        Text(dc, "AFTER JOHN M. PHILLIPS'S 1987 GAME FOR HEWSON", new Point(cx, 160), 22, Dim, center: true);
        dc.FillRectangle(Brush(0xC0000000), new Rect(0, 470, Field.Width, 298));
        Text(dc, "TOWERS HAVE RISEN FROM THE SEA. POGO MUST CLIMB EACH ONE TO THE TOP BEFORE THE TIME RUNS OUT:", new Point(cx, 494), 20, White, center: true);
        Text(dc, "ROUND THE TOWER ON ITS LEDGES, THROUGH ITS DOORWAYS, PAST THE ROLLING BALLS AND THE FLYING EYES.", new Point(cx, 520), 20, White, center: true);
        Text(dc, "THEY KNOCK HIM OFF; SNOWBALLS STOP THE BALLS. FALL INTO THE SEA AND IT'S BACK TO THE BOTTOM.", new Point(cx, 546), 20, White, center: true);
        Text(dc, "PRESS ENTER TO START", new Point(cx, 604), 40, (int)(_clock * 2) % 2 == 0 ? Yellow : Orange, center: true);
        Text(dc, "LEFT/RIGHT: WALK ROUND   SPACE: JUMP   UP: DOORWAY   CTRL OR X: SNOWBALL   H: PAUSE", new Point(cx, 660), 20, Dim, center: true);
        Text(dc, $"HIGH SCORE {Engine.HighScores.TopScore:000000}", new Point(cx, 710), 24, Cyan, center: true);
    }

    private TowerDef? _titleTower;
    private World? _titleWorld;

    private static World MakeTitleWorld(TowerDef t)
    {
        var w = new World();
        Sim.Start(w, t);
        return w;
    }

    private void DrawGameOver(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        Text(dc, "GAME OVER", new Point(cx, 240), 110, Red, center: true);
        Text(dc, $"TOWERS CLIMBED {Engine.Cleared}", new Point(cx, 330), 34, White, center: true);
        DrawPogo(dc, cx, 470, 1, 0, Math.Sin(_clock * 3) * 0.3);
        Text(dc, $"SCORE {Engine.Score}", new Point(cx, 560), 44, Yellow, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "THE HIGHEST CLIMBERS", new Point(cx, 120), 64, 0xFFFFE040, center: true);
        Text(dc, "RANK NAME    SCORE  TOWERS", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
