namespace Rea.Domain;

/// <summary>Expected failures are data; neither branch can carry the other branch's value.</summary>
public abstract record Result<T, TError>
{
    private Result() { }

    public sealed record Success(T Value) : Result<T, TError>;
    public sealed record Failure(TError Error) : Result<T, TError>;

    public TResult Match<TResult>(Func<T, TResult> success, Func<TError, TResult> failure) =>
        this switch
        {
            Success value => success(value.Value),
            Failure error => failure(error.Error),
            _ => throw new InvalidOperationException("Unknown result variant")
        };
}
