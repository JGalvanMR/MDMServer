using Dapper;
using MDMServer.Data;
using MDMServer.Models;
using MDMServer.Repositories.Interfaces;

namespace MDMServer.Repositories;

public class CommandRepository : ICommandRepository
{
    private readonly IDbConnectionFactory _factory;
    private readonly ILogger<CommandRepository> _logger;

    public CommandRepository(IDbConnectionFactory factory, ILogger<CommandRepository> logger)
    {
        _factory = factory;
        _logger  = logger;
    }

    public async Task<Command> CreateAsync(Command command)
    {
        using var conn = await _factory.CreateConnectionAsync();
        var id = await conn.QuerySingleAsync<int>(@"
            INSERT INTO dbo.Commands
                (DeviceId, CommandType, Parameters, Status, Priority,
                 CreatedAt, UpdatedAt, ExpiresAt, CreatedByIp)
            VALUES
                (@DeviceId, @CommandType, @Parameters, 'Pending', @Priority,
                 GETUTCDATE(), GETUTCDATE(), @ExpiresAt, @CreatedByIp);
            SELECT CAST(SCOPE_IDENTITY() AS INT);",
            new {
                command.DeviceId, command.CommandType, command.Parameters,
                command.Priority, command.ExpiresAt, command.CreatedByIp
            }
        );
        command.Id = id;
        return command;
    }

    public async Task<Command?> GetByIdAsync(int id)
    {
        using var conn = await _factory.CreateConnectionAsync();
        return await conn.QueryFirstOrDefaultAsync<Command>(
            "SELECT * FROM dbo.Commands WHERE Id = @Id",
            new { Id = id }
        );
    }

    public async Task<List<Command>> GetPendingByDeviceIdAsync(string deviceId, int maxCount = 10)
    {
        // Usamos el SP para operación atómica (obtener Y marcar como Sent)
        using var conn = await _factory.CreateConnectionAsync();
        var result = await conn.QueryAsync<Command>(
            "EXEC dbo.sp_GetAndMarkPendingCommands @DeviceId, @MaxCommands",
            new { DeviceId = deviceId, MaxCommands = maxCount }
        );
        return result.ToList();
    }

    public async Task<List<Command>> GetByDeviceIdAsync(string deviceId,
        int page = 1, int pageSize = 20)
    {
        using var conn = await _factory.CreateConnectionAsync();
        var offset = (page - 1) * pageSize;
        var result = await conn.QueryAsync<Command>(@"
            SELECT * FROM dbo.Commands
            WHERE DeviceId = @DeviceId
            ORDER BY CreatedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
            new { DeviceId = deviceId, Offset = offset, PageSize = pageSize }
        );
        return result.ToList();
    }

    public async Task<int> GetPendingCountByDeviceIdAsync(string deviceId)
    {
        using var conn = await _factory.CreateConnectionAsync();
        return await conn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.Commands WHERE DeviceId = @DeviceId AND Status = 'Pending'",
            new { DeviceId = deviceId }
        );
    }

    public async Task<int> GetTotalCountByDeviceIdAsync(string deviceId)
    {
        using var conn = await _factory.CreateConnectionAsync();
        return await conn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.Commands WHERE DeviceId = @DeviceId",
            new { DeviceId = deviceId }
        );
    }

    public async Task MarkAsSentAsync(int id)
        => await UpdateStatusAsync(id, "Sent",
            "SentAt = GETUTCDATE(),");

    public async Task MarkAsExecutingAsync(int id)
        => await UpdateStatusAsync(id, "Executing");

    public async Task MarkAsExecutedAsync(int id, string? resultJson)
    {
        using var conn = await _factory.CreateConnectionAsync();
        await conn.ExecuteAsync(@"
            UPDATE dbo.Commands SET
                Status      = 'Executed',
                UpdatedAt   = GETUTCDATE(),
                ExecutedAt  = GETUTCDATE(),
                Result      = @Result
            WHERE Id = @Id",
            new { Id = id, Result = resultJson }
        );
    }

    public async Task MarkAsFailedAsync(int id, string errorMessage)
    {
        using var conn = await _factory.CreateConnectionAsync();
        await conn.ExecuteAsync(@"
            UPDATE dbo.Commands SET
                Status        = 'Failed',
                UpdatedAt     = GETUTCDATE(),
                ExecutedAt    = GETUTCDATE(),
                ErrorMessage  = @ErrorMessage,
                RetryCount    = RetryCount + 1
            WHERE Id = @Id",
            new { Id = id, ErrorMessage = errorMessage }
        );
    }

    public async Task CancelAsync(int id, string reason)
    {
        using var conn = await _factory.CreateConnectionAsync();
        await conn.ExecuteAsync(@"
            UPDATE dbo.Commands SET
                Status       = 'Cancelled',
                UpdatedAt    = GETUTCDATE(),
                ErrorMessage = @Reason
            WHERE Id = @Id AND Status IN ('Pending', 'Sent')",
            new { Id = id, Reason = reason }
        );
    }

    public async Task<List<Command>> GetByDeviceIdAndStatusAsync(string deviceId, string status)
    {
        using var conn = await _factory.CreateConnectionAsync();
        var result = await conn.QueryAsync<Command>(
            "SELECT * FROM dbo.Commands WHERE DeviceId = @DeviceId AND Status = @Status ORDER BY CreatedAt DESC",
            new { DeviceId = deviceId, Status = status }
        );
        return result.ToList();
    }

    public async Task<int> CancelAllPendingByDeviceIdAsync(string deviceId)
    {
        using var conn = await _factory.CreateConnectionAsync();
        return await conn.ExecuteAsync(@"
            UPDATE dbo.Commands SET
                Status    = 'Cancelled',
                UpdatedAt = GETUTCDATE(),
                ErrorMessage = 'Cancelado en masa por administrador'
            WHERE DeviceId = @DeviceId AND Status IN ('Pending', 'Sent')",
            new { DeviceId = deviceId }
        );
    }

    // ── Helper ──────────────────────────────────────────────────────────────
    private async Task UpdateStatusAsync(int id, string status, string extraSets = "")
    {
        using var conn = await _factory.CreateConnectionAsync();
        await conn.ExecuteAsync($@"
            UPDATE dbo.Commands SET
                Status    = @Status,
                {extraSets}
                UpdatedAt = GETUTCDATE()
            WHERE Id = @Id",
            new { Id = id, Status = status }
        );
    }
}