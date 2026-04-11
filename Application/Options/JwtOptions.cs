namespace Application.Options;

public class JwtOptions
{
    public required string SecretKey { get; set; }
    public required string SecretKeyFile { get; set; }
    public required string Issuer { get; set; }
    public required string Audience { get; set; }
}