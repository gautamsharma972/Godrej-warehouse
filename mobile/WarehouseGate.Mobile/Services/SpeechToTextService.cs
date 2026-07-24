namespace WarehouseGate.Mobile.Services;

// Wraps Android's on-device SpeechRecognizer (Platforms/Android/MainActivity.cs) behind a
// simple static API, matching this project's existing static-service convention. No mature
// open-source cross-platform MAUI speech-to-text package exists today (checked: the only
// candidates are beta-only or have near-zero adoption), so this is Android-only for now -
// a graceful no-op everywhere else.
public static class SpeechToTextService
{
    public static bool IsSupported =>
#if ANDROID
        true;
#else
        false;
#endif

    public static async Task<string?> ListenAsync()
    {
#if ANDROID
        var status = await Permissions.RequestAsync<Permissions.Microphone>();
        if (status != PermissionStatus.Granted)
        {
            return null;
        }

        if (Platform.CurrentActivity is MainActivity mainActivity)
        {
            return await mainActivity.StartSpeechRecognitionAsync();
        }

        return null;
#else
        await Task.CompletedTask;
        return null;
#endif
    }
}
