using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Speech;
using Android.Views;
using AndroidX.Core.View;

namespace WarehouseGate.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private const int SpeechRequestCode = 4210;
    private TaskCompletionSource<string?>? _speechResult;
    private bool _handlingBack;

    protected override void AttachBaseContext(Context? @base)
    {
        if (@base is not null && @base.Resources?.Configuration is { } current && current.FontScale > 1.15f)
        {
            var capped = new Android.Content.Res.Configuration(current) { FontScale = 1.15f };
            base.AttachBaseContext(@base.CreateConfigurationContext(capped));
            return;
        }

        base.AttachBaseContext(@base);
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Android 15+ draws edge-to-edge by default. Clear the splash theme's fullscreen flag
        // once MAUI owns the window and ask Android to keep application content inside system bars.
        Window?.ClearFlags(WindowManagerFlags.Fullscreen);
        if (Window is not null)
        {
            WindowCompat.SetDecorFitsSystemWindows(Window, true);
        }
    }

    public override async void OnBackPressed()
    {
        if (_handlingBack)
        {
            return;
        }

        _handlingBack = true;
        try
        {
            if (Shell.Current is AppShell shell && await shell.HandleHardwareBackAsync())
            {
                return;
            }

#pragma warning disable CA1422
            base.OnBackPressed();
#pragma warning restore CA1422
        }
        finally
        {
            _handlingBack = false;
        }
    }

    // Launches Android's on-device speech recognizer and returns the top transcription result,
    // or null if the user cancelled / nothing was recognized. Bridges the callback-based
    // OnActivityResult API to an awaitable Task for SpeechToTextService to consume.
    public Task<string?> StartSpeechRecognitionAsync()
    {
        _speechResult = new TaskCompletionSource<string?>();

        var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
        intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
        intent.PutExtra(RecognizerIntent.ExtraMaxResults, 1);

        StartActivityForResult(intent, SpeechRequestCode);

        return _speechResult.Task;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode != SpeechRequestCode)
        {
            return;
        }

        var text = resultCode == Result.Ok
            ? data?.GetStringArrayListExtra(RecognizerIntent.ExtraResults)?.FirstOrDefault()
            : null;

        _speechResult?.TrySetResult(text);
    }
}
