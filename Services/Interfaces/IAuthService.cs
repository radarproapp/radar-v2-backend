namespace RadarV2.Services.Interfaces;

public class AuthResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string Role { get; set; } = "User";

    /// <summary>The version the issued token should carry. See UserDocument.TokenVersion.</summary>
    public int TokenVersion { get; set; }
}

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(string name, string email, string password);
    Task<AuthResult> LoginAsync(string email, string password);
}
