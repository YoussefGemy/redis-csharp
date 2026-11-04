using System.Net.Sockets;
using System.Text;

namespace Redis;

public class ClientConnection
{
    private readonly Socket _socket;
    private readonly CommandExecutor _executor;

    private readonly DataStore _db;

    private readonly byte[] _buffer = new byte[1024];
    private bool _inMulti = false;
    private readonly List<string[]> _queued = new();

    private readonly Dictionary<string, long> _watched = new();

    public ClientConnection(Socket socket, CommandExecutor executor, DataStore db)
    {
        _socket = socket;
        _executor = executor;
        _db = db;
    }

    public async Task RunAsync()
    {
        while (true)
        {
            int bytesRead = await _socket.ReceiveAsync(_buffer); // wait for the client's command
            if (bytesRead == 0) break;

            string request = Encoding.UTF8.GetString(_buffer, 0, bytesRead);
            string[] parts = request.Split("\r\n");
            string command = parts[2].ToUpper();

            string reply;

            if (command == "EXEC")
            {
                if (!_inMulti)
                {
                    reply = "-ERR EXEC without MULTI\r\n";
                }
                else
                {
                    _inMulti = false;

                    bool watchedKeyChanged = _watched.Any(w => _db.GetVersion(w.Key) != w.Value);

                    if (watchedKeyChanged)
                    {
                        reply = "*-1\r\n";
                    }
                    else
                    {
                        reply = $"*{_queued.Count}\r\n";

                        foreach (string[] queuedParts in _queued)
                        {
                            reply += await _executor.ExecuteAsync(queuedParts);
                        }
                    }

                    _queued.Clear();
                    _watched.Clear();
                }
            }
            else if (command == "DISCARD")
            {
                if (!_inMulti)
                {
                    reply = "-ERR DISCARD without MULTI\r\n";
                }
                else
                {
                    _inMulti = false;
                    reply = "+OK\r\n";
                    _queued.Clear();
                    _watched.Clear();
                }
            }
            else if (command == "WATCH")
            {
                if (_inMulti)
                {
                    reply = "-ERR WATCH inside MULTI is not allowed\r\n";
                }
                else
                {
                    for (int i = 4; i < parts.Length; i += 2)
                    {
                        _watched.TryAdd(parts[i], _db.GetVersion(parts[i]));
                    }

                    reply = "+OK\r\n";
                }
            }
            else if (_inMulti)
            {
                _queued.Add(parts);
                reply = "+QUEUED\r\n";
            }
            else if (command == "UNWATCH")
            {
                _watched.Clear();
                reply = "+OK\r\n";
            }
            else if (command == "MULTI")
            {
                _inMulti = true;
                reply = "+OK\r\n";
            }
            else
            {
                reply = await _executor.ExecuteAsync(parts);
            }

            await _socket.SendAsync(Encoding.UTF8.GetBytes(reply));
        }

        _socket.Close();
    }
}
