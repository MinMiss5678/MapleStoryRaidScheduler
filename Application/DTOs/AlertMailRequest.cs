using System.ComponentModel.DataAnnotations;

namespace Application.DTOs;

public class AlertMailRequest
{
    [Required]
    public required string Subject { get; set; }

    [Required]
    public required string Body { get; set; }
}
