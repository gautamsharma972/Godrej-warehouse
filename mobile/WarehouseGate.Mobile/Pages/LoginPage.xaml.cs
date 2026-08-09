using WarehouseGate.Mobile.Controls;
using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Pages;

public partial class LoginPage : ContentPage
{
    private const string RememberKey = "login.remember";
    private const string RememberedUserKey = "login.username";

    private bool? _isVeryCompact;

    public LoginPage()
    {
        InitializeComponent();

        if (Preferences.Get(RememberKey, false))
        {
            RememberMeCheckBox.IsChecked = true;
            UserNameEntry.Text = Preferences.Get(RememberedUserKey, string.Empty);
        }
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        var veryCompact = width < 390 || height < 700;
        if (_isVeryCompact == veryCompact)
        {
            return;
        }

        _isVeryCompact = veryCompact;
        ApplyResponsiveLayout(veryCompact);
    }

    // Mobile app - unlike the Web portal, there's no desktop-style branding panel here at any
    // width: the sign-in card is the whole screen. veryCompact just tightens padding/spacing/font
    // sizes further for small phones so the card and its footer fit without scrolling.
    private void ApplyResponsiveLayout(bool veryCompact)
    {
        SecureBadge.IsVisible = !veryCompact;
        FooterDivider.IsVisible = !veryCompact;
        CardFooter.IsVisible = !veryCompact;

        RightPanel.Padding = veryCompact ? new Thickness(14, 8, 14, 12) : new Thickness(24, 20, 24, 24);
        SignInCard.Padding = veryCompact ? new Thickness(18, 15) : new Thickness(28, 26);
        SignInCard.MaximumWidthRequest = 460;
        CardStack.Spacing = veryCompact ? 10 : 16;
        FormStack.Spacing = veryCompact ? 10 : 14;
        WelcomeLabel.FontSize = veryCompact ? 25 : 30;
        EyebrowLabel.FontSize = veryCompact ? 10 : 12;
        IntroLabel.FontSize = veryCompact ? 11 : 13;
        OrgCodeFieldBorder.HeightRequest = veryCompact ? 46 : 52;
        UserNameFieldBorder.HeightRequest = veryCompact ? 46 : 52;
        PasswordFieldBorder.HeightRequest = veryCompact ? 46 : 52;
        LoginButton.HeightRequest = veryCompact ? 48 : 54;
    }

    private void OnUserNameChanged(object? sender, TextChangedEventArgs e) =>
        UserNameCheckLabel.IsVisible = !string.IsNullOrWhiteSpace(e.NewTextValue);

    // MAUI's Border doesn't get a native "focused outline" the way a real Android/iOS text field
    // does when a child Entry receives focus - without this, the two fields look identical whether
    // you're typing in them or not. Matches the Material outlined-text-field pattern: a thin neutral
    // border at rest, a thicker brand-colored one while editing.
    private void OnFieldFocused(object? sender, FocusEventArgs e) =>
        UiHelpers.SetFieldFocus(FieldBorderFor(sender), true);

    private void OnFieldUnfocused(object? sender, FocusEventArgs e) =>
        UiHelpers.SetFieldFocus(FieldBorderFor(sender), false);

    private Border FieldBorderFor(object? entry) => entry switch
    {
        var e when e == OrgCodeEntry => OrgCodeFieldBorder,
        var e when e == UserNameEntry => UserNameFieldBorder,
        _ => PasswordFieldBorder
    };

    private void OnTogglePasswordVisibility(object? sender, TappedEventArgs e)
    {
        PasswordEntry.IsPassword = !PasswordEntry.IsPassword;
        PasswordEyeLabel.Text = PasswordEntry.IsPassword ? IconGlyphs.Eye : IconGlyphs.EyeSlash;
    }

    private void OnRememberMeLabelTapped(object? sender, TappedEventArgs e) =>
        RememberMeCheckBox.IsChecked = !RememberMeCheckBox.IsChecked;

    private async void OnForgotPasswordTapped(object? sender, TappedEventArgs e) =>
        await DisplayAlert("Forgot password",
            "Ask your SuperAdmin to reset your password from the admin portal (Admin > Users).", "OK");

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        ErrorBorder.IsVisible = false;

        if (string.IsNullOrWhiteSpace(UserNameEntry.Text) || string.IsNullOrWhiteSpace(PasswordEntry.Text))
        {
            ShowError("Enter both username and password.");
            return;
        }

        LoginButton.IsEnabled = false;
        LoginButton.Text = "Signing in…";
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;

        try
        {
            var result = await ApiClient.LoginAsync(UserNameEntry.Text.Trim(), PasswordEntry.Text, OrgCodeEntry.Text?.Trim());
            Session.Token = result.Token;
            Session.Role = result.Role;
            Session.DisplayName = result.DisplayName;
            Session.WarehouseName = result.WarehouseName;
            Session.RegionName = result.RegionName;

            if (RememberMeCheckBox.IsChecked)
            {
                Preferences.Set(RememberKey, true);
                Preferences.Set(RememberedUserKey, UserNameEntry.Text.Trim());
            }
            else
            {
                Preferences.Remove(RememberKey);
                Preferences.Remove(RememberedUserKey);
            }

            if (Session.IsSupervisor || Session.IsSecurity)
            {
                // Hub start is fire-and-forget: it retries/tears down safely on its own (see
                // SupervisorHubClient.StartAsync), so login never waits on - or fails because
                // of - the realtime handshake. Navigation targets the Dashboard tab EXPLICITLY:
                // Shell remembers the previously-selected tab across logout/login, so routing
                // to just "//SupervisorTabs" would land a returning user on whatever tab they
                // last had open instead of the dashboard.
                _ = SupervisorHubClient.StartAsync();
                await Shell.Current.GoToAsync(Session.IsSupervisor
                    ? "//SupervisorTabs/SupervisorDashboardPage"
                    : "//SecurityTabs/SecurityDashboardPage");
            }
            else
            {
                Session.Clear();
                ShowError("This account isn't provisioned for the mobile app — Office, Logistics Manager, and SuperAdmin accounts use the web portal.");
            }
        }
        catch (ApiException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception)
        {
            ShowError("Could not reach the server. Check your connection and try again.");
        }
        finally
        {
            LoginButton.IsEnabled = true;
            LoginButton.Text = "Log in";
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
        }
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorBorder.IsVisible = true;
    }
}
