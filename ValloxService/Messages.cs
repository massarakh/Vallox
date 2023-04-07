namespace Vallox.ValloxService;

public class Messages
{
    public static object checksum_16(int[] data)
    {
        var c = 0;
        foreach (var i in Enumerable.Range(0, data.Length / 2))
        {
            c = c + (data[i * 2 + 1] << 8) + data[i * 2];
        }
        return c & 0xFFFF;
    }
}