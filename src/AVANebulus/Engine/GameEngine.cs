namespace AVANebulus.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>The tower named; a moment before the climb.</summary>
    Start,
    Play,
    /// <summary>Into the sea (or out of time): a moment, then back at the foot of the tower, or the game is over.</summary>
    Dying,
    /// <summary>At the top: the time left is counted into the score, then the next tower.</summary>
    Up,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Jump, Land, Throw, BallDown, Crumble, Door, Knocked, Splash, Die, Top, Tick, ExtraLife,
    SayHurry,
    Music, MusicStop, TitleTune, GameOver, Victory,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 16;
    public const double AttractSeconds = 45;
    public const double HighScoreSeconds = 9;
    public const double StartSeconds = 2, DyingSeconds = 2, GameOverSeconds = 4;
    public const int Lives0 = 3, ExtraEvery = 10000, TimePoints = 20;

    private double _accumulator, _idle;
    private bool _startPrev, _tunePending = true, _music, _hurried;
    private int _nextExtra;

    public GameEngine(HighScoreTable? table = null)
    {
        HighScores = table ?? HighScoreTable.Load();
        NewGame();
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    public int Lives { get; private set; }
    public int Score => World.Score;
    public int TowerIndex { get; private set; }
    /// <summary>Towers climbed this game.</summary>
    public int Cleared { get; private set; }
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = null; }
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed || c.Jump && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) { Music(false); ShowHighScores(-1); }
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > GameOverSeconds)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Cleared);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void NewGame()
    {
        World = new World();
        Lives = Lives0;
        Cleared = 0;
        _nextExtra = ExtraEvery;
        Enter(0);
    }

    private void Enter(int index)
    {
        TowerIndex = index;
        int score = World.Score;
        Sim.Start(World, TowerBook.Build(index));
        World.Score = score;
        _hurried = false;
        SetPhase(Phase.Start);
        Message = World.Tower.Name;
        MessageTime = StartSeconds;
        Pilot.Reset();
    }

    public void StartGame()
    {
        NewGame();
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
    }

    private void StartAttract()
    {
        NewGame();
        State = GameState.Attract;
        StateTime = 0;
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Music(false);
        Message = null;
        NewGame();
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase == Phase.Play) Music(true);
    }

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        var w = World;
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime > StartSeconds) { SetPhase(Phase.Play); Music(true); }
                break;
            case Phase.Play:
            {
                int before = w.Score;
                Sim.Step(w, c);
                if (w.Score != before) ExtraLives();
                if (!_hurried && w.TimeLeft < 20 && !w.Pogo.Dead) { _hurried = true; Play(Sfx.SayHurry); }
                if (w.Pogo.Dead) { SetPhase(Phase.Dying); Music(false); Message = w.Cause; MessageTime = DyingSeconds; }
                else if (w.Up) { SetPhase(Phase.Up); Music(false); Message = "TOWER CLIMBED!"; MessageTime = 2.5; }
                break;
            }
            case Phase.Dying:
                if (PhaseTime > DyingSeconds)
                {
                    Lives--;
                    if (Lives <= 0) { End(); return; }
                    // Out of time: the tower again from the start, with the full time. Into the sea: back at the foot.
                    if (w.TimeLeft <= 0) { int score = w.Score; Sim.Start(w, w.Tower); w.Score = score; _hurried = false; }
                    else Sim.Respawn(w);
                    Pilot.Reset();
                    SetPhase(Phase.Play);
                    Music(true);
                }
                break;
            case Phase.Up:
                // The time left, counted in.
                if (w.TimeLeft > 0 && PhaseTime > 0.8)
                {
                    double take = Math.Min(w.TimeLeft, dt * 40);
                    int was = (int)w.TimeLeft;
                    w.TimeLeft -= take;
                    w.Score += (was - (int)w.TimeLeft) * TimePoints;
                    if (was != (int)w.TimeLeft) Play(Sfx.Tick);
                    ExtraLives();
                }
                if (w.TimeLeft <= 0 && PhaseTime > 2.5)
                {
                    Cleared++;
                    if ((TowerIndex + 1) % TowerBook.Count == 0) { Play(Sfx.Victory); Message = "EVERY TOWER CLIMBED!"; MessageTime = 3; }
                    Enter(TowerIndex + 1);
                }
                break;
        }
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    private void End()
    {
        Play(Sfx.GameOver);
        Music(false);
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        State = GameState.GameOver;
        StateTime = 0;
    }

    private void ExtraLives()
    {
        while (World.Score >= _nextExtra)
        {
            _nextExtra += ExtraEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight onto a tower, past its name.</summary>
    public void Go(int tower = 0)
    {
        NewGame();
        State = GameState.Playing;
        Enter(tower);
        SetPhase(Phase.Play);
        Message = null;
    }

    public void SetLives(int n) => Lives = n;
}
