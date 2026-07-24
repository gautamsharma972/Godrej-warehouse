using Microsoft.Maui.Controls.Shapes;

namespace WarehouseGate.Mobile.Services;

public static class UiHelpers
{
    // Shared outlined-text-field focus treatment: thin neutral border at rest, thicker brand-
    // colored border while the wrapped Entry/Editor has keyboard focus. Used by every Border-
    // wrapped text field across Login, Gate Check-In, and Vehicle Status so the "actively typing"
    // affordance looks identical everywhere in the app.
    public static void SetFieldFocus(Border border, bool focused)
    {
        border.Stroke = focused
            ? (Color)Application.Current!.Resources["Primary"]
            : (Color)Application.Current!.Resources["CardBorderLight"];
        border.StrokeThickness = focused ? 2 : 1.4;
    }

    // Renders a horizontal progress stepper into a Grid, with a thin connecting line between
    // each dot. Dot columns are Auto-sized (so a dot always stays centered under its caption,
    // however wide) interleaved with Star "gap" columns holding the connector - the connector
    // therefore spans exactly from one dot's edge to the next, with no pixel math and no
    // overshoot past the first/last dot. Steps before currentIndex are "done", currentIndex is
    // "current", the rest are "future"; the connecting line itself is a plain light track, not
    // progress-colored.
    public static void BuildStepper(Grid container, int currentIndex, string[] labels)
    {
        container.Children.Clear();
        container.ColumnDefinitions.Clear();
        container.RowDefinitions.Clear();
        container.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        container.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var done = (Color)Application.Current!.Resources["StatusSuccess"];
        var current = (Color)Application.Current.Resources["Accent"];
        var future = (Color)Application.Current.Resources["Gray300"];
        var track = (Color)Application.Current.Resources["CardBorderLight"];

        for (var i = 0; i < labels.Length; i++)
        {
            var dotColumn = container.ColumnDefinitions.Count;
            container.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var color = i < currentIndex ? done : i == currentIndex ? current : future;

            var dot = new Ellipse
            {
                WidthRequest = i == currentIndex ? 16 : 12,
                HeightRequest = i == currentIndex ? 16 : 12,
                Fill = new SolidColorBrush(color),
                HorizontalOptions = LayoutOptions.Center
            };
            Grid.SetColumn(dot, dotColumn);
            Grid.SetRow(dot, 0);
            container.Children.Add(dot);

            var caption = new Label
            {
                Text = labels[i],
                FontSize = 10,
                FontFamily = i == currentIndex ? "PoppinsSemiBold" : "PoppinsRegular",
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                TextColor = color
            };
            Grid.SetColumn(caption, dotColumn);
            Grid.SetRow(caption, 1);
            container.Children.Add(caption);

            if (i < labels.Length - 1)
            {
                var lineColumn = container.ColumnDefinitions.Count;
                container.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

                var connector = new BoxView
                {
                    HeightRequest = 2,
                    Color = track,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Fill
                };
                Grid.SetColumn(connector, lineColumn);
                Grid.SetRow(connector, 0);
                container.Children.Add(connector);
            }
        }
    }


    // A tappable alert row: tinted circular icon badge + bold count + label + chevron, with a
    // light press animation before navigating. Shared by SecurityDashboardPage and NotificationsPage
    // so both surfaces render the same "needs attention" items identically.
    public static View BuildAttentionCard(string icon, string colorKey, int count, string label, string route)
    {
        var color = (Color)Application.Current!.Resources[colorKey];
        var mutedColor = (Color)Application.Current!.Resources["TextSecondaryLight"];

        var badge = new Grid { WidthRequest = 46, HeightRequest = 46, VerticalOptions = LayoutOptions.Center };
        badge.Children.Add(new Border
        {
            StrokeShape = new Ellipse(),
            StrokeThickness = 0,
            BackgroundColor = color,
            Opacity = 0.15
        });
        badge.Children.Add(new Label
        {
            Text = icon, FontFamily = "FaSolid", FontSize = 16, TextColor = color,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center
        });

        var countLabel = new Label
        {
            Text = count.ToString(),
            FontFamily = "PoppinsBold",
            FontSize = 22,
            TextColor = color,
            VerticalOptions = LayoutOptions.Center
        };

        var messageLabel = new Label
        {
            Text = label,
            FontSize = 13,
            TextColor = mutedColor,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.WordWrap
        };

        var chevron = new Label
        {
            Text = IconGlyphs.ChevronRight, FontFamily = "FaSolid", FontSize = 14,
            TextColor = mutedColor, VerticalOptions = LayoutOptions.Center
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 14
        };
        grid.Children.Add(badge);
        Grid.SetColumn(badge, 0);
        grid.Children.Add(messageLabel);
        Grid.SetColumn(messageLabel, 1);
        grid.Children.Add(countLabel);
        Grid.SetColumn(countLabel, 2);
        grid.Children.Add(chevron);
        Grid.SetColumn(chevron, 3);

        var card = new Border
        {
            Style = (Style)Application.Current!.Resources["CardBorder"],
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Padding = new Thickness(18, 16),
            Content = grid
        };
        card.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                await card.ScaleTo(0.97, 80, Easing.CubicOut);
                await card.ScaleTo(1, 80, Easing.CubicIn);
                await Shell.Current.GoToAsync(route);
            })
        });
        return card;
    }

    public static View BuildDashboardAttentionRow(string icon, string colorKey, int count, string label, string route)
    {
        var color = (Color)Application.Current!.Resources[colorKey];
        var mutedColor = (Color)Application.Current.Resources["TextSecondaryLight"];

        var badge = new Grid { WidthRequest = 42, HeightRequest = 42, VerticalOptions = LayoutOptions.Center };
        badge.Children.Add(new Border
        {
            StrokeShape = new Ellipse(),
            StrokeThickness = 0,
            BackgroundColor = color,
            Opacity = 0.16
        });
        badge.Children.Add(new Label
        {
            Text = icon,
            FontFamily = "FaSolid",
            FontSize = 16,
            TextColor = color,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        });

        var countLabel = new Label
        {
            Text = count.ToString(),
            FontFamily = "PoppinsBold",
            FontSize = 21,
            TextColor = color,
            VerticalOptions = LayoutOptions.Center
        };

        var messageLabel = new Label
        {
            Text = label,
            FontSize = 12,
            TextColor = mutedColor,
            VerticalOptions = LayoutOptions.Center
        };

        var detailLabel = new Label
        {
            Text = "View details",
            FontFamily = "PoppinsRegular",
            FontSize = 12,
            TextColor = color,
            VerticalOptions = LayoutOptions.Center
        };

        var chevron = new Label
        {
            Text = IconGlyphs.ChevronRight,
            FontFamily = "FaSolid",
            FontSize = 13,
            TextColor = mutedColor,
            VerticalOptions = LayoutOptions.Center
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 14
        };

        grid.Children.Add(badge);
        Grid.SetColumn(badge, 0);
        grid.Children.Add(countLabel);
        Grid.SetColumn(countLabel, 1);
        grid.Children.Add(messageLabel);
        Grid.SetColumn(messageLabel, 2);
        grid.Children.Add(detailLabel);
        Grid.SetColumn(detailLabel, 3);
        grid.Children.Add(chevron);
        Grid.SetColumn(chevron, 4);

        var row = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(18, 14),
            Content = grid
        };
        row.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                await row.ScaleTo(0.985, 80, Easing.CubicOut);
                await row.ScaleTo(1, 80, Easing.CubicIn);
                await Shell.Current.GoToAsync(route);
            })
        });

        return row;
    }

    public static View BuildDashboardAttentionDivider() =>
        new BoxView
        {
            HeightRequest = 1,
            Color = (Color)Application.Current!.Resources["CardBorderLight"],
            Margin = new Thickness(0)
        };

    // The "nothing needs attention" empty-state card shown when every attention count is zero.
    public static View BuildAllClearCard(string title = "All caught up", string subtitle = "No vehicles need attention right now.")
    {
        var successColor = (Color)Application.Current!.Resources["StatusSuccess"];

        var badge = new Grid { WidthRequest = 44, HeightRequest = 44, HorizontalOptions = LayoutOptions.Center };
        badge.Children.Add(new Border
        {
            StrokeShape = new Ellipse(),
            StrokeThickness = 0,
            BackgroundColor = successColor,
            Opacity = 0.15
        });
        badge.Children.Add(new Label
        {
            Text = IconGlyphs.CircleCheck, FontFamily = "FaSolid", FontSize = 18, TextColor = successColor,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center
        });

        return new Border
        {
            Style = (Style)Application.Current!.Resources["CardBorder"],
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                HorizontalOptions = LayoutOptions.Center,
                Children =
                {
                    badge,
                    new Label
                    {
                        Text = title, FontFamily = "PoppinsSemiBold", FontSize = 15,
                        HorizontalOptions = LayoutOptions.Center
                    },
                    new Label
                    {
                        Text = subtitle,
                        Style = (Style)Application.Current!.Resources["MetaLabel"],
                        HorizontalOptions = LayoutOptions.Center
                    }
                }
            }
        };
    }
}
