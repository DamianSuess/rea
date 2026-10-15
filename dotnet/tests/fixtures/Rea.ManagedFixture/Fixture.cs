using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Rea.ManagedFixture;

public static class Fixture
{
    // Inspecting this fixture must not load or initialize its target module.
    [ModuleInitializer]
    internal static void RejectExecution() => throw new InvalidOperationException("Static-analysis fixture must never execute");

    [DllImport("rea_missing_native_fixture", EntryPoint = "fixture_entry")]
    public static extern int NativeDeclaration(int value);

    public sealed class Nested<T>
    {
        public T? Value;
        public static string Text() => "retained fixture string";
    }
}
