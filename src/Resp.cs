using System.Text;

namespace Redis;

public static class Resp
{
    public static string BulkString(string text) => $"${Encoding.UTF8.GetByteCount(text)}\r\n{text}\r\n";
}
