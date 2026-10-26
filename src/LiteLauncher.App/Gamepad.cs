using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace LiteLauncher.App;

public enum GamepadButton { Up, Down, Left, Right, A, B, Y, Start, Select }

/// <summary>
/// Tiny XInput poller (Xbox controllers and anything Steam Input exposes as XInput).
/// Raises edge-triggered events with auto-repeat for held directions.
/// </summary>
public sealed class Gamepad : IDisposable
{
    private const ushort DPadUp = 0x0001, DPadDown = 0x0002, DPadLeft = 0x0004, DPadRight = 0x0008;
    private const ushort StartBtn = 0x0010, SelectBtn = 0x0020, ABtn = 0x1000, BBtn = 0x2000, YBtn = 0x8000;
    private const short StickDeadzone = 16000;

    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan RepeatRate = TimeSpan.FromMilliseconds(110);

    private readonly DispatcherTimer _timer;
    private readonly Dictionary<GamepadButton, DateTime> _nextFire = new();

    public Gamepad()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(30) };
        _timer.Tick += (_, _) => Poll();
    }

    public event Action<GamepadButton>? ButtonPressed;

    /// <summary>Select + Start held together for about a second and a half (works even when no window has focus).</summary>
    public event Action? ComboHeld;

    private static readonly TimeSpan ComboHold = TimeSpan.FromMilliseconds(1500);
    private DateTime? _comboStart;
    private bool _comboFired;

    public void Start() => _timer.Start();

    public void Dispose() => _timer.Stop();

    private void Poll()
    {
        XInputState state = default;
        var connected = false;
        for (uint i = 0; i < 4 && !connected; i++)
        {
            try { connected = XInputGetState(i, out state) == 0; }
            catch (DllNotFoundException) { _timer.Stop(); return; } // XInput unavailable
        }
        if (!connected) return;

        var g = state.Gamepad;
        Update(GamepadButton.Up,    (g.Buttons & DPadUp) != 0    || g.ThumbLY >  StickDeadzone, repeat: true);
        Update(GamepadButton.Down,  (g.Buttons & DPadDown) != 0  || g.ThumbLY < -StickDeadzone, repeat: true);
        Update(GamepadButton.Left,  (g.Buttons & DPadLeft) != 0  || g.ThumbLX < -StickDeadzone, repeat: true);
        Update(GamepadButton.Right, (g.Buttons & DPadRight) != 0 || g.ThumbLX >  StickDeadzone, repeat: true);
        Update(GamepadButton.A,     (g.Buttons & ABtn) != 0,     repeat: false);
        Update(GamepadButton.B,     (g.Buttons & BBtn) != 0,     repeat: false);
        Update(GamepadButton.Y,     (g.Buttons & YBtn) != 0,     repeat: false);
        Update(GamepadButton.Start, (g.Buttons & StartBtn) != 0, repeat: false);
        Update(GamepadButton.Select, (g.Buttons & SelectBtn) != 0, repeat: false);
        UpdateCombo((g.Buttons & SelectBtn) != 0 && (g.Buttons & StartBtn) != 0);
    }

    private void UpdateCombo(bool both)
    {
        if (!both)
        {
            _comboStart = null;
            _comboFired = false;
            return;
        }

        var now = DateTime.UtcNow;
        _comboStart ??= now;
        if (_comboFired || now - _comboStart < ComboHold) return;

        _comboFired = true; // once per hold
        ComboHeld?.Invoke();
    }

    private void Update(GamepadButton button, bool down, bool repeat)
    {
        if (!down) { _nextFire.Remove(button); return; }

        var now = DateTime.UtcNow;
        if (_nextFire.TryGetValue(button, out var due))
        {
            if (!repeat || now < due) return;
            _nextFire[button] = now + RepeatRate;
        }
        else
        {
            _nextFire[button] = now + RepeatDelay;
        }

        ButtonPressed?.Invoke(button);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetState(uint userIndex, out XInputState state);
}
