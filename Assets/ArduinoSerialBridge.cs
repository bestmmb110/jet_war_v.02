// ArduinoSerialBridge.cs  (NEW file - put in Assets/Scripts/)
// Reads the Arduino Pro Micro over COM port and feeds the game in two ways:
//  1) Injects real Windows keyboard/mouse events (A, D, C, Enter, Esc, left/right mouse,
//     mouse movement) so the ORIGINAL scripts work unchanged, keyboard & mouse keep working.
//  2) Exposes ArduinoInput.Pot01 (0..1) for the speed potentiometer.
//
// REQUIRES: Edit > Project Settings > Player > Other Settings >
//           Api Compatibility Level = ".NET Framework" (needed for System.IO.Ports).
// Windows only (Standalone / Editor).

using System;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

public static class ArduinoInput
{
    public static volatile bool Connected;
    public static volatile float Pot01;          // 0..1 speed potentiometer
    public static float StickX, StickY;          // -1..1
    public static volatile int Buttons;          // bit mask
}

public class ArduinoSerialBridge : MonoBehaviour
{
    [Header("Serial")]
    public string portName = "";                 // empty = auto-detect, or e.g. "COM5"
    public int baud = 115200;

    [Header("Joystick -> mouse")]
    public float mouseSpeed = 18f;               // pixels per frame at full deflection
    [Range(0f, 0.4f)] public float deadZone = 0.08f;
    public bool invertY = false;                 // flip if pitch feels reversed

    [Header("Pot smoothing")]
    [Range(0.01f, 1f)] public float potSmooth = 0.2f;

    SerialPort port;
    Thread thread;
    volatile bool running;
    volatile int rawX = 512, rawY = 512, rawPot = 0;
    int centerX = 512, centerY = 512;
    int prevMask;
    float potFiltered;
    float retryTimer;

    // bit order must match the Arduino sketch
    const int B_LEFT = 0, B_RIGHT = 1, B_FIRE = 2, B_WEAPON = 3, B_CAM = 4, B_ENTER = 5, B_ESC = 6, B_CENTER = 7;

    static ArduinoSerialBridge instance;

    void Awake()
    {
        if (instance != null) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start() { TryOpen(); }

    void TryOpen()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        try
        {
            string name = portName;
            if (string.IsNullOrEmpty(name))
            {
                string[] ports = SerialPort.GetPortNames();
                if (ports.Length == 0) return;
                name = ports[ports.Length - 1];   // Pro Micro usually the newest COM
            }
            port = new SerialPort(name, baud) { ReadTimeout = 200, DtrEnable = true, NewLine = "\n" };
            port.Open();
            running = true;
            thread = new Thread(ReadLoop) { IsBackground = true };
            thread.Start();
            ArduinoInput.Connected = true;
            Debug.Log("Arduino connected on " + name);
        }
        catch (Exception e)
        {
            Debug.LogWarning("Arduino open failed: " + e.Message);
            Close();
        }
#endif
    }

    void ReadLoop()
    {
        while (running)
        {
            try
            {
                string line = port.ReadLine().Trim();
                if (!line.StartsWith("J,")) continue;
                string[] p = line.Split(',');
                if (p.Length != 5) continue;
                ArduinoInput.Buttons = int.Parse(p[1]);
                rawX = int.Parse(p[2]);
                rawY = int.Parse(p[3]);
                rawPot = int.Parse(p[4]);
            }
            catch (TimeoutException) { }
            catch (Exception) { running = false; ArduinoInput.Connected = false; }
        }
    }

    void Update()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!ArduinoInput.Connected)
        {
            retryTimer += Time.unscaledDeltaTime;
            if (retryTimer > 2f) { retryTimer = 0; Close(); TryOpen(); }
            return;
        }

        int mask = ArduinoInput.Buttons;

        // pot
        potFiltered = Mathf.Lerp(potFiltered, Mathf.Clamp01(rawPot / 1023f), potSmooth);
        ArduinoInput.Pot01 = potFiltered;

        // stick
        if (Bit(mask, B_CENTER) && !Bit(prevMask, B_CENTER)) { centerX = rawX; centerY = rawY; }
        float sx = Mathf.Clamp((rawX - centerX) / 512f, -1f, 1f);
        float sy = Mathf.Clamp((rawY - centerY) / 512f, -1f, 1f);
        ArduinoInput.StickX = sx; ArduinoInput.StickY = sy;

        if (!Application.isFocused) { prevMask = mask; return; }

        // mouse movement
        float ax = Mathf.Abs(sx) < deadZone ? 0 : sx;
        float ay = Mathf.Abs(sy) < deadZone ? 0 : sy;
        if (ax != 0 || ay != 0)
            WinInput.MouseMove(Mathf.RoundToInt(ax * mouseSpeed), Mathf.RoundToInt((invertY ? -ay : ay) * mouseSpeed));

        // buttons (edge detection)
        Edge(mask, B_LEFT,   () => WinInput.Key(0x41, true),  () => WinInput.Key(0x41, false)); // A
        Edge(mask, B_RIGHT,  () => WinInput.Key(0x44, true),  () => WinInput.Key(0x44, false)); // D
        Edge(mask, B_FIRE,   () => WinInput.Mouse(true, false), () => WinInput.Mouse(false, false)); // LMB
        Edge(mask, B_WEAPON, () => WinInput.Mouse(true, true),  () => WinInput.Mouse(false, true));  // RMB
        Edge(mask, B_CAM,    () => WinInput.Key(0x43, true),  () => WinInput.Key(0x43, false)); // C
        Edge(mask, B_ENTER,  () => WinInput.Key(0x0D, true),  () => WinInput.Key(0x0D, false)); // Enter
        Edge(mask, B_ESC,    () => WinInput.Key(0x1B, true),  () => WinInput.Key(0x1B, false)); // Esc

        prevMask = mask;
#endif
    }

    static bool Bit(int m, int b) { return (m & (1 << b)) != 0; }

    void Edge(int mask, int bit, Action down, Action up)
    {
        bool now = Bit(mask, bit), was = Bit(prevMask, bit);
        if (now && !was) down();
        else if (!now && was) up();
    }

    void Close()
    {
        running = false;
        ArduinoInput.Connected = false;
        try { if (thread != null) thread.Join(300); } catch { }
        try { if (port != null && port.IsOpen) port.Close(); } catch { }
        port = null;
    }

    void OnDestroy() { if (instance == this) Close(); }
    void OnApplicationQuit() { Close(); }
}

// Minimal Win32 SendInput wrapper
public static class WinInput
{
    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion u; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint code, uint mapType);

    const uint KEYEVENTF_KEYUP = 2, KEYEVENTF_SCANCODE = 8;
    const uint MOUSEEVENTF_MOVE = 1, LDOWN = 2, LUP = 4, RDOWN = 8, RUP = 16;

    public static void Key(ushort vk, bool down)
    {
        var i = new INPUT { type = 1 };
        i.u.ki.wVk = vk;
        i.u.ki.wScan = (ushort)MapVirtualKey(vk, 0);
        i.u.ki.dwFlags = down ? 0u : KEYEVENTF_KEYUP;
        SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT)));
    }

    public static void Mouse(bool down, bool right)
    {
        var i = new INPUT { type = 0 };
        i.u.mi.dwFlags = right ? (down ? RDOWN : RUP) : (down ? LDOWN : LUP);
        SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT)));
    }

    public static void MouseMove(int dx, int dy)
    {
        var i = new INPUT { type = 0 };
        i.u.mi.dx = dx; i.u.mi.dy = dy; i.u.mi.dwFlags = MOUSEEVENTF_MOVE;
        SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT)));
    }
}
