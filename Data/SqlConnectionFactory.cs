using System.Data;
using Microsoft.Data.SqlClient;

namespace MDMServer.Data;

public class SqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;
    private readonly ILogger<SqlConnectionFactory> _logger;

    public SqlConnectionFactory(IConfiguration configuration,
        ILogger<SqlConnectionFactory> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionString 'DefaultConnection' no configurada.");
        _logger = logger;
    }

    public IDbConnection CreateConnection()
    {
        var conn = new SqlConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public async Task<IDbConnection> CreateConnectionAsync()
    {
        var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        return conn;
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            _logger.LogInformation("Conexión a SQL Server exitosa.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo conectar a SQL Server: {Message}", ex.Message);
            return false;
        }
    }
}