namespace RadarV2.Services.Interfaces;

public interface IUserSessionService
{
    string? UserId { get; }
    string? UserName { get; }
    bool IsAuthenticated { get; }
    void SetUser(string userId, string userName);
    void ClearUser();
}
