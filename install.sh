#!/bin/sh
{
# Compatibility launcher only; all installation behavior lives in C#.
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec dotnet run --project "$root/dotnet/tools/Rea.Installer/Rea.Installer.csproj" --configuration Release -- "$@"
}
