using Avalonia.Input;
using AVANebulus.Engine;

namespace AVANebulus.Tests;

public class GameplayTests
{
    private const double T = Field.Tile;
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avanebulus-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A bare tower: a ring at the foot, one at the top, and whatever else is asked for.</summary>
    private static World Bare(Action<TowerDef>? build = null, int rows = 12)
    {
        var t = new TowerDef { Rows = rows, Grid = new Tile[Field.Cols, rows], Time = 100, TopRow = 2 };
        for (int c = 0; c < Field.Cols; c++) { t.Grid[c, rows - 1] = Tile.Ledge; t.Grid[c, 2] = Tile.Ledge; }
        build?.Invoke(t);
        var w = new World();
        Sim.Start(w, t);
        return w;
    }

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    [Fact]
    public void He_walks_round_and_round_the_tower()
    {
        var w = Bare();
        Run(w, 60, new Controls(1));
        Assert.Equal(0.5 * T + Pogo.Speed, w.Pogo.X, 3);
        Run(w, (int)(60 * Field.Round / Pogo.Speed), new Controls(1));
        Assert.Equal(0.5 * T + Pogo.Speed, w.Pogo.X, 0);
    }

    [Fact]
    public void A_jump_lands_on_a_ledge_one_row_up()
    {
        var w = Bare(t => { t.Grid[1, 10] = Tile.Ledge; t.Grid[2, 10] = Tile.Ledge; });
        Sim.Step(w, new Controls(1, true));
        for (int i = 0; i < 60 && w.Pogo.Airborne; i++) Sim.Step(w, new Controls(1));
        Assert.False(w.Pogo.Airborne);
        Assert.Equal(10 * T, w.Pogo.Y, 3);
    }

    [Fact]
    public void Two_rows_up_is_out_of_reach()
    {
        var w = Bare(t => { for (int c = 1; c <= 4; c++) t.Grid[c, 9] = Tile.Ledge; });
        Sim.Step(w, new Controls(1, true));
        Run(w, 90, new Controls(1));
        Assert.Equal(11 * T, w.Pogo.Y, 3);
    }

    [Fact]
    public void Off_the_end_of_a_ledge_he_drops_to_the_next_below()
    {
        var w = Bare(t => { t.Grid[0, 8] = Tile.Ledge; t.Grid[3, 10] = Tile.Ledge; });
        w.Pogo.Y = 8 * T;
        Run(w, 90, new Controls(1));
        Assert.False(w.Pogo.Dead);
        Assert.Equal(11 * T, w.Pogo.Y, 3);
    }

    [Fact]
    public void A_doorway_takes_him_through_to_the_far_side()
    {
        var w = Bare(t => { t.Grid[0, 11] = Tile.Door; t.Grid[Field.Cols / 2, 11] = Tile.Door; });
        Sim.Step(w, new Controls(Up: true));
        Assert.Equal((Field.Cols / 2 + 0.5) * T, w.Pogo.X, 3);
        Assert.Contains(Sfx.Door, w.Heard);
    }

    [Fact]
    public void A_crumbling_ledge_gives_way_under_him()
    {
        var w = Bare(t => t.Grid[0, 8] = Tile.Crumble);
        w.Pogo.Y = 8 * T;
        Run(w, 90);
        Assert.Equal(Tile.Empty, w.Grid[0, 8]);
        Assert.Equal(11 * T, w.Pogo.Y, 3);
    }

    [Fact]
    public void A_ball_knocks_him_off_his_ledge_and_a_snowball_stops_it()
    {
        var w = Bare(t => { for (int c = 0; c <= 6; c++) t.Grid[c, 8] = Tile.Ledge; t.Balls.Add(new BallDef(8, 0.5 * T, 6.5 * T, 60)); });
        w.Pogo.Y = 8 * T;
        Run(w, 60 * 8);
        Assert.True(w.Pogo.Y > 8 * T, "knocked off");
        var s = Bare(t => { for (int c = 0; c <= 6; c++) t.Grid[c, 8] = Tile.Ledge; t.Balls.Add(new BallDef(8, 4.5 * T, 6.5 * T, 0)); });
        s.Pogo.Y = 8 * T;
        Run(s, 2);
        int before = s.Score;
        Sim.Step(s, new Controls(Fire: true));
        Run(s, 30);
        Assert.True(s.Balls[0].Dead);
        Assert.Equal(Sim.BallPoints, s.Score - before);
    }

    [Fact]
    public void An_eye_knocks_him_off_too()
    {
        var w = Bare(t => { for (int c = 0; c <= 6; c++) t.Grid[c, 8] = Tile.Ledge; t.Eyes.Add(new EyeDef(8 * T - 22, 80, 0.1)); });
        w.Pogo.Y = 8 * T;
        bool knocked = false;
        for (int i = 0; i < 60 * 15 && !knocked; i++) { Sim.Step(w, default); knocked = w.Pogo.Knocked > 0; }
        Assert.True(knocked);
    }

    [Fact]
    public void Into_the_sea_is_a_life_lost()
    {
        var w = Bare(t => { for (int c = 2; c <= 6; c++) t.Grid[c, t.Rows - 1] = Tile.Empty; });
        Run(w, 120, new Controls(1));
        Assert.True(w.Pogo.Dead);
        Assert.Equal("INTO THE SEA", w.Cause);
    }

    [Fact]
    public void Climbing_scores_and_the_top_ring_is_the_end_of_the_tower()
    {
        var w = Bare(t => { t.Grid[1, 10] = Tile.Ledge; t.Grid[2, 10] = Tile.Ledge; });
        Sim.Step(w, new Controls(1, true));
        Run(w, 60, new Controls(1));
        Assert.Equal(Sim.RowPoints, w.Score);
        w.Pogo.Y = 2 * T;
        Run(w, 2);
        Assert.True(w.Up);
    }

    [Fact]
    public void The_clock_runs_out()
    {
        var w = Bare();
        w.TimeLeft = 1;
        Run(w, 70);
        Assert.Equal("OUT OF TIME", w.Cause);
    }

    // ───────────────────────────── the game ─────────────────────────────

    private static GameEngine Engine() => new(HighScoreTable.Load(TempFolder())) { Silent = true };

    [Fact]
    public void At_the_top_the_time_left_scores_and_on_to_the_next_tower()
    {
        var g = Engine();
        g.Go();
        g.World.TimeLeft = 50;
        g.World.Pogo.Y = g.World.Tower.TopRow * T;
        g.Run(2);
        Assert.Equal(Phase.Up, g.Phase);
        g.Run(60 * 6);
        Assert.Equal(1, g.TowerIndex);
        Assert.Equal(1, g.Cleared);
        Assert.True(g.Score >= 49 * GameEngine.TimePoints);
    }

    [Fact]
    public void Into_the_sea_and_back_to_the_foot_and_the_last_life_is_game_over()
    {
        var g = Engine();
        g.Go();
        Sim.Die(g.World, "INTO THE SEA");
        g.Run((int)(60 * GameEngine.DyingSeconds) + 3);
        Assert.Equal(GameEngine.Lives0 - 1, g.Lives);
        Assert.Equal((g.World.Tower.Rows - 1) * T, g.World.Pogo.Y, 3);
        g.SetLives(1);
        Sim.Die(g.World, "INTO THE SEA");
        g.Run((int)(60 * GameEngine.DyingSeconds) + 3);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void The_title_gives_way_to_the_demonstration_and_enter_starts_a_game()
    {
        var g = Engine();
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 8);
        Assert.True(g.World.Pogo.Y < (g.World.Tower.Rows - 1) * T || g.World.Pogo.X != 0.5 * T);
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
    }

    [Fact]
    public void Paused_nothing_moves()
    {
        var g = Engine();
        g.Go();
        g.Run(10, new Controls(1));
        g.Pause();
        double x = g.World.Pogo.X;
        g.Run(60, new Controls(1));
        Assert.Equal(x, g.World.Pogo.X);
        g.Resume();
        g.Run(10, new Controls(1));
        Assert.NotEqual(x, g.World.Pogo.X);
    }

    [Fact]
    public void The_keys_walk_jump_go_through_doorways_and_throw()
    {
        using var input = new InputHandler();
        foreach (var (key, check) in new (Key, Func<Controls, bool>)[]
                 {
                     (Key.Left, c => c.Dx == -1), (Key.Right, c => c.Dx == 1), (Key.O, c => c.Dx == -1), (Key.P, c => c.Dx == 1),
                     (Key.Space, c => c.Jump), (Key.Up, c => c.Up), (Key.LeftCtrl, c => c.Fire), (Key.X, c => c.Fire),
                 })
        {
            input.OnKeyDown(key);
            Assert.True(check(input.Player), key.ToString());
            input.OnKeyUp(key);
        }
    }
}
