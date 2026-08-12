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
        FooterDivider.IsVisible = !veryCompact;
        CardFooter.IsVisible = !veryCompact;

        RightPanel.Padding = veryCompact ? new Thickness(14, 16, 14, 14) : new Thickness(16, 18, 16, 16);
        SignInCard.Padding = 0;
        SignInCard.MaximumWidthRequest = 560;
        CardStack.Spacing = veryCompact ? 12 : 16;
        FormStack.Spacing = veryCompact ? 10 : 12;
        WelcomeLabel.FontSize = veryCompact ? 18 : 19;
        EyebrowLabel.FontSize = veryCompact ? 8 : 9;
        IntroLabel.FontSize = veryCompact ? 11 : 12;
        LoginButton.HeightRequest = veryCompact ? 48 : 50;
    }

    private void OnUserNameChanged(object? sender, TextChangedEventArgs e) =>
        UserNameCheckLabel.IsVisible = !string.IsNullOrWhiteSpace(e.NewTextValue);

    private void OnEntryHandlerChanged(object? sender, EventArgs e)
    {
#if ANDROID
        if (sender is Entry entry)
        {
            ApplyAndroidEntryStyle(entry);
            entry.Dispatcher.Dispatch(() => ApplyAndroidEntryStyle(entry));
        }
#endif
    }

    private void OnEntryLoaded(object? sender, EventArgs e)
    {
#if ANDROID
        if (sender is Entry entry)
        {
            ApplyAndroidEntryStyle(entry);
        }
#endif
    }

#if ANDROID
    private static void ApplyAndroidEntryStyle(Entry entry)
    {
        if (entry.Handler?.PlatformView is not Android.Widget.EditText nativeEntry)
        {
            return;
        }

        var transparent = Android.Graphics.Color.Transparent;
        nativeEntry.SetTextColor(Android.Graphics.Color.ParseColor("#172033"));
        nativeEntry.SetHintTextColor(Android.Graphics.Color.ParseColor("#879591"));
        nativeEntry.Gravity = Android.Views.GravityFlags.CenterVertical;
        nativeEntry.SetPadding(0, 0, 0, 0);
        nativeEntry.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(transparent);
        nativeEntry.Background = new Android.Graphics.Drawables.ColorDrawable(transparent);
        nativeEntry.CompoundDrawableTintList = Android.Content.Res.ColorStateList.ValueOf(transparent);
        nativeEntry.SetCompoundDrawablesRelativeWithIntrinsicBounds(0, 0, 0, 0);

        var parent = nativeEntry.Parent;
        while (parent is Android.Views.View parentView)
        {
            if (parentView is Google.Android.Material.TextField.TextInputLayout textInputLayout)
            {
                textInputLayout.BoxBackgroundMode = 0;
                textInputLayout.EndIconMode = 0;
                textInputLayout.HintEnabled = false;
                break;
            }

            parent = parentView.Parent;
        }
    }
#endif

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
        PasswordEyeLabel.Source = PasswordEntry.IsPassword ? "login_eye.svg" : "login_eye_off.svg";
#if ANDROID
        PasswordEntry.Dispatcher.Dispatch(() => ApplyAndroidEntryStyle(PasswordEntry));
#endif
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
            Session.ExpiresAtUtc = result.ExpiresAtUtc;
            Session.Persist();

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
            LoginButton.Text = "Sign in";
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
