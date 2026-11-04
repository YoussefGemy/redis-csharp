using System.Collections.Concurrent;

namespace Redis;

public class DataStore
{
    public ConcurrentDictionary<string, (string Value, DateTime? ExpiresAt)> Store { get; } = new();
    public ConcurrentDictionary<string, List<string>> Lists { get; } = new();
    public ConcurrentDictionary<string, Queue<TaskCompletionSource<string>>> Waiters { get; } = new();

    public void ServeWaiters(string key, List<string> list)
    {
        if (!Waiters.TryGetValue(key, out Queue<TaskCompletionSource<string>>? queue)) return;

        while (queue.Count > 0 && list.Count > 0)
        {
            TaskCompletionSource<string> waiter = queue.Dequeue();

            if (waiter.TrySetResult(list[0]))
            {
                list.RemoveAt(0);
            }
        }
    }
}
