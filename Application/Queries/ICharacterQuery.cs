using Domain.Entities;

namespace Application.Queries;

public interface ICharacterQuery
{
    Task<IEnumerable<Character>> GetByDiscordIdAsync(ulong discordId);
}