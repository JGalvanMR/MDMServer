namespace MDMServer.Core.Exceptions;

public class MdmException : Exception
{
    public int    StatusCode { get; }
    public string ErrorCode  { get; }

    public MdmException(string message, int statusCode = 400, string errorCode = "MDM_ERROR")
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode  = errorCode;
    }
}

public class DeviceNotFoundException : MdmException
{
    public DeviceNotFoundException(string deviceId)
        : base($"Dispositivo '{deviceId}' no encontrado.", 404, "DEVICE_NOT_FOUND") { }
}

public class CommandNotFoundException : MdmException
{
    public CommandNotFoundException(int id)
        : base($"Comando con Id={id} no encontrado.", 404, "COMMAND_NOT_FOUND") { }
}

public class UnauthorizedException : MdmException
{
    public UnauthorizedException(string message = "No autorizado.")
        : base(message, 401, "UNAUTHORIZED") { }
}

public class DeviceInactiveException : MdmException
{
    public DeviceInactiveException(string deviceId)
        : base($"El dispositivo '{deviceId}' está inactivo.", 403, "DEVICE_INACTIVE") { }
}