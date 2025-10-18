namespace Application.DTOs;

public class RegisterDto
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public int[] Weekdays { get; set; }
    public string[] Timeslots { get; set; }
    public List<CharacterRegisterDto> CharacterRegisters { get; set; }
}