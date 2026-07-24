using WarehouseGate.Mobile.Models;

namespace WarehouseGate.Mobile.Pages;

public partial class PhotoViewerPage : ContentPage
{
    public PhotoViewerPage(PhotoDisplayItem item)
    {
        InitializeComponent();

        if (item.HasLocalImage)
        {
            PhotoImage.Source = item.Source;
            PhotoImage.IsVisible = true;
        }
        else
        {
            PlaceholderContainer.IsVisible = true;
        }

        DescriptionLabel.Text = item.Description;
    }

    private async void OnCloseTapped(object? sender, EventArgs e) => await Navigation.PopModalAsync();
}
