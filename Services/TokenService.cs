using System.Security.Cryptography;
using MDMServer.Services.Interfaces;

namespace MDMServer.Services;

public class TokenService : ITokenService
{
    private readonly int _tokenLength;

    public TokenService(IConfiguration config)
    {
        _tokenLength = config.GetValue<int>("Mdm:TokenLength", 64);
    }

    public string GenerateDeviceToken()
    {
        // Generar bytes criptográficamente seguros
        var bytes = RandomNumberGenerator.GetBytes(_tokenLength / 2);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public bool ValidateTokenFormat(string token)
        => !string.IsNullOrWhiteSpace(token)
           && token.Length >= 32
           && token.All(c => char.IsLetterOrDigit(c));
}