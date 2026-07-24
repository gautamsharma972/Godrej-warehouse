namespace WarehouseGate.Mobile.Services;

public static class AppConfig
{
    // Android emulator can't see the host machine as "localhost" - it maps the host to 10.0.2.2.
    // A physical device would need the dev machine's LAN IP instead; see README.
    public static string ApiBaseUrl =>
#if ANDROID
        "http://10.0.2.2:5080";
#else
        "https://localhost:7174";
#endif
}
