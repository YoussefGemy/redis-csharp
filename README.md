[![progress-banner](https://backend.codecrafters.io/progress/redis/98fed42d-e2e5-4ef3-a4c3-16e3b31a64ab)](https://app.codecrafters.io/users/YoussefGemy?r=2qF)

# redis-csharp

A Redis-compatible server written from scratch in C# / .NET — no Redis libraries, just raw TCP sockets and the [RESP protocol](https://redis.io/docs/latest/develop/reference/protocol-spec/).

Built while working through the [CodeCrafters "Build Your Own Redis"](https://codecrafters.io/challenges/redis) challenge.

> **Status: in progress.** Strings, lists (including blocking pops), transactions and optimistic locking (`WATCH` / `UNWATCH`) work today. Streams are in progress (`XADD`, `XRANGE` and `XREAD` across one or more streams work; blocking `XREAD` is next), then replication.

## What works

| Area | Commands | Notes |
|---|---|---|
| Basics | `PING`, `ECHO`, `TYPE` | `TYPE` reports `string`, `list`, `stream` or `none` |
| Strings | `SET`, `GET`, `INCR` | `SET` supports `PX` / `EX` expiry; expired keys are removed lazily on read. `INCR` creates missing keys and rejects non-integer values |
| Lists | `RPUSH`, `LPUSH`, `LPOP`, `LLEN`, `LRANGE` | `LPOP` with a count, `LRANGE` with negative indexes |
| Blocking | `BLPOP` | Optional timeout (fractional seconds, `0` = wait forever). Blocked clients are woken in FIFO order when an item is pushed |
| Transactions | `MULTI`, `EXEC`, `DISCARD` | Commands are queued per connection; a failing command returns its error inside the `EXEC` array while the rest still run |
| Optimistic locking | `WATCH`, `UNWATCH` | Watch one or more keys, including keys that don't exist yet; `EXEC` returns a null array and runs nothing if any of them was modified since `WATCH`. Watches are cleared after `EXEC`, `DISCARD` or `UNWATCH`. `WATCH` is not allowed inside `MULTI` |
| Streams | `XADD`, `XRANGE`, `XREAD` | `XADD` takes explicit IDs (`1526919030474-0`), an auto-generated sequence number (`1526919030474-*`) or a fully auto-generated ID (`*`); IDs must be strictly increasing and greater than `0-0`. `XRANGE` returns entries in an inclusive range, with `-` / `+` for the start / end of the stream. `XREAD STREAMS` reads one or more streams at once, returning each stream's entries strictly after its given ID, in the order the keys were listed; streams with nothing new are left out |

## How it works

- **Concurrency:** each client connection runs on its own async task, so many clients can be served at once without a thread per client.
- **Storage:** keys live in `ConcurrentDictionary` instances; each list is guarded by its own lock so pushes and pops from different clients don't race.
- **Blocking pops:** `BLPOP` registers a `TaskCompletionSource` in a per-key queue and races it against a timeout. `RPUSH`/`LPUSH` hand new items directly to waiting clients, longest waiter first. `TrySetResult` / `TrySetCanceled` decide who wins when a push and a timeout happen at the same moment, so a timed-out client never swallows an item.
- **Transactions:** each connection keeps its own "in a transaction" flag and command queue. All normal commands go through `CommandExecutor`, so a command runs the same way whether it arrives normally or is replayed by `EXEC`.
- **Optimistic locking:** every write bumps a per-key version number (like a `RowVersion` column). `WATCH` records the version each key had (a key that was never written counts as version 0, so creating it counts as a change), and `EXEC` aborts if any of them has moved on.
- **Streams:** each stream is an append-only list of entries. Entry IDs are stored as two numbers (milliseconds, sequence) and compared as a tuple, so `10-0` correctly sorts after `9-0`. Generating a sequence number and validating the ID happen under the same lock as the append, so concurrent `XADD`s can't produce out-of-order IDs. `XRANGE` and `XREAD` share one ID parser; when the sequence number is left out it defaults to the lowest or highest value, depending on which end of the range it's on.
- **Protocol:** requests are parsed as RESP arrays of bulk strings, and replies are encoded as simple strings, integers, bulk strings, arrays or errors.

## Project structure

```
src/
  Program.cs           starts the TCP listener and creates a ClientConnection per client
  ClientConnection.cs  one connected client: the read/reply loop and per-client state (transaction queue, watched keys)
  CommandExecutor.cs   runs normal commands (SET, GET, INCR, RPUSH, BLPOP, XADD, ...) against the shared data
  DataStore.cs         the shared data: strings, lists, streams, blocked BLPOP clients and key versions
  StreamEntry.cs       one stream entry: its ID (milliseconds + sequence) and field-value pairs
  Resp.cs              RESP encoding helpers
```

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
redis-cli XADD sensor 1526919030474-0 temperature 36 humidity 95
redis-cli XADD sensor '*' temperature 37
redis-cli XRANGE sensor - +
redis-cli XREAD STREAMS sensor 1526919030474-0
redis-cli XADD weather 1-1 wind 12
redis-cli XREAD STREAMS sensor weather 0-0 0-0
redis-cli TYPE sensor
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

Optimistic locking with two clients:

```
client A> WATCH counter
OK
client A> MULTI
OK
client A> INCR counter
QUEUED
                          client B> SET counter 100
                          OK
client A> EXEC
(nil)          <- aborted, because counter changed after WATCH
```

## Roadmap

- [x] `BLPOP` timeout
- [x] Transactions (`MULTI`, `EXEC`, `DISCARD`)
- [x] Optimistic locking (`WATCH`, `UNWATCH`)
- [ ] Robust RESP parsing (multiple commands per packet, requests larger than the read buffer)
- [x] `TYPE`
- [ ] Streams: `XADD`, `XRANGE` and multi-stream `XREAD` done; `XREAD BLOCK` and `$` next
- [ ] Pub/Sub, sorted sets
- [ ] RDB / AOF persistence and leader/follower replication
