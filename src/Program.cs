using System.Net;
using System.Net.Sockets;
using System.Text;

// You can use print statements as follows for debugging, they'll be visible when running tests.
Console.WriteLine("Logs from your program will appear here!");

TcpListener server = new TcpListener(IPAddress.Any, 6379);
server.Start();

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
            string argument = parts[4];
            int length = Encoding.UTF8.GetByteCount(argument);
            reply = $"${length}\r\n{argument}\r\n";
        }
        else
        {
            reply = "-ERR unknown command\r\n";
        }

        await client.SendAsync(Encoding.UTF8.GetBytes(reply));
    }

    client.Close();
}
