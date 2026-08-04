namespace Application.Interface;

public interface IMicrosoftMailService
{
    Task SendMailAsync(string subject, string body);
}
