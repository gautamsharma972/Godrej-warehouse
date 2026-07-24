using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Speech;

namespace WarehouseGate.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private const int SpeechRequestCode = 4210;
    private TaskCompletionSource<string?>? _speechResult;

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
