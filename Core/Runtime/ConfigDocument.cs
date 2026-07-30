using System.Collections.Generic;

namespace PFound.RemoteGameConfig.Core
{
    /// <summary>
    /// A parsed remote configuration document: the flat config values it carries, its feature-flag
    /// definitions, its experiment definitions, and an optional version stamp used to tell whether a
    /// refresh actually changed anything (the conditional-fetch signal). A document is a whole-or-nothing
    /// unit: a document that fails to parse never becomes a partial <see cref="ConfigDocument"/> — the
    /// reader returns failure and the caller keeps the last good document instead.
    /// </summary>
    public sealed class ConfigDocument
    {
        public static readonly ConfigDocument Empty = new ConfigDocument(
            new Dictionary<string, ConfigValue>(0), FeatureFlagSet.Empty, new List<ExperimentDefinition>(0), null);

        public IReadOnlyDictionary<string, ConfigValue> Values { get; }
        public FeatureFlagSet Flags { get; }
        public IReadOnlyList<ExperimentDefinition> Experiments { get; }

        /// <summary>Document version/etag; null when the backend does not stamp one.</summary>
        public string Version { get; }

        public ConfigDocument(
            IReadOnlyDictionary<string, ConfigValue> values,
            FeatureFlagSet flags,
            IReadOnlyList<ExperimentDefinition> experiments,
            string version)
        {
            Values = values;
            Flags = flags;
            Experiments = experiments;
            Version = version;
        }
    }
}
