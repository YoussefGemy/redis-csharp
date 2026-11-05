using System.Globalization;
using static Redis.Resp;

namespace Redis;

public class CommandExecutor
{
    private readonly DataStore _db;

    public CommandExecutor(DataStore db)
    {
        _db = db;
    } 

    public async Task<string> ExecuteAsync(string[] parts)
    {
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

            _db.Store[key] = (value, expiresAt);

            _db.Touch(key);

            reply = "+OK\r\n";
        }
        else if (command == "INCR")
        {
            string key = parts[4];
            if (!_db.Store.TryGetValue(key, out var entry) ||
                (entry.ExpiresAt != null && DateTime.UtcNow >= entry.ExpiresAt))
            {
                _db.Store[key] = ("1", null);
                _db.Touch(key);
                reply = ":1\r\n";
            }
            else if (!long.TryParse(entry.Value, out long number))
            {
                reply = "-ERR value is not an integer or out of range\r\n";
            }
            else
            {
                lock (_db.Store)
                {
                    number++;
                    _db.Store[key] = (number.ToString(), entry.ExpiresAt);
                }
                _db.Touch(key);
                reply = $":{number}\r\n";
            }
        }
        else if (command == "GET")
        {
            string key = parts[4];

            if (!_db.Store.TryGetValue(key, out var entry))
            {
                reply = "$-1\r\n";
            }
            else if (entry.ExpiresAt != null && DateTime.UtcNow >= entry.ExpiresAt)
            {
                _db.Store.TryRemove(key, out _);
                reply = "$-1\r\n";
            }
            else
            {
                reply = BulkString(entry.Value);
            }
        }
        else if (command == "TYPE")
        {
            string key = parts[4];
            if (_db.Store.TryGetValue(key, out var entry) &&
                (entry.ExpiresAt == null || DateTime.UtcNow < entry.ExpiresAt))
            {
                reply = "+string\r\n";
            }
            else if (_db.Lists.TryGetValue(key, out List<string>? list) && list.Count > 0)
            {
                reply = "+list\r\n";
            }
            else if (_db.Streams.TryGetValue(key, out List<StreamEntry>? stream) && stream.Count > 0)
            {
                reply = "+stream\r\n";
            }
            else
            {
                reply = "+none\r\n";
            }
        }
        else if (command == "RPUSH")
        {
            string key = parts[4];
            List<string> list = _db.Lists.GetOrAdd(key, _ => new List<string>());

            int count;

            lock (list)
            {
                for (int i = 6; i < parts.Length; i += 2)
                {
                    list.Add(parts[i]);
                }
                count = list.Count;
                _db.ServeWaiters(key, list);
            }

            _db.Touch(key);

            reply = $":{count}\r\n";
        }
        else if (command == "LPUSH")
        {
            string key = parts[4];
            List<string> list = _db.Lists.GetOrAdd(key, _ => new List<string>());

            int count;

            lock (list)
            {
                for (int i = 6; i < parts.Length; i += 2)
                {
                    list.Insert(0, parts[i]);
                }
                count = list.Count;
                _db.ServeWaiters(key, list);
            }

            _db.Touch(key);

            reply = $":{count}\r\n";
        }
        else if (command == "LLEN")
        {
            string key = parts[4];

            int count = 0;
            if (_db.Lists.TryGetValue(key, out List<string>? list))
            {
                lock (list)
                {
                    count = list.Count;
                }
            }
            reply = $":{count}\r\n";
        }
        else if (command == "LPOP")
        {
            string key = parts[4];

            bool hasCount = parts.Length > 6;

            int NumberOfElements = 1;
            if (hasCount)
            {
                NumberOfElements = int.Parse(parts[6]);
            }

            List<string> items = new();
            reply = "$-1\r\n";

            if (_db.Lists.TryGetValue(key, out List<string>? list))
            {
                lock (list)
                {
                    while (list.Count > 0 && NumberOfElements > 0)
                    {
                        items.Add(list[0]);
                        list.RemoveAt(0);
                        NumberOfElements -= 1;
                    }
                }
            }

            if (items.Count > 0)
            {
                _db.Touch(key);

                if (hasCount)
                {
                    reply = $"*{items.Count}\r\n";

                    foreach (string item in items)
                    {
                        reply += BulkString(item);
                    }
                }
                else
                {
                    reply = BulkString(items[0]);
                }
            }
        }
        else if (command == "BLPOP")
        {
            string key = parts[4];

            double timeoutSeconds = double.Parse(parts[6], CultureInfo.InvariantCulture);

            TimeSpan timeout = timeoutSeconds == 0
                ? Timeout.InfiniteTimeSpan
                : TimeSpan.FromSeconds(timeoutSeconds);

            List<string> list = _db.Lists.GetOrAdd(key, _ => new List<string>());

            string? value = null;
            TaskCompletionSource<string>? waiter = null;

            lock (list)
            {
                if (list.Count > 0)
                {
                    value = list[0];
                    list.RemoveAt(0);
                    _db.Touch(key);
                }
                else
                {
                    waiter = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _db.Waiters.GetOrAdd(key, _ => new Queue<TaskCompletionSource<string>>()).Enqueue(waiter);
                }
            }

            if (waiter != null)
            {
                await Task.WhenAny(waiter.Task, Task.Delay(timeout));

                if (!waiter.TrySetCanceled())
                {
                    value = await waiter.Task;
                }
            }

            reply = value == null
                ? "*-1\r\n"
                : $"*2\r\n{BulkString(key)}{BulkString(value)}";
        }
        else if (command == "LRANGE")
        {
            string key = parts[4];
            int start = int.Parse(parts[6]);
            int stop = int.Parse(parts[8]);

            List<string> items = new();

            if (_db.Lists.TryGetValue(key, out List<string>? list))
            {
                lock (list)
                {
                    if (start < 0)
                    {
                        start += list.Count;
                        if (start < 0)
                        {
                            start = 0;
                        }
                    }

                    if (stop < 0)
                    {
                        stop += list.Count;
                    }

                    if (stop >= list.Count)
                    {
                        stop = list.Count - 1;
                    }

                    for (int i = start; i <= stop; i++)
                    {
                        items.Add(list[i]);
                    }
                }
            }

            reply = $"*{items.Count}\r\n";

            foreach (string item in items)
            {
                reply += BulkString(item);
            }
        }
        else if (command == "XADD")
        {
            string key = parts[4];

            string[] idParts = parts[6].Split('-');

            List<(string Field, string Value)> fields = new();
            for (int i = 8; i + 2 < parts.Length; i += 4)
            {
                fields.Add((parts[i], parts[i + 2]));
            }

            long ms = long.Parse(idParts[0]);

            bool autoSeq = idParts[1] == "*";
            long seq = autoSeq ? 0 : long.Parse(idParts[1]);

            if (autoSeq || !(ms == 0 && seq == 0))
            {
                List<StreamEntry> stream = _db.Streams.GetOrAdd(key, _ => new List<StreamEntry>());

                StreamEntry? added = null;

                lock (stream)
                {
                    if (autoSeq)
                    {
                        if (stream.Count > 0 && stream[^1].Ms == ms)
                        {
                            seq = stream[^1].Seq + 1;
                        }
                        else
                        {
                            seq = ms == 0 ? 1 : 0;
                        }
                    }

                    if (stream.Count == 0 || (ms, seq).CompareTo((stream[^1].Ms, stream[^1].Seq)) > 0)
                    {
                        added = new StreamEntry(ms, seq, fields);
                        stream.Add(added);
                    }
                }

                if (added == null)
                {
                    reply = "-ERR The ID specified in XADD is equal or smaller than the target stream top item\r\n";
                }
                else
                {
                    _db.Touch(key);
                    reply = BulkString(added.Id);
                }
            }
            else
            {
                reply = "-ERR The ID specified in XADD must be greater than 0-0\r\n";
            }
        }
        else
        {
            reply = "-ERR unknown command\r\n";
        }

        return reply;
    }
}
