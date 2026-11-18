using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace AutoClickerApp
{
    internal enum ActionKind
    {
        Keyboard,
        Mouse
    }

    internal enum MouseKind
    {
        Left,
        Right,
        Middle,
        X1,
        X2
    }

    internal sealed class InputAction
    {
        public ActionKind Kind;
        public ushort Vk;
        public MouseKind Mouse;
        public string Display;

        public override string ToString()
        {
            return Display;
        }
    }

    internal static class NativeInput
    {
        private const uint INPUT_MOUSE = 0;
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_XDOWN = 0x0080;
        private const uint MOUSEEVENTF_XUP = 0x0100;

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        private static readonly HashSet<ushort> ExtendedKeys = BuildExtendedKeys();

        private static HashSet<ushort> BuildExtendedKeys()
        {
            HashSet<ushort> keys = new HashSet<ushort>();
            ushort[] list = new ushort[]
            {
                0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28,
                0x2C, 0x2D, 0x2E, 0x5B, 0x5C, 0x5D, 0x6F, 0x90,
                0xA3, 0xA5, 0xAD, 0xAE, 0xAF, 0xB0, 0xB1, 0xB2, 0xB3
            };
            for (int i = 0; i < list.Length; i++)
            {
                keys.Add(list[i]);
            }
            return keys;
        }

        public static void Send(InputAction action, int holdMs)
        {
            if (action == null)
            {
                return;
            }
            if (action.Kind == ActionKind.Keyboard)
            {
                SendKey(action.Vk);
            }
            else
            {
                SendMouse(action.Mouse, holdMs);
            }
        }

        private static void SendKey(ushort vk)
        {
            ushort scan = (ushort)MapVirtualKey(vk, 0);
            uint extended = ExtendedKeys.Contains(vk) ? KEYEVENTF_EXTENDEDKEY : 0u;
            INPUT down = NewKeyInput(vk, scan, extended);
            INPUT up = NewKeyInput(vk, scan, extended | KEYEVENTF_KEYUP);
            INPUT[] inputs = new INPUT[] { down, up };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private static INPUT NewKeyInput(ushort vk, ushort scan, uint flags)
        {
            INPUT input = new INPUT();
            input.type = INPUT_KEYBOARD;
            input.U.ki = new KEYBDINPUT();
            input.U.ki.wVk = vk;
            input.U.ki.wScan = scan;
            input.U.ki.dwFlags = flags;
            return input;
        }

        private static void SendMouse(MouseKind kind, int holdMs)
        {
            if (CursorOverOwnWindow())
            {
                return;
            }
            uint down;
            uint up;
            uint data = 0;
            switch (kind)
            {
                case MouseKind.Left:
                    down = MOUSEEVENTF_LEFTDOWN;
                    up = MOUSEEVENTF_LEFTUP;
                    break;
                case MouseKind.Right:
                    down = MOUSEEVENTF_RIGHTDOWN;
                    up = MOUSEEVENTF_RIGHTUP;
                    break;
                case MouseKind.Middle:
                    down = MOUSEEVENTF_MIDDLEDOWN;
                    up = MOUSEEVENTF_MIDDLEUP;
                    break;
                case MouseKind.X1:
                    down = MOUSEEVENTF_XDOWN;
                    up = MOUSEEVENTF_XUP;
                    data = 1;
                    break;
                case MouseKind.X2:
                    down = MOUSEEVENTF_XDOWN;
                    up = MOUSEEVENTF_XUP;
                    data = 2;
                    break;
                default:
                    return;
            }

            INPUT press = NewMouseInput(down, data);
            SendInput(1, new INPUT[] { press }, Marshal.SizeOf(typeof(INPUT)));
            if (holdMs > 0)
            {
                Thread.Sleep(holdMs);
            }
            INPUT release = NewMouseInput(up, data);
            SendInput(1, new INPUT[] { release }, Marshal.SizeOf(typeof(INPUT)));
        }

        private static bool CursorOverOwnWindow()
        {
            POINT point;
            if (!GetCursorPos(out point))
            {
                return false;
            }
            IntPtr hWnd = WindowFromPoint(point);
            if (hWnd == IntPtr.Zero)
            {
                return false;
            }
            uint processId;
            GetWindowThreadProcessId(hWnd, out processId);
            return processId == GetCurrentProcessId();
        }

        private static INPUT NewMouseInput(uint flags, uint data)
        {
            INPUT input = new INPUT();
            input.type = INPUT_MOUSE;
            input.U.mi = new MOUSEINPUT();
            input.U.mi.dwFlags = flags;
            input.U.mi.mouseData = data;
            return input;
        }
    }

    internal sealed class MainForm : Form
    {
        private const int HotkeyStartId = 0x51A1;
        private const int HotkeyStopId = 0x51A2;
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_NOREPEAT = 0x4000;

        private const ushort VK_F6 = 0x75;
        private const ushort VK_F7 = 0x76;
        private const int VK_ESCAPE = 0x1B;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        [DllImport("user32.dll")]
        private static extern bool FlashWindowEx(ref FLASHWINFO info);

        private static readonly Color StartColor = Color.FromArgb(46, 160, 67);
        private static readonly Color StartHoverColor = Color.FromArgb(56, 176, 78);
        private static readonly Color StopColor = Color.FromArgb(204, 68, 60);
        private static readonly Color StopHoverColor = Color.FromArgb(186, 56, 48);
        private static readonly Color TextColor = Color.FromArgb(33, 37, 41);
        private static readonly Color MutedColor = Color.FromArgb(108, 117, 125);
        private static readonly Color BlueColor = Color.FromArgb(0, 102, 204);
        private static readonly Color OrangeColor = Color.FromArgb(196, 112, 0);
        private static readonly Color GreenColor = Color.FromArgb(46, 160, 67);
        private static readonly Color RedColor = Color.FromArgb(204, 68, 60);

        private readonly ListBox lstActions;
        private readonly Button btnCapture;
        private readonly Button btnMouse;
        private readonly Button btnRemove;
        private readonly Button btnUp;
        private readonly Button btnDown;
        private readonly Button btnClear;
        private readonly NumericUpDown numInterval;
        private readonly NumericUpDown numCount;
        private readonly NumericUpDown numHold;
        private readonly Button btnStart;
        private readonly Button btnStop;
        private readonly Label lblStatus;
        private readonly Label lblHint;
        private readonly Label lblEmpty;
        private readonly Label lblStopwatch;
        private readonly Label lblCountdown;
        private readonly Label lblTimerHint;
        private readonly Button btnStopwatchToggle;
        private readonly Button btnStopwatchReset;
        private readonly Button btnCountdownToggle;
        private readonly Button btnCountdownReset;
        private readonly NumericUpDown numHours;
        private readonly NumericUpDown numMinutes;
        private readonly NumericUpDown numSeconds;
        private readonly System.Windows.Forms.Timer captureTimer;
        private readonly System.Windows.Forms.Timer countdownTimer;
        private readonly System.Windows.Forms.Timer uiTimer;
        private readonly System.Windows.Forms.Timer clockTimer;
        private readonly Font chipFont;
        private readonly bool[] previousKeys = new bool[256];
        private readonly Stopwatch stopwatch;
        private readonly Stopwatch countdownWatch;
        private readonly byte[] chimeData;
        private readonly byte[] startSoundData;
        private readonly byte[] stopSoundData;

        private bool capturing;
        private int countdown;
        private bool running;
        private long cyclesDone;
        private int totalCount;
        private int intervalMs;
        private Thread worker;
        private CancellationTokenSource cancelSource;
        private bool stopwatchRunning;
        private TimeSpan stopwatchAccumulated;
        private TimeSpan countdownRemaining;
        private CountdownState countdownState;

        private enum CountdownState
        {
            Idle,
            Running,
            Paused
        }

        public MainForm()
        {
            Text = "Auto Clicker";
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(248, 249, 251);
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(640, 608);
            KeyPreview = true;
            DoubleBuffered = true;

            chipFont = new Font("Microsoft YaHei UI", 7.5F);

            Label lblList = new Label();
            lblList.Text = "操作列表（每轮按顺序执行）";
            lblList.Location = new Point(16, 12);
            lblList.AutoSize = true;
            lblList.ForeColor = Color.FromArgb(73, 80, 87);
            Controls.Add(lblList);

            lstActions = new ListBox();
            lstActions.Location = new Point(16, 36);
            lstActions.Size = new Size(465, 200);
            lstActions.BorderStyle = BorderStyle.FixedSingle;
            lstActions.DrawMode = DrawMode.OwnerDrawFixed;
            lstActions.ItemHeight = 28;
            lstActions.IntegralHeight = false;
            lstActions.HorizontalScrollbar = true;
            lstActions.Font = new Font("Microsoft YaHei UI", 10F);
            lstActions.BackColor = Color.White;
            lstActions.AccessibleName = "操作列表";
            lstActions.DrawItem += DrawActionItem;
            lstActions.SelectedIndexChanged += delegate { UpdateButtonState(); };
            Controls.Add(lstActions);

            lblEmpty = new Label();
            lblEmpty.Location = new Point(24, 48);
            lblEmpty.Size = new Size(450, 176);
            lblEmpty.BackColor = Color.White;
            lblEmpty.TextAlign = ContentAlignment.MiddleCenter;
            lblEmpty.ForeColor = Color.FromArgb(150, 156, 162);
            lblEmpty.Font = new Font("Microsoft YaHei UI", 9F);
            lblEmpty.Cursor = Cursors.Hand;
            lblEmpty.Text = "还没有添加按键\r\n\r\n点击右侧「捕获按键…」，再按下要连点的键盘键\r\n也可以直接在捕获状态下点击鼠标左键";
            lblEmpty.Click += delegate { BeginCapture(); };
            Controls.Add(lblEmpty);
            lblEmpty.BringToFront();

            btnCapture = MakeButton("捕获按键…", new Point(491, 36), new Size(124, 32));
            btnCapture.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            btnCapture.ForeColor = BlueColor;
            btnCapture.AccessibleName = "捕获按键";
            btnCapture.Click += delegate { BeginCapture(); };
            Controls.Add(btnCapture);

            btnMouse = MakeButton("添加鼠标键 ▾", new Point(491, 72), new Size(124, 32));
            btnMouse.AccessibleName = "添加鼠标键";
            ContextMenuStrip mouseMenu = new ContextMenuStrip();
            mouseMenu.Font = new Font("Microsoft YaHei UI", 9F);
            mouseMenu.Items.Add(MakeMouseMenuItem("鼠标左键", MouseKind.Left));
            mouseMenu.Items.Add(MakeMouseMenuItem("鼠标右键", MouseKind.Right));
            mouseMenu.Items.Add(MakeMouseMenuItem("鼠标中键", MouseKind.Middle));
            mouseMenu.Items.Add(new ToolStripSeparator());
            mouseMenu.Items.Add(MakeMouseMenuItem("鼠标侧键 1", MouseKind.X1));
            mouseMenu.Items.Add(MakeMouseMenuItem("鼠标侧键 2", MouseKind.X2));
            btnMouse.Click += delegate { mouseMenu.Show(btnMouse, new Point(0, btnMouse.Height)); };
            Controls.Add(btnMouse);

            btnRemove = MakeSmallButton("删除选中", new Point(491, 108));
            btnRemove.AccessibleName = "删除选中";
            btnRemove.Click += delegate { RemoveSelected(); };
            Controls.Add(btnRemove);

            btnUp = MakeSmallButton("上移", new Point(491, 140));
            btnUp.AccessibleName = "上移";
            btnUp.Click += delegate { MoveSelected(-1); };
            Controls.Add(btnUp);

            btnDown = MakeSmallButton("下移", new Point(491, 172));
            btnDown.AccessibleName = "下移";
            btnDown.Click += delegate { MoveSelected(1); };
            Controls.Add(btnDown);

            btnClear = MakeSmallButton("清空列表", new Point(491, 204));
            btnClear.AccessibleName = "清空列表";
            btnClear.ForeColor = RedColor;
            btnClear.Click += delegate { ClearList(); };
            Controls.Add(btnClear);

            GroupBox gbExec = new GroupBox();
            gbExec.Text = "执行设置";
            gbExec.Location = new Point(16, 244);
            gbExec.Size = new Size(608, 86);
            gbExec.ForeColor = Color.FromArgb(73, 80, 87);
            Controls.Add(gbExec);

            Label lblInterval = new Label();
            lblInterval.Text = "间隔";
            lblInterval.Location = new Point(14, 30);
            lblInterval.AutoSize = true;
            gbExec.Controls.Add(lblInterval);

            numInterval = new NumericUpDown();
            numInterval.Location = new Point(58, 26);
            numInterval.Size = new Size(80, 26);
            numInterval.Minimum = 1;
            numInterval.Maximum = 60000;
            numInterval.Value = 1000;
            numInterval.Increment = 10;
            numInterval.TextAlign = HorizontalAlignment.Right;
            numInterval.BorderStyle = BorderStyle.FixedSingle;
            numInterval.AccessibleName = "间隔毫秒";
            gbExec.Controls.Add(numInterval);

            Label lblMs = new Label();
            lblMs.Text = "毫秒";
            lblMs.Location = new Point(144, 30);
            lblMs.AutoSize = true;
            gbExec.Controls.Add(lblMs);

            Label lblCount = new Label();
            lblCount.Text = "次数";
            lblCount.Location = new Point(206, 30);
            lblCount.AutoSize = true;
            gbExec.Controls.Add(lblCount);

            numCount = new NumericUpDown();
            numCount.Location = new Point(252, 26);
            numCount.Size = new Size(94, 26);
            numCount.Minimum = 0;
            numCount.Maximum = 100000000;
            numCount.Value = 0;
            numCount.Increment = 10;
            numCount.TextAlign = HorizontalAlignment.Right;
            numCount.BorderStyle = BorderStyle.FixedSingle;
            numCount.AccessibleName = "执行次数";
            gbExec.Controls.Add(numCount);

            Label lblInfinite = new Label();
            lblInfinite.Text = "（0 = 无限）";
            lblInfinite.Location = new Point(352, 30);
            lblInfinite.AutoSize = true;
            lblInfinite.ForeColor = MutedColor;
            gbExec.Controls.Add(lblInfinite);

            Label lblHold = new Label();
            lblHold.Text = "按住";
            lblHold.Location = new Point(462, 30);
            lblHold.AutoSize = true;
            gbExec.Controls.Add(lblHold);

            numHold = new NumericUpDown();
            numHold.Location = new Point(496, 26);
            numHold.Size = new Size(64, 26);
            numHold.Minimum = 0;
            numHold.Maximum = 500;
            numHold.Value = 40;
            numHold.Increment = 5;
            numHold.TextAlign = HorizontalAlignment.Right;
            numHold.BorderStyle = BorderStyle.FixedSingle;
            numHold.AccessibleName = "鼠标按住毫秒";
            gbExec.Controls.Add(numHold);

            Label lblHoldMs = new Label();
            lblHoldMs.Text = "毫秒";
            lblHoldMs.Location = new Point(564, 30);
            lblHoldMs.AutoSize = true;
            gbExec.Controls.Add(lblHoldMs);

            Label lblExecHint = new Label();
            lblExecHint.Text = "每轮按列表顺序发送一遍；游戏里鼠标无效时，把「按住」调到 40–60 毫秒。";
            lblExecHint.Location = new Point(14, 60);
            lblExecHint.AutoSize = true;
            lblExecHint.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblExecHint.ForeColor = MutedColor;
            gbExec.Controls.Add(lblExecHint);

            GroupBox gbControl = new GroupBox();
            gbControl.Text = "控制";
            gbControl.Location = new Point(16, 338);
            gbControl.Size = new Size(608, 100);
            gbControl.ForeColor = Color.FromArgb(73, 80, 87);
            Controls.Add(gbControl);

            btnStart = new Button();
            btnStart.Text = "开始  (F6)";
            btnStart.Location = new Point(18, 26);
            btnStart.Size = new Size(150, 44);
            btnStart.FlatStyle = FlatStyle.Flat;
            btnStart.FlatAppearance.BorderSize = 0;
            btnStart.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            btnStart.BackColor = StartColor;
            btnStart.ForeColor = Color.White;
            btnStart.Cursor = Cursors.Hand;
            btnStart.UseVisualStyleBackColor = false;
            btnStart.AccessibleName = "开始";
            btnStart.Click += delegate { BeginStart(false); };
            btnStart.MouseEnter += delegate
            {
                if (btnStart.Enabled)
                {
                    btnStart.BackColor = StartHoverColor;
                }
            };
            btnStart.MouseLeave += delegate { UpdateButtonState(); };
            gbControl.Controls.Add(btnStart);

            btnStop = new Button();
            btnStop.Text = "停止  (F7)";
            btnStop.Location = new Point(180, 26);
            btnStop.Size = new Size(150, 44);
            btnStop.FlatStyle = FlatStyle.Flat;
            btnStop.FlatAppearance.BorderSize = 0;
            btnStop.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            btnStop.BackColor = StopColor;
            btnStop.ForeColor = Color.White;
            btnStop.Cursor = Cursors.Hand;
            btnStop.UseVisualStyleBackColor = false;
            btnStop.AccessibleName = "停止";
            btnStop.Click += delegate { StopNow(); };
            btnStop.MouseEnter += delegate
            {
                if (btnStop.Enabled)
                {
                    btnStop.BackColor = StopHoverColor;
                }
            };
            btnStop.MouseLeave += delegate { UpdateButtonState(); };
            gbControl.Controls.Add(btnStop);

            lblStatus = new Label();
            lblStatus.Text = "空闲";
            lblStatus.Location = new Point(344, 26);
            lblStatus.Size = new Size(246, 44);
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            lblStatus.ForeColor = MutedColor;
            lblStatus.AutoEllipsis = true;
            gbControl.Controls.Add(lblStatus);

            lblHint = new Label();
            lblHint.Text = "F6 开始 / F7 停止（任意窗口生效）；用按钮启动会先倒计时 3 秒。";
            lblHint.Location = new Point(18, 74);
            lblHint.AutoSize = true;
            lblHint.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblHint.ForeColor = MutedColor;
            gbControl.Controls.Add(lblHint);

            GroupBox gbTimers = new GroupBox();
            gbTimers.Text = "计时 / 倒计时";
            gbTimers.Location = new Point(16, 446);
            gbTimers.Size = new Size(608, 146);
            gbTimers.ForeColor = Color.FromArgb(73, 80, 87);
            Controls.Add(gbTimers);

            Label lblStopwatchCaption = new Label();
            lblStopwatchCaption.Text = "计时器";
            lblStopwatchCaption.Location = new Point(16, 34);
            lblStopwatchCaption.AutoSize = true;
            gbTimers.Controls.Add(lblStopwatchCaption);

            lblStopwatch = new Label();
            lblStopwatch.Text = "00:00:00.00";
            lblStopwatch.Location = new Point(64, 24);
            lblStopwatch.Size = new Size(180, 34);
            lblStopwatch.Font = new Font("Consolas", 15F, FontStyle.Bold);
            lblStopwatch.ForeColor = TextColor;
            lblStopwatch.TextAlign = ContentAlignment.MiddleLeft;
            lblStopwatch.AccessibleName = "计时显示";
            gbTimers.Controls.Add(lblStopwatch);

            Label lblStopwatchUnit = new Label();
            lblStopwatchUnit.Text = "时 : 分 : 秒 . 百分秒";
            lblStopwatchUnit.Location = new Point(250, 35);
            lblStopwatchUnit.AutoSize = true;
            lblStopwatchUnit.Font = new Font("Microsoft YaHei UI", 8F);
            lblStopwatchUnit.ForeColor = MutedColor;
            gbTimers.Controls.Add(lblStopwatchUnit);

            btnStopwatchToggle = MakeButton("开始", new Point(470, 30), new Size(68, 30));
            btnStopwatchToggle.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnStopwatchToggle.AccessibleName = "计时开始";
            btnStopwatchToggle.Click += delegate { ToggleStopwatch(); };
            gbTimers.Controls.Add(btnStopwatchToggle);

            btnStopwatchReset = MakeButton("重置", new Point(544, 30), new Size(68, 30));
            btnStopwatchReset.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnStopwatchReset.AccessibleName = "计时重置";
            btnStopwatchReset.Click += delegate { ResetStopwatch(); };
            gbTimers.Controls.Add(btnStopwatchReset);

            Label lblCountdownCaption = new Label();
            lblCountdownCaption.Text = "倒计时";
            lblCountdownCaption.Location = new Point(16, 86);
            lblCountdownCaption.AutoSize = true;
            gbTimers.Controls.Add(lblCountdownCaption);

            numHours = new NumericUpDown();
            numHours.Location = new Point(64, 82);
            numHours.Size = new Size(46, 26);
            numHours.Minimum = 0;
            numHours.Maximum = 99;
            numHours.Value = 0;
            numHours.TextAlign = HorizontalAlignment.Right;
            numHours.BorderStyle = BorderStyle.FixedSingle;
            numHours.AccessibleName = "倒计时小时";
            gbTimers.Controls.Add(numHours);

            Label lblHoursUnit = new Label();
            lblHoursUnit.Text = "时";
            lblHoursUnit.Location = new Point(112, 86);
            lblHoursUnit.AutoSize = true;
            lblHoursUnit.ForeColor = Color.FromArgb(73, 80, 87);
            gbTimers.Controls.Add(lblHoursUnit);

            numMinutes = new NumericUpDown();
            numMinutes.Location = new Point(132, 82);
            numMinutes.Size = new Size(46, 26);
            numMinutes.Minimum = 0;
            numMinutes.Maximum = 59;
            numMinutes.Value = 1;
            numMinutes.TextAlign = HorizontalAlignment.Right;
            numMinutes.BorderStyle = BorderStyle.FixedSingle;
            numMinutes.AccessibleName = "倒计时分钟";
            gbTimers.Controls.Add(numMinutes);

            Label lblMinutesUnit = new Label();
            lblMinutesUnit.Text = "分";
            lblMinutesUnit.Location = new Point(180, 86);
            lblMinutesUnit.AutoSize = true;
            lblMinutesUnit.ForeColor = Color.FromArgb(73, 80, 87);
            gbTimers.Controls.Add(lblMinutesUnit);

            numSeconds = new NumericUpDown();
            numSeconds.Location = new Point(200, 82);
            numSeconds.Size = new Size(46, 26);
            numSeconds.Minimum = 0;
            numSeconds.Maximum = 59;
            numSeconds.Value = 0;
            numSeconds.TextAlign = HorizontalAlignment.Right;
            numSeconds.BorderStyle = BorderStyle.FixedSingle;
            numSeconds.AccessibleName = "倒计时秒";
            gbTimers.Controls.Add(numSeconds);

            Label lblSecondsUnit = new Label();
            lblSecondsUnit.Text = "秒";
            lblSecondsUnit.Location = new Point(248, 86);
            lblSecondsUnit.AutoSize = true;
            lblSecondsUnit.ForeColor = Color.FromArgb(73, 80, 87);
            gbTimers.Controls.Add(lblSecondsUnit);

            lblCountdown = new Label();
            lblCountdown.Text = "00:01:00";
            lblCountdown.Location = new Point(268, 78);
            lblCountdown.Size = new Size(140, 34);
            lblCountdown.Font = new Font("Consolas", 15F, FontStyle.Bold);
            lblCountdown.ForeColor = TextColor;
            lblCountdown.TextAlign = ContentAlignment.MiddleLeft;
            lblCountdown.AccessibleName = "倒计时显示";
            gbTimers.Controls.Add(lblCountdown);

            Label lblCountdownUnit = new Label();
            lblCountdownUnit.Text = "时:分:秒";
            lblCountdownUnit.Location = new Point(414, 86);
            lblCountdownUnit.AutoSize = true;
            lblCountdownUnit.Font = new Font("Microsoft YaHei UI", 8F);
            lblCountdownUnit.ForeColor = MutedColor;
            gbTimers.Controls.Add(lblCountdownUnit);

            btnCountdownToggle = MakeButton("开始", new Point(470, 84), new Size(68, 30));
            btnCountdownToggle.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnCountdownToggle.AccessibleName = "倒计时开始";
            btnCountdownToggle.Click += delegate { ToggleCountdown(); };
            gbTimers.Controls.Add(btnCountdownToggle);

            btnCountdownReset = MakeButton("重置", new Point(544, 84), new Size(68, 30));
            btnCountdownReset.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnCountdownReset.AccessibleName = "倒计时重置";
            btnCountdownReset.Click += delegate { ResetCountdown(); };
            gbTimers.Controls.Add(btnCountdownReset);

            lblTimerHint = new Label();
            lblTimerHint.Text = "倒计时结束时播放 3 次提示音，任务栏图标会闪烁。";
            lblTimerHint.Location = new Point(16, 120);
            lblTimerHint.AutoSize = true;
            lblTimerHint.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblTimerHint.ForeColor = MutedColor;
            lblTimerHint.AccessibleName = "计时提示";
            gbTimers.Controls.Add(lblTimerHint);

            captureTimer = new System.Windows.Forms.Timer();
            captureTimer.Interval = 15;
            captureTimer.Tick += CaptureTick;

            countdownTimer = new System.Windows.Forms.Timer();
            countdownTimer.Interval = 1000;
            countdownTimer.Tick += CountdownTick;

            uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 100;
            uiTimer.Tick += UiTick;
            uiTimer.Start();

            stopwatch = new Stopwatch();
            countdownWatch = new Stopwatch();
            stopwatchAccumulated = TimeSpan.Zero;
            countdownState = CountdownState.Idle;
            countdownRemaining = ReadCountdownInput();

            clockTimer = new System.Windows.Forms.Timer();
            clockTimer.Interval = 50;
            clockTimer.Tick += ClockTick;
            clockTimer.Start();

            chimeData = BuildChimeWav();
            startSoundData = BuildStartSoundWav();
            stopSoundData = BuildStopSoundWav();

            numHours.ValueChanged += delegate { OnCountdownInputChanged(); };
            numMinutes.ValueChanged += delegate { OnCountdownInputChanged(); };
            numSeconds.ValueChanged += delegate { OnCountdownInputChanged(); };
            UpdateCountdownDisplay();

            FormClosing += OnFormClosing;
            UpdateButtonState();
        }

        private Button MakeButton(string text, Point location, Size size)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = location;
            b.Size = size;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(206, 212, 218);
            b.FlatAppearance.BorderSize = 1;
            b.BackColor = Color.White;
            b.ForeColor = TextColor;
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            b.Font = new Font("Microsoft YaHei UI", 9F);
            return b;
        }

        private Button MakeSmallButton(string text, Point location)
        {
            Button b = MakeButton(text, location, new Size(124, 28));
            b.Font = new Font("Microsoft YaHei UI", 8.5F);
            return b;
        }

        private ToolStripMenuItem MakeMouseMenuItem(string text, MouseKind kind)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += delegate { AddAction(CreateMouseAction(kind)); };
            return item;
        }

        private static InputAction CreateMouseAction(MouseKind kind)
        {
            InputAction action = new InputAction();
            action.Kind = ActionKind.Mouse;
            action.Mouse = kind;
            action.Display = MouseName(kind);
            return action;
        }

        private static InputAction CreateKeyAction(ushort vk)
        {
            InputAction action = new InputAction();
            action.Kind = ActionKind.Keyboard;
            action.Vk = vk;
            action.Display = KeyName(vk);
            return action;
        }

        private static string MouseName(MouseKind kind)
        {
            switch (kind)
            {
                case MouseKind.Left: return "鼠标左键";
                case MouseKind.Right: return "鼠标右键";
                case MouseKind.Middle: return "鼠标中键";
                case MouseKind.X1: return "鼠标侧键 1";
                case MouseKind.X2: return "鼠标侧键 2";
            }
            return "鼠标键";
        }

        private static string KeyName(ushort vk)
        {
            if (vk >= 0x30 && vk <= 0x39)
            {
                return ((char)vk).ToString();
            }
            if (vk >= 0x41 && vk <= 0x5A)
            {
                return ((char)vk).ToString();
            }
            if (vk >= 0x70 && vk <= 0x87)
            {
                return "F" + (vk - 0x6F);
            }
            if (vk >= 0x60 && vk <= 0x69)
            {
                return "小键盘 " + (vk - 0x60);
            }
            switch (vk)
            {
                case 0x08: return "退格 Backspace";
                case 0x09: return "Tab";
                case 0x0D: return "回车 Enter";
                case 0x13: return "Pause";
                case 0x14: return "大写锁定 CapsLock";
                case 0x1B: return "Esc";
                case 0x20: return "空格 Space";
                case 0x21: return "PageUp";
                case 0x22: return "PageDown";
                case 0x23: return "End";
                case 0x24: return "Home";
                case 0x25: return "方向键 左";
                case 0x26: return "方向键 上";
                case 0x27: return "方向键 右";
                case 0x28: return "方向键 下";
                case 0x2C: return "PrintScreen";
                case 0x2D: return "Insert";
                case 0x2E: return "Delete";
                case 0x5B: return "Win 键";
                case 0x5C: return "右 Win 键";
                case 0x5D: return "菜单键";
                case 0x6A: return "小键盘 *";
                case 0x6B: return "小键盘 +";
                case 0x6D: return "小键盘 -";
                case 0x6E: return "小键盘 .";
                case 0x6F: return "小键盘 /";
                case 0x90: return "NumLock";
                case 0x91: return "ScrollLock";
                case 0xA0: return "左 Shift";
                case 0xA1: return "右 Shift";
                case 0xA2: return "左 Ctrl";
                case 0xA3: return "右 Ctrl";
                case 0xA4: return "左 Alt";
                case 0xA5: return "右 Alt";
                case 0xBA: return "分号 ;";
                case 0xBB: return "等号 =";
                case 0xBC: return "逗号 ,";
                case 0xBD: return "减号 -";
                case 0xBE: return "句点 .";
                case 0xBF: return "斜杠 /";
                case 0xC0: return "反引号 `";
                case 0xDB: return "左方括号 [";
                case 0xDC: return "反斜杠 \\";
                case 0xDD: return "右方括号 ]";
                case 0xDE: return "引号 '";
            }
            return "VK 0x" + vk.ToString("X2");
        }

        private void DrawActionItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= lstActions.Items.Count)
            {
                return;
            }
            InputAction item = (InputAction)lstActions.Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color back = selected ? Color.FromArgb(0, 120, 215) : Color.White;
            using (SolidBrush brush = new SolidBrush(back))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            bool isKey = item.Kind == ActionKind.Keyboard;
            Color typeColor = isKey ? BlueColor : Color.FromArgb(222, 116, 26);
            Rectangle chip = new Rectangle(e.Bounds.X + 8, e.Bounds.Y + 6, 26, 16);
            using (SolidBrush chipBrush = new SolidBrush(selected ? Color.White : typeColor))
            {
                e.Graphics.FillRectangle(chipBrush, chip);
            }
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                using (SolidBrush textBrush = new SolidBrush(selected ? typeColor : Color.White))
                {
                    e.Graphics.DrawString(isKey ? "键" : "鼠", chipFont, textBrush,
                        new RectangleF(chip.X, chip.Y, chip.Width, chip.Height), format);
                }
            }

            string text = isKey ? "键盘 · " + item.Display : item.Display;
            Rectangle textRect = new Rectangle(chip.Right + 8, e.Bounds.Y,
                e.Bounds.Width - chip.Width - 24, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, lstActions.Font, textRect,
                selected ? Color.White : TextColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }

        private void BeginCapture()
        {
            if (running || capturing || countdown > 0)
            {
                return;
            }
            capturing = true;
            for (int i = 0; i < 256; i++)
            {
                previousKeys[i] = IsKeyDown(i);
            }
            ActiveControl = null;
            Focus();
            captureTimer.Start();
            UpdateButtonState();
            SetStatus("捕获中：请按下键盘键或鼠标键（Esc 取消）", BlueColor);
        }

        private void EndCapture(bool canceled)
        {
            if (!capturing)
            {
                return;
            }
            captureTimer.Stop();
            capturing = false;
            UpdateButtonState();
            if (canceled)
            {
                SetStatus("已取消捕获", MutedColor);
            }
            if (lstActions.CanFocus)
            {
                lstActions.Focus();
            }
        }

        private static bool IsKeyDown(int vk)
        {
            return (NativeInput.GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        private void CaptureTick(object sender, EventArgs e)
        {
            for (int vk = 1; vk <= 254; vk++)
            {
                bool down = IsKeyDown(vk);
                if (down && !previousKeys[vk])
                {
                    previousKeys[vk] = true;
                    if (vk == VK_ESCAPE)
                    {
                        EndCapture(true);
                        return;
                    }
                    CaptureKey(vk);
                    return;
                }
                previousKeys[vk] = down;
            }
        }

        private void CaptureKey(int vk)
        {
            InputAction action;
            if (vk == 0x01)
            {
                action = CreateMouseAction(MouseKind.Left);
            }
            else if (vk == 0x02)
            {
                action = CreateMouseAction(MouseKind.Right);
            }
            else if (vk == 0x04)
            {
                action = CreateMouseAction(MouseKind.Middle);
            }
            else if (vk == 0x05)
            {
                action = CreateMouseAction(MouseKind.X1);
            }
            else if (vk == 0x06)
            {
                action = CreateMouseAction(MouseKind.X2);
            }
            else
            {
                action = CreateKeyAction((ushort)vk);
            }
            EndCapture(false);
            AddAction(action);
        }

        private void AddAction(InputAction action)
        {
            for (int i = 0; i < lstActions.Items.Count; i++)
            {
                InputAction existing = (InputAction)lstActions.Items[i];
                if (existing.Kind == action.Kind && existing.Vk == action.Vk && existing.Mouse == action.Mouse)
                {
                    lstActions.SelectedIndex = i;
                    SetStatus("“" + action.Display + "” 已经在列表中", MutedColor);
                    return;
                }
            }
            lstActions.Items.Add(action);
            lstActions.SelectedIndex = lstActions.Items.Count - 1;
            lstActions.Refresh();
            SetStatus("已添加：" + action.Display, MutedColor);
            UpdateButtonState();
        }

        private void RemoveSelected()
        {
            int index = lstActions.SelectedIndex;
            if (index < 0)
            {
                return;
            }
            lstActions.Items.RemoveAt(index);
            if (lstActions.Items.Count > 0)
            {
                lstActions.SelectedIndex = Math.Min(index, lstActions.Items.Count - 1);
            }
            lstActions.Refresh();
            SetStatus("已删除选中项", MutedColor);
            UpdateButtonState();
        }

        private void MoveSelected(int delta)
        {
            int index = lstActions.SelectedIndex;
            int target = index + delta;
            if (index < 0 || target < 0 || target >= lstActions.Items.Count)
            {
                return;
            }
            object item = lstActions.Items[index];
            lstActions.Items.RemoveAt(index);
            lstActions.Items.Insert(target, item);
            lstActions.SelectedIndex = target;
            UpdateButtonState();
        }

        private void ClearList()
        {
            if (lstActions.Items.Count == 0)
            {
                return;
            }
            if (MessageBox.Show(this, "确定清空操作列表？", "连点器",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                return;
            }
            lstActions.Items.Clear();
            lstActions.Refresh();
            SetStatus("列表已清空", MutedColor);
            UpdateButtonState();
        }

        private void BeginStart(bool viaHotkey)
        {
            if (running || capturing || countdown > 0)
            {
                return;
            }
            if (lstActions.Items.Count == 0)
            {
                SetStatus("请先添加至少一个按键", RedColor);
                return;
            }
            if (viaHotkey)
            {
                StartNow();
                return;
            }
            countdown = 3;
            UpdateButtonState();
            SetStatus("3 秒后开始，请切换到目标窗口…", OrangeColor);
            countdownTimer.Start();
        }

        private void CountdownTick(object sender, EventArgs e)
        {
            if (countdown > 0)
            {
                countdown--;
                if (countdown > 0)
                {
                    SetStatus(countdown + " 秒后开始，请切换到目标窗口…", OrangeColor);
                }
                else
                {
                    countdownTimer.Stop();
                    StartNow();
                }
            }
            UpdateButtonState();
        }

        private void StartNow()
        {
            if (running)
            {
                return;
            }
            if (lstActions.Items.Count == 0)
            {
                SetStatus("请先添加至少一个按键", RedColor);
                return;
            }
            ActiveControl = null;
            InputAction[] snapshot = new InputAction[lstActions.Items.Count];
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i] = (InputAction)lstActions.Items[i];
            }
            totalCount = (int)numCount.Value;
            intervalMs = (int)numInterval.Value;
            int holdMs = (int)numHold.Value;
            cyclesDone = 0;
            cancelSource = new CancellationTokenSource();
            running = true;
            worker = new Thread(delegate() { RunLoop(snapshot, intervalMs, totalCount, holdMs, cancelSource.Token); });
            worker.IsBackground = true;
            worker.Name = "auto-clicker";
            worker.Start();
            UpdateButtonState();
            SetStatus(totalCount > 0 ? "运行中 · 0 / " + totalCount + " 次" : "运行中 · 已执行 0 次", GreenColor);
            PlayStartSound();
        }

        private void RunLoop(InputAction[] actions, int interval, int count, int holdMs, CancellationToken token)
        {
            Stopwatch watch = Stopwatch.StartNew();
            int done = 0;
            while (!token.IsCancellationRequested)
            {
                long cycleStart = watch.ElapsedMilliseconds;
                for (int i = 0; i < actions.Length; i++)
                {
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }
                    try
                    {
                        NativeInput.Send(actions[i], holdMs);
                    }
                    catch
                    {
                    }
                }
                done++;
                Interlocked.Exchange(ref cyclesDone, done);
                if (count > 0 && done >= count)
                {
                    break;
                }
                long target = cycleStart + interval;
                while (true)
                {
                    long remaining = target - watch.ElapsedMilliseconds;
                    if (remaining <= 0)
                    {
                        break;
                    }
                    if (token.WaitHandle.WaitOne((int)Math.Min(remaining, 10L)))
                    {
                        break;
                    }
                }
            }
            PostWorkerFinished();
        }

        private void PostWorkerFinished()
        {
            try
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke(new MethodInvoker(OnWorkerFinished));
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void OnWorkerFinished()
        {
            if (!running)
            {
                return;
            }
            running = false;
            UpdateButtonState();
            SetStatus("已完成 · 共执行 " + Interlocked.Read(ref cyclesDone) + " 次", GreenColor);
            PlayStopSound();
        }

        private void StopNow()
        {
            if (countdown > 0)
            {
                countdownTimer.Stop();
                countdown = 0;
                UpdateButtonState();
                SetStatus("已取消", MutedColor);
                PlayStopSound();
                return;
            }
            if (!running || cancelSource == null)
            {
                return;
            }
            cancelSource.Cancel();
            Thread currentWorker = worker;
            if (currentWorker != null && currentWorker != Thread.CurrentThread)
            {
                currentWorker.Join(500);
            }
            running = false;
            UpdateButtonState();
            SetStatus("已停止 · 共执行 " + Interlocked.Read(ref cyclesDone) + " 次", MutedColor);
            PlayStopSound();
        }

        private void UiTick(object sender, EventArgs e)
        {
            if (!running)
            {
                return;
            }
            long done = Interlocked.Read(ref cyclesDone);
            string text = totalCount > 0
                ? "运行中 · " + done + " / " + totalCount + " 次"
                : "运行中 · 已执行 " + done + " 次";
            SetStatus(text, GreenColor);
        }

        private void SetStatus(string text, Color color)
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = color;
        }

        private void UpdateButtonState()
        {
            bool locked = running || capturing || countdown > 0;
            bool canStop = (running || countdown > 0) && !capturing;

            btnStart.Enabled = !locked;
            btnStop.Enabled = canStop;
            btnCapture.Enabled = !running && countdown == 0;
            btnMouse.Enabled = !locked;
            btnRemove.Enabled = !locked && lstActions.SelectedIndex >= 0;
            btnUp.Enabled = !locked && lstActions.SelectedIndex > 0;
            btnDown.Enabled = !locked && lstActions.SelectedIndex >= 0
                && lstActions.SelectedIndex < lstActions.Items.Count - 1;
            btnClear.Enabled = !locked && lstActions.Items.Count > 0;
            lstActions.Enabled = !locked;
            numInterval.Enabled = !locked;
            numCount.Enabled = !locked;
            numHold.Enabled = !locked;

            lblEmpty.Visible = lstActions.Items.Count == 0;
            if (lblEmpty.Visible)
            {
                lblEmpty.BringToFront();
            }

            btnCapture.Text = capturing ? "请按键…" : "捕获按键…";
            btnCapture.BackColor = capturing ? Color.FromArgb(255, 243, 205) : Color.White;

            btnStart.BackColor = btnStart.Enabled ? StartColor : Color.FromArgb(226, 229, 233);
            btnStart.ForeColor = btnStart.Enabled ? Color.White : Color.FromArgb(150, 155, 160);
            btnStop.BackColor = btnStop.Enabled ? StopColor : Color.FromArgb(226, 229, 233);
            btnStop.ForeColor = btnStop.Enabled ? Color.White : Color.FromArgb(150, 155, 160);
        }

        private TimeSpan ReadCountdownInput()
        {
            double seconds = (double)numHours.Value * 3600.0
                + (double)numMinutes.Value * 60.0
                + (double)numSeconds.Value;
            return TimeSpan.FromSeconds(seconds);
        }

        private void OnCountdownInputChanged()
        {
            if (countdownState != CountdownState.Idle)
            {
                return;
            }
            countdownRemaining = ReadCountdownInput();
            lblCountdown.ForeColor = TextColor;
            lblTimerHint.Text = "倒计时结束时播放 3 次提示音。";
            lblTimerHint.ForeColor = MutedColor;
            UpdateCountdownDisplay();
        }

        private void ToggleStopwatch()
        {
            if (stopwatchRunning)
            {
                stopwatch.Stop();
                stopwatchAccumulated += stopwatch.Elapsed;
                stopwatch.Reset();
                stopwatchRunning = false;
                btnStopwatchToggle.Text = "继续";
            }
            else
            {
                stopwatch.Reset();
                stopwatch.Start();
                stopwatchRunning = true;
                btnStopwatchToggle.Text = "暂停";
            }
            UpdateStopwatchDisplay();
        }

        private void ResetStopwatch()
        {
            stopwatch.Reset();
            stopwatchAccumulated = TimeSpan.Zero;
            stopwatchRunning = false;
            btnStopwatchToggle.Text = "开始";
            UpdateStopwatchDisplay();
        }

        private void UpdateStopwatchDisplay()
        {
            TimeSpan elapsed = stopwatchAccumulated + (stopwatchRunning ? stopwatch.Elapsed : TimeSpan.Zero);
            int hours = (int)elapsed.TotalHours;
            lblStopwatch.Text = string.Format("{0:00}:{1:00}:{2:00}.{3:00}",
                hours, elapsed.Minutes, elapsed.Seconds, elapsed.Milliseconds / 10);
        }

        private void ToggleCountdown()
        {
            if (countdownState == CountdownState.Running)
            {
                countdownRemaining -= countdownWatch.Elapsed;
                if (countdownRemaining < TimeSpan.Zero)
                {
                    countdownRemaining = TimeSpan.Zero;
                }
                countdownWatch.Reset();
                countdownState = CountdownState.Paused;
                btnCountdownToggle.Text = "继续";
                UpdateCountdownDisplay();
                return;
            }

            if (countdownState == CountdownState.Paused)
            {
                countdownWatch.Restart();
                countdownState = CountdownState.Running;
                btnCountdownToggle.Text = "暂停";
                return;
            }

            TimeSpan total = ReadCountdownInput();
            if (total <= TimeSpan.Zero)
            {
                lblTimerHint.Text = "请先设置倒计时时间。";
                lblTimerHint.ForeColor = RedColor;
                return;
            }
            countdownRemaining = total;
            countdownWatch.Restart();
            countdownState = CountdownState.Running;
            btnCountdownToggle.Text = "暂停";
            SetCountdownInputsEnabled(false);
            lblCountdown.ForeColor = TextColor;
            lblTimerHint.Text = "倒计时进行中…";
            lblTimerHint.ForeColor = MutedColor;
            UpdateCountdownDisplay();
        }

        private void ResetCountdown()
        {
            countdownWatch.Reset();
            countdownState = CountdownState.Idle;
            countdownRemaining = ReadCountdownInput();
            btnCountdownToggle.Text = "开始";
            SetCountdownInputsEnabled(true);
            lblCountdown.ForeColor = TextColor;
            lblTimerHint.Text = "倒计时结束时播放 3 次提示音。";
            lblTimerHint.ForeColor = MutedColor;
            UpdateCountdownDisplay();
        }

        private void SetCountdownInputsEnabled(bool enabled)
        {
            numHours.Enabled = enabled;
            numMinutes.Enabled = enabled;
            numSeconds.Enabled = enabled;
        }

        private void UpdateCountdownDisplay()
        {
            UpdateCountdownDisplay(countdownRemaining);
        }

        private void UpdateCountdownDisplay(TimeSpan remaining)
        {
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }
            int totalSeconds = (int)Math.Ceiling(remaining.TotalSeconds);
            lblCountdown.Text = string.Format("{0:00}:{1:00}:{2:00}",
                totalSeconds / 3600, (totalSeconds / 60) % 60, totalSeconds % 60);
        }

        private void ClockTick(object sender, EventArgs e)
        {
            if (stopwatchRunning)
            {
                UpdateStopwatchDisplay();
            }
            if (countdownState == CountdownState.Running)
            {
                TimeSpan left = countdownRemaining - countdownWatch.Elapsed;
                if (left <= TimeSpan.Zero)
                {
                    FinishCountdown();
                }
                else
                {
                    UpdateCountdownDisplay(left);
                }
            }
        }

        private void FinishCountdown()
        {
            countdownWatch.Reset();
            countdownState = CountdownState.Idle;
            countdownRemaining = TimeSpan.Zero;
            lblCountdown.Text = "00:00:00";
            lblCountdown.ForeColor = RedColor;
            btnCountdownToggle.Text = "开始";
            SetCountdownInputsEnabled(true);
            lblTimerHint.Text = "倒计时结束 · 时间到";
            lblTimerHint.ForeColor = OrangeColor;
            PlayChime();
            FlashTaskbar();
        }

        private void PlayChime()
        {
            PlaySoundData(chimeData, 3, 200);
        }

        private void PlayStartSound()
        {
            PlaySoundData(startSoundData, 1, 0);
        }

        private void PlayStopSound()
        {
            PlaySoundData(stopSoundData, 1, 0);
        }

        private static void PlaySoundData(byte[] data, int repeat, int gapMs)
        {
            if (data == null)
            {
                return;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    using (MemoryStream stream = new MemoryStream(data))
                    using (SoundPlayer player = new SoundPlayer(stream))
                    {
                        player.Load();
                        for (int i = 0; i < repeat; i++)
                        {
                            player.PlaySync();
                            if (i < repeat - 1 && gapMs > 0)
                            {
                                Thread.Sleep(gapMs);
                            }
                        }
                    }
                }
                catch
                {
                    try
                    {
                        SystemSounds.Exclamation.Play();
                    }
                    catch
                    {
                    }
                }
            });
        }

        private void FlashTaskbar()
        {
            if (!IsHandleCreated)
            {
                return;
            }
            FLASHWINFO info = new FLASHWINFO();
            info.cbSize = (uint)Marshal.SizeOf(typeof(FLASHWINFO));
            info.hwnd = Handle;
            info.dwFlags = 0x00000003 | 0x0000000C;
            info.uCount = 3;
            FlashWindowEx(ref info);
        }

        private static byte[] BuildChimeWav()
        {
            return BuildToneWav(new double[] { 880.0, 1318.51, 1760.0 },
                new double[] { 0.16, 0.16, 0.32 }, 0.22, 30);
        }

        private static byte[] BuildStartSoundWav()
        {
            return BuildToneWav(new double[] { 659.25, 987.77 },
                new double[] { 0.10, 0.20 }, 0.26, 45);
        }

        private static byte[] BuildStopSoundWav()
        {
            return BuildToneWav(new double[] { 783.99, 523.25 },
                new double[] { 0.10, 0.24 }, 0.26, 45);
        }

        private static byte[] BuildToneWav(double[] frequencies, double[] durations, double amplitude, int gapMs)
        {
            const int rate = 44100;
            List<short> samples = new List<short>();

            for (int note = 0; note < frequencies.Length; note++)
            {
                int count = (int)(rate * durations[note]);
                for (int i = 0; i < count; i++)
                {
                    double t = (double)i / rate;
                    double fadeIn = Math.Min(1.0, t / 0.012);
                    double fadeOut = Math.Min(1.0, (durations[note] - t) / 0.04);
                    double envelope = Math.Max(0.0, Math.Min(fadeIn, fadeOut));
                    double value = Math.Sin(2.0 * Math.PI * frequencies[note] * t) * amplitude * envelope;
                    samples.Add((short)Math.Round(value * short.MaxValue));
                }
                if (note < frequencies.Length - 1)
                {
                    int gap = rate * gapMs / 1000;
                    for (int i = 0; i < gap; i++)
                    {
                        samples.Add(0);
                    }
                }
            }

            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                int dataSize = samples.Count * 2;
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataSize);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(rate);
                writer.Write(rate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataSize);
                for (int i = 0; i < samples.Count; i++)
                {
                    writer.Write(samples[i]);
                }
                writer.Flush();
                return stream.ToArray();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                if (capturing)
                {
                    EndCapture(true);
                    return true;
                }
                if (countdown > 0)
                {
                    StopNow();
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            bool startOk = RegisterHotKey(Handle, HotkeyStartId, MOD_NOREPEAT, VK_F6);
            bool stopOk = RegisterHotKey(Handle, HotkeyStopId, MOD_NOREPEAT, VK_F7);
            if (!startOk || !stopOk)
            {
                lblHint.Text = "F6 / F7 热键被其他程序占用，请使用界面按钮控制。";
                lblHint.ForeColor = RedColor;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (!capturing)
                {
                    if (id == HotkeyStartId)
                    {
                        BeginStart(true);
                    }
                    else if (id == HotkeyStopId)
                    {
                        StopNow();
                    }
                }
                return;
            }
            base.WndProc(ref m);
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            captureTimer.Stop();
            countdownTimer.Stop();
            uiTimer.Stop();
            clockTimer.Stop();
            if (cancelSource != null)
            {
                cancelSource.Cancel();
            }
            if (worker != null)
            {
                worker.Join(300);
            }
            UnregisterHotKey(Handle, HotkeyStartId);
            UnregisterHotKey(Handle, HotkeyStopId);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                MessageBox.Show("程序发生错误：" + e.Exception.Message, "连点器",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.Run(new MainForm());
        }
    }
}
