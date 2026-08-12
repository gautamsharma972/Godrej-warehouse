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
					ConfigureAndroidEntry(handler.PlatformView));
				Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("Background", (handler, _) =>
					ConfigureAndroidEntry(handler.PlatformView));
				Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("IsPassword", (handler, _) =>
					ConfigureAndroidEntry(handler.PlatformView));

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
#elif WINDOWS
				// WinUI wraps Entry in a native TextBox (or a PasswordBox once IsPassword is set) that
				// draws its own square-cornered border and, for PasswordBox specifically, a built-in
				// "reveal password" eye button - both render on top of/next to our own rounded custom
				// Border + eye Label (see LoginPage.xaml), giving every field a doubled border and the
				// password field two eye icons side by side. Stripping the native border and hiding the
				// native reveal button on every Entry keeps just our own styling, app-wide.
				Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("Background", (handler, _) =>
					ConfigureWindowsEntry(handler.PlatformView));
				Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("IsPassword", (handler, _) =>
					ConfigureWindowsEntry(handler.PlatformView));
#endif
			});

#if DEBUG
		builder.Services.AddHybridWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}

#if ANDROID
	private static void ConfigureAndroidEntry(Android.Widget.EditText entry)
	{
		var transparent = Android.Graphics.Color.Transparent;
		entry.SetTextColor(Android.Graphics.Color.ParseColor("#20232B"));
		entry.SetHintTextColor(Android.Graphics.Color.ParseColor("#6B7280"));
		entry.Gravity = Android.Views.GravityFlags.CenterVertical;
		entry.SetPadding(0, 0, 0, 0);
		entry.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(transparent);
		entry.Background = new Android.Graphics.Drawables.ColorDrawable(transparent);
		entry.CompoundDrawableTintList = Android.Content.Res.ColorStateList.ValueOf(transparent);
		entry.SetCompoundDrawablesRelativeWithIntrinsicBounds(0, 0, 0, 0);
	}
#elif WINDOWS
	private static void ConfigureWindowsEntry(Microsoft.UI.Xaml.Controls.Control entry)
	{
		entry.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);

		if (entry is Microsoft.UI.Xaml.Controls.PasswordBox passwordBox)
		{
			passwordBox.PasswordRevealMode = Microsoft.UI.Xaml.Controls.PasswordRevealMode.Hidden;
		}
	}
#endif
}
