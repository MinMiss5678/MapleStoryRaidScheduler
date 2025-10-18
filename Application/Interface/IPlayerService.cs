using Domain.Entities;

namespace Application.Interface;

public interface IPlayerService
{
    Task CreateAsync(Player player);
}