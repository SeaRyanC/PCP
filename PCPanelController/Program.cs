using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PCPanelController {
    class Program {
        static void Main(string[] args) {
            Thread t = new Thread(ListenerThread) {
                IsBackground = true
            };
            t.Start();

            NotifyIcon icon = new NotifyIcon {
                Icon = Resources.favicon,
                Text = "PC Panel Controller",
                ContextMenu = new ContextMenu()
            };
            icon.ContextMenu.MenuItems.Add(new MenuItem() {
                Text = "Exit",
            });
            icon.ContextMenu.MenuItems[0].Click += Exit_Click;
            icon.Visible = true;

            Application.Run();

            return;

            void Exit_Click(object sender, EventArgs e) {
                icon.Visible = false;
                t.Abort();
                Environment.Exit(0);
            }
        }

        private static void ListenerThread() {
            //               1             2             3            4
            // Press         Back       Play/Pause     Next
            // Hold                                    Display      Sleep
            PortListener.StartListening((action, button, duration) => {
                switch (button) {
                    case 1:
                        if (action == PressType.Short) {
                            if (ActiveProcessName() == "vmconnect") {
                                SpotifyMsg(Win32.APPCOMMAND_MEDIA_PREVIOUSTRACK);
                            } else {
                                SendInputHelper.SimulateKey(SendInputHelper.ScanCodeShort.MEDIA_PREV_TRACK);
                            }
                        }
                        break;
                    case 2:
                        if (action == PressType.Short) {
                            if (ActiveProcessName() == "vmconnect") {
                                SpotifyMsg(Win32.APPCOMMAND_MEDIA_PLAY_PAUSE);
                            } else {
                                SendInputHelper.SimulateKey(SendInputHelper.ScanCodeShort.MEDIA_PLAY_PAUSE);
                            }
                        }
                        break;
                    case 3:
                        if (action == PressType.Hold) {
                            Actions.ToggleDisplayMode();
                        } else if (action == PressType.Short) {
                            if (ActiveProcessName() == "vmconnect") {
                                SpotifyMsg(Win32.APPCOMMAND_MEDIA_NEXTTRACK);
                            } else {
                                SendInputHelper.SimulateKey(SendInputHelper.ScanCodeShort.MEDIA_NEXT_TRACK);
                            }
                        }
                        break;
                    case 4:
                        if (action == PressType.Hold) {
                            Actions.SleepDisplays();
                        }
                        break;
                }
            });
        }

        private static string ActiveProcessName() {
            IntPtr hWnd = Win32.GetForegroundWindow();
            foreach (var p in Process.GetProcesses()) {
                if (p.MainWindowHandle == hWnd) {
                    return p.ProcessName;
                }
            }
            return "";
        }

        private static void SpotifyMsg(uint msg) {
            foreach (var p in Process.GetProcesses()) {
                if (p.ProcessName.IndexOf("spotify", 0, StringComparison.InvariantCultureIgnoreCase) >= 0) {
                    if (p.MainWindowHandle != IntPtr.Zero) {
                        Win32.PostMessage(p.MainWindowHandle, Win32.WM_APPCOMMAND, 0, msg << 16);
                        break;
                    }
                }
            }
        }
    }

    public enum PressType {
        Down,
        Up,
        Short,
        Long,
        Hold
    }

    static class PortListener {
        public static void StartListening(Action<PressType, int, TimeSpan> buttonListener) {
            DateTime[] downTimes = { DateTime.MaxValue, DateTime.MaxValue, DateTime.MaxValue, DateTime.MaxValue };
            bool[] reportedHolds = { false, false, false, false };

            var sp = new SerialPort("COM3", 9600) {
                ReadBufferSize = 32
            };
            sp.DataReceived += DataReceived;
            sp.Open();

            while (true) {
                Thread.Sleep(40);
                for (int i = 0; i < 4; i++) {
                    TimeSpan diff = (DateTime.UtcNow - downTimes[i]);
                    if (!reportedHolds[i] && diff.TotalMilliseconds > 500) {
                        reportedHolds[i] = true;
                        buttonListener(PressType.Hold, i + 1, diff);
                    }
                }
            }

            void DataReceived(object sender, SerialDataReceivedEventArgs e) {
                var s = sp.ReadLine();

                if (s[0] == 'b') {
                    int buttonZeroBased = Int32.Parse(s[1].ToString());
                    if (s[3] == '0') {
                        // Down
                        // Report 'down'
                        buttonListener(PressType.Down, buttonZeroBased + 1, TimeSpan.Zero);

                        downTimes[buttonZeroBased] = DateTime.UtcNow;
                        reportedHolds[buttonZeroBased] = false;
                    } else if (s[3] == '1') {
                        // Up
                        if (downTimes[buttonZeroBased] == DateTime.MaxValue) {
                            // Do nothing; this is the initial readout
                        } else {
                            var length = DateTime.UtcNow - downTimes[buttonZeroBased];
                            // Report 'up'
                            buttonListener(PressType.Up, buttonZeroBased + 1, length);

                            // Report short or long
                            if (length.TotalMilliseconds > 400) {
                                buttonListener(PressType.Long, buttonZeroBased + 1, length);
                            } else {
                                buttonListener(PressType.Short, buttonZeroBased + 1, length);
                            }

                            downTimes[buttonZeroBased] = DateTime.MaxValue;
                        }
                    }
                }
            }
        }

    }

    static class Actions {
        public static void SleepDisplays() {
            Win32.PostMessage(Win32.HWND_BROADCAST, Win32.WM_SYSCOMMAND, Win32.SC_MONITORPOWER, 2);
        }

        public static void ToggleDisplayMode() {
            var ds = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "DisplaySwitch.exe");
            if (File.Exists(ds)) {
                ProcessStartInfo psi = new ProcessStartInfo(ds);
                psi.UseShellExecute = true;
                if (Screen.AllScreens.Length == 2) {
                    psi.Arguments = "/clone";
                } else {
                    psi.Arguments = "/extend";
                }
                Process.Start(psi);
            } else {
                Debug.Fail("what");
            }
        }
    }

    static class Win32 {

        public static IntPtr HWND_BROADCAST = (IntPtr)0xFFFF;
        public static int SC_MONITORPOWER = 0xF170;
        public static uint WM_SYSCOMMAND = 0x112;
        public static uint WM_KEYDOWN = 0x100;
        public static uint WM_KEYUP = 0x101;
        public static uint WM_APPCOMMAND = 0x319;
        public static uint APPCOMMAND_MEDIA_NEXTTRACK = 11;
        public static uint APPCOMMAND_MEDIA_PREVIOUSTRACK = 12;
        public static uint APPCOMMAND_MEDIA_PLAY_PAUSE = 14;
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, int wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, int wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);
        [DllImport("user32.dll")]
        public static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, uint wParam, int lParam);
        [DllImport("user32.dll")]
        public static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, uint wParam, uint lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
    }
}
