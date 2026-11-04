[![progress-banner](https://backend.codecrafters.io/progress/redis/98fed42d-e2e5-4ef3-a4c3-16e3b31a64ab)](https://app.codecrafters.io/users/YoussefGemy?r=2qF)

# redis-csharp

A Redis-compatible server written from scratch in C# / .NET — no Redis libraries, just raw TCP sockets and the [RESP protocol](https://redis.io/docs/latest/develop/reference/protocol-spec/).

Built while working through the [CodeCrafters "Build Your Own Redis"](https://codecrafters.io/challenges/redis) challenge.

> **Status: in progress.** Strings, lists (including blocking pops) and transactions work today; optimistic locking, streams and replication are next.

## What works

| Area | Commands | Notes |
|---|---|---|
| Basics | `PING`, `ECHO` | |
| Strings | `SET`, `GET`, `INCR` | `SET` supports `PX` / `EX` expiry; expired keys are removed lazily on read. `INCR` creates missing keys and rejects non-integer values |
| Lists | `RPUSH`, `LPUSH`, `LPOP`, `LLEN`, `LRANGE` | `LPOP` with a count, `LRANGE` with negative indexes |
| Blocking | `BLPOP` | Optional timeout (fractional seconds, `0` = wait forever). Blocked clients are woken in FIFO order when an item is pushed |
| Transactions | `MULTI`, `EXEC`, `DISCARD` | Commands are queued per connection; a failing command returns its error inside the `EXEC` array while the rest still run |

## How it works

- **Concurrency:** each client connection runs on its own async task, so many clients can be served at once without a thread per client.
- **Storage:** keys live in `ConcurrentDictionary` instances; each list is guarded by its own lock so pushes and pops from different clients don't race.
- **Blocking pops:** `BLPOP` registers a `TaskCompletionSource` in a per-key queue and races it against a timeout. `RPUSH`/`LPUSH` hand new items directly to waiting clients, longest waiter first. `TrySetResult` / `TrySetCanceled` decide who wins when a push and a timeout happen at the same moment, so a timed-out client never swallows an item.
- **Transactions:** each connection keeps its own "in a transaction" flag and command queue. Command handling lives in a single `ExecuteAsync` function, so a command runs the same way whether it arrives normally or is replayed by `EXEC`.
- **Protocol:** requests are parsed as RESP arrays of bulk strings, and replies are encoded as simple strings, integers, bulk strings, arrays or errors.

## Run it

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet run --project codecrafters-redis.csproj
```

The server listens on port `6379`. Talk to it with the real Redis CLI:

```sh
redis-cli PING
redis-cli SET greeting hello PX 5000
redis-cli GET greeting
redis-cli RPUSH queue a b c
redis-cli LRANGE queue 0 -1
redis-cli BLPOP queue 0.5
```

A transaction, from an interactive `redis-cli` session:

```
> MULTI
OK
> SET counter 41
QUEUED
> INCR counter
QUEUED
> EXEC
1) OK
2) (integer) 42
```

## Roadmap

- [x] `BLPOP` timeout
- [x] Transactions (`MULTI`, `EXEC`, `DISCARD`)
- [ ] Optimistic locking (`WATCH`)
- [ ] Robust RESP parsing (multiple commands per packet, requests larger than the read buffer)
- [ ] `TYPE`, streams (`XADD`, `XRANGE`, `XREAD`)
- [ ] Pub/Sub, sorted sets
- [ ] RDB / AOF persistence and leader/follower replication
