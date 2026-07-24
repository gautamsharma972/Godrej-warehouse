using WarehouseGate.Mobile.Controls;
using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Pages;

public partial class LoginPage : ContentPage
{
    private const string RememberKey = "login.remember";
    private const string RememberedUserKey = "login.username";

    private bool? _isWide;
    private bool? _isCompact;
    private bool? _isVeryCompact;

    public LoginPage()
    {
        InitializeComponent();
        HeroGraphicsView.Drawable = new LoginHeroDrawable();

        if (Preferences.Get(RememberKey, false))
        {
            RememberMeCheckBox.IsChecked = true;
            UserNameEntry.Text = Preferences.Get(RememberedUserKey, string.Empty);
        }
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        var wide = width >= 900 && width >= height;
        var compact = !wide;
        var veryCompact = compact && (width < 390 || height < 700);

        if (_isWide == wide && _isCompact == compact && _isVeryCompact == veryCompact)
        {
            return;
        }

        _isWide = wide;
        _isCompact = compact;
        _isVeryCompact = veryCompact;
        ApplyResponsiveLayout(wide, veryCompact);
    }

    // Tablet: brand/illustration panel on the left, sign-in card on the right.
    // Phone: compact brand header stacked above the card.
    private void ApplyResponsiveLayout(bool wide, bool veryCompact)
    {
        LeftPanel.IsVisible = wide;
        CompactHeader.IsVisible = !wide;
        SecureBadge.IsVisible = wide || !veryCompact;
        FooterDivider.IsVisible = !veryCompact;
        CardFooter.IsVisible = !veryCompact;

        if (wide)
        {
            RootGrid.RowDefinitions = new RowDefinitionCollection { new RowDefinition(GridLength.Star) };
            RootGrid.ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(new GridLength(55, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(45, GridUnitType.Star))
            };
            Grid.SetRow(LeftPanel, 0);
            Grid.SetColumn(LeftPanel, 0);
            Grid.SetRow(RightPanel, 0);
            Grid.SetColumn(RightPanel, 1);

            RightPanel.Padding = new Thickness(46);
            SignInCard.Padding = new Thickness(40);
            SignInCard.MaximumWidthRequest = 520;
            CardStack.Spacing = 18;
            FormStack.Spacing = 14;
            WelcomeLabel.FontSize = 34;
            EyebrowLabel.FontSize = 13;
            IntroLabel.FontSize = 13;
            UserNameFieldBorder.HeightRequest = 54;
            PasswordFieldBorder.HeightRequest = 54;
            LoginButton.HeightRequest = 54;
            CompactHeader.Padding = new Thickness(22, 26, 22, 18);
            CompactHeader.Spacing = 12;
            CompactLogo.WidthRequest = 58;
            CompactLogo.HeightRequest = 58;
            CompactTitle.FontSize = 27;
            CompactSubtitle.FontSize = 13;
        }
        else
        {
            RootGrid.ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star) };
            RootGrid.RowDefinitions = new RowDefinitionCollection
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            };
            Grid.SetRow(CompactHeader, 0);
            Grid.SetColumn(CompactHeader, 0);
            Grid.SetRow(RightPanel, 1);
            Grid.SetColumn(RightPanel, 0);

            RightPanel.Padding = veryCompact ? new Thickness(14, 8, 14, 12) : new Thickness(20, 14, 20, 18);
            SignInCard.Padding = veryCompact ? new Thickness(18, 15) : new Thickness(24, 22);
            SignInCard.MaximumWidthRequest = 460;
            CardStack.Spacing = veryCompact ? 10 : 13;
            FormStack.Spacing = veryCompact ? 10 : 12;
            WelcomeLabel.FontSize = veryCompact ? 25 : 28;
            EyebrowLabel.FontSize = veryCompact ? 10 : 11;
            IntroLabel.FontSize = veryCompact ? 11 : 12;
            UserNameFieldBorder.HeightRequest = veryCompact ? 46 : 50;
            PasswordFieldBorder.HeightRequest = veryCompact ? 46 : 50;
            LoginButton.HeightRequest = veryCompact ? 48 : 52;
            CompactHeader.Padding = veryCompact ? new Thickness(16, 12, 16, 8) : new Thickness(22, 20, 22, 14);
            CompactHeader.Spacing = veryCompact ? 6 : 10;
            CompactLogo.WidthRequest = veryCompact ? 42 : 52;
            CompactLogo.HeightRequest = veryCompact ? 42 : 52;
            CompactTitle.FontSize = veryCompact ? 21 : 24;
            CompactSubtitle.FontSize = veryCompact ? 11 : 12;
        }
    }

    private void OnUserNameChanged(object? sender, TextChangedEventArgs e) =>
        UserNameCheckLabel.IsVisible = !string.IsNullOrWhiteSpace(e.NewTextValue);

    // MAUI's Border doesn't get a native "focused outline" the way a real Android/iOS text field
    // does when a child Entry receives focus - without this, the two fields look identical whether
    // you're typing in them or not. Matches the Material outlined-text-field pattern: a thin neutral
    // border at rest, a thicker brand-colored one while editing.
    private void OnFieldFocused(object? sender, FocusEventArgs e) =>
        UiHelpers.SetFieldFocus(sender == UserNameEntry ? UserNameFieldBorder : PasswordFieldBorder, true);

    private void OnFieldUnfocused(object? sender, FocusEventArgs e) =>
        UiHelpers.SetFieldFocus(sender == UserNameEntry ? UserNameFieldBorder : PasswordFieldBorder, false);

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
            var result = await ApiClient.LoginAsync(UserNameEntry.Text.Trim(), PasswordEntry.Text);
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
