using System.ComponentModel.DataAnnotations;

namespace Domain.Entities;

public class Period
{
    [Key]
    public int Id { get; set; }
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset EndDate { get; set; }
}