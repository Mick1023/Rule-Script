namespace RuleScript.Core.Runtime;

/// <summary>
/// One registered host function implementation. A name may own several overloads with distinct parameter signatures.
/// </summary>
internal sealed record HostFunctionOverload(
    RuleScriptFunctionSymbol? Signature,
    Func<IReadOnlyList<object?>, object?>? Function,
    Func<IReadOnlyList<object?>, CancellationToken, Task<object?>>? AsyncFunction)
{
    public bool IsAsync => AsyncFunction is not null;

    /// <summary>
    /// Untyped and variadic registrations accept any argument list, so they cannot coexist with other overloads of the same kind.
    /// </summary>
    public bool IsVariadic => Signature is null || Signature.HostMetadata?.IsVariadic == true;

    public string? SignatureKey => IsVariadic ? null : RuleScriptFunctionSignatureComparer.GetSignatureKey(Signature!);

    /// <summary>
    /// Adds an overload to an existing set, replacing the overload it supersedes.
    /// A fixed signature replaces the same signature and any variadic registration of the same sync/async kind;
    /// a variadic registration replaces every overload of its kind.
    /// </summary>
    public static HostFunctionOverload[] Add(IReadOnlyList<HostFunctionOverload>? existing, HostFunctionOverload overload)
    {
        if (existing is null)
        {
            return [overload];
        }

        var key = overload.SignatureKey;
        return existing
            .Where(candidate => candidate.IsAsync != overload.IsAsync
                || (key is not null && !candidate.IsVariadic && candidate.SignatureKey != key))
            .Append(overload)
            .ToArray();
    }
}
