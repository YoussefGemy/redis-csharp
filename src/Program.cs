using System.Net;
using System.Net.Sockets;
using System.Text;

// You can use print statements as follows for debugging, they'll be visible when running tests.
Console.WriteLine("Logs from your program will appear here!");

TcpListener server = new TcpListener(IPAddress.Any, 6379);
server.Start();

byte[] response = Encoding.UTF8.GetBytes("+PONG\r\n");

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

        await client.SendAsync(response);
    }

    client.Close();
}
