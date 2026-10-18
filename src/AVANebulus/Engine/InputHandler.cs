using Avalonia.Input;

namespace AVANebulus.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: left and right, up and down (up the screen is -1), and a button.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update()
    {
        _pad.Update();
    }

    /// <summary>
    /// Left and right (or A and D, or O and P) to walk round the tower; Space or Z to jump; up (or W or Q) into a
    /// doorway; Ctrl, X or F to throw a snowball. On a gamepad: the stick or D-pad, A to jump, up for a doorway, B or X
    /// to throw.
    /// </summary>
    public Controls Player
    {
        get
        {
            var pad = _pad;
            int dx = (Any(Key.Right, Key.D, Key.P, Key.NumPad6) ? 1 : 0) - (Any(Key.Left, Key.A, Key.O, Key.NumPad4) ? 1 : 0);
            if (dx == 0) dx = pad.DPadLeft || pad.LeftStickX < -0.4f ? -1 : pad.DPadRight || pad.LeftStickX > 0.4f ? 1 : 0;
            bool jump = Any(Key.Space, Key.Z) || pad.ButtonA;
            bool up = Any(Key.Up, Key.W, Key.Q, Key.NumPad8) || pad.DPadUp || pad.LeftStickY < -0.5f;
            bool fire = Any(Key.LeftCtrl, Key.RightCtrl, Key.X, Key.F) || pad.ButtonB || pad.ButtonX;
            return new Controls(dx, jump, up, fire);
        }
    }

    /// <summary>Enter or 1.</summary>
    public bool Start => Any(Key.Enter, Key.D1, Key.NumPad1) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
