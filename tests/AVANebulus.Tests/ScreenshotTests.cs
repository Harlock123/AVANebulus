using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVANebulus.Engine;
using AVANebulus.Views;
using Controls = AVANebulus.Engine.Controls;

namespace AVANebulus.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set NEB_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avanebulus-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("NEB_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVANebulus-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("POG", 18400, 6);
        g.HighScores.Insert("AVA", 6200, 2);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 120 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    private static void Play(int tower, string name, Func<GameEngine, bool> moment, int seconds = 150)
    {
        var (w, c, g) = Open();
        g.Go(tower);
        g.SetLives(20);
        g.Pilot.Reset();
        Assert.True(PlayUntil(g, c, moment, seconds), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void At_the_foot() => Play(0, "foot", g => g.World.Clock > 3 && !g.World.Pogo.Airborne);

    [AvaloniaFact]
    public void A_jump() => Play(1, "jump", g => g.World.Pogo.Airborne && g.World.Pogo.Vy < 0 && g.World.Clock > 8);

    [AvaloniaFact]
    public void A_doorway() => Play(2, "doorway", g => g.World.Grid[Spot.Of(g.World.Pogo).Col, Spot.Of(g.World.Pogo).Row] == Tile.Door && !g.World.Pogo.Airborne, 200);

    [AvaloniaFact]
    public void A_ball() => Play(3, "ball", g => g.World.Balls.Any(b => !b.Dead && Math.Abs(TowerDef.Delta(g.World.Pogo.X, Sim.BallAt(b).X)) < 200 && Math.Abs(Sim.BallAt(b).Y - g.World.Pogo.Y) < 120), 200);

    [AvaloniaFact]
    public void An_eye() => Play(5, "eye", g => g.World.Eyes.Any(e => Math.Abs(TowerDef.Delta(g.World.Pogo.X, Sim.EyeAt(g.World, e).X)) < 180 && Math.Abs(Sim.EyeAt(g.World, e).Y - g.World.Pogo.Y) < 150), 200);

    [AvaloniaFact]
    public void Near_the_top() => Play(4, "top", g => g.World.Pogo.Y < (g.World.Tower.TopRow + 5) * Field.Tile && !g.World.Pogo.Airborne, 200);

    [AvaloniaFact]
    public void Climbed() => Play(0, "climbed", g => g.Phase == Phase.Up && g.PhaseTime > 1.2, 200);

    [AvaloniaFact]
    public void Game_over()
    {
        var (w, c, g) = Open();
        g.Go();
        g.SetLives(1);
        g.World.Score = 2340;
        Sim.Die(g.World, "INTO THE SEA");
        for (int i = 0; i < 600 && !(g.State == GameState.GameOver && g.StateTime > 1); i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Shot(w, c, "game-over");
    }
}
