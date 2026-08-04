namespace Domain.Entities;

// DataAnnotation（含 [Key]）對 Dapper 實體無作用（見 Character.cs 說明）。
public class Period
{
    public int Id { get; set; }
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset EndDate { get; set; }
}
