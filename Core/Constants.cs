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

        public static readonly string[] All =
    {
        LockDevice, DisableCamera, EnableCamera, EnableKioskMode, DisableKioskMode,
        GetDeviceInfo, RebootDevice, WipeData, SetScreenTimeout,
        InstallApp, UninstallApp, ListApps, EnableWifi, DisableWifi, SetWifiConfig,
        ClearAppData, GetLocation, SetVolume, EnableBluetooth, DisableBluetooth,
        SetBrightness, SendMessage, WakeScreen
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