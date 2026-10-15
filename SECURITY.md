# Security

The application is a C# static file inspection tool. It does not implement the
legacy MCP server, active engine adapters, debugger or instrumentation workflows.
Restored bridge sources are separate and are not enabled or executed by the C# app. Old
claims about authenticated bridge transports do not apply to this checkout.

Inspection opens the selected local file for reading. Managed inspection uses
metadata parsing, not `Assembly.Load`, and never invokes target code. Treat
parsers and input files as untrusted boundaries. File sharing and snapshot
digests do not establish an atomic cross-platform filesystem snapshot or sandbox.

Report vulnerabilities privately through the repository's Security tab. Include
the commit/package version, OS, selected command, impact, and smallest useful
reproduction. Do not post credentials, proprietary binaries, private provider
projects or unrelated local information publicly.

The local preview package has not been published by this cutover. Historical
release support and verification do not establish verification of this rewrite.

Restored PR readiness automation checks metadata using trusted default-branch C#
code, scoped API credentials and no PR-head execution. Release qualification is
not publication authority. Unported provider and publication lanes fail explicitly.
