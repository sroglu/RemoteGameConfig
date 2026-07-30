using System.Collections.Generic;

namespace PFound.RemoteGameConfig.Core
{
    /// <summary>
    /// Assembles a <see cref="GameConfig"/> from the trust tiers. It seeds the embedded-default tier from
    /// the game's sections, overlays the cached then the fresh remote document, then the server overrides,
    /// and merges key-by-key so any tier that is absent or partial simply falls through to the one below.
    /// Feature flags and experiment definitions come from the newest present document (fresh, else cached),
    /// with any server-forced variant applied on top of that document's own assignment.
    /// </summary>
    public static class GameConfigResolver
    {
        public static GameConfig Resolve(
            IReadOnlyList<GameConfigSection> sections,
            ConfigDocument cached,
            ConfigDocument fresh,
            ServerOverrides overrides,
            string installId)
        {
            var layers = new List<ConfigLayer>(4)
            {
                new ConfigLayer(ConfigTrustTier.EmbeddedDefault, BuildEmbeddedDefaults(sections)),
                new ConfigLayer(ConfigTrustTier.CachedRemote, cached.Values),
                new ConfigLayer(ConfigTrustTier.FreshRemote, fresh.Values),
                new ConfigLayer(ConfigTrustTier.ServerOverride, overrides.Values)
            };

            ConfigValueMap merged = ConfigOverlay.Merge(layers);

            bool freshPresent = !ReferenceEquals(fresh, ConfigDocument.Empty);
            ConfigDocument active = freshPresent ? fresh : cached;

            FeatureFlagSet flags = active.Flags;
            IReadOnlyList<ExperimentDefinition> experiments = ApplyServerVariants(active.Experiments, overrides.ExperimentVariants);
            string version = active.Version;

            return new GameConfig(merged, flags, experiments, installId, version);
        }

        /// <summary>Convenience overload for the common single-document case (no separate cached tier).</summary>
        public static GameConfig Resolve(
            IReadOnlyList<GameConfigSection> sections,
            ConfigDocument document,
            ServerOverrides overrides,
            string installId)
            => Resolve(sections, ConfigDocument.Empty, document, overrides, installId);

        static Dictionary<string, ConfigValue> BuildEmbeddedDefaults(IReadOnlyList<GameConfigSection> sections)
        {
            var defaults = new Dictionary<string, ConfigValue>();
            for (int i = 0; i < sections.Count; i++)
            {
                GameConfigSection section = sections[i];
                var writer = new SectionDefaultWriter(defaults, section.SectionName + ".");
                section.WriteDefaults(writer);
            }
            return defaults;
        }

        static IReadOnlyList<ExperimentDefinition> ApplyServerVariants(
            IReadOnlyList<ExperimentDefinition> experiments,
            IReadOnlyDictionary<string, string> forcedVariants)
        {
            if (forcedVariants.Count == 0) return experiments;

            var result = new List<ExperimentDefinition>(experiments.Count);
            for (int i = 0; i < experiments.Count; i++)
            {
                ExperimentDefinition experiment = experiments[i];
                if (forcedVariants.TryGetValue(experiment.Key, out string forced))
                {
                    result.Add(new ExperimentDefinition(experiment.Key, experiment.Salt, experiment.Variants, forced));
                }
                else
                {
                    result.Add(experiment);
                }
            }
            return result;
        }
    }
}
