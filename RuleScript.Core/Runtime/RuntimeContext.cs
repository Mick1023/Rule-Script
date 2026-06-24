using RuleScript.Core.Diagnostics;
using System.Collections.ObjectModel;

namespace RuleScript.Core.Runtime;

public sealed class RuntimeContext
{
    private readonly Dictionary<string, RuntimeValue> _variables = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, RuntimeValue> Variables =>
        new ReadOnlyDictionary<string, RuntimeValue>(new Dictionary<string, RuntimeValue>(_variables, StringComparer.Ordinal));

    public void Set(string name, object? value)
    {
        ValidateName(name);
        _variables[name] = RuntimeValue.FromObject(value);
    }

    public object? Get(string name)
    {
        return GetValue(name).Value;
    }

    public bool Contains(string name)
    {
        ValidateName(name);
        return _variables.ContainsKey(name);
    }

    public T? Get<T>(string name)
    {
        return GetValue(name).As<T>();
    }

    public bool TryGet(string name, out object? value)
    {
        ValidateName(name);

        if (_variables.TryGetValue(name, out var runtimeValue))
        {
            value = runtimeValue.Value;
            return true;
        }

        value = null;
        return false;
    }

    public object? GetOrDefault(string name, object? defaultValue = null)
    {
        return TryGet(name, out var value) ? value : defaultValue;
    }

    public T? GetOrDefault<T>(string name, T? defaultValue = default)
    {
        return TryGet(name, out var value) && value is T typedValue ? typedValue : defaultValue;
    }

    public RuntimeValue GetValue(string name)
    {
        ValidateName(name);

        if (_variables.TryGetValue(name, out var value))
        {
            return value;
        }

        throw new RuntimeException($"Variable '{name}' is not defined.");
    }

    public void SetValue(string name, RuntimeValue value)
    {
        ValidateName(name);
        _variables[name] = value ?? RuntimeValue.Null;
    }

    public void Clear()
    {
        _variables.Clear();
    }

    public bool Remove(string name)
    {
        ValidateName(name);
        return _variables.Remove(name);
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Variable name cannot be empty.", nameof(name));
        }
    }
}
