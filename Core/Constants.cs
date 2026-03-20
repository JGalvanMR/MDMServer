namespace MDMServer.Core;

public static class MdmConstants
{
    public static class CommandTypes
    {
        public const string LockDevice        = "LOCK_DEVICE";
        public const string DisableCamera     = "DISABLE_CAMERA";
        public const string EnableCamera      = "ENABLE_CAMERA";
        public const string EnableKioskMode   = "ENABLE_KIOSK_MODE";
        public const string DisableKioskMode  = "DISABLE_KIOSK_MODE";
        public const string GetDeviceInfo     = "GET_DEVICE_INFO";
        public const string RebootDevice      = "REBOOT_DEVICE";
        public const string WipeData          = "WIPE_DATA";
        public const string SetScreenTimeout  = "SET_SCREEN_TIMEOUT";

        public static readonly string[] All =
        {
            LockDevice, DisableCamera, EnableCamera,
            EnableKioskMode, DisableKioskMode, GetDeviceInfo,
            RebootDevice, WipeData, SetScreenTimeout
        };

        // Comandos destructivos que requieren parámetro confirm:true
        public static readonly string[] Destructive = { RebootDevice, WipeData };
    }

    public static class CommandStatuses
    {
        public const string Pending   = "Pending";
        public const string Sent      = "Sent";
        public const string Executing = "Executing";
        public const string Executed  = "Executed";
        public const string Failed    = "Failed";
        public const string Cancelled = "Cancelled";
        public const string Expired   = "Expired";
    }

    public static class Headers
    {
        public const string DeviceToken = "Device-Token";
        public const string AdminApiKey = "X-Admin-Key";
        public const string RequestId   = "X-Request-Id";
    }

    public static class LogCategories
    {
        public const string Poll        = "POLL";
        public const string Command     = "COMMAND";
        public const string Register    = "REGISTER";
        public const string Heartbeat   = "HEARTBEAT";
        public const string Security    = "SECURITY";
    }
}