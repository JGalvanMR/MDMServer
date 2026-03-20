namespace MDMServer.Services.Interfaces;

public interface ITokenService
{
    string GenerateDeviceToken();
    bool   ValidateTokenFormat(string token);
}