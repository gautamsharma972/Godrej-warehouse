using Microsoft.Extensions.Logging;
using ZXing.Net.Maui.Controls;

namespace WarehouseGate.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseBarcodeReader()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("Poppins-Regular.ttf", "PoppinsRegular");
				fonts.AddFont("Poppins-Medium.ttf", "PoppinsMedium");
				fonts.AddFont("Poppins-SemiBold.ttf", "PoppinsSemiBold");
				fonts.AddFont("Poppins-Bold.ttf", "PoppinsBold");
				fonts.AddFont("FontAwesome-Solid.ttf", "FaSolid");
			})
			.ConfigureMauiHandlers(handlers =>
			{
#if ANDROID
				// Android draws its own underline under Entry; our card layouts already
				// draw a divider BoxView, so the native one is just a duplicate line.
				Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoUnderline", (handler, _) =>
				{
					handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
					handler.PlatformView.Background = null;
				});

				// Every Entry already has its own custom clear/reveal icon and the login form has
				// its own "Remember username" checkbox - Android/the keyboard's own autofill
				// overlay (suggestion strip, a second native password-reveal eye) only duplicates
				// that and covers our fields, so opt every Entry out of the OS autofill pass.
				// ImportantForAutofill needs API 26+; the app supports back to API 21, so older
				// devices just keep the (harmless, if slightly noisier) native autofill behavior.
				Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoAutofill", (handler, _) =>
				{
					if (OperatingSystem.IsAndroidVersionAtLeast(26))
					{
						handler.PlatformView.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
					}
				});
#endif
			});

#if DEBUG
		builder.Services.AddHybridWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
