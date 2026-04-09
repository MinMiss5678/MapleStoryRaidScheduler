namespace Domain.Entities;

public class BossTemplate
{
    public int Id { get; set; }
    public int BossId { get; set; }
    public string Name { get; set; }
    public List<BossTemplateRequirement> Requirements { get; set; } = [];
}