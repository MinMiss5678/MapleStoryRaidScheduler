namespace Application.Interface;

public interface IDiscordService
{
    Task SendMessageAsync(string message);
}