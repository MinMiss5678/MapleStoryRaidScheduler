using Domain.Entities;

namespace Application.DTOs;

public class RegisterDto
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public List<PlayerAvailability> Availabilities { get; set; } = [];
    public List<CharacterRegisterDto> CharacterRegisters { get; set; }
}