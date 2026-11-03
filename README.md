[![progress-banner](https://backend.codecrafters.io/progress/redis/98fed42d-e2e5-4ef3-a4c3-16e3b31a64ab)](https://app.codecrafters.io/users/YoussefGemy?r=2qF)

# redis-csharp

A Redis-compatible server written from scratch in C# / .NET — no Redis libraries, just raw TCP sockets and the [RESP protocol](https://redis.io/docs/latest/develop/reference/protocol-spec/).

Built while working through the [CodeCrafters "Build Your Own Redis"](https://codecrafters.io/challenges/redis) challenge.

> **Status: in progress.** The server handles strings and lists today; streams, transactions and replication are next.

## What works

| Area | Commands | Notes |
|---|---|---|
| Basics | `PING`, `ECHO` | |
| Strings | `SET`, `GET` | `SET` supports `PX` / `EX` expiry; expired keys are removed lazily on read |
| Lists | `RPUSH`, `LPUSH`, `LPOP`, `LLEN`, `LRANGE` | `LPOP` with a count, `LRANGE` with negative indexes |
| Blocking | `BLPOP` | Blocked clients are woken in FIFO order when an item is pushed |

## How it works

- **Concurrency:** each client connection runs on its own async task, so many clients can be served at once without a thread per client.
- **Storage:** keys live in `ConcurrentDictionary` instances; each list is guarded by its own lock so pushes and pops from different clients don't race.
- **Blocking pops:** `BLPOP` registers a `TaskCompletionSource` in a per-key queue and awaits it. `RPUSH`/`LPUSH` hand new items directly to waiting clients, longest waiter first.
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
```

## Roadmap

- [ ] `BLPOP` timeout (currently waits indefinitely)
- [ ] Robust RESP parsing (multiple commands per packet, requests larger than the read buffer)
- [ ] `TYPE`, streams (`XADD`, `XRANGE`, `XREAD`)
- [ ] Transactions (`MULTI`, `EXEC`, `DISCARD`)
- [ ] RDB persistence and leader/follower replication
