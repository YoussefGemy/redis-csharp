using System.Net;
using System.Net.Sockets;
using Redis;

// You can use print statements as follows for debugging, they'll be visible when running tests.
Console.WriteLine("Logs from your program will appear here!");

TcpListener server = new TcpListener(IPAddress.Any, 6379);
server.Start();

DataStore db = new();
CommandExecutor executor = new(db);

while (true)
{
    Socket client = await server.AcceptSocketAsync(); // wait for client
    _ = new ClientConnection(client, executor).RunAsync();
}
