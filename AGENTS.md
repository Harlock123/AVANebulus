# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A platform game round the outside of a turning tower, in the style of John M. Phillips's 1987 game Nebulus, in
C# on .NET 10 and Avalonia 12, drawn through Avalonia's `DrawingContext`. One self-contained executable per platform (Windows, macOS
and Linux, x64 and ARM). Everything in it is original: the graphics are drawn in code, and the sounds, tunes and
voice are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVANebulus.slnx                 # the SDK version is in global.json (10.0.400 or later)
dotnet test AVANebulus.slnx                  # every test runs headless: no window opens
dotnet run --project src/AVANebulus          # play it; --no-audio for silence
NEB_SHOTS=$PWD/Images dotnet test tests/AVANebulus.Tests --filter ScreenshotTests   # refresh the README's pictures
NEB_PROBE=1 dotnet test tests/AVANebulus.Tests --filter PilotProbe --logger "console;verbosity=detailed"   # tuning probe
python3 tools/make_sounds.py                 # rebuild src/AVANebulus/Sounds (uses tools/speech.py)
```

`NEB_PROBE=lazy` runs the probe with a Pogo that just walks round and jumps now and then.
`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVANebulus/Engine/`: the game, with no Avalonia drawing in it.
  - `Tower.cs` has `Controls`, the `Field` (measures and the 1/60 s `Step`), the tiles, `TowerDef` (a tower
    unrolled flat, its columns wrapping round) and `TowerBook`, which generates the towers.
  - `World.cs` has the `Dice` random source and the `World`: the tower's grid, Pogo, the balls, eyes and
    snowballs, the clock and score, kept together so it can be copied whole (`World.Copy()`).
  - `Sim.cs` steps the tower: walking, the fixed jump arc, falls, crumbling ledges, doorways, enemies and the sea.
  - `Graph.cs` maps every ledge Pogo can stand on and the moves between them. The tower builder and the computer
    player both use it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and the fixed-step loop.
  - `Pilot.cs` is the computer Pogo, used by the attract mode and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVANebulus/Views/`: `GameCanvas` draws the fixed 1024x768 picture, scaled to the window, and runs the
  60 Hz timer; `GameCanvas.Draw.cs` holds the drawing.
- `src/AVANebulus/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `src/AVANebulus/Sounds/`: the generated WAV files.
- `tests/AVANebulus.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: `GameplayTests`, `TowerTests`,
  `ScreenshotTests` and the opt-in `PilotProbe`.
- `tools/`: `make_sounds.py` and `speech.py`, a small formant speech synthesiser for the voice.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` feeds frame time into an accumulator and runs the simulation in fixed
  `Field.Step` (1/60 s) steps, so tests can play towers deterministically. Use the seeded `Dice` struct for any
  randomness, never `Random.Shared`.
- **Every tower can be climbed.** `TowerBook` builds each tower from a fixed seed and keeps trying seeds until
  `Graph` proves the top can be reached from the foot. `TowerTests` checks every tower. Change the jump arc or
  the tile rules in `Sim` and `Graph` together.
- **Copyable world.** Everything that changes during play lives in `World`, and `World.Copy()` must copy all of
  it: the pilot tries moves out on copies. A new field needs adding to the copy.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the world and draws it.
- **Input goes through `InputHandler`.** There is no `dy` here: `Controls` has `Dx`, `Jump`, `Up` (into a
  doorway) and `Fire`. `Up` comes from the up keys, D-pad up or the stick pushed up (negative `LeftStickY`). Test
  new controls through `InputHandler`, not by building `Controls` directly.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVANebulus`); tests load
  the table from a temporary folder and run with `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
After a release it asks the portfolio site to refresh, if the `SITE_REFRESH_TOKEN` secret is set.
