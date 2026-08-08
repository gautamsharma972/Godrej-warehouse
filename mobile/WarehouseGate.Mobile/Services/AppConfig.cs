namespace WarehouseGate.Mobile.Services;

public static class AppConfig
{
    // Android emulator can't see the host machine as "localhost" - it maps the host to 10.0.2.2.
    // A physical device needs the dev machine's actual LAN IP instead (find it with
    // `ipconfig`/Get-NetIPAddress on the dev machine - it changes if the machine reconnects to
    // a different network or gets a new DHCP lease) - and HTTP, not HTTPS, since a physical
    // device won't trust the API's self-signed dev certificate. The matching IP must also be
    // allow-listed in Platforms/Android/Resources/xml/network_security_config.xml, since Android
    // blocks cleartext HTTP by default.
    public static string ApiBaseUrl =>
#if ANDROID
        //"http://10.0.2.2:5080/"; // Android emulator
        //"https://gcplapi.logivue.in";
        "http://192.168.1.68:5080/"; // physical device on the same Wi-Fi as the dev machine
    #else
        "https://localhost:7174";
#endif
}
