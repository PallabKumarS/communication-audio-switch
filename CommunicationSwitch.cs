using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CommunicationSwitch
{
    public enum EDataFlow { eRender, eCapture, eAll }
    public enum ERole { eConsole, eMultimedia, eCommunications }

    [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMNotificationClient
    {
        void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int newState);
        void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        void OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string defaultDeviceId);
        void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr key);
    }

    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceCollection
    {
        int GetCount(out int pcDevices);
        int Item(int nDevice, out IMMDevice ppDevice);
    }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        int Activate(ref Guid id, int clsCtx, IntPtr activationParams, out IntPtr interfacePointer);
        int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out int state);
    }

    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
        int GetCount(out int count);
        int GetAt(int iProp, out PropertyKey pkey);
        int GetValue(ref PropertyKey key, out PropVariant pv);
        int SetValue(ref PropertyKey key, ref PropVariant pv);
        int Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct PropertyKey
    {
        public Guid fmtid;
        public int pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(2)] public ushort wReserved1;
        [FieldOffset(4)] public ushort wReserved2;
        [FieldOffset(6)] public ushort wReserved3;
        [FieldOffset(8)] public IntPtr ptr;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    public class MMDeviceEnumeratorComObject { }

    [Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPolicyConfig
    {
        int GetMixFormat(string pszDeviceName, IntPtr ppFormat);
        int GetDeviceFormat(string pszDeviceName, bool bDefault, IntPtr ppFormat);
        int ResetDeviceFormat(string pszDeviceName);
        int SetDeviceFormat(string pszDeviceName, IntPtr pEndpointFormat, IntPtr MixFormat);
        int GetProcessingPeriod(string pszDeviceName, bool bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
        int SetProcessingPeriod(string pszDeviceName, IntPtr pmftPeriod);
        int GetShareMode(string pszDeviceName, IntPtr pMode);
        int SetShareMode(string pszDeviceName, IntPtr mode);
        int GetPropertyValue(string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);
        int SetPropertyValue(string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, ERole role);
        int SetEndpointVisibility(string pszDeviceName, bool bVisible);
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    public class PolicyConfigClientComObject { }

    public class AudioDeviceInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool IsDefaultPlayback { get; set; }
        public bool IsDefaultCommunication { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    public class AppConfig
    {
        public bool AutoStart { get; set; }
        public bool MinimizeToTray { get; set; }
        public bool ShowNotifications { get; set; }
        public bool IsMonitoring { get; set; }
        public string DefaultFallback { get; set; }
        public Dictionary<string, string> DeviceMappings { get; set; }

        public const string TARGET_SAME = "__SAME__";
        public const string TARGET_KEEP = "__KEEP__";

        public AppConfig()
        {
            AutoStart = true;
            MinimizeToTray = true;
            ShowNotifications = false;
            IsMonitoring = true;
            DefaultFallback = TARGET_SAME;
            DeviceMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static class Logger
    {
        private static readonly object _lock = new object();
        private static string _primaryLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CommunicationSwitch.log");
        private static string _fallbackLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CommunicationSwitch", "CommunicationSwitch.log");

        public static string ActiveLogPath
        {
            get
            {
                try
                {
                    string dir = Path.GetDirectoryName(_primaryLog);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    string test = Path.Combine(dir, "perm_test.tmp");
                    File.WriteAllText(test, "ok");
                    File.Delete(test);
                    return _primaryLog;
                }
                catch
                {
                    return _fallbackLog;
                }
            }
        }

        public static void Log(string message)
        {
            string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}", DateTime.Now, message);
            Console.WriteLine(line);

            lock (_lock)
            {
                WriteToLog(ActiveLogPath, line);
            }
        }

        private static void WriteToLog(string path, string line)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                if (File.Exists(path) && new FileInfo(path).Length > 5 * 1024 * 1024)
                {
                    string oldFile = path + ".old";
                    if (File.Exists(oldFile)) File.Delete(oldFile);
                    File.Move(path, oldFile);
                }
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch { }
        }
    }

    public static class ConfigManager
    {
        private static readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private static string _primaryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        private static string _fallbackPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CommunicationSwitch", "config.json");

        public static string ConfigFilePath
        {
            get
            {
                if (File.Exists(_primaryPath)) return _primaryPath;
                if (File.Exists(_fallbackPath)) return _fallbackPath;
                try
                {
                    string dir = Path.GetDirectoryName(_primaryPath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    string test = Path.Combine(dir, "perm_test.tmp");
                    File.WriteAllText(test, "ok");
                    File.Delete(test);
                    return _primaryPath;
                }
                catch
                {
                    return _fallbackPath;
                }
            }
        }

        public static AppConfig Load(List<AudioDeviceInfo> currentDevices)
        {
            try
            {
                string path = ConfigFilePath;
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var cfg = _serializer.Deserialize<AppConfig>(json);
                    if (cfg != null)
                    {
                        if (cfg.DeviceMappings == null)
                            cfg.DeviceMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        return cfg;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Error loading config: " + ex.Message);
            }

            // Defaults: Smart detection
            var config = new AppConfig();
            if (currentDevices != null)
            {
                foreach (var d in currentDevices)
                {
                    string lower = d.Name.ToLowerInvariant();
                    if (lower.Contains("speaker") || lower.Contains("speakr"))
                    {
                        // Default Speaker to Headphone if available
                        config.DeviceMappings[d.Name] = "Headphone";
                    }
                    else
                    {
                        config.DeviceMappings[d.Name] = AppConfig.TARGET_SAME;
                    }
                }
            }
            Save(config);
            return config;
        }

        public static void Save(AppConfig config)
        {
            try
            {
                string path = ConfigFilePath;
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string json = _serializer.Serialize(config);
                File.WriteAllText(path, json);
                Logger.Log("Configuration saved to: " + path);
            }
            catch (Exception ex)
            {
                Logger.Log("Error saving config: " + ex.Message);
            }
        }

        public static void SyncAutoStart(bool enable)
        {
            try
            {
                string exePath = Application.ExecutablePath;
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            key.SetValue("CommunicationSwitch", "\"" + exePath + "\" --tray");
                        }
                        else
                        {
                            if (key.GetValue("CommunicationSwitch") != null)
                                key.DeleteValue("CommunicationSwitch");
                        }
                    }
                }

                // Update Scheduled Task if present
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = "schtasks.exe";
                    psi.Arguments = enable ?
                        string.Format("/Change /TN \"CommunicationSwitch\" /ENABLE") :
                        string.Format("/Change /TN \"CommunicationSwitch\" /DISABLE");
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    Process.Start(psi);
                }
                catch { }

                Logger.Log("Autostart setting updated: " + enable);
            }
            catch (Exception ex)
            {
                Logger.Log("Error updating autostart: " + ex.Message);
            }
        }
    }

    public class AudioDeviceManager : IMMNotificationClient
    {
        private static readonly PropertyKey PKEY_Device_FriendlyName = new PropertyKey
        {
            fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
            pid = 14
        };

        private readonly IMMDeviceEnumerator _enumerator;
        public AppConfig Config { get; set; }

        private bool _isUpdating = false;
        private string _lastProcessedConsoleId = null;

        public event Action<string, string, string> DeviceSwitched; // oldPlay, newPlay, newComm
        public event Action StateChanged;

        public AudioDeviceManager()
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        }

        public void Start()
        {
            int hr = _enumerator.RegisterEndpointNotificationCallback(this);
            Logger.Log("Registered CoreAudio endpoint callback (hr=" + hr + ").");
            SyncCurrentState(true);
        }

        public void Stop()
        {
            try
            {
                _enumerator.UnregisterEndpointNotificationCallback(this);
            }
            catch { }
        }

        public string GetDeviceName(IMMDevice device)
        {
            if (device == null) return "Unknown";
            try
            {
                IPropertyStore store;
                if (device.OpenPropertyStore(0, out store) == 0 && store != null)
                {
                    PropertyKey key = PKEY_Device_FriendlyName;
                    PropVariant pv;
                    store.GetValue(ref key, out pv);
                    if (pv.vt == 31 && pv.ptr != IntPtr.Zero)
                    {
                        return Marshal.PtrToStringUni(pv.ptr);
                    }
                }
            }
            catch { }
            return "Unknown Device";
        }

        public List<AudioDeviceInfo> GetPlaybackDevices()
        {
            var list = new List<AudioDeviceInfo>();
            try
            {
                IMMDevice defaultConsole = null;
                string defConsoleId = null;
                if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, out defaultConsole) == 0 && defaultConsole != null)
                {
                    defaultConsole.GetId(out defConsoleId);
                }

                IMMDevice defaultComm = null;
                string defCommId = null;
                if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eCommunications, out defaultComm) == 0 && defaultComm != null)
                {
                    defaultComm.GetId(out defCommId);
                }

                IMMDeviceCollection coll;
                if (_enumerator.EnumAudioEndpoints(EDataFlow.eRender, 1, out coll) == 0 && coll != null)
                {
                    int count;
                    coll.GetCount(out count);
                    for (int i = 0; i < count; i++)
                    {
                        IMMDevice dev;
                        if (coll.Item(i, out dev) == 0 && dev != null)
                        {
                            string id;
                            dev.GetId(out id);
                            string name = GetDeviceName(dev);
                            list.Add(new AudioDeviceInfo
                            {
                                Id = id,
                                Name = name,
                                IsDefaultPlayback = (id == defConsoleId),
                                IsDefaultCommunication = (id == defCommId)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Error getting playback devices: " + ex.Message);
            }
            return list;
        }

        public AudioDeviceInfo GetDefaultDevice(ERole role)
        {
            try
            {
                IMMDevice dev;
                if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, role, out dev) == 0 && dev != null)
                {
                    string id;
                    dev.GetId(out id);
                    return new AudioDeviceInfo { Id = id, Name = GetDeviceName(dev) };
                }
            }
            catch { }
            return new AudioDeviceInfo { Id = "", Name = "None" };
        }

        public AudioDeviceInfo FindDeviceMatch(string matchPattern)
        {
            var devices = GetPlaybackDevices();
            if (string.IsNullOrEmpty(matchPattern)) return null;

            // Exact ID match
            foreach (var d in devices)
            {
                if (string.Equals(d.Id, matchPattern, StringComparison.OrdinalIgnoreCase))
                    return d;
            }

            // Exact Name match
            foreach (var d in devices)
            {
                if (string.Equals(d.Name, matchPattern, StringComparison.OrdinalIgnoreCase))
                    return d;
            }

            // Substring match
            foreach (var d in devices)
            {
                if (d.Name.IndexOf(matchPattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    return d;
            }

            return null;
        }

        public string ResolveTargetCommunicationDevice(string playbackName, string playbackId, out string targetName)
        {
            targetName = playbackName;
            if (Config == null || !Config.IsMonitoring)
                return null;

            string rule = null;

            // Check if exact ID has a rule
            if (Config.DeviceMappings.ContainsKey(playbackId))
            {
                rule = Config.DeviceMappings[playbackId];
            }
            // Check exact Name
            else if (Config.DeviceMappings.ContainsKey(playbackName))
            {
                rule = Config.DeviceMappings[playbackName];
            }
            else
            {
                // Substring match
                foreach (var kvp in Config.DeviceMappings)
                {
                    if (playbackName.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        rule = kvp.Value;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(rule))
            {
                rule = Config.DefaultFallback ?? AppConfig.TARGET_SAME;
            }

            if (rule == AppConfig.TARGET_KEEP)
            {
                return AppConfig.TARGET_KEEP;
            }

            if (rule == AppConfig.TARGET_SAME)
            {
                targetName = playbackName;
                return playbackId;
            }

            // Rule specifies a target device name/pattern
            var match = FindDeviceMatch(rule);
            if (match != null)
            {
                targetName = match.Name;
                return match.Id;
            }

            // If target device not currently plugged in, fallback to same
            targetName = playbackName;
            return playbackId;
        }

        public void SyncCurrentState(bool isInitial = false)
        {
            try
            {
                var curPlay = GetDefaultDevice(ERole.eConsole);
                var curComm = GetDefaultDevice(ERole.eCommunications);

                if (isInitial)
                {
                    Logger.Log(string.Format("Initial State -> Playback: '{0}', Communication: '{1}'", curPlay.Name, curComm.Name));
                }

                if (Config == null || !Config.IsMonitoring) return;

                string targetName;
                string targetId = ResolveTargetCommunicationDevice(curPlay.Name, curPlay.Id, out targetName);

                if (targetId == AppConfig.TARGET_KEEP || string.IsNullOrEmpty(targetId))
                    return;

                if (curComm.Id != targetId)
                {
                    Logger.Log(string.Format("Syncing: Default Playback is '{0}' -> Routing Communication to '{1}'", curPlay.Name, targetName));
                    SetCommunicationDevice(targetId, targetName);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Error in SyncCurrentState: " + ex.Message);
            }
        }

        public void SetCommunicationDevice(string deviceId, string deviceName)
        {
            try
            {
                var policy = (IPolicyConfig)new PolicyConfigClientComObject();
                int hr = policy.SetDefaultEndpoint(deviceId, ERole.eCommunications);
                Marshal.ReleaseComObject(policy);

                if (hr == 0)
                {
                    Logger.Log(string.Format("SUCCESS: Set Communication device to '{0}'.", deviceName));
                }
                else
                {
                    Logger.Log(string.Format("WARNING: SetDefaultEndpoint returned 0x{0:X8}.", hr));
                }
            }
            catch (Exception ex)
            {
                Logger.Log("ERROR setting communication device: " + ex.Message);
            }
        }

        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string defaultDeviceId)
        {
            if (flow != EDataFlow.eRender || role != ERole.eConsole || string.IsNullOrEmpty(defaultDeviceId))
                return;

            lock (this)
            {
                if (_isUpdating) return;
                if (_lastProcessedConsoleId == defaultDeviceId) return;
                _lastProcessedConsoleId = defaultDeviceId;
            }

            ThreadPool.QueueUserWorkItem(state =>
            {
                try
                {
                    _isUpdating = true;
                    var curPlay = GetDefaultDevice(ERole.eConsole);
                    var curComm = GetDefaultDevice(ERole.eCommunications);

                    Logger.Log(string.Format("DETECTED: Default Playback changed to '{0}'.", curPlay.Name));

                    if (Config == null || !Config.IsMonitoring)
                    {
                        Logger.Log("Monitoring is paused or disabled. Skipping switch.");
                        return;
                    }

                    string targetName;
                    string targetId = ResolveTargetCommunicationDevice(curPlay.Name, defaultDeviceId, out targetName);

                    if (targetId == AppConfig.TARGET_KEEP)
                    {
                        Logger.Log(string.Format("RULE [KEEP]: Keeping communication device unchanged on '{0}'.", curComm.Name));
                    }
                    else if (!string.IsNullOrEmpty(targetId))
                    {
                        if (curComm.Id != targetId)
                        {
                            Logger.Log(string.Format("ROUTING: Setting communication device to '{0}'...", targetName));
                            SetCommunicationDevice(targetId, targetName);

                            if (DeviceSwitched != null)
                            {
                                DeviceSwitched(curPlay.Name, targetName, targetId);
                            }
                        }
                        else
                        {
                            Logger.Log(string.Format("Communication device already matches target '{0}'.", targetName));
                        }
                    }

                    if (StateChanged != null)
                    {
                        StateChanged();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("ERROR in device change handler: " + ex.Message);
                }
                finally
                {
                    _isUpdating = false;
                }
            });
        }

        public void OnDeviceStateChanged(string deviceId, int newState)
        {
            if (StateChanged != null) StateChanged();
        }

        public void OnDeviceAdded(string deviceId)
        {
            if (StateChanged != null) StateChanged();
        }

        public void OnDeviceRemoved(string deviceId)
        {
            if (StateChanged != null) StateChanged();
        }

        public void OnPropertyValueChanged(string deviceId, IntPtr key) { }
    }

    public class MainForm : Form
    {
        private AudioDeviceManager _manager;
        private AppConfig _config;
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;

        private Label _lblStatusBadge;
        private Label _lblPlaybackValue;
        private Label _lblCommValue;
        private TableLayoutPanel _rulesTable;
        private CheckBox _chkAutoStart;
        private CheckBox _chkMinimizeTray;
        private CheckBox _chkNotifications;
        private Button _btnSave;
        private Button _btnRefresh;
        private Button _btnToggleMonitor;
        private Button _btnLogs;

        private readonly Dictionary<string, ComboBox> _rowCombos = new Dictionary<string, ComboBox>();
        private ComboBox _cmbFallback;

        public const int WM_COMMUNICATION_SWITCH_SHOW = 0x8000 + 101;

        public MainForm(AudioDeviceManager manager, AppConfig config, bool startMinimized)
        {
            _manager = manager;
            _config = config;

            InitializeComponent();
            SetupTrayIcon();

            _manager.DeviceSwitched += OnDeviceSwitched;
            _manager.StateChanged += OnStateChanged;

            RefreshUIState();

            if (startMinimized)
            {
                this.WindowState = FormWindowState.Minimized;
                this.ShowInTaskbar = false;
            }
        }

        private void InitializeComponent()
        {
            this.Text = "Communication Switch - Audio Router";
            this.Size = new Size(680, 640);
            this.MinimumSize = new Size(640, 560);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            this.BackColor = Color.FromArgb(248, 249, 250);
            this.Icon = CreateAppIcon();

            // Main Layout
            TableLayoutPanel mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.ColumnCount = 1;
            mainLayout.RowCount = 5;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110f)); // Header
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));  // Section title
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // Rules grid scroll
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110f)); // Options
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));  // Action buttons
            mainLayout.Padding = new Padding(18);

            // 1. Header Card
            Panel headerCard = new Panel();
            headerCard.Dock = DockStyle.Fill;
            headerCard.BackColor = Color.White;
            headerCard.Padding = new Padding(14);
            headerCard.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Color.FromArgb(222, 226, 230), 1))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, headerCard.Width - 1, headerCard.Height - 1);
                }
            };

            Label title = new Label();
            title.Text = "Communication Switch";
            title.Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold);
            title.Location = new Point(14, 10);
            title.AutoSize = true;

            _lblStatusBadge = new Label();
            _lblStatusBadge.Text = "● ACTIVE MONITORING";
            _lblStatusBadge.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            _lblStatusBadge.ForeColor = Color.FromArgb(40, 167, 69);
            _lblStatusBadge.BackColor = Color.FromArgb(232, 245, 233);
            _lblStatusBadge.Padding = new Padding(6, 2, 6, 2);
            _lblStatusBadge.Location = new Point(240, 13);
            _lblStatusBadge.AutoSize = true;

            Label lblPlayback = new Label();
            lblPlayback.Text = "Default Playback Output:";
            lblPlayback.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblPlayback.ForeColor = Color.FromArgb(108, 117, 125);
            lblPlayback.Location = new Point(14, 42);
            lblPlayback.AutoSize = true;

            _lblPlaybackValue = new Label();
            _lblPlaybackValue.Text = "...";
            _lblPlaybackValue.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            _lblPlaybackValue.ForeColor = Color.FromArgb(33, 37, 41);
            _lblPlaybackValue.Location = new Point(180, 42);
            _lblPlaybackValue.AutoSize = true;

            Label lblComm = new Label();
            lblComm.Text = "Default Communication:";
            lblComm.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblComm.ForeColor = Color.FromArgb(108, 117, 125);
            lblComm.Location = new Point(14, 66);
            lblComm.AutoSize = true;

            _lblCommValue = new Label();
            _lblCommValue.Text = "...";
            _lblCommValue.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            _lblCommValue.ForeColor = Color.FromArgb(13, 110, 253);
            _lblCommValue.Location = new Point(180, 66);
            _lblCommValue.AutoSize = true;

            headerCard.Controls.Add(title);
            headerCard.Controls.Add(_lblStatusBadge);
            headerCard.Controls.Add(lblPlayback);
            headerCard.Controls.Add(_lblPlaybackValue);
            headerCard.Controls.Add(lblComm);
            headerCard.Controls.Add(_lblCommValue);
            mainLayout.Controls.Add(headerCard, 0, 0);

            // 2. Rules Section Title
            Panel titlePanel = new Panel();
            titlePanel.Dock = DockStyle.Fill;
            Label secTitle = new Label();
            secTitle.Text = "Device Routing Rules";
            secTitle.Font = new Font("Segoe UI Semibold", 11.5f, FontStyle.Bold);
            secTitle.Location = new Point(0, 8);
            secTitle.AutoSize = true;

            Label secDesc = new Label();
            secDesc.Text = "When you switch your Output device, choose which Communication device to activate:";
            secDesc.ForeColor = Color.FromArgb(108, 117, 125);
            secDesc.Location = new Point(0, 30);
            secDesc.AutoSize = true;

            titlePanel.Controls.Add(secTitle);
            titlePanel.Controls.Add(secDesc);
            mainLayout.Controls.Add(titlePanel, 0, 1);

            // 3. Rules Table in Scrollable Panel
            Panel tableContainer = new Panel();
            tableContainer.Dock = DockStyle.Fill;
            tableContainer.AutoScroll = true;
            tableContainer.BackColor = Color.White;
            tableContainer.BorderStyle = BorderStyle.FixedSingle;

            _rulesTable = new TableLayoutPanel();
            _rulesTable.Dock = DockStyle.Top;
            _rulesTable.AutoSize = true;
            _rulesTable.ColumnCount = 3;
            _rulesTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            _rulesTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40f));
            _rulesTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            _rulesTable.Padding = new Padding(12);

            tableContainer.Controls.Add(_rulesTable);
            mainLayout.Controls.Add(tableContainer, 0, 2);

            // 4. Options Panel
            GroupBox grpOptions = new GroupBox();
            grpOptions.Text = "Preferences";
            grpOptions.Dock = DockStyle.Fill;
            grpOptions.Padding = new Padding(10);

            _chkAutoStart = new CheckBox();
            _chkAutoStart.Text = "Start automatically when Windows starts (Run on startup)";
            _chkAutoStart.Checked = _config.AutoStart;
            _chkAutoStart.Location = new Point(14, 24);
            _chkAutoStart.AutoSize = true;

            _chkMinimizeTray = new CheckBox();
            _chkMinimizeTray.Text = "Minimize to System Tray when closing the window";
            _chkMinimizeTray.Checked = _config.MinimizeToTray;
            _chkMinimizeTray.Location = new Point(14, 48);
            _chkMinimizeTray.AutoSize = true;

            _chkNotifications = new CheckBox();
            _chkNotifications.Text = "Show notification popup when communication device switches";
            _chkNotifications.Checked = _config.ShowNotifications;
            _chkNotifications.Location = new Point(14, 72);
            _chkNotifications.AutoSize = true;

            grpOptions.Controls.Add(_chkAutoStart);
            grpOptions.Controls.Add(_chkMinimizeTray);
            grpOptions.Controls.Add(_chkNotifications);
            mainLayout.Controls.Add(grpOptions, 0, 3);

            // 5. Actions Bar
            FlowLayoutPanel actionPanel = new FlowLayoutPanel();
            actionPanel.Dock = DockStyle.Fill;
            actionPanel.FlowDirection = FlowDirection.RightToLeft;
            actionPanel.Padding = new Padding(0, 8, 0, 0);

            _btnSave = new Button();
            _btnSave.Text = "Save && Apply Rules";
            _btnSave.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            _btnSave.BackColor = Color.FromArgb(13, 110, 253);
            _btnSave.ForeColor = Color.White;
            _btnSave.FlatStyle = FlatStyle.Flat;
            _btnSave.FlatAppearance.BorderSize = 0;
            _btnSave.Size = new Size(160, 36);
            _btnSave.Click += OnSaveClicked;

            _btnRefresh = new Button();
            _btnRefresh.Text = "Refresh Devices";
            _btnRefresh.Size = new Size(130, 36);
            _btnRefresh.Click += (s, e) => RefreshUIState();

            _btnToggleMonitor = new Button();
            _btnToggleMonitor.Text = _config.IsMonitoring ? "Pause Monitor" : "Resume Monitor";
            _btnToggleMonitor.Size = new Size(120, 36);
            _btnToggleMonitor.Click += OnToggleMonitor;

            _btnLogs = new Button();
            _btnLogs.Text = "View Logs";
            _btnLogs.Size = new Size(100, 36);
            _btnLogs.Click += OnViewLogs;

            actionPanel.Controls.Add(_btnSave);
            actionPanel.Controls.Add(_btnRefresh);
            actionPanel.Controls.Add(_btnToggleMonitor);
            actionPanel.Controls.Add(_btnLogs);
            mainLayout.Controls.Add(actionPanel, 0, 4);

            this.Controls.Add(mainLayout);
        }

        private void SetupTrayIcon()
        {
            _trayMenu = new ContextMenuStrip();
            var itemOpen = _trayMenu.Items.Add("Open Configuration");
            itemOpen.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            itemOpen.Click += (s, e) => RestoreWindow();

            _trayMenu.Items.Add(new ToolStripSeparator());

            var itemStatus = _trayMenu.Items.Add("Status: Active");
            itemStatus.Enabled = false;

            var itemToggle = _trayMenu.Items.Add(_config.IsMonitoring ? "Pause Monitoring" : "Resume Monitoring");
            itemToggle.Click += OnToggleMonitor;

            var itemLogs = _trayMenu.Items.Add("View Activity Log...");
            itemLogs.Click += OnViewLogs;

            _trayMenu.Items.Add(new ToolStripSeparator());

            var itemExit = _trayMenu.Items.Add("Exit");
            itemExit.Click += (s, e) => ForceExit();

            _trayIcon = new NotifyIcon();
            _trayIcon.Text = "Communication Switch";
            _trayIcon.Icon = this.Icon;
            _trayIcon.ContextMenuStrip = _trayMenu;
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += (s, e) => RestoreWindow();
        }

        public void RestoreWindow()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.ShowInTaskbar = true;
            this.BringToFront();
            this.Activate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && _config.MinimizeToTray)
            {
                e.Cancel = true;
                this.Hide();
                this.ShowInTaskbar = false;
                if (_config.ShowNotifications)
                {
                    _trayIcon.ShowBalloonTip(2000, "Communication Switch", "Running minimized in system tray.", ToolTipIcon.Info);
                }
            }
            else
            {
                base.OnFormClosing(e);
            }
        }

        private void ForceExit()
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _manager.Stop();
            Application.Exit();
        }

        private void OnToggleMonitor(object sender, EventArgs e)
        {
            _config.IsMonitoring = !_config.IsMonitoring;
            ConfigManager.Save(_config);
            UpdateMonitoringVisuals();
        }

        private void UpdateMonitoringVisuals()
        {
            if (_config.IsMonitoring)
            {
                _lblStatusBadge.Text = "● ACTIVE MONITORING";
                _lblStatusBadge.ForeColor = Color.FromArgb(40, 167, 69);
                _lblStatusBadge.BackColor = Color.FromArgb(232, 245, 233);
                _btnToggleMonitor.Text = "Pause Monitor";
            }
            else
            {
                _lblStatusBadge.Text = "⏸ MONITORING PAUSED";
                _lblStatusBadge.ForeColor = Color.FromArgb(108, 117, 125);
                _lblStatusBadge.BackColor = Color.FromArgb(233, 236, 239);
                _btnToggleMonitor.Text = "Resume Monitor";
            }
        }

        private void OnViewLogs(object sender, EventArgs e)
        {
            try
            {
                string logPath = Logger.ActiveLogPath;
                if (File.Exists(logPath))
                {
                    Process.Start("notepad.exe", logPath);
                }
                else
                {
                    MessageBox.Show(this, "Log file has not been created yet.", "Logs", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not open log: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void RefreshUIState()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(RefreshUIState));
                return;
            }

            var curPlay = _manager.GetDefaultDevice(ERole.eConsole);
            var curComm = _manager.GetDefaultDevice(ERole.eCommunications);

            _lblPlaybackValue.Text = curPlay.Name;
            _lblCommValue.Text = curComm.Name;

            string tip = string.Format("Play: {0}\nComm: {1}",
                curPlay.Name.Length > 22 ? curPlay.Name.Substring(0, 20) + ".." : curPlay.Name,
                curComm.Name.Length > 22 ? curComm.Name.Substring(0, 20) + ".." : curComm.Name);
            if (tip.Length >= 64) tip = tip.Substring(0, 63);
            _trayIcon.Text = tip;

            UpdateMonitoringVisuals();

            // Populate Rules Table
            var devices = _manager.GetPlaybackDevices();
            _rulesTable.SuspendLayout();
            _rulesTable.Controls.Clear();
            _rowCombos.Clear();

            // Header Row
            Label h1 = new Label { Text = "When Output Device Is:", Font = new Font("Segoe UI", 9f, FontStyle.Bold), AutoSize = true };
            Label h2 = new Label { Text = "", AutoSize = true };
            Label h3 = new Label { Text = "Set Communication Device To:", Font = new Font("Segoe UI", 9f, FontStyle.Bold), AutoSize = true };

            _rulesTable.Controls.Add(h1, 0, 0);
            _rulesTable.Controls.Add(h2, 1, 0);
            _rulesTable.Controls.Add(h3, 2, 0);

            int row = 1;
            foreach (var d in devices)
            {
                Label lblDev = new Label
                {
                    Text = d.Name + (d.IsDefaultPlayback ? "  (Active Output)" : ""),
                    Font = d.IsDefaultPlayback ? new Font("Segoe UI", 9.5f, FontStyle.Bold) : new Font("Segoe UI", 9.5f),
                    ForeColor = d.IsDefaultPlayback ? Color.FromArgb(13, 110, 253) : Color.FromArgb(33, 37, 41),
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Height = 32
                };

                Label arrow = new Label
                {
                    Text = "➜",
                    Font = new Font("Segoe UI", 10f),
                    ForeColor = Color.FromArgb(173, 181, 189),
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Height = 32
                };

                ComboBox cmb = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Dock = DockStyle.Fill,
                    Height = 30
                };

                // Options
                cmb.Items.Add(new ComboOption("Same as Output (Mirror)", AppConfig.TARGET_SAME));
                foreach (var target in devices)
                {
                    cmb.Items.Add(new ComboOption(target.Name, target.Name));
                }
                cmb.Items.Add(new ComboOption("Do Not Change (Keep Previous)", AppConfig.TARGET_KEEP));

                // Select current config value
                string currentTarget = AppConfig.TARGET_SAME;
                if (_config.DeviceMappings.ContainsKey(d.Name))
                {
                    currentTarget = _config.DeviceMappings[d.Name];
                }
                else
                {
                    // Check substring matches
                    foreach (var kvp in _config.DeviceMappings)
                    {
                        if (d.Name.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            currentTarget = kvp.Value;
                            break;
                        }
                    }
                }

                SelectComboValue(cmb, currentTarget);

                _rulesTable.Controls.Add(lblDev, 0, row);
                _rulesTable.Controls.Add(arrow, 1, row);
                _rulesTable.Controls.Add(cmb, 2, row);
                _rowCombos[d.Name] = cmb;

                row++;
            }

            // Fallback row for unrecognized/future devices
            Label lblAny = new Label
            {
                Text = "Any other / Future device",
                Font = new Font("Segoe UI", 9f, FontStyle.Italic),
                ForeColor = Color.FromArgb(108, 117, 125),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Height = 32
            };
            Label arrowAny = new Label
            {
                Text = "➜",
                Font = new Font("Segoe UI", 10f),
                ForeColor = Color.FromArgb(173, 181, 189),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Height = 32
            };
            _cmbFallback = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Height = 30
            };
            _cmbFallback.Items.Add(new ComboOption("Same as Output (Mirror)", AppConfig.TARGET_SAME));
            foreach (var target in devices)
            {
                _cmbFallback.Items.Add(new ComboOption(target.Name, target.Name));
            }
            _cmbFallback.Items.Add(new ComboOption("Do Not Change (Keep Previous)", AppConfig.TARGET_KEEP));
            SelectComboValue(_cmbFallback, _config.DefaultFallback ?? AppConfig.TARGET_SAME);

            _rulesTable.Controls.Add(lblAny, 0, row);
            _rulesTable.Controls.Add(arrowAny, 1, row);
            _rulesTable.Controls.Add(_cmbFallback, 2, row);

            _rulesTable.ResumeLayout();
        }

        private void SelectComboValue(ComboBox cmb, string value)
        {
            for (int i = 0; i < cmb.Items.Count; i++)
            {
                var opt = cmb.Items[i] as ComboOption;
                if (opt != null)
                {
                    if (string.Equals(opt.Value, value, StringComparison.OrdinalIgnoreCase) ||
                        opt.Display.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        cmb.SelectedIndex = i;
                        return;
                    }
                }
            }
            cmb.SelectedIndex = 0; // Default to Mirror
        }

        private void OnSaveClicked(object sender, EventArgs e)
        {
            // Collect rules from UI
            foreach (var kvp in _rowCombos)
            {
                var opt = kvp.Value.SelectedItem as ComboOption;
                if (opt != null)
                {
                    _config.DeviceMappings[kvp.Key] = opt.Value;
                }
            }

            if (_cmbFallback != null)
            {
                var fb = _cmbFallback.SelectedItem as ComboOption;
                if (fb != null)
                {
                    _config.DefaultFallback = fb.Value;
                }
            }

            _config.AutoStart = _chkAutoStart.Checked;
            _config.MinimizeToTray = _chkMinimizeTray.Checked;
            _config.ShowNotifications = _chkNotifications.Checked;

            ConfigManager.Save(_config);
            ConfigManager.SyncAutoStart(_config.AutoStart);

            _manager.SyncCurrentState();
            RefreshUIState();

            MessageBox.Show(this, "Routing rules and preferences saved successfully!", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnDeviceSwitched(string playName, string commName, string commId)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => OnDeviceSwitched(playName, commName, commId)));
                return;
            }

            RefreshUIState();

            if (_config.ShowNotifications && _trayIcon != null)
            {
                _trayIcon.ShowBalloonTip(2500, "Communication Switched",
                    string.Format("Playback: {0}\nCommunication: {1}", playName, commName),
                    ToolTipIcon.Info);
            }
        }

        private void OnStateChanged()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(OnStateChanged));
                return;
            }
            RefreshUIState();
        }

        private class ComboOption
        {
            public string Display { get; set; }
            public string Value { get; set; }
            public ComboOption(string display, string value)
            {
                Display = display;
                Value = value;
            }
            public override string ToString()
            {
                return Display;
            }
        }

        private Icon CreateAppIcon()
        {
            try
            {
                using (Bitmap bmp = new Bitmap(32, 32))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    using (Brush b = new SolidBrush(Color.FromArgb(13, 110, 253)))
                    {
                        g.FillEllipse(b, 2, 2, 28, 28);
                    }

                    using (Pen p = new Pen(Color.White, 2.5f))
                    {
                        p.StartCap = LineCap.Round;
                        p.EndCap = LineCap.Round;
                        // Draw headphones
                        g.DrawArc(p, 9, 7, 14, 14, 180, 180);
                        g.DrawLine(p, 9, 14, 9, 21);
                        g.DrawLine(p, 23, 14, 23, 21);
                        // Earpads
                        using (Brush eb = new SolidBrush(Color.White))
                        {
                            g.FillEllipse(eb, 7, 18, 5, 8);
                            g.FillEllipse(eb, 20, 18, 5, 8);
                        }
                    }

                    IntPtr hIcon = bmp.GetHicon();
                    return Icon.FromHandle(hIcon);
                }
            }
            catch
            {
                return SystemIcons.Application;
            }
        }
    }

    class Program
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        const int SW_RESTORE = 9;
        const int STD_OUTPUT_HANDLE = -11;
        const int STD_ERROR_HANDLE = -12;
        const int ATTACH_PARENT_PROCESS = -1;

        static Mutex _appMutex;
        const string MUTEX_NAME = "Global\\CommunicationSwitch_Unique_Mutex_7324ce48";

        static void InitializeConsole()
        {
            if (AttachConsole(ATTACH_PARENT_PROCESS))
            {
                try
                {
                    IntPtr stdOut = GetStdHandle(STD_OUTPUT_HANDLE);
                    var safeHandleOut = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdOut, false);
                    var streamOut = new FileStream(safeHandleOut, FileAccess.Write);
                    var writerOut = new StreamWriter(streamOut, System.Text.Encoding.Default) { AutoFlush = true };
                    Console.SetOut(writerOut);

                    IntPtr stdErr = GetStdHandle(STD_ERROR_HANDLE);
                    var safeHandleErr = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdErr, false);
                    var streamErr = new FileStream(safeHandleErr, FileAccess.Write);
                    var writerErr = new StreamWriter(streamErr, System.Text.Encoding.Default) { AutoFlush = true };
                    Console.SetError(writerErr);
                }
                catch { }
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";

            if (mode == "--status" || mode == "-status" || mode == "status" ||
                mode == "--test" || mode == "-test" || mode == "test" ||
                mode == "--help" || mode == "-help" || mode == "/?" ||
                mode == "--kill" || mode == "--stop" || mode == "-stop")
            {
                InitializeConsole();
            }

            if (mode == "--help" || mode == "-help" || mode == "/?")
            {
                PrintHelp();
                return;
            }

            if (mode == "--kill" || mode == "--stop" || mode == "-stop")
            {
                KillRunningInstances();
                return;
            }

            if (mode == "--status" || mode == "-status" || mode == "status")
            {
                ShowStatus();
                return;
            }

            if (mode == "--test" || mode == "-test" || mode == "test")
            {
                RunTest();
                return;
            }

            bool startMinimized = (mode == "--tray" || mode == "--minimized" || mode == "-tray");

            bool createdNew;
            _appMutex = new Mutex(true, MUTEX_NAME, out createdNew);

            if (!createdNew)
            {
                // Already running! If user launched from start menu/explorer without args, restore existing window
                if (!startMinimized)
                {
                    IntPtr hWnd = FindWindow(null, "Communication Switch - Audio Router");
                    if (hWnd != IntPtr.Zero)
                    {
                        ShowWindow(hWnd, SW_RESTORE);
                        SetForegroundWindow(hWnd);
                    }
                }
                return;
            }

            Logger.Log("=== CommunicationSwitch Application Started ===");

            AudioDeviceManager manager = null;
            try
            {
                manager = new AudioDeviceManager();
                var currentDevices = manager.GetPlaybackDevices();
                var config = ConfigManager.Load(currentDevices);
                manager.Config = config;

                manager.Start();

                var form = new MainForm(manager, config, startMinimized);
                Application.Run(form);
            }
            catch (Exception ex)
            {
                Logger.Log("FATAL ERROR: " + ex.ToString());
            }
            finally
            {
                if (manager != null) manager.Stop();
                if (_appMutex != null)
                {
                    _appMutex.ReleaseMutex();
                    _appMutex.Close();
                }
                Logger.Log("=== CommunicationSwitch Application Exited ===");
            }
        }

        static void PrintHelp()
        {
            Console.WriteLine("\nCommunication Switch - Audio Communication Device Auto-Sync");
            Console.WriteLine("-------------------------------------------------------------");
            Console.WriteLine("Options:");
            Console.WriteLine("  (no args)    Open GUI Configuration window");
            Console.WriteLine("  --tray       Start minimized to System Tray");
            Console.WriteLine("  --status     Display current device configuration");
            Console.WriteLine("  --test       Run live detection & switching test");
            Console.WriteLine("  --stop       Stop any running background instance");
            Console.WriteLine("  --help       Show this help screen\n");
        }

        static void ShowStatus()
        {
            Console.WriteLine("\n=== CommunicationSwitch Status ===");
            try
            {
                var manager = new AudioDeviceManager();
                var play = manager.GetDefaultDevice(ERole.eConsole);
                var comm = manager.GetDefaultDevice(ERole.eCommunications);

                Console.WriteLine("Default Playback Device:      " + play.Name + " (" + play.Id + ")");
                Console.WriteLine("Default Communication Device: " + comm.Name + " (" + comm.Id + ")");

                var devices = manager.GetPlaybackDevices();
                Console.WriteLine("\nDetected Playback Devices (" + devices.Count + "):");
                foreach (var d in devices)
                {
                    string tag = "";
                    if (d.IsDefaultPlayback && d.IsDefaultCommunication) tag = " [Playback & Comm]";
                    else if (d.IsDefaultPlayback) tag = " [Default Playback]";
                    else if (d.IsDefaultCommunication) tag = " [Default Comm]";
                    Console.WriteLine("  - " + d.Name + tag);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error querying status: " + ex.Message);
            }
            Console.WriteLine("==================================\n");
        }

        static void KillRunningInstances()
        {
            Console.WriteLine("\nStopping CommunicationSwitch processes...");
            Process current = Process.GetCurrentProcess();
            Process[] procs = Process.GetProcessesByName("CommunicationSwitch");
            int stopped = 0;
            foreach (var p in procs)
            {
                if (p.Id != current.Id)
                {
                    try
                    {
                        p.Kill();
                        stopped++;
                        Console.WriteLine("Terminated PID " + p.Id);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Could not terminate PID " + p.Id + ": " + ex.Message);
                    }
                }
            }
            Console.WriteLine("Stopped " + stopped + " instance(s).\n");
        }

        static void RunTest()
        {
            Console.WriteLine("\n=== Running Live Audio Switcher Test ===");
            var manager = new AudioDeviceManager();
            var policy = (IPolicyConfig)new PolicyConfigClientComObject();
            var devices = manager.GetPlaybackDevices();
            var config = ConfigManager.Load(devices);
            manager.Config = config;

            var origPlay = manager.GetDefaultDevice(ERole.eConsole);
            var origComm = manager.GetDefaultDevice(ERole.eCommunications);

            Console.WriteLine("Original Playback Device: " + origPlay.Name);
            Console.WriteLine("Original Comm Device:     " + origComm.Name);

            manager.Start();

            string headphoneId = "{0.0.0.00000000}.{7b534d4e-952e-4eb2-bde4-428c7b4a9abf}";
            string earphoneId = "{0.0.0.00000000}.{c3c5571a-cbec-4353-bbf2-849adaf9c441}";
            string speakerId = "{0.0.0.00000000}.{bdb48bb5-4022-485a-b75f-95c11ade91e5}";

            Console.WriteLine("\n[TEST 1] Changing Output to Earphone...");
            policy.SetDefaultEndpoint(earphoneId, ERole.eConsole);
            Thread.Sleep(1200);
            var comm = manager.GetDefaultDevice(ERole.eCommunications);
            Console.WriteLine("Result Comm Device: " + comm.Name);

            Console.WriteLine("\n[TEST 2] Changing Output to Speaker...");
            policy.SetDefaultEndpoint(speakerId, ERole.eConsole);
            Thread.Sleep(1200);
            comm = manager.GetDefaultDevice(ERole.eCommunications);
            Console.WriteLine("Result Comm Device: " + comm.Name);

            Console.WriteLine("\n[TEST 3] Changing Output to Headphone...");
            policy.SetDefaultEndpoint(headphoneId, ERole.eConsole);
            Thread.Sleep(1200);
            comm = manager.GetDefaultDevice(ERole.eCommunications);
            Console.WriteLine("Result Comm Device: " + comm.Name);

            // Restore
            Console.WriteLine("\nRestoring original state...");
            policy.SetDefaultEndpoint(origPlay.Id, ERole.eConsole);
            policy.SetDefaultEndpoint(origComm.Id, ERole.eCommunications);
            Thread.Sleep(500);

            manager.Stop();
            Console.WriteLine("Test Finished.\n");
        }
    }
}
