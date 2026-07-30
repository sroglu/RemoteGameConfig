using System;
using System.Collections.Generic;
using PFound.RemoteGameConfig.Core;

namespace PFound.RemoteGameConfig.Core.Tests
{
    /// <summary>
    /// Standalone mono/csc runner for the engine-free RemoteGameConfig core: value model + typed
    /// accessors, section defaults, trust-order overlay (key-by-key fallthrough), JSON document parsing
    /// incl. corrupt-input tolerance, deterministic run-stable A/B bucketing + distribution, server
    /// variant override, first-exposure signalling, and feature-flag resolution (plain / rollout / gated).
    /// </summary>
    internal static class Program
    {
        // A representative typed section used across the suites.
        sealed class GameplaySection : GameConfigSection
        {
            public override string SectionName => "gameplay";

            public override void WriteDefaults(SectionDefaultWriter defaults)
            {
                defaults.Set("maxLives", 3);
                defaults.Set("difficulty", "normal");
                defaults.Set("coinMultiplier", 1.0f);
                defaults.Set("tutorialEnabled", true);
            }

            public int MaxLives => GetInt("maxLives", 3);
            public string Difficulty => GetString("difficulty", "normal");
            public float CoinMultiplier => GetFloat("coinMultiplier", 1.0f);
            public bool TutorialEnabled => GetBool("tutorialEnabled", true);
        }

        static IReadOnlyList<GameConfigSection> Sections() => new GameConfigSection[] { new GameplaySection() };

        static int Main()
        {
            DefaultOnly_YieldsEmbeddedDefaults();
            Overlay_OverridesOnlyPresentKeys();
            Corrupt_Document_Retains_Defaults();
            TypedAccessors_And_UnknownKeyFallback();
            TrustOrder_FallthroughKeyByKey();
            Bucketing_IsDeterministicAndRunStable();
            Bucketing_DistributionWithinTolerance();
            ServerVariant_OverridesClientBucketing();
            Exposure_FiresOnceWithFinalVariant();
            Flags_Plain_Rollout_Gated();
            Json_ParsesFullDocument();
            ConfigValue_CrossReads();
            NotModified_And_Failed_ResultShape();
            ServiceTests.RunAll();

            return TestKit.Summary("RemoteGameConfig.Core");
        }

        static void DefaultOnly_YieldsEmbeddedDefaults()
        {
            GameConfig config = GameConfigResolver.Resolve(Sections(), ConfigDocument.Empty, ServerOverrides.None, "install-A");
            var gameplay = config.Section(new GameplaySection());

            TestKit.Check(gameplay.MaxLives == 3, "default MaxLives");
            TestKit.Check(gameplay.Difficulty == "normal", "default Difficulty");
            TestKit.Check(Math.Abs(gameplay.CoinMultiplier - 1.0f) < 0.0001f, "default CoinMultiplier");
            TestKit.Check(gameplay.TutorialEnabled, "default TutorialEnabled");
            TestKit.Check(config.GetInt("gameplay.maxLives", 99) == 3, "flat accessor reads default");
        }

        static void Overlay_OverridesOnlyPresentKeys()
        {
            const string json = "{ \"config\": { \"gameplay\": { \"maxLives\": 5 } } }";
            ConfigDocumentReader.TryParse(json, out ConfigDocument doc);
            GameConfig config = GameConfigResolver.Resolve(Sections(), doc, ServerOverrides.None, "install-A");
            var gameplay = config.Section(new GameplaySection());

            TestKit.Check(gameplay.MaxLives == 5, "overlay overrides maxLives");
            TestKit.Check(gameplay.Difficulty == "normal", "overlay keeps difficulty default");
            TestKit.Check(gameplay.TutorialEnabled, "overlay keeps tutorial default");
        }

        static void Corrupt_Document_Retains_Defaults()
        {
            bool parsed = ConfigDocumentReader.TryParse("{ this is : not json", out ConfigDocument doc);
            TestKit.Check(!parsed, "corrupt json rejected (no throw)");
            TestKit.Check(doc == null, "corrupt json yields no document");

            // Last-good present, fresh failed -> service would pass Empty as fresh; cached retained.
            const string goodJson = "{ \"config\": { \"gameplay\": { \"maxLives\": 7 } } }";
            ConfigDocumentReader.TryParse(goodJson, out ConfigDocument cached);
            GameConfig config = GameConfigResolver.Resolve(Sections(), cached, ConfigDocument.Empty, ServerOverrides.None, "install-A");
            TestKit.Check(config.GetInt("gameplay.maxLives", 0) == 7, "cached retained when fresh failed");
        }

        static void TypedAccessors_And_UnknownKeyFallback()
        {
            const string json = "{ \"config\": { \"gameplay\": { \"maxLives\": 9, \"coinMultiplier\": 2.5, \"difficulty\": \"hard\", \"tutorialEnabled\": false } } }";
            ConfigDocumentReader.TryParse(json, out ConfigDocument doc);
            GameConfig config = GameConfigResolver.Resolve(Sections(), doc, ServerOverrides.None, "install-A");

            TestKit.Check(config.GetInt("gameplay.maxLives", 0) == 9, "GetInt");
            TestKit.Check(Math.Abs(config.GetFloat("gameplay.coinMultiplier", 0f) - 2.5f) < 0.0001f, "GetFloat");
            TestKit.Check(config.GetString("gameplay.difficulty", "?") == "hard", "GetString");
            TestKit.Check(config.GetBool("gameplay.tutorialEnabled", true) == false, "GetBool");

            TestKit.Check(config.GetInt("gameplay.unknown", 42) == 42, "unknown key returns fallback (int)");
            TestKit.Check(config.GetString("nope", "fb") == "fb", "unknown key returns fallback (string)");
            TestKit.Check(config.GetBool("missing", true), "unknown key returns fallback (bool)");
        }

        static void TrustOrder_FallthroughKeyByKey()
        {
            var defaults = new Dictionary<string, ConfigValue>
            {
                { "k.a", ConfigValue.Of(1) }, { "k.b", ConfigValue.Of(1) }, { "k.c", ConfigValue.Of(1) }, { "k.d", ConfigValue.Of(1) }
            };
            var cached = new Dictionary<string, ConfigValue> { { "k.b", ConfigValue.Of(2) }, { "k.c", ConfigValue.Of(2) }, { "k.d", ConfigValue.Of(2) } };
            var fresh = new Dictionary<string, ConfigValue> { { "k.c", ConfigValue.Of(3) }, { "k.d", ConfigValue.Of(3) } };
            var over = new Dictionary<string, ConfigValue> { { "k.d", ConfigValue.Of(4) } };

            ConfigValueMap merged = ConfigOverlay.Merge(new List<ConfigLayer>
            {
                new ConfigLayer(ConfigTrustTier.EmbeddedDefault, defaults),
                new ConfigLayer(ConfigTrustTier.CachedRemote, cached),
                new ConfigLayer(ConfigTrustTier.FreshRemote, fresh),
                new ConfigLayer(ConfigTrustTier.ServerOverride, over)
            });

            TestKit.Check(merged.GetInt("k.a", 0) == 1, "only default -> default");
            TestKit.Check(merged.GetInt("k.b", 0) == 2, "cached wins over default");
            TestKit.Check(merged.GetInt("k.c", 0) == 3, "fresh wins over cached");
            TestKit.Check(merged.GetInt("k.d", 0) == 4, "server override wins over all");
        }

        static void Bucketing_IsDeterministicAndRunStable()
        {
            var variants = new List<VariantRange>
            {
                new VariantRange("control", 0, 50),
                new VariantRange("treatment", 50, 100)
            };
            var exp = new ExperimentDefinition("exp1", "salt1", variants);

            string first = ExperimentBucketing.Assign(exp, "install-XYZ").Variant;
            bool stable = true;
            for (int i = 0; i < 1000; i++)
            {
                if (ExperimentBucketing.Assign(exp, "install-XYZ").Variant != first) { stable = false; break; }
            }
            TestKit.Check(stable, "same install+salt -> same variant across 1000 repeats");

            // Known-answer run-stability: FNV-1a is fixed, so these are stable across runs/platforms.
            TestKit.Check(StableHash.Bucket("install-XYZ.salt1", 100) == ExperimentBucketing.Assign(exp, "install-XYZ").Bucket, "bucket derives from stable hash");
        }

        static void Bucketing_DistributionWithinTolerance()
        {
            var variants = new List<VariantRange>
            {
                new VariantRange("control", 0, 50),
                new VariantRange("treatment", 50, 100)
            };
            var exp = new ExperimentDefinition("split", "abc", variants);

            int control = 0;
            const int n = 10000;
            for (int i = 0; i < n; i++)
            {
                if (ExperimentBucketing.Assign(exp, "id-" + i + "-" + Guid.NewGuid().ToString("N")).Variant == "control") control++;
            }
            double ratio = (double)control / n;
            TestKit.Check(ratio > 0.45 && ratio < 0.55, "50/50 split within tolerance (" + ratio.ToString("0.###") + ")");
        }

        static void ServerVariant_OverridesClientBucketing()
        {
            var variants = new List<VariantRange> { new VariantRange("control", 0, 50), new VariantRange("treatment", 50, 100) };

            // Same experiment, once client-bucketed and once server-forced to the opposite variant.
            var clientExp = new ExperimentDefinition("exp", "s", variants);
            ExperimentAssignment client = ExperimentBucketing.Assign(clientExp, "install-K");

            string opposite = client.Variant == "control" ? "treatment" : "control";
            var serverExp = new ExperimentDefinition("exp", "s", variants, opposite);
            ExperimentAssignment server = ExperimentBucketing.Assign(serverExp, "install-K");

            TestKit.Check(server.Variant == opposite, "server variant wins over client bucketing");
            TestKit.Check(server.FromServer, "assignment marked FromServer");
        }

        static void Exposure_FiresOnceWithFinalVariant()
        {
            var variants = new List<VariantRange> { new VariantRange("control", 0, 50), new VariantRange("treatment", 50, 100) };
            var exp = new ExperimentDefinition("onboarding", "s", variants, "treatment"); // server-forced
            GameConfig config = new GameConfig(
                ConfigValueMap.Empty, FeatureFlagSet.Empty, new List<ExperimentDefinition> { exp }, "install-Q", null);

            int exposures = 0;
            ExperimentExposure captured = default;
            config.ExperimentExposed += e => { exposures++; captured = e; };

            config.GetVariant("onboarding");
            config.GetVariant("onboarding");
            config.GetVariant("onboarding");

            TestKit.Check(exposures == 1, "exposure fires exactly once");
            TestKit.Check(captured.Variant == "treatment", "exposure carries final (server) variant");
            TestKit.Check(captured.InstallId == "install-Q", "exposure carries install id");
        }

        static void Flags_Plain_Rollout_Gated()
        {
            const string json =
                "{ \"flags\": { \"plainOn\": true, \"plainOff\": false, " +
                "\"rolled\": { \"default\": false, \"rollout\": 100 }, " +
                "\"none\": { \"default\": false, \"rollout\": 0 }, " +
                "\"gated\": { \"default\": false, \"experiment\": \"exp\", \"variant\": \"treatment\" } }, " +
                "\"experiments\": [ { \"key\": \"exp\", \"salt\": \"s\", \"assigned\": \"treatment\", " +
                "\"variants\": [ { \"name\": \"control\", \"min\": 0, \"max\": 50 }, { \"name\": \"treatment\", \"min\": 50, \"max\": 100 } ] } ] }";
            ConfigDocumentReader.TryParse(json, out ConfigDocument doc);
            GameConfig config = GameConfigResolver.Resolve(Sections(), doc, ServerOverrides.None, "install-F");

            TestKit.Check(config.IsEnabled("plainOn"), "plain flag on");
            TestKit.Check(!config.IsEnabled("plainOff"), "plain flag off");
            TestKit.Check(config.IsEnabled("rolled"), "100% rollout on");
            TestKit.Check(!config.IsEnabled("none"), "0% rollout off");
            TestKit.Check(config.IsEnabled("gated"), "experiment-gated flag on for treatment");
            TestKit.Check(!config.IsEnabled("unknownFlag"), "unknown flag off");
        }

        static void Json_ParsesFullDocument()
        {
            const string json =
                "{ \"version\": \"v1\", \"config\": { \"gameplay\": { \"maxLives\": 5, \"coinMultiplier\": 1.5, \"name\": \"hero\" } }, " +
                "\"flags\": { \"shop\": true }, " +
                "\"experiments\": [ { \"key\": \"e\", \"salt\": \"x\", \"variants\": [ { \"name\": \"a\", \"min\": 0, \"max\": 100 } ] } ] }";
            bool ok = ConfigDocumentReader.TryParse(json, out ConfigDocument doc);

            TestKit.Check(ok, "full document parses");
            TestKit.Check(doc.Version == "v1", "version parsed");
            TestKit.Check(doc.Values["gameplay.maxLives"].AsInt(0) == 5, "flattened int value");
            TestKit.Check(Math.Abs(doc.Values["gameplay.coinMultiplier"].AsFloat(0) - 1.5f) < 0.0001f, "flattened float value");
            TestKit.Check(doc.Values["gameplay.name"].AsString("") == "hero", "flattened string value");
            TestKit.Check(doc.Flags.Count == 1, "one flag parsed");
            TestKit.Check(doc.Experiments.Count == 1, "one experiment parsed");
            TestKit.Check(doc.Experiments[0].Variants.Count == 1, "experiment variant parsed");
        }

        static void ConfigValue_CrossReads()
        {
            ConfigValue asInt = ConfigValue.Of(5);
            TestKit.Check(Math.Abs(asInt.AsFloat(0) - 5f) < 0.0001f, "int reads as float");
            TestKit.Check(asInt.AsBool(false), "non-zero int reads as true");

            ConfigValue asFloat = ConfigValue.Of(2.9f);
            TestKit.Check(asFloat.AsInt(0) == 2, "float truncates to int");

            ConfigValue asString = ConfigValue.Of("42");
            TestKit.Check(asString.AsInt(0) == 42, "numeric string reads as int");
        }

        static void NotModified_And_Failed_ResultShape()
        {
            ConfigFetchResult notModified = ConfigFetchResult.NotModified("v2");
            TestKit.Check(notModified.Status == ConfigFetchStatus.NotModified, "not-modified status");
            TestKit.Check(notModified.Version == "v2", "not-modified carries version");

            ConfigFetchResult failed = ConfigFetchResult.Failed("offline");
            TestKit.Check(failed.Status == ConfigFetchStatus.Failed, "failed status");
            TestKit.Check(failed.Document == ConfigDocument.Empty, "failed carries empty document");
        }
    }
}
