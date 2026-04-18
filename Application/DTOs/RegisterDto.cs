namespace Application.DTOs;

public class RegisterDto
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public List<PlayerAvailabilityDto> Availabilities { get; set; } = [];
    public required List<CharacterRegisterDto> CharacterRegisters { get; set; }
}
