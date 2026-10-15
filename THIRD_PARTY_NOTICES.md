# Third-party notices

The current application uses only the .NET 10 base class library, including
System.Reflection.Metadata. Its project files declare no third-party NuGet
package dependencies. Framework-dependent local tool packages require the
user's installed .NET runtime and do not bundle a runtime or external engines.

Former npm dependencies, native binaries and third-party submodule checkouts are
not distributed by the application package. Original engine bridge source is
restored separately under `bridge/` as an explicit language exception; original
copyright notices remain in those files, and external dependencies are not
bundled. Prior dependency notices remain in the corresponding Git history.
See LICENSE for the REA source license. Installed SDK/runtime licensing remains
with the SDK/runtime distribution.
