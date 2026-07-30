using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PFound.RemoteGameConfig.Core;

namespace PFound.RemoteGameConfig.Core.Tests
{
    /// <summary>
    /// Engine-free coverage of the boot/refresh orchestrator: boot on embedded defaults, apply a fetched
    /// document, raise a change signal only when values changed, keep current config on not-modified and
    /// on failure (offline tolerance), and force refresh past the not-modified short-circuit.
    /// </summary>
    internal static class ServiceTests
    {
        sealed class FixedIdentity : IInstallIdentity
        {
            public FixedIdentity(string id) { InstallId = id; }
            public string InstallId { get; }
        }

        sealed class EconomySection : GameConfigSection
        {
            public override string SectionName => "economy";
            public override void WriteDefaults(SectionDefaultWriter defaults) => defaults.Set("coins", 100);
        }

        // A scripted source that replays a queue of results, recording the forceRefresh flags it saw.
        sealed class ScriptedSource : IGameConfigSource
        {
            readonly Queue<ConfigFetchResult> _results;
            public readonly List<bool> ForceFlags = new List<bool>();
            public readonly List<string> KnownVersions = new List<string>();

            public ScriptedSource(params ConfigFetchResult[] results) => _results = new Queue<ConfigFetchResult>(results);

            public Task<ConfigFetchResult> FetchAsync(ConfigFetchContext context, string knownVersion, bool forceRefresh, CancellationToken cancellationToken = default)
            {
                ForceFlags.Add(forceRefresh);
                KnownVersions.Add(knownVersion);
                return Task.FromResult(_results.Count > 0 ? _results.Dequeue() : ConfigFetchResult.Failed("drained"));
            }
        }

        static IReadOnlyList<GameConfigSection> Sections() => new GameConfigSection[] { new EconomySection() };

        static ConfigDocument Doc(string json)
        {
            ConfigDocumentReader.TryParse(json, out ConfigDocument doc);
            return doc;
        }

        public static void RunAll()
        {
            Boot_OnEmbeddedDefaults();
            Refresh_AppliesFetchedDocument();
            Change_FiresOnlyOnValueChange();
            Offline_KeepsCurrentConfig();
            ForceRefresh_PassesFlag();
        }

        static void Boot_OnEmbeddedDefaults()
        {
            var service = new RemoteGameConfigService(Sections(), new ScriptedSource(), new FixedIdentity("i1"), "1.0", "editor", "US");
            TestKit.Check(service.Current != null, "current valid after construction");
            TestKit.Check(service.Current.GetInt("economy.coins", 0) == 100, "boots on embedded default");
        }

        static void Refresh_AppliesFetchedDocument()
        {
            ConfigDocument doc = Doc("{ \"version\": \"v1\", \"config\": { \"economy\": { \"coins\": 250 } } }");
            var source = new ScriptedSource(ConfigFetchResult.Fetched(doc, ServerOverrides.None, "v1"));
            var service = new RemoteGameConfigService(Sections(), source, new FixedIdentity("i1"), "1.0", "editor", "US");

            ConfigFetchStatus status = service.RefreshAsync().GetAwaiter().GetResult();
            TestKit.Check(status == ConfigFetchStatus.Fetched, "refresh reports fetched");
            TestKit.Check(service.Current.GetInt("economy.coins", 0) == 250, "fetched document applied");
            TestKit.Check(service.Current.Version == "v1", "version updated");
        }

        static void Change_FiresOnlyOnValueChange()
        {
            ConfigDocument changed = Doc("{ \"version\": \"v2\", \"config\": { \"economy\": { \"coins\": 500 } } }");
            ConfigDocument sameValues = Doc("{ \"version\": \"v2\", \"config\": { \"economy\": { \"coins\": 500 } } }");
            var source = new ScriptedSource(
                ConfigFetchResult.Fetched(changed, ServerOverrides.None, "v2"),
                ConfigFetchResult.Fetched(sameValues, ServerOverrides.None, "v2"),
                ConfigFetchResult.NotModified("v2"));
            var service = new RemoteGameConfigService(Sections(), source, new FixedIdentity("i1"), "1.0", "editor", "US");

            int changes = 0;
            service.Changed += _ => changes++;

            service.RefreshAsync().GetAwaiter().GetResult();      // defaults -> 500 : changed
            service.RefreshAsync().GetAwaiter().GetResult();      // 500 -> 500     : no change
            service.RefreshAsync().GetAwaiter().GetResult();      // not modified   : no change

            TestKit.Check(changes == 1, "change fires once, only on a value change");
        }

        static void Offline_KeepsCurrentConfig()
        {
            ConfigDocument doc = Doc("{ \"version\": \"v1\", \"config\": { \"economy\": { \"coins\": 300 } } }");
            var source = new ScriptedSource(
                ConfigFetchResult.Fetched(doc, ServerOverrides.None, "v1"),
                ConfigFetchResult.Failed("offline"));
            var service = new RemoteGameConfigService(Sections(), source, new FixedIdentity("i1"), "1.0", "editor", "US");

            service.RefreshAsync().GetAwaiter().GetResult();      // apply 300
            ConfigFetchStatus status = service.RefreshAsync().GetAwaiter().GetResult(); // fails

            TestKit.Check(status == ConfigFetchStatus.Failed, "offline reports failed");
            TestKit.Check(service.Current.GetInt("economy.coins", 0) == 300, "offline keeps last good config");
        }

        static void ForceRefresh_PassesFlag()
        {
            var source = new ScriptedSource(ConfigFetchResult.NotModified("v1"), ConfigFetchResult.NotModified("v1"));
            var service = new RemoteGameConfigService(Sections(), source, new FixedIdentity("i1"), "1.0", "editor", "US");

            service.RefreshAsync(false).GetAwaiter().GetResult();
            service.RefreshAsync(true).GetAwaiter().GetResult();

            TestKit.Check(source.ForceFlags.Count == 2 && !source.ForceFlags[0] && source.ForceFlags[1], "force flag threaded to source");
        }
    }
}
