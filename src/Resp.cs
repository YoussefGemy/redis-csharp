using System.Text;

namespace Redis;

public static class Resp
{
    public static string BulkString(string text) => $"${Encoding.UTF8.GetByteCount(text)}\r\n{text}\r\n";

    public static string EncodeEntry(StreamEntry entry)
    {
        string fields = $"*{entry.Fields.Count * 2}\r\n";

        foreach (var (field, value) in entry.Fields)
        {
            fields += BulkString(field) + BulkString(value);
        }

        return $"*2\r\n{BulkString(entry.Id)}{fields}";
    }
}
