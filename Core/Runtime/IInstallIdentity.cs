namespace PFound.RemoteGameConfig.Core
{
    /// <summary>
    /// The stable per-install identity contract. Implementations return a GUID generated on first launch
    /// and persisted so it survives sessions — it is the bucketing key (same install ⇒ same experiment
    /// variant across sessions) and the analytics join key. It must never be the device's hardware id
    /// (unstable on iOS across reinstalls, and privacy-sensitive); a freshly generated, app-owned GUID is
    /// both stable for this install and disposable. The contract lives in the engine-free Core so
    /// bucketing is testable without Unity; the persisting implementation lives in the Unity layer.
    /// </summary>
    public interface IInstallIdentity
    {
        string InstallId { get; }
    }
}
