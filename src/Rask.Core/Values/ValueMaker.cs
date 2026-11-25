using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Rask.Values;

/// <summary>
///     A value object's generated <c>TryCreate</c> over the one value it holds, as a form's model asks it when that value
///     is typed into a field bound to the value object itself. Named by generated code; not an API to write against.
/// </summary>
/// <typeparam name="TValue">The value object.</typeparam>
/// <typeparam name="THeld">The value it holds.</typeparam>
/// <param name="value">The value as typed.</param>
/// <param name="result">The value object when its rules accept it; otherwise null.</param>
/// <param name="problems">What its rules say against it; empty when they accept it.</param>
/// <returns>Whether its rules accept it.</returns>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate bool ValueMaker<TValue, in THeld>(THeld value, [NotNullWhen(true)] out TValue? result, out IReadOnlyList<Problem> problems)
    where TValue : Value;
