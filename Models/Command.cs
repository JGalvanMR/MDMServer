namespace MDMServer.Models;

public class Command
{
    public int       Id            { get; set; }
    public string    DeviceId      { get; set; } = string.Empty;
    public string    CommandType   { get; set; } = string.Empty;
    public string?   Parameters    { get; set; }
    public string    Status        { get; set; } = "Pending";
    public int       Priority      { get; set; } = 5;
    public DateTime  CreatedAt     { get; set; }
    public DateTime  UpdatedAt     { get; set; }
    public DateTime? SentAt        { get; set; }
    public DateTime? ExecutedAt    { get; set; }
    public DateTime? ExpiresAt     { get; set; }
    public string?   Result        { get; set; }
    public string?   ErrorMessage  { get; set; }
    public string?   CreatedByIp   { get; set; }
    public int       RetryCount    { get; set; }
}