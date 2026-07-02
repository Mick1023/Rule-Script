using RuleScript.Core.Diagnostics;
using System.Collections.ObjectModel;

namespace RuleScript.Core.Runtime;

/// <summary>
/// Stores variables shared between the host application and RuleScript execution.
/// </summary>
public sealed class RuntimeContext
{
    private readonly Dictionary<string, RuntimeValue> _variables = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private RuleScriptSourceLocation? _currentLocation;

    /// <summary>
    /// Gets a read-only snapshot of current variables.
    /// </summary>
    public IReadOnlyDictionary<string, RuntimeValue> Variables
    {
        get
        {
            lock (_sync)
            {
                return new ReadOnlyDictionary<string, RuntimeValue>(new Dictionary<string, RuntimeValue>(_variables, StringComparer.Ordinal));
            }
        }
    }

    /// <summary>
    /// Gets a read-only snapshot of current variable names.
    /// </summary>
    public IReadOnlyList<string> VariableNames
    {
        get
        {
            lock (_sync)
            {
                return _variables.Keys.Order(StringComparer.Ordinal).ToArray();
            }
        }
    }

    /// <summary>
    /// Gets the last source location reported during execution.
    /// </summary>
    public RuleScriptSourceLocation? CurrentLocation
    {
        get { lock (_sync) { return _currentLocation; } }
        internal set { lock (_sync) { _currentLocation = value; } }
    }

    /// <summary>
    /// Sets a variable value.
    /// </summary>
    public void Set(string name, object? value)
    {
        ValidateName(name);
        lock (_sync) { _variables[name] = RuntimeValue.FromObject(value); }
    }

    /// <summary>
    /// Gets a variable value or throws when the variable is missing.
    /// </summary>
    public object? Get(string name)
    {
        return GetValue(name).Value;
    }

    /// <summary>
    /// Returns whether the variable exists.
    /// </summary>
    public bool Contains(string name)
    {
        ValidateName(name);
        lock (_sync) { return _variables.ContainsKey(name); }
    }

    /// <summary>
    /// Gets a typed variable value or throws when the variable is missing.
    /// </summary>
    public T? Get<T>(string name)
    {
        return GetValue(name).As<T>();
    }

    /// <summary>
    /// Tries to get a variable value.
    /// </summary>
    public bool TryGet(string name, out object? value)
    {
        ValidateName(name);

        lock (_sync)
        {
            if (_variables.TryGetValue(name, out var runtimeValue))
            {
                value = runtimeValue.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Gets a variable value or a default value when the variable is missing.
    /// </summary>
    public object? GetOrDefault(string name, object? defaultValue = null)
    {
        return TryGet(name, out var value) ? value : defaultValue;
    }

    /// <summary>
    /// Gets a typed variable value or a default value when missing or type mismatched.
    /// </summary>
    public T? GetOrDefault<T>(string name, T? defaultValue = default)
    {
        return TryGet(name, out var value) && value is T typedValue ? typedValue : defaultValue;
    }

    /// <summary>
    /// Gets the raw runtime value for a variable.
    /// </summary>
    public RuntimeValue GetValue(string name)
    {
        ValidateName(name);

        lock (_sync)
        {
            if (_variables.TryGetValue(name, out var value))
            {
                return value;
            }
        }

        throw new RuntimeException($"Variable '{name}' is not defined.");
    }

    /// <summary>
    /// Sets a raw runtime value.
    /// </summary>
    public void SetValue(string name, RuntimeValue value)
    {
        ValidateName(name);
        lock (_sync) { _variables[name] = value ?? RuntimeValue.Null; }
    }

    /// <summary>
    /// Removes all variables.
    /// </summary>
    public void Clear()
    {
        lock (_sync) { _variables.Clear(); }
    }

    /// <summary>
    /// Removes a variable.
    /// </summary>
    public bool Remove(string name)
    {
        ValidateName(name);
        lock (_sync) { return _variables.Remove(name); }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Variable name cannot be empty.", nameof(name));
        }
    }
}
