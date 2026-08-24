using PasskeyPick.WindowOpening;
using System.Windows.Automation;

namespace PasskeyPick;

public static class Extensions {

    public static bool nameContainsAny(this AutomationElement element, IEnumerable<string> possibleSubstrings) {
        string name = element.Current.Name;
        // #2: in addition to a prefix, there is sometimes also a suffix after the substring
        return possibleSubstrings.Any(possibleSubstring => name.Contains(possibleSubstring, StringComparison.CurrentCulture));
    }

    /// <summary>Drops <see langword="null"/> items from a sequence.</summary>
    public static IEnumerable<T> Compact<T>(this IEnumerable<T?> source) where T: class =>
        source.Where(item => item is not null)!;

    /// <summary>Converts a window wrapper to its UI Automation element, or <see langword="null"/> when the handle is
    /// zero or the window is already gone.</summary>
    public static AutomationElement? ToAutomationElement(this SystemWindow window) =>
        window.HWnd == IntPtr.Zero ? null : AutomationElement.FromHandle(window.HWnd);

    /// <summary>Lists the direct children of a UI Automation element (one level, not recursive).</summary>
    public static IEnumerable<AutomationElement> Children(this AutomationElement parent) =>
        parent.FindAll(TreeScope.Children, Condition.TrueCondition).Cast<AutomationElement>();

    // The WaitForFirst* methods are minimal in-repo replacements for the Unfucked.Windows helpers this program used,
    // with the same semantics: power-series backoff starting at 8 ms capped at 500 ms between attempts, retrying while
    // the element is missing (and while the result transformer throws), and null on timeout or cancellation.

    /// <summary>Finds the first matching child/descendant, waiting for it to appear with power-series backoff.
    /// Returns <see langword="null"/> when <paramref name="maxWait"/> elapsed or <paramref name="cancellationToken"/>
    /// was canceled first; a non-positive <paramref name="maxWait"/> waits forever.</summary>
    public static AutomationElement? WaitForFirst(this AutomationElement parent, TreeScope scope, Condition condition, TimeSpan maxWait = default,
        CancellationToken cancellationToken = default) =>
        parent.WaitForFirst(scope, condition, element => element, maxWait, cancellationToken);

    /// <inheritdoc cref="WaitForFirst(AutomationElement, TreeScope, Condition, TimeSpan, CancellationToken)"/>
    /// <param name="resultTransformer">Applied to the found element to produce the return value; if it throws, the
    /// wait keeps retrying, so it may safely touch parts of the element that are not available yet.</param>
    public static TResult? WaitForFirst<TResult>(this AutomationElement parent, TreeScope scope, Condition condition, Func<AutomationElement, TResult> resultTransformer,
        TimeSpan maxWait = default, CancellationToken cancellationToken = default) where TResult: class {
        DateTime deadline = maxWait > TimeSpan.Zero ? DateTime.UtcNow + maxWait : DateTime.MaxValue;
        TimeSpan delay    = RETRY_DELAY_MIN;
        while (true) {
            if (cancellationToken.IsCancellationRequested) {
                return null;
            }
            try {
                if (parent.FindFirst(scope, condition) is { } element) {
                    return resultTransformer(element);
                }
            } catch (Exception e) when (e is not OutOfMemoryException) {
                // element not available yet (or transformer raced it); keep retrying until the deadline
            }
            if (DateTime.UtcNow >= deadline) {
                return null;
            }
            Thread.Sleep(delay);
            delay = delay < RETRY_DELAY_MAX ? delay + delay : RETRY_DELAY_MAX;
        }
    }

    /// <inheritdoc cref="WaitForFirst(AutomationElement, TreeScope, Condition, TimeSpan, CancellationToken)"/>
    public static async Task<AutomationElement?> WaitForFirstAsync(this AutomationElement parent, TreeScope scope, Condition condition, TimeSpan maxWait = default,
        CancellationToken cancellationToken = default) {
        DateTime deadline = maxWait > TimeSpan.Zero ? DateTime.UtcNow + maxWait : DateTime.MaxValue;
        TimeSpan delay    = RETRY_DELAY_MIN;
        while (true) {
            if (cancellationToken.IsCancellationRequested) {
                return null;
            }
            try {
                if (parent.FindFirst(scope, condition) is { } element) {
                    return element;
                }
            } catch (Exception e) when (e is not OutOfMemoryException) {
                // element not available yet; keep retrying until the deadline
            }
            if (DateTime.UtcNow >= deadline) {
                return null;
            }
            try {
                await Task.Delay(delay, cancellationToken);
            } catch (OperationCanceledException) {
                return null;
            }
            delay = delay < RETRY_DELAY_MAX ? delay + delay : RETRY_DELAY_MAX;
        }
    }

    /// <inheritdoc cref="WaitForFirstAsync(AutomationElement, TreeScope, Condition, TimeSpan, CancellationToken)"/>
    /// <param name="resultTransformer">Applied to the found element to produce the return value; if it throws, the
    /// wait keeps retrying, so it may safely touch parts of the element that are not available yet.</param>
    public static async Task<TResult?> WaitForFirstAsync<TResult>(this AutomationElement parent, TreeScope scope, Condition condition,
        Func<AutomationElement, Task<TResult>> resultTransformer, TimeSpan maxWait = default, CancellationToken cancellationToken = default) where TResult: class {
        DateTime deadline = maxWait > TimeSpan.Zero ? DateTime.UtcNow + maxWait : DateTime.MaxValue;
        TimeSpan delay    = RETRY_DELAY_MIN;
        while (true) {
            if (cancellationToken.IsCancellationRequested) {
                return null;
            }
            try {
                if (parent.FindFirst(scope, condition) is { } element) {
                    return await resultTransformer(element);
                }
            } catch (Exception e) when (e is not OutOfMemoryException) {
                // element not available yet (or transformer raced it); keep retrying until the deadline
            }
            if (DateTime.UtcNow >= deadline) {
                return null;
            }
            try {
                await Task.Delay(delay, cancellationToken);
            } catch (OperationCanceledException) {
                return null;
            }
            delay = delay < RETRY_DELAY_MAX ? delay + delay : RETRY_DELAY_MAX;
        }
    }

    private static readonly TimeSpan RETRY_DELAY_MIN = TimeSpan.FromMilliseconds(8);
    private static readonly TimeSpan RETRY_DELAY_MAX = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// <para>Create an <see cref="AndCondition"/> or <see cref="OrCondition"/> for a <paramref name="property"/> from a series of <paramref name="values"/>, which have fewer than 2 items in it.</para>
    /// <para>This avoids a crash in the <see cref="AndCondition"/> and <see cref="OrCondition"/> constructors if the array has size 1.</para>
    /// </summary>
    /// <param name="property">The name of the UI property to match against, such as <see cref="AutomationElement.NameProperty"/> or <see cref="AutomationElement.AutomationIdProperty"/>.</param>
    /// <param name="and"><c>true</c> to make a conjunction (AND), <c>false</c> to make a disjunction (OR)</param>
    /// <param name="values">Zero or more property values to match against.</param>
    /// <returns>A <see cref="Condition"/> that matches the values against the property, without throwing an <see cref="ArgumentException"/> if <paramref name="values"/> has length &lt; 2.</returns>
    public static Condition singletonSafeCondition(this AutomationProperty property, bool and, IEnumerable<string> values) {
        Condition[] propertyConditions = values.Select<string, Condition>(allowedValue => new PropertyCondition(property, allowedValue)).ToArray();
        return propertyConditions.Length switch {
            0 when and => Condition.TrueCondition,
            0          => Condition.FalseCondition,
            1          => propertyConditions[0],
            _ when and => new AndCondition(propertyConditions),
            _          => new OrCondition(propertyConditions)
        };
    }

}