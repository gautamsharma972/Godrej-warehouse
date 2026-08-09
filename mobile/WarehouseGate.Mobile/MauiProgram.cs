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
				// Anchored on the real "TextColor" property key, not a made-up custom key name -
				// found the hard way (via an Android.Util.Log diagnostic that proved the mapping
				// action itself never ran) that this MAUI version doesn't auto-invoke synthetic
				// AppendToMapping keys during the initial handler-connect pass the way it does for
				// keys that correspond to a real property. "TextColor" always changes MAUI's own
				// default text color, but appending here runs our code AFTER that default action for
				// the same key, on every connect and every subsequent TextColor update - which is
				// what actually guarantees this runs at all.
				//
				// Reported directly from a real device as typed text being invisible in the login
				// fields - reproduced here and root-caused via a UI dump plus a pixel-level zoom of
				// the actual screenshot: the widget's committed text was correct and non-empty
				// ("GCPL_test123"), and its color WAS the intended dark tone once this mapping
				// actually ran - the real bug is vertical: the native EditText's hint and typed text
				// are both anchored too high in their allocated space (a gravity/line-height default
				// that shifted with the MAUI 9 -> 10 + Material3 version bump), so most of each glyph
				// renders above the app's own Border container and gets clipped by it - only a thin
				// top sliver survives, which reads as faint "invisible" text at a glance. Forcing
				// CenterVertical gravity keeps the full glyph within the Border's visible bounds
				// regardless of what the platform's own default resolves to.
				Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("TextColor", (handler, _) =>
				{
					handler.PlatformView.SetTextColor(Android.Graphics.Color.ParseColor("#20232B"));
					handler.PlatformView.SetHintTextColor(Android.Graphics.Color.ParseColor("#6B7280"));
					handler.PlatformView.Gravity = Android.Views.GravityFlags.CenterVertical;
					// Android draws its own underline under Entry; our card layouts already draw a
					// divider BoxView, so the native one is just a duplicate line.
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
