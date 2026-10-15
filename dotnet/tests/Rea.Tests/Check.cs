using Rea.Domain;

namespace Rea.Tests;

internal sealed record Case(string Name, Func<Task> Run)
{
    public static Case Sync(string name, Action test) => new(name, () => { test(); return Task.CompletedTask; });
}

internal static class Check
{
    public static void True(bool condition, string message = "Expected true")
    { if (!condition) throw new InvalidOperationException(message); }
    public static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}"); }
    public static T Success<T, E>(Result<T, E> result) => result is Result<T, E>.Success success
        ? success.Value : throw new InvalidOperationException($"Expected success: {result}");
    public static E Failure<T, E>(Result<T, E> result) => result is Result<T, E>.Failure failure
        ? failure.Error : throw new InvalidOperationException($"Expected failure: {result}");
}
