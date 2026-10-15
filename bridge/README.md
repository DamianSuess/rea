# Restored engine bridge sources

This folder is restored from `ef250b14442ad99458ef7ba1e59b78c809238675`.
The engine-required Python, Java and Swift sources are the user's explicit
exception to the C# core/orchestration policy. `source-manifest.json` records the
32 original source files and their SHA-256 values. Repository tests check their
presence and provenance. Application packages do not bundle these sources.

These are **not enabled C# integrations**. Ghidra/JADX Java code, Hopper/Cutter/
debugger Python code and platform Swift helpers require their respective engines,
SDKs or original external dependencies. NativeAOT extensions also need their
original external extension dependencies. Restoring source does not install
engines, establish authorization, compile bridge projects, verify protocols or
enable execution/mutation workflows.

No bridge or target was executed during restoration. Provider-specific workflow
filenames remain available as explicit unavailable gates until a C# adapter and
matching real-engine verification exist. Future integrations must follow
CONTRIBUTING.md, including authority, ownership, lifecycle and verification gates.

See [current C# workflow status](../docs/dotnet-workflows.md). Historical protocol
and provider guides are restored under `docs/` and `.agents/` with status banners.
