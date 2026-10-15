# Contributing

REA's current implementation is C# on .NET 10. The root README is the current
feature contract; historical releases and restored historical documentation do not establish
support. Preserve the C# application/orchestration policy in AGENTS.md and its
explicit exception for restored engine-required sources under `bridge/`.

## Development

Use `global.json` and the build, test, and packaging commands in README.md.
Run relevant regression tests after code changes and package verification after
packaging changes. CI builds and tests on Windows, Linux, and macOS; a local
Windows pass alone does not establish other-platform behavior. Keep generated
assemblies and packages out of Git. See [testing](docs/testing.md) and
[architecture](docs/architecture.mermaid).

## Contribution scope

REA focuses on authorized software inspection and evidence. Format inspection,
disassembly/decompilation, ordinary extraction, general static analysis,
vulnerability analysis and evidence reporting can be in scope. Assess actual
behavior, defaults, side effects and full workflows, not tool names.

Target execution, attaching, instrumentation, scripts, and memory/register
mutation need explicit target selection, authority, provenance, resource limits,
cancellation and cleanup boundaries. This C# checkout currently implements static
file reads only. Engine-side JavaScript is excluded from this implementation.

Do not integrate dedicated commercial-protection recovery, anti-debugging or
anti-instrumentation evasion, DRM/license/anti-cheat/access-control bypass,
exploit or attack-payload automation, credential theft, covert persistence,
unauthorized access or bulk attack workflows. Optional dependencies and examples
do not exempt capabilities from these boundaries.

### Fail-closed integration

Do not merge, enable, or officially distribute a capability while material scope,
authority, side-effect or required verification questions remain unresolved.
Record unknowns rather than assuming safety. For new providers or capabilities
involving target execution, injection, mutation or protection recovery, obtain
an explicit maintainer scope decision in a linked issue/design discussion before
substantial implementation for integration. Small prototypes and draft PRs can
help resolve questions but do not authorize integration. Scope expansions need
a new decision. Ordinary fixes within established boundaries do not.

## Reporting issues

Describe the expected and actual outcome, smallest useful input or reproduction,
version and OS, and observed evidence. Distinguish execution from source-derived
reasoning and hypotheses. Use `NOT_ENOUGH_INFO` with an explanation where narrative
answers or evidence are unavailable. Incomplete reports and early proposals are
welcome; do not claim tests or runtime reproductions that did not occur. Report
sensitive vulnerabilities privately as described in SECURITY.md.

## Pull request ownership and readiness

Use Conventional Commit titles. Explain concrete behavior changes, compatibility,
validation that actually ran, and unavailable coverage. Mark breaking changes.
Address actionable automated findings or explain disagreements.

AI-assisted contributions require a human owner to review the full diff, understand
the behavior and boundaries, check validation and its limits, and explicitly
declare initial readiness for maintainer review. Leave the Author readiness
checkboxes for that human to complete and keep the PR in draft until then. Later
pushes must keep the diff and validation current; they do not require another
initial-readiness declaration. Automated checks do not establish human review,
scope approval or permission to merge.
