using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PFound.RemoteGameConfig.Core
{
    /// <summary>
    /// The backend-agnostic orchestrator: it holds the current resolved <see cref="GameConfig"/>, drives
    /// the boot fetch and TTL/forced refreshes through an <see cref="IGameConfigSource"/>, and raises a
    /// change signal when a refresh actually changed values and forwards experiment-exposure signals.
    ///
    /// It is fully engine-free so the whole boot/refresh/offline/change-detection path is unit-testable
    /// without Unity; the Unity layer only supplies a concrete source, a persisted install identity, and
    /// a bootstrapper that calls <see cref="RefreshAsync"/>. <see cref="Current"/> is valid immediately
    /// after construction (embedded defaults), so the game always has a complete config to read — a fetch
    /// failure or offline start never leaves it null and never throws to the caller.
    /// </summary>
    public sealed class RemoteGameConfigService
    {
        readonly IReadOnlyList<GameConfigSection> _sections;
        readonly IGameConfigSource _source;
        readonly IInstallIdentity _identity;
        readonly string _appVersion;
        readonly string _platform;
        readonly string _countryOrLocale;

        ConfigDocument _lastGoodDocument = ConfigDocument.Empty;
        ServerOverrides _lastOverrides = ServerOverrides.None;

        /// <summary>The resolved config the game reads. Never null after construction.</summary>
        public GameConfig Current { get; private set; }

        /// <summary>Raised after a refresh whose merged values or version actually changed.</summary>
        public event Action<GameConfig> Changed;

        /// <summary>Forwards the first-exposure signal of whichever <see cref="GameConfig"/> is current.</summary>
        public event Action<ExperimentExposure> ExperimentExposed;

        public RemoteGameConfigService(
            IReadOnlyList<GameConfigSection> sections,
            IGameConfigSource source,
            IInstallIdentity identity,
            string appVersion,
            string platform,
            string countryOrLocale,
            ConfigDocument embeddedManifest = null)
        {
            _sections = sections;
            _source = source;
            _identity = identity;
            _appVersion = appVersion;
            _platform = platform;
            _countryOrLocale = countryOrLocale;

            // An optional baked manifest overlays the code-level section defaults so the very first
            // launch (before any fetch, possibly offline) can ship a richer known-good configuration.
            if (embeddedManifest != null) _lastGoodDocument = embeddedManifest;

            // Fully configured on embedded defaults/manifest before any fetch (first launch / offline).
            Current = BuildSnapshot();
        }

        /// <summary>
        /// Fetch once (typically at boot) and re-resolve. A not-modified or failed fetch keeps the current
        /// config (offline tolerance); a fetched document that changed values raises <see cref="Changed"/>.
        /// Returns the fetch status so a caller can log or retry. <paramref name="forceRefresh"/> bypasses
        /// the source's TTL for a LiveOps event start.
        /// </summary>
        public async Task<ConfigFetchStatus> RefreshAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            ConfigFetchContext context = BuildContext();
            ConfigFetchResult result = await _source.FetchAsync(context, Current.Version, forceRefresh, cancellationToken);

            if (result.Status == ConfigFetchStatus.Fetched)
            {
                _lastGoodDocument = result.Document;
                _lastOverrides = result.Overrides;

                GameConfig previous = Current;
                GameConfig next = BuildSnapshot();
                Current = next;

                if (next.DiffersFrom(previous))
                {
                    Changed?.Invoke(next);
                }
            }

            // NotModified / Failed: keep the current config untouched — no throw, no change event.
            return result.Status;
        }

        ConfigFetchContext BuildContext()
        {
            return new ConfigFetchContext(
                _identity.InstallId, _appVersion, _platform, _countryOrLocale, Current.AssignedCohorts());
        }

        GameConfig BuildSnapshot()
        {
            GameConfig snapshot = GameConfigResolver.Resolve(_sections, _lastGoodDocument, _lastOverrides, _identity.InstallId);
            snapshot.ExperimentExposed += Forward;
            return snapshot;
        }

        void Forward(ExperimentExposure exposure) => ExperimentExposed?.Invoke(exposure);
    }
}
