using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PFound.RemoteGameConfig.Core;
using PFound.RemoteResourceCache;
using PFound.RemoteResourceCache.Core;
using UnityEngine;

namespace PFound.RemoteGameConfig
{
    /// <summary>
    /// The reference backend: fetches a plain JSON config document from a URL that any CDN, nginx, or
    /// file server can host — no server-side targeting, so it never returns server overrides. It reuses
    /// <see cref="ResourceCache{T}"/> (as a <c>byte[]</c> cache) for the network fetch, disk cache,
    /// TTL, retry/backoff, single-flight dedupe, and offline-serve-last, rather than reimplementing any
    /// of that: a fetch within TTL is served from cache without touching the network, and when the
    /// network is down the last cached document is served. Conditional fetch is done at the document
    /// level: after parsing, the document's <c>version</c> is compared to the caller's known version to
    /// answer <see cref="ConfigFetchStatus.NotModified"/> (RemoteResourceCache has no HTTP ETag /
    /// If-None-Match hook — see the module notes).
    /// </summary>
    public sealed class StaticJsonConfigSource : IGameConfigSource
    {
        readonly string _documentUrl;
        readonly ResourceCache<byte[]> _cache;

        public StaticJsonConfigSource(
            string documentUrl,
            IResourceTransport transport = null,
            CachePolicy policy = null,
            string diskSubdirectory = "RemoteGameConfig",
            Func<DateTime> clock = null)
        {
            _documentUrl = documentUrl;

            CachePolicy effectivePolicy = policy ?? new CachePolicy { Ttl = TimeSpan.FromHours(6) };
            string root = Path.Combine(Application.persistentDataPath, diskSubdirectory);
            var store = new FileBlobStore(root);
            DiskCache disk = DiskCache.FromPolicy(store, Path.Combine(root, "config-index.bin"), effectivePolicy, clock);

            _cache = new ResourceCache<byte[]>(
                transport ?? RemoteResourceCacheDefaults.Transport,
                disk,
                Identity,
                effectivePolicy,
                clock: clock);
        }

        static byte[] Identity(byte[] rawBytes) => rawBytes;

        public async Task<ConfigFetchResult> FetchAsync(
            ConfigFetchContext context, string knownVersion, bool forceRefresh, CancellationToken cancellationToken = default)
        {
            if (forceRefresh)
            {
                // Bypass TTL for a LiveOps event start: drop the cached generation so GetAsync re-hits remote.
                _cache.ClearMemory();
                await _cache.ClearDiskAsync();
            }

            ResourceResult<byte[]> result = await _cache.GetAsync(_documentUrl, cancellationToken);
            if (!result.Success)
            {
                return ConfigFetchResult.Failed(result.Failure.Kind + ": " + result.Failure.Message);
            }

            if (!ConfigDocumentReader.TryParse(result.Value, out ConfigDocument document))
            {
                // Corrupt/partial document: never partially apply — report failure so the last good config stays.
                return ConfigFetchResult.Failed("Config document failed to parse.");
            }

            if (!forceRefresh && knownVersion != null && document.Version != null && document.Version == knownVersion)
            {
                return ConfigFetchResult.NotModified(document.Version);
            }

            return ConfigFetchResult.Fetched(document, ServerOverrides.None, document.Version);
        }
    }
}
