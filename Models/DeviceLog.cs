namespace MDMServer.Models;

public class DeviceLog
{
    public int      Id        { get; set; }
    public string   DeviceId  { get; set; } = string.Empty;
    public string   Level     { get; set; } = "INFO";
    public string?  Category  { get; set; }
    public string?  Message   { get; set; }
    public string?  Metadata  { get; set; }
    public DateTime CreatedAt { get; set; }
}