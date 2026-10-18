using AVANebulus.Engine;

namespace AVANebulus.Tests;

/// <summary>How the computer Pogo gets on on each tower (set NEB_PROBE to run it, for tuning).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_Pogo_does()
    {
        if (Environment.GetEnvironmentVariable("NEB_PROBE") is not { } mode) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < TowerBook.Count; i++)
        {
            var t = TowerBook.Build(i);
            output.WriteLine($"tower {i} built by {sw.ElapsedMilliseconds} ms");
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "neb-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.Go(i);
            g.SetLives(30);
            var deaths = new Dictionary<string, int>();
            var doing = new Dictionary<string, int>();
            int knocks = 0;
            var last = g.Phase;
            bool wasKnocked = false;
            int tick = 0;
            for (; tick < 60 * 400 && g.State == GameState.Playing && g.Cleared == 0; tick++)
            {
                var c = mode == "lazy" ? new Controls(1, tick % 40 == 0) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[g.World.Cause] = deaths.GetValueOrDefault(g.World.Cause) + 1;
                if (g.World.Pogo.Knocked > 0 && !wasKnocked) knocks++;
                wasKnocked = g.World.Pogo.Knocked > 0;
                last = g.Phase;
                doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
            }
            output.WriteLine($"{t.Name}: {(g.Cleared > 0 ? "CLIMBED" : "no")} after {tick / 60}s (time allowed {t.Time}), {deaths.Values.Sum()} deaths ({string.Join(", ", deaths.Select(d => $"{d.Key} x{d.Value}"))}), {knocks} knocks; {string.Join(", ", doing.OrderByDescending(d => d.Value).Take(4).Select(d => $"{d.Key} {d.Value / 60}s"))}");
        }
        output.WriteLine($"total {sw.ElapsedMilliseconds} ms");
    }
}
