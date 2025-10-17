namespace Oire.SharpSync.Sync;

/// <summary>
/// Thread-safe counter for tracking progress
/// </summary>
internal class ProgressCounter
{
    private int _value;
    
    public int Value => _value;
    
    public int Increment() => Interlocked.Increment(ref _value);
}