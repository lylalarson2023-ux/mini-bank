namespace ADN_pay.Shared.Domain;

public sealed class Result<T>
{
    public bool IsSuccess { get; private init; }
    public T? Value { get; private init; }
    public string? ErrorCode { get; private init; }
    public string? ErrorMessage { get; private init; }

    private Result() { }

    public static Result<T> Success(T value) =>
        new() { IsSuccess = true, Value = value };

    public static Result<T> Failure(string code, string message) =>
        new() { IsSuccess = false, ErrorCode = code, ErrorMessage = message };

    public Result<TOut> Map<TOut>(Func<T, TOut> mapper) =>
        IsSuccess
            ? Result<TOut>.Success(mapper(Value!))
            : Result<TOut>.Failure(ErrorCode!, ErrorMessage!);
}

public static class Result
{
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
    public static Result<T> Failure<T>(string code, string message) => Result<T>.Failure(code, message);
}
