using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

public class UserSessionService : IUserSessionService
{
    public string? UserId { get; private set; }
    public string? UserName { get; private set; }
    public bool IsAuthenticated => UserId != null;

    public void SetUser(string userId, string userName)
    {
        UserId = userId;
        UserName = userName;
    }

    public void ClearUser()
    {
        UserId = null;
        UserName = null;
    }
}
