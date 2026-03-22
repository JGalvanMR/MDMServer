// Services/CommandExpiryService.cs — NUEVO ARCHIVO
using Dapper;
using MDMServer.Data;

namespace MDMServer.Services;

public class CommandExpiryService : BackgroundService
{
    private readonly IDbConnectionFactory _factory;
    private readonly ILogger<CommandExpiryService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(60);

    public CommandExpiryService(
        IDbConnectionFactory factory,
        ILogger<CommandExpiryService> logger)
    {
        _factory = factory;
        _logger  = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CommandExpiryService iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, stoppingToken);
                await ExpireCommandsAsync();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en CommandExpiryService: {Message}", ex.Message);
            }
        }
    }

    private async Task ExpireCommandsAsync()
    {
        using var conn = await _factory.CreateConnectionAsync();
        var count = await conn.QuerySingleAsync<int>("EXEC dbo.sp_ExpireOldCommands");
        if (count > 0)
            _logger.LogInformation("Comandos expirados: {Count}", count);
    }
}