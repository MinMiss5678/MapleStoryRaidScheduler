using Domain.Entities;
using System.Threading.Tasks;

namespace Application.Interface;

public interface ISystemConfigService
{
    Task<SystemConfig> GetAsync();
    Task UpdateAsync(SystemConfig config);
}
