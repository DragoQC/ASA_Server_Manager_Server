namespace AsaServerManager.Web.Constants;

public static class RconProtocolConstants
{
    public const string Host = "127.0.0.1";
    public const int AuthPacketType = 3;
    public const int ExecPacketType = 2;
    public const int ResponsePacketType = 0;
    public const int AuthFailureId = -1;
    public const int ProbeTimeoutMilliseconds = 3000;
    public const int ReadTimeoutMilliseconds = 250;
}
