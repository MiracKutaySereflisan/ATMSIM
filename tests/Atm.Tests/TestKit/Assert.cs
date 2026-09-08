// Assert.cs
//
// What this file does: it holds the checks a test makes. "I expected 300, what did I
// actually get?" A failed check throws, and the runner catches the throw and reports
// the test as red.
//
// Why it is this short: only the checks actually used by tests in this repository are
// implemented. An assertion nobody calls is dead code, and dead code in a testing
// helper is worse than elsewhere - it looks like coverage that does not exist. When a
// test needs a check that is missing, it gets added here, in that same commit.
//
// The signatures match xUnit's, for the reason explained in FactAttribute.cs.

namespace Xunit;

/// <summary>Thrown when a check fails. The runner treats it as a red test.</summary>
public sealed class AssertionFailedException(string message) : Exception(message);

/// <summary>The checks available to tests.</summary>
public static class Assert
{
    /// <summary>Fails unless the two values are equal.</summary>
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new AssertionFailedException(
                $"Beklenen: <{Show(expected)}>{Environment.NewLine}Gelen   : <{Show(actual)}>");
        }
    }

    /// <summary>Fails unless the condition holds. The message says what was expected.</summary>
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new AssertionFailedException(message);
        }
    }

    /// <summary>Fails unless the condition is false. The message says what was expected.</summary>
    public static void False(bool condition, string message)
    {
        if (condition)
        {
            throw new AssertionFailedException(message);
        }
    }

    /// <summary>Fails when the value is null.</summary>
    public static void NotNull(object? value)
    {
        if (value is null)
        {
            throw new AssertionFailedException("Beklenen: null olmayan bir değer. Gelen: null.");
        }
    }

    /// <summary>
    /// Runs the action and demands that it throws the given exception type. A rule
    /// that is only written in a comment is not enforced; this is how "time does not
    /// run backwards" becomes something the build can fail on.
    /// </summary>

    /// <summary>Fails unless the collection has nothing in it.</summary>
    public static void Empty<T>(IEnumerable<T> items)
    {
        var list = items.ToList();

        if (list.Count == 0)
        {
            return;
        }

        throw new AssertionFailedException(
            $"Boş olması bekleniyordu, {list.Count} öğe var:{Environment.NewLine}" +
            string.Join(Environment.NewLine, list.Select(i => "  " + i)));
    }

    /// <summary>Fails when the collection has nothing in it.</summary>
    public static void NotEmpty<T>(IEnumerable<T> items)
    {
        if (items.Any())
        {
            return;
        }

        throw new AssertionFailedException("En az bir öğe bekleniyordu, koleksiyon boş.");
    }

    /// <summary>
    /// Fails unless exactly one item matches, and returns it.
    /// </summary>
    /// <remarks>
    /// "Exactly one" rather than "at least one" on purpose: a check that a finding was
    /// reported passes just as well when it was reported three times, and three copies of
    /// the same finding is itself a defect - a report a human has to read twice.
    /// </remarks>
    public static T Single<T>(IEnumerable<T> items, Func<T, bool> matches)
    {
        var found = items.Where(matches).ToList();

        if (found.Count == 1)
        {
            return found[0];
        }

        var all = items.ToList();

        throw new AssertionFailedException(
            $"Tam olarak bir eşleşme bekleniyordu, {found.Count} bulundu. " +
            $"Koleksiyondaki {all.Count} öğe:{Environment.NewLine}" +
            string.Join(Environment.NewLine, all.Select(i => "  " + i)));
    }

    /// <summary>Fails unless at least one item matches.</summary>
    public static void Contains<T>(IEnumerable<T> items, Func<T, bool> matches)
    {
        var all = items.ToList();

        if (all.Any(matches))
        {
            return;
        }

        throw new AssertionFailedException(
            $"Eşleşen öğe yok. Koleksiyondaki {all.Count} öğe:{Environment.NewLine}" +
            string.Join(Environment.NewLine, all.Select(i => "  " + i)));
    }

    /// <summary>Fails unless the text contains the expected fragment.</summary>
    public static void Contains(string expected, string actual)
    {
        if (actual.Contains(expected, StringComparison.Ordinal))
        {
            return;
        }

        throw new AssertionFailedException(
            $"Metinde \"{expected}\" bekleniyordu. Metin:{Environment.NewLine}{actual}");
    }

    /// <summary>Fails unless the value is null.</summary>
    public static void Null(object? value)
    {
        if (value is null)
        {
            return;
        }

        throw new AssertionFailedException($"Boş (null) değer bekleniyordu, gelen: {Show(value)}");
    }

    /// <summary>Fails when the condition holds. The short form, for a plain claim.</summary>
    public static void False(bool condition) =>
        False(condition, "Koşulun yanlış olması bekleniyordu, doğru çıktı.");

    /// <summary>Fails when the condition does not hold. The short form.</summary>
    public static void True(bool condition) =>
        True(condition, "Koşulun doğru olması bekleniyordu, yanlış çıktı.");

    /// <summary>
    /// Fails when the two values are equal. The opposite of <see cref="Equal{T}"/>, and
    /// used where the claim is that something CHANGED - a date that moved, an identity
    /// that is not the old one. Written as its own assertion rather than as
    /// True(a != b) so that a failure says what the two values were.
    /// </summary>
    public static void NotEqual<T>(T notExpected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(notExpected, actual))
        {
            return;
        }

        throw new AssertionFailedException(
            $"İki değerin farklı olması bekleniyordu, ikisi de: {Show(actual)}");
    }

    public static TException Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException expected)
        {
            return expected;
        }
        catch (Exception other)
        {
            throw new AssertionFailedException(
                $"Beklenen hata: <{typeof(TException).Name}>{Environment.NewLine}" +
                $"Gelen hata   : <{other.GetType().Name}: {other.Message}>");
        }

        throw new AssertionFailedException(
            $"Beklenen hata: <{typeof(TException).Name}>. Hiç hata atılmadı.");
    }

    private static string Show<T>(T value) => value?.ToString() ?? "null";
}
