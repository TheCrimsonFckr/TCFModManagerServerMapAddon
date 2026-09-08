# TCFModManager Server Map Addon

The server half of TCFModManager's Server map feature. It publishes what a server expects its
players to be running, so the app can tell someone their install is behind **before** they launch,
instead of after a raid fails to load.

It serves a list, never mod files. TCFModManager installs from The Forge and nowhere else, and a
server that could push bytes would break the one rule the app is built on. There is no file route.

This mod is an addon — it does nothing on its own. The client half is
[TCFModManager](https://github.com/TheCrimsonFckr/TCFModManager), and a machine not running that app
is invisible to this one.

## Three routes

| route | auth | what it is |
|---|---|---|
| `GET /tcfservermap/hello` | none | The handshake. Protocol, mod version, server name, whether a key is required, and the published list's name, revision and entry count. |
| `GET /tcfservermap/list` | key | The published `.tcfmodlist`, served verbatim. 404 when nothing is published. |
| `POST /tcfservermap/echo` | key | Diagnostic: reports the request body exactly as it arrived. |

`/hello` is unauthenticated on purpose — it is asked before anyone has been given a key, it discloses
nothing a port scan would not, and gating it would leave a client unable to tell "wrong address" from
"right address, no key".

## Four projects, and why

```
ServerMap.Shared            net9.0   the contract, pure BCL, zero dependencies
ServerMap.Server            net9.0   the payload - every route, all the logic
ServerMap.Server.Stub       net10.0  the SPT 4.1.x stub
ServerMap.Server.Stub.Spt40 net9.0   the SPT 4.0.13 stub
```

The **stub** is the only thing that sits where SPT's mod loader insists on looking, and it holds no
feature logic: it finds the payload and forwards to it. Delete the payload folder and the stub keeps
loading, logs one line and does nothing — which is the supported way to remove this feature.

The **payload** is one build shared by both SPT lines. It touches no SPT type and no ASP.NET type,
because the contract hands it a flattened `PayloadRequest`, so there is nothing in it for an SPT
version bump to break.

### Supporting two SPT versions

SPT changed its mod surface in five incompatible ways between 4.0.13 and 4.1.x, and not one of them
can be bridged from a single source file:

| | 4.0.13 | 4.1.x |
|---|---|---|
| runtime | .NET 9 | .NET 10 |
| metadata | `AbstractModMetadata` (abstract record), `IsBundleMod` | `IModMetadata` (interface), `HasPrepatcher` |
| `IOnLoad` | `Task OnLoad()` | `Task OnLoadAsync(CancellationToken)` |
| `CanHandle` | `(MongoId, HttpContext)` | `(HttpContext)` |
| handler | `Handle(MongoId, HttpContext)` | `HandleAsync(MongoId, HttpContext, CancellationToken)` |
| `ISptLogger<T>` | `Core.Models.Utils` | `Common.Models.Logging` |

So there are two stubs, three small files each, and they are the *entire* cost of the second version.
Both compile to `TCFMM.ServerMap.Stub.dll` in a `TCFMM.ServerMap\` folder, because only one is ever
installed — the one matching the server it is going into, enforced by each stub's `SptVersion` range
(`>=4.0.0 <4.1.0` and `>=4.1.0 <4.2.0`). SPT's own version gate refusing to load the wrong one is a
much better failure than a `MissingMethodException` halfway through startup.

The payload and the contract are `net9.0` for the same reason: a .NET 9 assembly loads on a .NET 10
host, and the reverse does not. A higher target would strand 4.0.13.

## Building

See [BUILD.md](BUILD.md). In short: each stub needs its own SPT install to reference, and they are
**separate properties** on purpose — building both on one machine means having both installs on
hand, and pointing one variable at each in turn is exactly how a 4.1 assembly ends up referenced by
the 4.0 build without anyone noticing until runtime.

| project | property | environment variable | points at |
|---|---|---|---|
| `ServerMap.Server.Stub` | `SptServerDir` | `SPT_SERVER_DIR` | the 4.1 folder holding `SPT.Server.exe` |
| `ServerMap.Server.Stub.Spt40` | `Spt40ServerDir` | `SPT_SERVER_40_DIR` | the 4.0.13 folder holding `SPT.Server.exe` |

The SPT assemblies are referenced from the install rather than NuGet: 4.1.5 is not published there
(newest package is 4.1.2), and these are the exact assemblies the server will load this mod beside.
All are `Private=false`, so none are copied into the mod folder — a duplicate there would risk a
type-identity conflict against the host's own copies.

## Deploying

Two folders, and the split is the point:

```
<SPT root>\user\mods\TCFMM.ServerMap\        <- the matching stub's bin\Release\TCFMM.ServerMap\
<SPT root>\TCFModManager\ServerMap\payload\  <- ServerMap.Server\bin\Release\payload\
<SPT root>\TCFModManager\ServerMap\config\   <- the operator's list, and the generated key
```

The stub finds the payload by walking up from its own folder looking for
`TCFModManager\ServerMap\payload`, so it does not care which SPT layout it is in. `config\` sits
beside `payload\` rather than inside it because a payload folder is something an update replaces
wholesale, and the operator's list must not be collateral.

**The payload DLL is locked while the server is running.** Stop the server before replacing it, or
the copy silently fails and you spend the next hour debugging the previous build.

## Publishing a list

The operator curates the list in TCFModManager, exports it, and drops the `.tcfmodlist` into
`config\`. `published.tcfmodlist` always wins; failing that, a folder holding exactly one
`.tcfmodlist` serves it. Several files and no preferred name is genuinely ambiguous, so nothing is
served rather than a guess.

The file is re-read whenever its size or timestamp moves, so dropping in a new export takes effect
without restarting the server.

Entries carry a **scope** — `Both`, `Client` or `Server` — set in the app. A server-scoped entry
(this mod, `fika-server`, anything living in `user\mods`) is published so the operator's own list
stays complete, but the client skips it: it is not on The Forge and sending someone to install it
would be an impossible errand.

## The shared key

Generated when the server starts, into `config\servermap-key.txt` — 24 characters in six groups of
four, from an alphabet with no lookalikes. The operator needs it *before* anyone connects, since it
is the thing they send out with the address.

Every route but `/hello` requires it in the `X-ServerMap-Key` header. TCFModManager reads the file
itself when the app is running on the same machine as the server, which is the operator's own case.

## What to look for

Start the server. In its log:

- **Loaded:** `[TCFMM ServerMap] Ready - serving /tcfservermap from <path>`
- **Payload deleted:** `[TCFMM ServerMap] No payload found, so this mod does nothing. Looked for
  TCFMM.ServerMap.Payload.dll in: <every folder it checked>` — and the server starts normally.
- **Found but unloadable:** an error naming the exception. If it complains about casting
  `IServerMapPayload` to `IServerMapPayload`, the contract got loaded twice and the payload folder
  has a copy of `TCFMM.ServerMap.Shared.dll` in it that should not be there.

Then, from another machine:

```
curl -k https://<server-ip>:6969/tcfservermap/hello
```

`-k` is required: SPT serves a self-signed certificate for `CN=localhost`, so both the chain and the
hostname fail validation. That is expected, and is what the app's trust-on-first-use thumbprint
pinning handles properly.

**Before any of this**, `SPT_Data/configs/http.json` must not be bound to loopback: `ip` to
`0.0.0.0`, `backendIp` to the machine's LAN address, and TCP 6969 allowed inbound.
