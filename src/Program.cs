using System.Net;
using System.Net.Sockets;
using System.Text;

// You can use print statements as follows for debugging, they'll be visible when running tests.
Console.WriteLine("Logs from your program will appear here!");

TcpListener server = new TcpListener(IPAddress.Any, 6379);
server.Start();
Socket client = server.AcceptSocket(); // wait for client

byte[] buffer = new byte[1024];
byte[] response = Encoding.UTF8.GetBytes("+PONG\r\n");

while (true){
    int bytesRead = client.Receive(buffer); // wait for the client's command
    if (bytesRead == 0) break;
    
    client.Send(response);
}