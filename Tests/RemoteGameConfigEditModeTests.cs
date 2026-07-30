using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PFound.RemoteGameConfig.Core;
using PFound.RemoteResourceCache.Core;
using UnityEngine;

namespace PFound.RemoteGameConfig.Tests
{
    /// <summary>
    /// EditMode coverage of the Unity glue: install-id generation + cross-session persistence, and the
    /// StaticJson backend end-to-end over a stub transport (boot fetch populates from a JSON document,
    /// the offline path serves the cached document, and the change signal reflects a value-changing
    /// refresh). The engine-free resolution/bucketing/overlay is covered by the standalone Core runner.
    /// </summary>
    public sealed class RemoteGameConfigEditModeTests
    {
        const string Url = "https://config.test/game.json";

        sealed class FixedIdentity : IInstallIdentity
        {
            public FixedIdentity(string id) { InstallId = id; }
            public string InstallId { get; }
        }

        // A toggleable in-memory transport: serves the current document bytes, or throws to simulate offline.
        sealed class StubTransport : IResourceTransport
        {
            public byte[] Payload;
            public bool Offline;
            public int Calls;

            public Task<byte[]> FetchAsync(string key, CancellationToken cancellationToken = default)
            {
                Calls++;
                if (Offline) throw new Exception("simulated offline");
                return Task.FromResult(Payload);
            }
        }

        sealed class EconomySection : GameConfigSection
        {
            public override string SectionName => "economy";
            public override void WriteDefaults(SectionDefaultWriter defaults) => defaults.Set("coins", 100);
        }

        static IReadOnlyList<GameConfigSection> Sections() => new GameConfigSection[] { new EconomySection() };

        static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

        static StaticJsonConfigSource NewSource(StubTransport transport)
        {
            // A unique disk subdirectory + long TTL keeps each test isolated from prior on-disk state.
            string subdir = "RemoteGameConfigTests/" + Guid.NewGuid().ToString("N");
            var policy = new CachePolicy { Ttl = TimeSpan.FromHours(24) };
            return new StaticJsonConfigSource(Url, transport, policy, subdir);
        }

        [Test]
        public void InstallId_IsGenerated_AndPersistsAcrossSessions()
        {
            string key = "PFound.RemoteGameConfig.Test." + Guid.NewGuid().ToString("N");
            try
            {
                var first = new PersistentInstallIdentity(key);
                Assert.IsFalse(string.IsNullOrEmpty(first.InstallId), "install id generated");

                // A second construction with the same key simulates a later session.
                var second = new PersistentInstallIdentity(key);
                Assert.AreEqual(first.InstallId, second.InstallId, "install id persists across sessions");
                Assert.AreNotEqual(SystemInfo.deviceUniqueIdentifier, first.InstallId, "install id is not the device id");
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void BootFetch_PopulatesFromDocument()
        {
            var transport = new StubTransport
            {
                Payload = Utf8("{ \"version\": \"v1\", \"config\": { \"economy\": { \"coins\": 250 } } }")
            };
            var service = new RemoteGameConfigService(Sections(), NewSource(transport), new FixedIdentity("i1"),
                "1.0", "editor", "US");

            Assert.AreEqual(100, service.Current.GetInt("economy.coins", 0), "starts on embedded default");

            ConfigFetchStatus status = service.RefreshAsync().GetAwaiter().GetResult();

            Assert.AreEqual(ConfigFetchStatus.Fetched, status);
            Assert.AreEqual(250, service.Current.GetInt("economy.coins", 0), "boot fetch applied the document");
        }

        [Test]
        public void Offline_ServesCachedDocument()
        {
            var transport = new StubTransport
            {
                Payload = Utf8("{ \"version\": \"v1\", \"config\": { \"economy\": { \"coins\": 300 } } }")
            };
            StaticJsonConfigSource source = NewSource(transport);
            var service = new RemoteGameConfigService(Sections(), source, new FixedIdentity("i1"), "1.0", "editor", "US");

            service.RefreshAsync().GetAwaiter().GetResult(); // warms cache with coins=300

            transport.Offline = true;
            ConfigFetchStatus status = service.RefreshAsync().GetAwaiter().GetResult(); // network down

            Assert.AreNotEqual(ConfigFetchStatus.Failed, status, "cached document served while offline");
            Assert.AreEqual(300, service.Current.GetInt("economy.coins", 0), "offline keeps the cached values");
        }

        [Test]
        public void ChangeEvent_FiresOnValueChange()
        {
            var transport = new StubTransport
            {
                Payload = Utf8("{ \"version\": \"v1\", \"config\": { \"economy\": { \"coins\": 500 } } }")
            };
            StaticJsonConfigSource source = NewSource(transport);
            var service = new RemoteGameConfigService(Sections(), source, new FixedIdentity("i1"), "1.0", "editor", "US");

            int changes = 0;
            service.Changed += _ => changes++;

            service.RefreshAsync().GetAwaiter().GetResult();        // defaults -> 500 : change
            service.RefreshAsync(true).GetAwaiter().GetResult();    // force: same doc -> no change

            Assert.AreEqual(1, changes, "change fires once, only when values changed");
            Assert.AreEqual(500, service.Current.GetInt("economy.coins", 0));
        }
    }
}
