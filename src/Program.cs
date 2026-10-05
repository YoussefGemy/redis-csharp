using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

// You can use print statements as follows for debugging, they'll be visible when running tests.
Console.WriteLine("Logs from your program will appear here!");

TcpListener server = new TcpListener(IPAddress.Any, 6379);
server.Start();

ConcurrentDictionary<string, (string Value, DateTime? ExpiresAt)> store = new();

while (true)
{
    Socket client = await server.AcceptSocketAsync(); // wait for client
    _ = HandleClientAsync(client);
}

async Task HandleClientAsync(Socket client)
{
    byte[] buffer = new byte[1024];

    while (true)
    {
        int bytesRead = await client.ReceiveAsync(buffer); // wait for the client's command
        if (bytesRead == 0) break;

        string request = Encoding.UTF8.GetString(buffer, 0, bytesRead);
        string[] parts = request.Split("\r\n");
        string command = parts[2].ToUpper();

        string reply;

        if (command == "PING")
        {
            reply = "+PONG\r\n";
        }
        else if (command == "ECHO")
        {
            reply = BulkString(parts[4]);
        }
        else if (command == "SET")
        {
            string key = parts[4];
            string value = parts[6];

            DateTime? expiresAt = null;

            if (parts.Length > 10)
            {
                string option = parts[8].ToUpper();
                long amount = long.Parse(parts[10]);

                if (option == "PX")
                {
                    expiresAt = DateTime.UtcNow.AddMilliseconds(amount);
                }
                else if (option == "EX")
                {
                    expiresAt = DateTime.UtcNow.AddSeconds(amount);
                }
            }

            store[key] = (value, expiresAt);

            reply = "+OK\r\n";
        }
        else if (command == "GET")
        {
            string key = parts[4];

            if (!store.TryGetValue(key, out var entry))
            {
                reply = "$-1\r\n";
            }
            else if (entry.ExpiresAt != null && DateTime.UtcNow >= entry.ExpiresAt)
            {
                store.TryRemove(key, out _);
                reply = "$-1\r\n";
            }
            else
            {
                reply = BulkString(entry.Value);
            }
        }
        else
        {
            reply = "-ERR unknown command\r\n";
        }

        await client.SendAsync(Encoding.UTF8.GetBytes(reply));
    }

    client.Close();
}

string BulkString(string text) => $"${Encoding.UTF8.GetByteCount(text)}\r\n{text}\r\n";
