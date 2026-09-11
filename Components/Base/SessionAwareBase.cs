using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using RadarV2.Models;
using RadarV2.Services;
using RadarV2.Services.Interfaces;

namespace RadarV2.Components.Base;

/// <summary>
/// Base for authenticated pages. Restores the Blazor circuit session from ProtectedLocalStorage
/// after a page refresh (when the in-memory UserSession scope is blank).
/// Derived pages should override OnAuthenticatedAsync and read data there; use Profile.
/// Do NOT add @inject for IUserProfileService, NavigationManager, or IUserSessionService —
/// they are provided here as protected properties.
/// </summary>
public abstract class SessionAwareBase : ComponentBase
{
    [Inject] protected IUserSessionService UserSession    { get; set; } = null!;
    [Inject] protected IUserProfileService UserProfileService { get; set; } = null!;
    [Inject] protected NavigationManager   Nav            { get; set; } = null!;
    [Inject] private   ProtectedLocalStorage LocalStorage { get; set; } = null!;

    protected UserProfile? Profile   { get; private set; }
    protected bool          AuthReady { get; private set; }

    protected sealed override async Task OnInitializedAsync()
    {
        if (UserSession.IsAuthenticated)
            await ResolveProfileAsync();
        // else: wait for OnAfterRenderAsync which can access ProtectedLocalStorage
    }

    protected sealed override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || UserSession.IsAuthenticated) return;

        try
        {
            var result = await LocalStorage.GetAsync<StoredSession>("radar_session");
            if (result.Success && result.Value is { UserId: not null, UserName: not null } session)
            {
                UserSession.SetUser(session.UserId, session.UserName);
                await ResolveProfileAsync();
                await InvokeAsync(StateHasChanged);
            }
            else
            {
                Nav.NavigateTo("/login");
            }
        }
        catch
        {
            Nav.NavigateTo("/login");
        }
    }

    private async Task ResolveProfileAsync()
    {
        Profile = await UserProfileService.GetCurrentUserAsync();
        if (Profile is null || !Profile.OnboardingComplete)
        {
            Nav.NavigateTo("/onboarding");
            return;
        }
        AuthReady = true;
        await OnAuthenticatedAsync();
    }

    /// <summary>Override to load page-specific data after authentication is confirmed.</summary>
    protected virtual Task OnAuthenticatedAsync() => Task.CompletedTask;
}
