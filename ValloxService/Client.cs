using Websocket.Client;

namespace Vallox.ValloxService;

public static class Client
{
    static Uri url = new Uri("ws://192.168.0.55");
    public static void Test()
    {
        using (var client = new WebsocketClient(url))
        {
            byte[] buf = new byte[] { 0x02, 0x00, 0xf3, 0x00, 0xf5, 0x00 };
            client.Start();
            client.Send(buf);
        }
    }

}