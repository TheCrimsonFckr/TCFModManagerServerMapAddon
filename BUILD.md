# Building

## What you need

| | why |
|---|---|
| .NET 9 SDK | the contract, the payload, and the 4.0.13 stub |
| .NET 10 SDK | the 4.1.x stub only — skip it if you are not building that one |
| an SPT 4.1.x install | reference assemblies for `ServerMap.Server.Stub` |
| an SPT 4.0.13 install | reference assemblies for `ServerMap.Server.Stub.Spt40` |

You do not need both SPT installs unless you are building both stubs. Each stub errors out at the
start of its own build naming the property it wants, rather than failing later with a missing
assembly.

## Pointing at your SPT installs

Copy `build/Directory.Build.local.props.example` to `build/Directory.Build.local.props` and edit the
two paths. That copy is gitignored — machine layout is never committed.

The two properties are deliberately separate. Building both stubs on one machine means having both
installs on hand, and pointing one variable at each in turn is exactly how a 4.1 assembly ends up
referenced by the 4.0 build without anyone noticing until runtime.

Environment variables (`SPT_SERVER_DIR`, `SPT_SERVER_40_DIR`) and `-p:` on the command line both
work instead, if you would rather not keep a file.

## Building

```
dotnet build src/ServerMap.Server/ServerMap.Server.csproj -c Release

dotnet build src/ServerMap.Server.Stub/ServerMap.Server.Stub.csproj -c Release ^
  -p:SptServerDir="G:\Single Player Tarkov 4.1\SPT_Runtime"

dotnet build src/ServerMap.Server.Stub.Spt40/ServerMap.Server.Stub.Spt40.csproj -c Release ^
  -p:Spt40ServerDir="D:\Single Player Tarkov Server\SPT"
```

Building the solution builds all four, which needs both SPT installs and both SDKs present.

## What comes out

```
src/ServerMap.Server/bin/Release/payload/                    the payload, both SPT lines
src/ServerMap.Server.Stub/bin/Release/TCFMM.ServerMap/       the 4.1.x mod folder
src/ServerMap.Server.Stub.Spt40/bin/Release/TCFMM.ServerMap/ the 4.0.13 mod folder
```

Both stubs produce a folder of the same name holding the same assembly name, because only one is
ever installed on a given server.

A release is one of the stub folders plus the payload folder, laid out as the deploy section of
[README.md](README.md) describes.

## Versioning

Each project carries its own `<Version>` in its `.csproj`; there is no repo-wide one, and
`build/Directory.Build.props` deliberately does not set it. The version an operator sees is the
payload's, reported by `/tcfservermap/hello`.
