# Install the C# preview

Bring your own .NET 10 SDK. From a source checkout on Linux or macOS:

```sh
sh ./install.sh --dry-run
sh ./install.sh
```

`install.sh` is a compatibility launcher; installation behavior lives in
`dotnet/tools/Rea.Installer/`. It requires the checkout. Piping a downloaded script
to a shell is no longer supported. No Node.js, npm, JavaScript or TypeScript is
used. `dotnet run` may generate build outputs even during a dry run; the installer
itself makes no installation changes during a dry run.

On Windows, or directly on any supported .NET platform:

```sh
dotnet run --project dotnet/tools/Rea.Installer --configuration Release -- --dry-run
dotnet run --project dotnet/tools/Rea.Installer --configuration Release -- --yes
```

The installer shows a plan and requires `yes` at its prompt, or explicit `--yes`.
It packs the current CLI and installs a .NET tool in
`~/.local/share/rea/dotnet-tool`. Select another location with `--tool-path DIRECTORY`.
The directory must be empty or new. Existing installations are preserved; automatic
upgrade/removal is unavailable. No user configuration or PATH is edited, so no
configuration backup is needed. Invoke the printed executable path or add its
directory to PATH yourself.

To install a local package instead of building the checkout:

```sh
sh ./install.sh --package ./dotnet/artifacts/packages/Rea.DotNet.Preview.0.1.0.nupkg --tool-path ./dotnet/artifacts/local-tool --yes
```

Packages are checked for REA identity, required assemblies and prohibited source
files; their SHA-256 is printed. Structural validation does not authenticate a
publisher. Use packages and checkout code you trust. Tool installation uses an
isolated local feed with remote sources cleared. Building uses the checkout's
normal .NET build configuration. On failure, staging files and any partial tool
installation are preserved for inspection; the staging path is reported.

`--no-setup` is accepted for compatibility. `--no-prompt` suppresses the approval
prompt and requires `--yes` to install. Registry `--version` selection is unavailable;
the old `REA_VERSION` variable is not consumed. There is no published C# release
channel. The removed `rea setup` command is never invoked. Provider installation,
agent registration and runtime installation remain unavailable. The current CLI
supports `inspect-header` and `inspect-managed-artifact`.
