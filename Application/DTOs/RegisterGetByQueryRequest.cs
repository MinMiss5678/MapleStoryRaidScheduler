namespace Application.DTOs;

public class RegisterGetByQueryRequest
{
    public int BossId { get; set; }
    public string? Job { get; set; }
    public string? Query { get; set; }
    public DateTimeOffset? SlotDateTime { get; set; }
}