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
        "https://gcplapi.logivue.in";
    //"http://10.0.2.2:5080";
#else
        
"https://gcplapi.logivue.in";
//"https://localhost:7174";
#endif
}
