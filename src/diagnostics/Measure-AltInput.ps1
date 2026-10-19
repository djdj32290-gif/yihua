param(
    [Parameter(Mandatory = $true)][string]$Report,
    [switch]$IncludeMouse
)

$ErrorActionPreference = 'Stop'
function Assert-DPath([string]$Value, [string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Value) -or
        ![IO.Path]::IsPathFullyQualified($Value) -or
        [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($Value)) -ine 'D:\') {
        throw "$Name 必须是 D 盘绝对路径。"
    }
}
Assert-DPath $Report 'Report'
foreach ($taskTempName in @('TEMP', 'TMP')) {
    $taskTempPath = [Environment]::GetEnvironmentVariable($taskTempName, 'Process')
    Assert-DPath $taskTempPath $taskTempName
    if (![IO.Directory]::Exists($taskTempPath)) { throw "$taskTempName 目录不存在：$taskTempPath" }
}
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw '请用 pwsh -NoProfile -STA -File 启动本脚本。'
}
$Report = [IO.Path]::GetFullPath($Report)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Report)) | Out-Null

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
# 不指定 OutputAssembly；编译结果只加载进当前进程。TEMP/TMP 已限制到 D 盘。
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace YihuaIndependentInputProbe {
    public sealed class AltEvent {
        public string Key { get; set; }
        public string Direction { get; set; }
        public double ElapsedMilliseconds { get; set; }
        public uint NativeTime { get; set; }
        public uint Flags { get; set; }
        public uint ScanCode { get; set; }
        public bool Repeat { get; set; }
        public double? HeldMilliseconds { get; set; }
        public uint? NativeHeldMilliseconds { get; set; }
    }
    public sealed class MouseEvent {
        public string Direction { get; set; }
        public double ElapsedMilliseconds { get; set; }
        public uint NativeTime { get; set; }
        public uint Flags { get; set; }
        public bool SystemLeftAltDown { get; set; }
        public bool SystemRightAltDown { get; set; }
    }
    public sealed class Snapshot {
        public string CollectedUtc { get; set; }
        public bool SystemLeftAltDown { get; set; }
        public bool SystemRightAltDown { get; set; }
        public double SystemLeftAltCurrentHeldMilliseconds { get; set; }
        public double SystemRightAltCurrentHeldMilliseconds { get; set; }
        public double SystemLeftAltLongestHeldMilliseconds { get; set; }
        public double SystemRightAltLongestHeldMilliseconds { get; set; }
        public bool KeyboardHookRegistered { get; set; }
        public bool MouseHookRequested { get; set; }
        public bool MouseHookRegistered { get; set; }
        public int KeyboardHookError { get; set; }
        public int MouseHookError { get; set; }
        public AltEvent[] AltRawEvents { get; set; }
        public MouseEvent[] MouseEvents { get; set; }
    }
    public static class Probe {
        delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct KeyboardData { public uint Vk, Scan, Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] struct MouseData { public Point Position; public uint Data, Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] struct Message { public IntPtr Window; public uint Id; public UIntPtr W; public IntPtr L; public uint Time; public Point Position; public uint Private; }
        [DllImport("user32.dll", SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern bool PeekMessage(out Message message, IntPtr window, uint first, uint last, uint remove);
        [DllImport("user32.dll")] static extern int GetMessage(out Message message, IntPtr window, uint first, uint last);
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint message, UIntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref Message message);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref Message message);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        static readonly object gate = new object();
        static readonly HookProc keyboardProc = Keyboard, mouseProc = Mouse;
        static readonly ManualResetEventSlim ready = new ManualResetEventSlim();
        static readonly Queue<AltEvent> altEvents = new Queue<AltEvent>();
        static readonly Queue<MouseEvent> mouseEvents = new Queue<MouseEvent>();
        static readonly long started = Stopwatch.GetTimestamp();
        static Thread hookThread;
        static uint hookThreadId;
        static IntPtr keyboardHook, mouseHook;
        static bool includeMouse, keyboardRegistered, mouseRegistered;
        static int keyboardError, mouseError;
        static bool leftDown, rightDown;
        static long leftSince, rightSince, leftRawSince = -1, rightRawSince = -1;
        static uint leftRawTime, rightRawTime;
        static double leftLongest, rightLongest;
        static double Ms(long ticks) { return ticks * 1000d / Stopwatch.Frequency; }
        static bool Down(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }
        public static void Reveal(IntPtr window) { ShowWindow(window, 5); SetForegroundWindow(window); }
        public static void Start(bool recordMouse) {
            includeMouse = recordMouse;
            hookThread = new Thread(HookLoop) { IsBackground = true, Name = "Independent Alt probe hooks" };
            hookThread.SetApartmentState(ApartmentState.STA);
            hookThread.Start(); ready.Wait(); Sample();
        }
        static void HookLoop() {
            hookThreadId = GetCurrentThreadId();
            Message message; PeekMessage(out message, IntPtr.Zero, 0, 0, 0);
            try {
                keyboardHook = SetWindowsHookEx(13, keyboardProc, GetModuleHandle(null), 0);
                keyboardError = keyboardHook == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
                keyboardRegistered = keyboardHook != IntPtr.Zero;
                if (includeMouse) {
                    mouseHook = SetWindowsHookEx(14, mouseProc, GetModuleHandle(null), 0);
                    mouseError = mouseHook == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
                    mouseRegistered = mouseHook != IntPtr.Zero;
                }
                ready.Set();
                while (GetMessage(out message, IntPtr.Zero, 0, 0) > 0) {
                    TranslateMessage(ref message); DispatchMessage(ref message);
                }
            } finally {
                ready.Set();
                if (keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(keyboardHook);
                if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
                keyboardHook = mouseHook = IntPtr.Zero;
            }
        }
        static IntPtr Keyboard(int code, IntPtr message, IntPtr data) {
            if (code >= 0) {
                var key = Marshal.PtrToStructure<KeyboardData>(data);
                uint vk = key.Vk == 0x12 ? ((key.Flags & 1) == 0 ? 0xA4u : 0xA5u) : key.Vk;
                if (vk == 0xA4 || vk == 0xA5) {
                    int kind = message.ToInt32();
                    if (kind == 0x100 || kind == 0x104 || kind == 0x101 || kind == 0x105) {
                        bool up = kind == 0x101 || kind == 0x105, left = vk == 0xA4;
                        long now = Stopwatch.GetTimestamp();
                        lock (gate) {
                            long since = left ? leftRawSince : rightRawSince;
                            uint nativeSince = left ? leftRawTime : rightRawTime;
                            var entry = new AltEvent {
                                Key = left ? "LeftAlt" : "RightAlt", Direction = up ? "Up" : "Down",
                                ElapsedMilliseconds = Ms(now - started), NativeTime = key.Time,
                                Flags = key.Flags, ScanCode = key.Scan, Repeat = !up && since >= 0,
                                HeldMilliseconds = up && since >= 0 ? (double?)Ms(now - since) : null,
                                NativeHeldMilliseconds = up && since >= 0 ? (uint?)unchecked(key.Time - nativeSince) : null
                            };
                            if (up) { if (left) leftRawSince = -1; else rightRawSince = -1; }
                            else if (since < 0) {
                                if (left) { leftRawSince = now; leftRawTime = key.Time; }
                                else { rightRawSince = now; rightRawTime = key.Time; }
                            }
                            altEvents.Enqueue(entry); while (altEvents.Count > 40) altEvents.Dequeue();
                        }
                    }
                }
            }
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        }
        static IntPtr Mouse(int code, IntPtr message, IntPtr data) {
            int kind = message.ToInt32();
            if (code >= 0 && (kind == 0x201 || kind == 0x202)) {
                var mouse = Marshal.PtrToStructure<MouseData>(data);
                var entry = new MouseEvent {
                    Direction = kind == 0x201 ? "Down" : "Up", ElapsedMilliseconds = Ms(Stopwatch.GetTimestamp() - started),
                    NativeTime = mouse.Time, Flags = mouse.Flags, SystemLeftAltDown = Down(0xA4), SystemRightAltDown = Down(0xA5)
                };
                lock (gate) { mouseEvents.Enqueue(entry); while (mouseEvents.Count > 40) mouseEvents.Dequeue(); }
            }
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        }
        public static void Sample() {
            long now = Stopwatch.GetTimestamp(); bool left = Down(0xA4), right = Down(0xA5);
            lock (gate) {
                if (left && !leftDown) leftSince = now;
                if (right && !rightDown) rightSince = now;
                if (leftDown) leftLongest = Math.Max(leftLongest, Ms(now - leftSince));
                if (rightDown) rightLongest = Math.Max(rightLongest, Ms(now - rightSince));
                leftDown = left; rightDown = right;
            }
        }
        public static Snapshot Read() {
            long now = Stopwatch.GetTimestamp();
            lock (gate) {
                return new Snapshot {
                    CollectedUtc = DateTime.UtcNow.ToString("O"), SystemLeftAltDown = leftDown, SystemRightAltDown = rightDown,
                    SystemLeftAltCurrentHeldMilliseconds = leftDown ? Ms(now - leftSince) : 0,
                    SystemRightAltCurrentHeldMilliseconds = rightDown ? Ms(now - rightSince) : 0,
                    SystemLeftAltLongestHeldMilliseconds = leftLongest, SystemRightAltLongestHeldMilliseconds = rightLongest,
                    KeyboardHookRegistered = keyboardRegistered, MouseHookRequested = includeMouse, MouseHookRegistered = mouseRegistered,
                    KeyboardHookError = keyboardError, MouseHookError = mouseError,
                    AltRawEvents = altEvents.ToArray(), MouseEvents = mouseEvents.ToArray()
                };
            }
        }
        public static void Stop() {
            if (hookThread != null && hookThread.IsAlive) {
                PostThreadMessage(hookThreadId, 0x12, UIntPtr.Zero, IntPtr.Zero);
                hookThread.Join(1500);
            }
        }
    }
}
'@

$taskForm = New-Object Windows.Forms.Form
$taskForm.Text = '独立 Alt 输入检测'
$taskForm.ClientSize = New-Object Drawing.Size(780, 570)
$taskForm.MinimumSize = New-Object Drawing.Size(680, 530)
$taskForm.StartPosition = 'CenterScreen'
$taskForm.Font = New-Object Drawing.Font('Microsoft YaHei UI', 11)
$taskInstruction = New-Object Windows.Forms.Label
$taskInstruction.Text = "独立检测，一划已退出；按住左 Alt 3 秒再松开。`r`n请先自行确认一划已退出；本探针不会控制一划进程。"
$taskInstruction.SetBounds(20, 16, 740, 60)
$taskInstruction.Anchor = 'Top,Left,Right'
$taskForm.Controls.Add($taskInstruction)
$taskStatus = New-Object Windows.Forms.Label
$taskStatus.SetBounds(20, 88, 740, 130)
$taskStatus.Anchor = 'Top,Left,Right'
$taskStatus.Font = New-Object Drawing.Font('Microsoft YaHei UI', 21, [Drawing.FontStyle]::Bold)
$taskForm.Controls.Add($taskStatus)
$taskEvents = New-Object Windows.Forms.TextBox
$taskEvents.Multiline = $true
$taskEvents.ReadOnly = $true
$taskEvents.ScrollBars = 'Vertical'
$taskEvents.SetBounds(20, 230, 740, 270)
$taskEvents.Anchor = 'Top,Bottom,Left,Right'
$taskForm.Controls.Add($taskEvents)
$taskFooter = New-Object Windows.Forms.Label
$taskFooter.SetBounds(20, 510, 740, 48)
$taskFooter.Anchor = 'Bottom,Left,Right'
$taskFooter.Text = "只记录左右 Alt 与可选左键；关闭窗口结束。`r`n报告：$Report"
$taskForm.Controls.Add($taskFooter)
$taskTimer = New-Object Windows.Forms.Timer
$taskTimer.Interval = 20
$taskWriteClock = [Diagnostics.Stopwatch]::StartNew()
$taskProbeStartedUtc = [DateTime]::UtcNow.ToString('O')
$taskUtf8 = New-Object Text.UTF8Encoding($false)
$taskTemporaryReport = "$Report.$PID.tmp"
$script:taskProbeRevealed = $false

function Write-ProbeReport([bool]$Finished) {
    $taskSnapshot = [YihuaIndependentInputProbe.Probe]::Read()
    $taskPayload = [ordered]@{
        Probe = 'YihuaIndependentAltInput'
        ProbePID = $PID
        StartedUtc = $taskProbeStartedUtc
        CollectedUtc = $taskSnapshot.CollectedUtc
        Finished = $Finished
        SampleIntervalMilliseconds = 20
        SystemLeftAltDown = $taskSnapshot.SystemLeftAltDown
        SystemRightAltDown = $taskSnapshot.SystemRightAltDown
        SystemLeftAltCurrentHeldMilliseconds = $taskSnapshot.SystemLeftAltCurrentHeldMilliseconds
        SystemRightAltCurrentHeldMilliseconds = $taskSnapshot.SystemRightAltCurrentHeldMilliseconds
        SystemLeftAltLongestHeldMilliseconds = $taskSnapshot.SystemLeftAltLongestHeldMilliseconds
        SystemRightAltLongestHeldMilliseconds = $taskSnapshot.SystemRightAltLongestHeldMilliseconds
        KeyboardHookRegistered = $taskSnapshot.KeyboardHookRegistered
        KeyboardHookError = $taskSnapshot.KeyboardHookError
        MouseHookRequested = $taskSnapshot.MouseHookRequested
        MouseHookRegistered = $taskSnapshot.MouseHookRegistered
        MouseHookError = $taskSnapshot.MouseHookError
        AltRawEvents = @($taskSnapshot.AltRawEvents)
        MouseEvents = @($taskSnapshot.MouseEvents)
    }
    [IO.File]::WriteAllText($taskTemporaryReport, ($taskPayload | ConvertTo-Json -Depth 6), $taskUtf8)
    [IO.File]::Move($taskTemporaryReport, $Report, $true)
}

$taskTimer.Add_Tick({
    if (!$script:taskProbeRevealed) {
        [YihuaIndependentInputProbe.Probe]::Reveal($taskForm.Handle)
        $script:taskProbeRevealed = $true
    }
    [YihuaIndependentInputProbe.Probe]::Sample()
    $taskSnapshot = [YihuaIndependentInputProbe.Probe]::Read()
    $taskLeftText = if ($taskSnapshot.SystemLeftAltDown) { '按住' } else { '松开' }
    $taskRightText = if ($taskSnapshot.SystemRightAltDown) { '按住' } else { '松开' }
    $taskStatus.Text = ('左 Alt：{0}　最长 {1:N0} 毫秒' -f $taskLeftText, $taskSnapshot.SystemLeftAltLongestHeldMilliseconds) + "`r`n" +
        ('右 Alt：{0}　最长 {1:N0} 毫秒' -f $taskRightText, $taskSnapshot.SystemRightAltLongestHeldMilliseconds)
    if ($taskWriteClock.ElapsedMilliseconds -ge 250) {
        $taskLines = @('最近 Alt 原始事件（仅按键与时间）：')
        if (!$taskSnapshot.KeyboardHookRegistered) { $taskLines += "键盘钩子注册失败，错误 $($taskSnapshot.KeyboardHookError)；系统按住状态仍在采样。" }
        foreach ($taskEvent in @($taskSnapshot.AltRawEvents | Select-Object -Last 8)) {
            $taskHeldText = if ($null -ne $taskEvent.HeldMilliseconds) { '；持续 {0:N0} ms' -f $taskEvent.HeldMilliseconds } else { '' }
            $taskLines += '{0:N0} ms  {1} {2}  native={3} flags=0x{4:X}{5}' -f $taskEvent.ElapsedMilliseconds, $taskEvent.Key, $taskEvent.Direction, $taskEvent.NativeTime, $taskEvent.Flags, $taskHeldText
        }
        $taskEvents.Text = $taskLines -join "`r`n"
        try { Write-ProbeReport $false }
        catch { $taskFooter.Text = "报告写入失败：$($_.Exception.Message)" }
        $taskWriteClock.Restart()
    }
})

try {
    [YihuaIndependentInputProbe.Probe]::Start($IncludeMouse.IsPresent)
    Write-ProbeReport $false
    $taskTimer.Start()
    [Windows.Forms.Application]::Run($taskForm)
}
finally {
    $taskTimer.Stop()
    [YihuaIndependentInputProbe.Probe]::Sample()
    try { Write-ProbeReport $true }
    finally {
        [YihuaIndependentInputProbe.Probe]::Stop()
        $taskTimer.Dispose()
        $taskForm.Dispose()
        if ([IO.File]::Exists($taskTemporaryReport)) { [IO.File]::Delete($taskTemporaryReport) }
    }
}
