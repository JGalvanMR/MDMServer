namespace MDMServer.Core;

public static class MdmConstants
{
    public static class CommandTypes
    {
        public const string LockDevice = "LOCK_DEVICE";
        public const string DisableCamera = "DISABLE_CAMERA";
        public const string EnableCamera = "ENABLE_CAMERA";
        public const string EnableKioskMode = "ENABLE_KIOSK_MODE";
        public const string DisableKioskMode = "DISABLE_KIOSK_MODE";
        public const string GetDeviceInfo = "GET_DEVICE_INFO";
        public const string RebootDevice = "REBOOT_DEVICE";
        public const string WipeData = "WIPE_DATA";
        public const string SetScreenTimeout = "SET_SCREEN_TIMEOUT";
        public const string InstallApp = "INSTALL_APP";
        public const string UninstallApp = "UNINSTALL_APP";
        public const string ListApps = "LIST_APPS";
        public const string EnableWifi = "ENABLE_WIFI";
        public const string DisableWifi = "DISABLE_WIFI";
        public const string SetWifiConfig = "SET_WIFI_CONFIG";
        public const string ClearAppData = "CLEAR_APP_DATA";
        public const string GetLocation = "GET_LOCATION";
        public const string SetVolume = "SET_VOLUME";
        public const string EnableBluetooth = "ENABLE_BLUETOOTH";
        public const string DisableBluetooth = "DISABLE_BLUETOOTH";
        public const string SetBrightness = "SET_BRIGHTNESS";
        public const string SendMessage = "SEND_MESSAGE";
        public const string WakeScreen = "WAKE_SCREEN";
        public const string TakeScreenshot = "TAKE_SCREENSHOT";
        public const string GetLogs = "GET_LOGS";
        public const string GetAppUsage = "GET_APP_USAGE";
        public const string GetNetworkInfo = "GET_NETWORK_INFO";
        public const string PushConfig = "PUSH_CONFIG";
        public const string StartLocationTrack = "START_LOCATION_TRACK";
        public const string StopLocationTrack = "STOP_LOCATION_TRACK";
        public const string RingDevice = "RING_DEVICE";
        public const string SetPasswordPolicy = "SET_PASSWORD_POLICY";
        public const string GetBatteryDetail = "GET_BATTERY_DETAIL";
        public const string StartScreenStream = "START_SCREEN_STREAM";
        public const string StopScreenStream = "STOP_SCREEN_STREAM";
		public const string GrantScreenCapture = "GRANT_SCREEN_CAPTURE";

        public static readonly string[] All =
    {
        LockDevice, DisableCamera, EnableCamera, EnableKioskMode, DisableKioskMode,
        GetDeviceInfo, RebootDevice, WipeData, SetScreenTimeout,
        InstallApp, UninstallApp, ListApps, EnableWifi, DisableWifi, SetWifiConfig,
        ClearAppData, GetLocation, SetVolume, EnableBluetooth, DisableBluetooth,
        SetBrightness, SendMessage, WakeScreen,TakeScreenshot, GetLogs, GetAppUsage,
        GetNetworkInfo, PushConfig, StartLocationTrack, StopLocationTrack, RingDevice,
        SetPasswordPolicy, GetBatteryDetail,StartScreenStream,
        StopScreenStream, GrantScreenCapture
    };

        // Comandos destructivos que requieren parámetro confirm:true
        public static readonly string[] Destructive =
        { RebootDevice, WipeData, ClearAppData, UninstallApp };
    }

    public static class CommandStatuses
    {
        public const string Pending = "Pending";
        public const string Sent = "Sent";
        public const string Executing = "Executing";
        public const string Executed = "Executed";
        public const string Failed = "Failed";
        public const string Cancelled = "Cancelled";
        public const string Expired = "Expired";
    }

    public static class Headers
    {
        public const string DeviceToken = "Device-Token";
        public const string AdminApiKey = "X-Admin-Key";
        public const string RequestId = "X-Request-Id";
    }

    public static class LogCategories
    {
        public const string Poll = "POLL";
        public const string Command = "COMMAND";
        public const string Register = "REGISTER";
        public const string Heartbeat = "HEARTBEAT";
        public const string Security = "SECURITY";
    }
}