namespace Oire.SharpSync.Core;

/// <summary>
/// Represents the current state of the sync engine
/// </summary>
public enum SyncEngineState {
    /// <summary>
    /// The engine is idle and not performing any sync operation
    /// </summary>
    Idle,

    /// <summary>
    /// The engine is actively synchronizing files
    /// </summary>
    Running,

    /// <summary>
    /// The engine is paused and waiting to be resumed
    /// </summary>
    Paused
}
