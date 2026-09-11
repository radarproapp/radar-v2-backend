namespace RadarV2.Services.Interfaces;

public class AuthResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
}

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(string name, string email, string password);
    Task<AuthResult> LoginAsync(string email, string password);
}
