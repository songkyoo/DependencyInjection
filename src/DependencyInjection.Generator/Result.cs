namespace Macaron.DependencyInjection.Generator;

internal static class Result
{
    public static OkResult Ok()
    {
        return new OkResult();
    }

    public static OkResult<T> Ok<T>(T value)
        where T : notnull
    {
        return new OkResult<T>(value);
    }

    public static Result<T, TError> Ok<T, TError>(T value)
        where T : notnull
        where TError : notnull
    {
        return new OkResult<T, TError>(value);
    }

    public static ErrorResult<TError> Error<TError>(TError error)
        where TError : notnull
    {
        return new ErrorResult<TError>(error);
    }

    public static Result<T, TError> Error<T, TError>(TError error)
        where T : notnull
        where TError : notnull
    {
        return new ErrorResult<T, TError>(error);
    }
}

internal readonly struct Result<TError> : IEquatable<Result<TError>>
    where TError : notnull
{
    #region Implicit Conversion Operators
    public static implicit operator Result<TError>(OkResult result)
    {
        return new Result<TError>(hasError: false, error: default);
    }

    public static implicit operator Result<TError>(ErrorResult<TError> result)
    {
        return new Result<TError>(hasError: true, error: result.Error);
    }
    #endregion

    #region Fields
    private readonly int _index;
    private readonly TError? _error;
    #endregion

    #region Properties
    public bool IsOk => _index switch
    {
        0 => throw new InvalidOperationException("Result is not initialized."),
        1 => true,
        2 => false,
        _ => throw new InvalidOperationException($"Invalid result state: {_index}"),
    };

    public bool IsError => !IsOk;

    public TError Error => !IsOk ? _error! : throw new InvalidOperationException("Cannot access the error of a successful result.");
    #endregion

    #region Constructors
    private Result(bool hasError, TError? error)
    {
        _index = hasError ? 2 : 1;
        _error = error;
    }
    #endregion

    #region Overrides
    public override bool Equals(object? obj)
    {
        return obj is Result<TError> result && Equals(result);
    }

    public override int GetHashCode()
    {
        return _index switch
        {
            0 => 0,
            1 => 1,
            2 => (_index, _error).GetHashCode(),
            _ => throw new InvalidOperationException($"Invalid result state: {_index}")
        };
    }

    public override string ToString()
    {
        return _index switch
        {
            0 => "<Uninitialized>",
            1 => "Ok",
            2 => $"Error({_error})",
            _ => throw new InvalidOperationException($"Invalid result state: {_index}")
        };
    }
    #endregion

    #region IEquatable<Result<TError>> Interface
    public bool Equals(Result<TError> other)
    {
        if (_index != other._index)
        {
            return false;
        }

        return _index switch
        {
            0 => true,
            1 => true,
            2 => EqualityComparer<TError?>.Default.Equals(_error, other._error),
            _ => throw new InvalidOperationException($"Invalid result state: {_index}"),
        };
    }
    #endregion

    #region Methods
    public Result<TOther, TError> Map<TOther>(Func<TOther> func)
        where TOther : notnull
    {
        return IsOk switch
        {
            true => Result.Ok(func()),
            false => Result.Error<TOther, TError>(_error!),
        };
    }

    public Result<TOther, TError> FlatMap<TOther>(Func<Result<TOther, TError>> func)
        where TOther : notnull
    {
        return IsOk switch
        {
            true => func(),
            false => Result.Error<TOther, TError>(_error!),
        };
    }

    public Result<TOtherError> MapError<TOtherError>(Func<TError, TOtherError> func)
        where TOtherError : notnull
    {
        return IsOk switch
        {
            true => Result.Ok(),
            false => Result.Error(func(_error!)),
        };
    }

    public Result<TOtherError> FlatMapError<TOtherError>(Func<TError, Result<TOtherError>> func)
        where TOtherError : notnull
    {
        return IsOk switch
        {
            true => Result.Ok(),
            false => func(_error!),
        };
    }
    #endregion
}

internal readonly struct Result<T, TError> : IEquatable<Result<T, TError>>
    where T : notnull
    where TError : notnull
{
    #region Implicit Conversion Operators
    public static implicit operator Result<T, TError>(OkResult<T> result)
    {
        return new Result<T, TError>(hasValue: true, value: result.Value, error: default);
    }

    public static implicit operator Result<T, TError>(OkResult<T, TError> result)
    {
        return new Result<T, TError>(hasValue: true, value: result.Value, error: default);
    }

    public static implicit operator Result<T, TError>(ErrorResult<TError> result)
    {
        return new Result<T, TError>(hasValue: false, value: default, error: result.Error);
    }

    public static implicit operator Result<T, TError>(ErrorResult<T, TError> result)
    {
        return new Result<T, TError>(hasValue: false, value: default, error: result.Error);
    }

    public static implicit operator Result<TError>(Result<T, TError> result)
    {
        return result.IsOk ? Result.Ok() : Result.Error(result.Error);
    }
    #endregion

    #region Fields
    private readonly int _index;
    private readonly T? _value;
    private readonly TError? _error;
    #endregion

    #region Properties
    public bool IsOk => _index switch
    {
        0 => throw new InvalidOperationException("Result is not initialized."),
        1 => true,
        2 => false,
        _ => throw new InvalidOperationException($"Invalid result state: {_index}"),
    };

    public bool IsError => !IsOk;

    public T Value => IsOk ? _value! : throw new InvalidOperationException("Cannot access the value of a failed result.");

    public TError Error => !IsOk ? _error! : throw new InvalidOperationException("Cannot access the error of a successful result.");
    #endregion

    #region Constructors
    private Result(bool hasValue, T? value, TError? error)
    {
        _index = hasValue ? 1 : 2;
        _value = value;
        _error = error;
    }
    #endregion

    #region Overrides
    public override bool Equals(object? obj)
    {
        return obj is Result<T, TError> result && Equals(result);
    }

    public override int GetHashCode()
    {
        return _index switch
        {
            0 => 0,
            1 => (_index, _value).GetHashCode(),
            2 => (_index, _error).GetHashCode(),
            _ => throw new InvalidOperationException($"Invalid result state: {_index}")
        };
    }

    public override string ToString()
    {
        return _index switch
        {
            0 => "<Uninitialized>",
            1 => $"Ok({_value})",
            2 => $"Error({_error})",
            _ => throw new InvalidOperationException($"Invalid result state: {_index}")
        };
    }
    #endregion

    #region IEquatable<Result<T, TError>> Interface
    public bool Equals(Result<T, TError> other)
    {
        if (_index != other._index)
        {
            return false;
        }

        return _index switch
        {
            0 => true,
            1 => EqualityComparer<T?>.Default.Equals(_value, other._value),
            2 => EqualityComparer<TError?>.Default.Equals(_error, other._error),
            _ => throw new InvalidOperationException($"Invalid result state: {_index}"),
        };
    }
    #endregion

    #region Methods
    public T GetOrElse(T value)
    {
        return IsOk ? _value! : value;
    }

    public Result<T, TError> OrElse(T value)
    {
        return IsOk switch
        {
            true => this,
            false => Result.Ok(value),
        };
    }

    public Result<TOther, TError> Map<TOther>(Func<T, TOther> func)
        where TOther : notnull
    {
        return IsOk switch
        {
            true => Result.Ok(func(_value!)),
            false => Result.Error<TOther, TError>(_error!),
        };
    }

    public Result<TOther, TError> FlatMap<TOther>(Func<T, Result<TOther, TError>> func)
        where TOther : notnull
    {
        return IsOk switch
        {
            true => func(_value!),
            false => Result.Error<TOther, TError>(_error!),
        };
    }

    public Result<T, TOtherError> MapError<TOtherError>(Func<TError, TOtherError> func)
        where TOtherError : notnull
    {
        return IsOk switch
        {
            true => Result.Ok<T, TOtherError>(_value!),
            false => Result.Error(func(_error!)),
        };
    }

    public Result<T, TOtherError> FlatMapError<TOtherError>(Func<TError, Result<T, TOtherError>> func)
        where TOtherError : notnull
    {
        return IsOk switch
        {
            true => Result.Ok<T, TOtherError>(_value!),
            false => func(_error!),
        };
    }
    #endregion
}

internal readonly struct OkResult
{
}

internal readonly struct OkResult<T>(T value)
{
    public readonly T Value = value;
}

internal readonly struct OkResult<T, TError>(T value)
{
    public readonly T Value = value;
}

internal readonly struct ErrorResult<TError>(TError error)
{
    public readonly TError Error = error;
}

internal readonly struct ErrorResult<T, TError>(TError error)
{
    public readonly TError Error = error;
}
