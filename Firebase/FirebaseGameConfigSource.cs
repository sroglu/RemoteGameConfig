#if PFOUND_FIREBASE
using System;
using System.Threading;
using System.Threading.Tasks;
using Firebase.RemoteConfig;
using PFound.RemoteGameConfig.Core;

namespace PFound.RemoteGameConfig.Firebase
{
    /// <summary>
    /// Backend that reads the config document from Firebase Remote Config. This whole assembly is gated by
    /// the <c>PFOUND_FIREBASE</c> scripting define (and the asmdef's define constraint), so it does not
    /// compile — and the module builds fine — until the Firebase SDK is present and the define is set.
    /// StaticJson remains the always-available reference backend.
    ///
    /// Firebase Remote Config performs its own fetch/activate/cache and evaluates server-side conditions,
    /// so this source does NOT layer RemoteResourceCache on top of it. The whole document is stored under
    /// one Remote Config parameter as a JSON string (the same document shape StaticJson serves) and parsed
    /// with the shared <see cref="ConfigDocumentReader"/>; because Remote Config already applies targeting,
    /// the returned document reflects any server-side variant/value assignment for this install.
    /// </summary>
    public sealed class FirebaseGameConfigSource : IGameConfigSource
    {
        public const string DefaultDocumentParameter = "game_config_document";

        readonly string _documentParameter;
        readonly TimeSpan _cacheExpiry;

        public FirebaseGameConfigSource(string documentParameter = DefaultDocumentParameter, TimeSpan cacheExpiry = default)
        {
            _documentParameter = documentParameter;
            _cacheExpiry = cacheExpiry == default ? TimeSpan.FromHours(6) : cacheExpiry;
        }

        public async Task<ConfigFetchResult> FetchAsync(
            ConfigFetchContext context, string knownVersion, bool forceRefresh, CancellationToken cancellationToken = default)
        {
            FirebaseRemoteConfig remoteConfig = FirebaseRemoteConfig.DefaultInstance;

            // Network fetch is an external IO boundary: translate its failure into a data result so the
            // game keeps its last good config rather than throwing.
            try
            {
                TimeSpan maxAge = forceRefresh ? TimeSpan.Zero : _cacheExpiry;
                await remoteConfig.FetchAsync(maxAge);
                await remoteConfig.ActivateAsync();
            }
            catch (Exception e)
            {
                return ConfigFetchResult.Failed("Firebase fetch failed: " + e.Message);
            }

            string json = remoteConfig.GetValue(_documentParameter).StringValue;
            if (string.IsNullOrEmpty(json))
            {
                return ConfigFetchResult.Failed("Firebase returned an empty config document.");
            }

            if (!ConfigDocumentReader.TryParse(json, out ConfigDocument document))
            {
                return ConfigFetchResult.Failed("Firebase config document failed to parse.");
            }

            if (!forceRefresh && knownVersion != null && document.Version != null && document.Version == knownVersion)
            {
                return ConfigFetchResult.NotModified(document.Version);
            }

            // Remote Config already applied server-side targeting; the parsed document is authoritative.
            return ConfigFetchResult.Fetched(document, ServerOverrides.None, document.Version);
        }
    }
}
#endif
