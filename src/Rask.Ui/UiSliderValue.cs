namespace Rask;

/// <summary>
///     A slider's value as the numbers it draws, and back: a number for one thumb, an array of two for a range.
/// </summary>
/// <remarks>
///     Every type is written out rather than reached through <c>INumber&lt;T&gt;</c>: one component then takes
///     both shapes, and nothing here needs reflection the trimmer cannot see.
/// </remarks>
internal static class UiSliderValue
{
    private const string Unsupported =
        "Ui.Slider binds int, long, float, double or decimal — and, as a range, an array of two of them.";

    /// <summary>Whether <typeparamref name="T" /> holds two thumbs.</summary>
    internal static bool IsPair<T>() => typeof(T).IsArray;

    internal static decimal[] Read<T>(T? value) => value switch
    {
        null => [],
        int number => [number],
        long number => [number],
        float number => [(decimal)number],
        double number => [(decimal)number],
        decimal number => [number],
        int[] pair => Array.ConvertAll(pair, number => (decimal)number),
        long[] pair => Array.ConvertAll(pair, number => (decimal)number),
        float[] pair => Array.ConvertAll(pair, number => (decimal)number),
        double[] pair => Array.ConvertAll(pair, number => (decimal)number),
        decimal[] pair => pair,
        _ => throw new NotSupportedException(Unsupported),
    };

    internal static T Write<T>(decimal[] values)
    {
        object written = default(T) switch
        {
            int => (int)values[0],
            long => (long)values[0],
            float => (float)values[0],
            double => (double)values[0],
            decimal => values[0],
            _ => WritePair<T>(values),
        };

        return (T)written;
    }

    private static object WritePair<T>(decimal[] values)
    {
        if (typeof(T) == typeof(int[]))
        {
            return Array.ConvertAll(values, number => (int)number);
        }

        if (typeof(T) == typeof(long[]))
        {
            return Array.ConvertAll(values, number => (long)number);
        }

        if (typeof(T) == typeof(float[]))
        {
            return Array.ConvertAll(values, number => (float)number);
        }

        if (typeof(T) == typeof(double[]))
        {
            return Array.ConvertAll(values, number => (double)number);
        }

        return typeof(T) == typeof(decimal[]) ? (decimal[])values.Clone() : throw new NotSupportedException(Unsupported);
    }
}
