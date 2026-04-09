namespace Domain.Entities;

public class CharacterRegister
{
    public int? Id { get; set; }
    public int PlayerRegisterId { get; set; }
    public string CharacterId { get; set; }
    public int BossId { get; set; }
    public int Rounds { get; set; }
}