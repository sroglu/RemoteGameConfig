using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PFound.RemoteGameConfig.Core
{
    /// <summary>
    /// What the game tells a backend about itself when it fetches config. A dumb JSON backend ignores
    /// all of it; a backend that does server-side targeting keys its decisions off the install id, the
    /// build, the platform, the locale, and which experiment cohorts the install is currently in.
    /// </summary>
    public sealed class ConfigFetchContext
    {
        public string InstallId { get; }
        public string AppVersion { get; }
        public string Platform { get; }
        public string CountryOrLocale { get; }

        /// <summary>Experiment key → the variant this install is currently in, so the backend can target follow-ups.</summary>
        public IReadOnlyDictionary<string, string> Cohorts { get; }

        public ConfigFetchContext(
            string installId,
            string appVersion,
            string platform,
            string countryOrLocale,
            IReadOnlyDictionary<string, string> cohorts)
        {
            InstallId = installId;
            AppVersion = appVersion;
            Platform = platform;
            CountryOrLocale = countryOrLocale;
            Cohorts = cohorts;
        }
    }

    /// <summary>How a fetch ended.</summary>
    public enum ConfigFetchStatus
    {
        /// <summary>A document was retrieved (and possibly server overrides).</summary>
        Fetched,

        /// <summary>The backend confirmed the caller's known version is still current; no new document.</summary>
        NotModified,

        /// <summary>Offline, transport error, or an unparseable document — the caller keeps its last good config.</summary>
        Failed
    }

    /// <summary>
    /// The outcome of one fetch. A failure is data, never an exception: the source resolves offline and
    /// parse faults into <see cref="ConfigFetchStatus.Failed"/> so the game keeps running on cached or
    /// embedded values. On success it carries the parsed document, any server overrides, and the version
    /// tag the next conditional fetch will present.
    /// </summary>
    public sealed class ConfigFetchResult
    {
        public ConfigFetchStatus Status { get; }
        public ConfigDocument Document { get; }
        public ServerOverrides Overrides { get; }
        public string Version { get; }
        public string FailureReason { get; }

        ConfigFetchResult(ConfigFetchStatus status, ConfigDocument document, ServerOverrides overrides, string version, string failureReason)
        {
            Status = status;
            Document = document;
            Overrides = overrides;
            Version = version;
            FailureReason = failureReason;
        }

        public static ConfigFetchResult Fetched(ConfigDocument document, ServerOverrides overrides, string version)
            => new ConfigFetchResult(ConfigFetchStatus.Fetched, document, overrides, version, null);

        public static ConfigFetchResult NotModified(string version)
            => new ConfigFetchResult(ConfigFetchStatus.NotModified, ConfigDocument.Empty, ServerOverrides.None, version, null);

        public static ConfigFetchResult Failed(string reason)
            => new ConfigFetchResult(ConfigFetchStatus.Failed, ConfigDocument.Empty, ServerOverrides.None, null, reason);
    }

    /// <summary>
    /// The pluggable backend seam: fetch the raw config document (and any server-assigned overrides) for
    /// a context. Everything above this — defaults, overlay, bucketing, typed access, caching — is
    /// backend-agnostic, so swapping backends is swapping the registered source with zero game-code
    /// change. Implementations must resolve network and parse failure into a
    /// <see cref="ConfigFetchStatus.Failed"/> result rather than throwing.
    /// </summary>
    public interface IGameConfigSource
    {
        /// <summary>
        /// Fetch config for the given context. <paramref name="knownVersion"/> is the version the caller
        /// already holds (null on first fetch); a backend that supports conditional fetch may answer
        /// <see cref="ConfigFetchStatus.NotModified"/> when it matches, so an unchanged document is not
        /// re-downloaded or re-parsed. When <paramref name="forceRefresh"/> is true the source bypasses
        /// its TTL / cache and re-hits the backend (a LiveOps event start), ignoring the not-modified
        /// short-circuit.
        /// </summary>
        Task<ConfigFetchResult> FetchAsync(ConfigFetchContext context, string knownVersion, bool forceRefresh, CancellationToken cancellationToken = default);
    }
}
