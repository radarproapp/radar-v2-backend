using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface INavigatorService
{
    Task<NavigatorFocus> GetTodaysFocusAsync(UserProfile profile);
    Task<NavigatorFocus> RefreshAsync(UserProfile profile);
}
