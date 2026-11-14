namespace Oire.SharpSync.Tests.Fixtures;

/// <summary>
/// Task helpers for tests that wait on a signal with a timeout.
/// </summary>
public static class TaskExtensions {
    /// <summary>
    /// Waits for <paramref name="task"/> to complete, giving up after <paramref name="timeout"/>.
    /// Unlike <c>Task.WhenAny(task, Task.Delay(timeout))</c>, the timer is released as soon as the task completes.
    /// </summary>
    /// <param name="task">The task to wait for.</param>
    /// <param name="timeout">How long to wait before giving up.</param>
    /// <returns><see langword="true"/> if the task completed within the timeout; otherwise <see langword="false"/>.</returns>
    public static async Task<bool> WaitOrTimeoutAsync(this Task task, TimeSpan timeout) {
        try {
            await task.WaitAsync(timeout).ConfigureAwait(false);
            return true;
        } catch (TimeoutException) {
            return false;
        }
    }
}
