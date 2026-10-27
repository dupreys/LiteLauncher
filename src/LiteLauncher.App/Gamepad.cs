using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace LiteLauncher.App;

public enum GamepadButton { Up, Down, Left, Right, A, B, Y, X, Start, Select, LeftShoulder, RightShoulder, LeftTrigger, RightTrigger }

/// <summary>
/// Controller poller. Xbox controllers (and anything Steam Input shows as XInput) are read through XInput; when no
/// XInput controller is connected, PlayStation, Switch Pro and generic DirectInput gamepads are read through the
/// Windows multimedia joystick API instead. Raises edge-triggered events with auto-repeat for held directions.
/// </summary>
public sealed class Gamepad : IDisposable
{
    private const ushort DPadUp = 0x0001, DPadDown = 0x0002, DPadLeft = 0x0004, DPadRight = 0x0008;
    private const ushort StartBtn = 0x0010, SelectBtn = 0x0020, ABtn = 0x1000, BBtn = 0x2000, YBtn = 0x8000, XBtn = 0x4000, LbBtn = 0x0100, RbBtn = 0x0200;
    private const short StickDeadzone = 16000;
    private const byte TriggerThreshold = 100;

    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan RepeatRate = TimeSpan.FromMilliseconds(110);

    private readonly DispatcherTimer _timer;
    private readonly Dictionary<GamepadButton, DateTime> _nextFire = new();
    private bool _xinputMissing;
    private int _joyScanCountdown;

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

    /// <summary>One reading of a controller, whatever API it came from.</summary>
    private struct Pad
    {
        public bool Up, Down, Left, Right, A, B, X, Y, Start, Select, LeftShoulder, RightShoulder, LeftTrigger, RightTrigger;
    }

    private void Poll()
    {
        if (!TryReadXInput(out var pad) && !TryReadJoystick(out pad)) return;

        Update(GamepadButton.Up, pad.Up, repeat: true);
        Update(GamepadButton.Down, pad.Down, repeat: true);
        Update(GamepadButton.Left, pad.Left, repeat: true);
        Update(GamepadButton.Right, pad.Right, repeat: true);
        Update(GamepadButton.A, pad.A, repeat: false);
        Update(GamepadButton.B, pad.B, repeat: false);
        Update(GamepadButton.Y, pad.Y, repeat: false);
        Update(GamepadButton.X, pad.X, repeat: false);
        Update(GamepadButton.LeftShoulder, pad.LeftShoulder, repeat: false);
        Update(GamepadButton.RightShoulder, pad.RightShoulder, repeat: false);
        Update(GamepadButton.LeftTrigger, pad.LeftTrigger, repeat: false);
        Update(GamepadButton.RightTrigger, pad.RightTrigger, repeat: false);
        Update(GamepadButton.Start, pad.Start, repeat: false);
        Update(GamepadButton.Select, pad.Select, repeat: false);
        UpdateCombo(pad.Select && pad.Start);
    }

    // ---- XInput --------------------------------------------------------------------------

    private bool TryReadXInput(out Pad pad)
    {
        pad = default;
        if (_xinputMissing) return false;

        XInputState state = default;
        var connected = false;
        for (uint i = 0; i < 4 && !connected; i++)
        {
            try { connected = XInputGetState(i, out state) == 0; }
            catch (DllNotFoundException) { _xinputMissing = true; return false; } // XInput unavailable: joystick API only
        }
        if (!connected) return false;

        var g = state.Gamepad;
        pad.Up = (g.Buttons & DPadUp) != 0 || g.ThumbLY > StickDeadzone;
        pad.Down = (g.Buttons & DPadDown) != 0 || g.ThumbLY < -StickDeadzone;
        pad.Left = (g.Buttons & DPadLeft) != 0 || g.ThumbLX < -StickDeadzone;
        pad.Right = (g.Buttons & DPadRight) != 0 || g.ThumbLX > StickDeadzone;
        pad.A = (g.Buttons & ABtn) != 0;
        pad.B = (g.Buttons & BBtn) != 0;
        pad.Y = (g.Buttons & YBtn) != 0;
        pad.X = (g.Buttons & XBtn) != 0;
        pad.LeftShoulder = (g.Buttons & LbBtn) != 0;
        pad.RightShoulder = (g.Buttons & RbBtn) != 0;
        pad.LeftTrigger = g.LeftTrigger > TriggerThreshold;
        pad.RightTrigger = g.RightTrigger > TriggerThreshold;
        pad.Start = (g.Buttons & StartBtn) != 0;
        pad.Select = (g.Buttons & SelectBtn) != 0;
        return true;
    }

    // ---- DirectInput-style controllers (PlayStation, Switch Pro, generic) via winmm ----------------

    private enum JoyLayout { Generic, PlayStation, Switch }

    private const int JoyIds = 4;
    private readonly JoyLayout?[] _joyLayout = new JoyLayout?[JoyIds]; // null = not a gamepad / not looked at yet
    private readonly bool[] _joyChecked = new bool[JoyIds];

    private bool TryReadJoystick(out Pad pad)
    {
        pad = default;

        // Devices that are not plugged in answer slowly; look for new ones only about once a second.
        var scanNew = --_joyScanCountdown <= 0;
        if (scanNew) _joyScanCountdown = 33;

        for (uint id = 0; id < JoyIds; id++)
        {
            if (!_joyChecked[id] && !scanNew) continue;

            var info = new JoyInfoEx { Size = (uint)Marshal.SizeOf<JoyInfoEx>(), Flags = 0xFF };
            uint result;
            try { result = joyGetPosEx(id, ref info); }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }

            if (result != 0)
            {
                _joyChecked[id] = false;
                _joyLayout[id] = null;
                continue;
            }

            if (!_joyChecked[id])
            {
                _joyChecked[id] = true;
                _joyLayout[id] = LayoutFor(id);
            }
            if (_joyLayout[id] is not { } layout) continue;

            pad = Read(info, layout);
            return true;
        }
        return false;
    }

    /// <summary>Only things that look like a gamepad count; wheels, flight sticks and virtual devices are ignored.</summary>
    private static JoyLayout? LayoutFor(uint id)
    {
        try
        {
            if (joyGetDevCapsW((UIntPtr)id, out var caps, (uint)Marshal.SizeOf<JoyCaps>()) != 0) return null;
            var name = caps.Name ?? "";
            if (Has(name, "dualsense") || Has(name, "dualshock") || Has(name, "wireless controller") || Has(name, "ps4") || Has(name, "ps5"))
                return JoyLayout.PlayStation;
            if (Has(name, "pro controller") || Has(name, "switch") || Has(name, "joy-con"))
                return JoyLayout.Switch;
            if (Has(name, "gamepad") || Has(name, "controller") || Has(name, "xbox") || Has(name, "joypad"))
                return JoyLayout.Generic;
            return null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ArgumentException)
        {
            return null;
        }

        static bool Has(string text, string part) => text.Contains(part, StringComparison.OrdinalIgnoreCase);
    }

    private static Pad Read(JoyInfoEx info, JoyLayout layout)
    {
        bool Down(int index) => index >= 0 && (info.Buttons & (1u << index)) != 0;

        // Button numbers, in the order: A (bottom), B (right), X (left), Y (top), LB, RB, LT, RT, Select, Start.
        int[] map = layout switch
        {
            JoyLayout.PlayStation => new[] { 1, 2, 0, 3, 4, 5, 6, 7, 8, 9 }, // Cross, Circle, Square, Triangle, L1, R1, L2, R2, Share, Options
            JoyLayout.Switch => new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 },       // B (bottom), A (right), Y (left), X (top), L, R, ZL, ZR, Minus, Plus
            _ => new[] { 0, 1, 2, 3, 4, 5, -1, -1, 6, 7 },                      // A, B, X, Y, LB, RB, Back, Start
        };

        var pad = new Pad
        {
            A = Down(map[0]),
            B = Down(map[1]),
            X = Down(map[2]),
            Y = Down(map[3]),
            LeftShoulder = Down(map[4]),
            RightShoulder = Down(map[5]),
            LeftTrigger = Down(map[6]),
            RightTrigger = Down(map[7]),
            Select = Down(map[8]),
            Start = Down(map[9]),
        };

        // D-pad: the hat switch, in hundredths of a degree (0 = up); values above 36000 mean "centred".
        var hat = info.Pov;
        if (hat <= 36000)
        {
            pad.Up = hat >= 31500 || hat <= 4500;
            pad.Right = hat is >= 4500 and <= 13500;
            pad.Down = hat is >= 13500 and <= 22500;
            pad.Left = hat is >= 22500 and <= 31500;
        }

        // Left stick: 0..65535 with the middle at 32768. Y grows downwards here.
        const int centre = 32768;
        pad.Left |= info.X < centre - StickDeadzone;
        pad.Right |= info.X > centre + StickDeadzone;
        pad.Up |= info.Y < centre - StickDeadzone;
        pad.Down |= info.Y > centre + StickDeadzone;
        return pad;
    }

    // ---- Select + Start hold ---------------------------------------------------------------

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

    // ---- Native ---------------------------------------------------------------------------

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

    [StructLayout(LayoutKind.Sequential)]
    private struct JoyInfoEx
    {
        public uint Size, Flags, X, Y, Z, R, U, V, Buttons, ButtonNumber, Pov, Reserved1, Reserved2;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct JoyCaps
    {
        public ushort Mid, Pid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public uint XMin, XMax, YMin, YMax, ZMin, ZMax, NumButtons, PeriodMin, PeriodMax, RMin, RMax, UMin, UMax, VMin, VMax, Caps, MaxAxes, NumAxes, MaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string RegKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string OemVxD;
    }

    [DllImport("winmm.dll")]
    private static extern uint joyGetPosEx(uint joyId, ref JoyInfoEx info);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern uint joyGetDevCapsW(UIntPtr joyId, out JoyCaps caps, uint size);
}
