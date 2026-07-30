using System.Collections.Generic;

namespace PFound.RemoteGameConfig.Core
{
    /// <summary>
    /// The most-authoritative trust tier: per-key value overrides and forced experiment variants a
    /// smart backend assigns for this specific install after server-side targeting. Values here win
    /// over every document tier; a forced variant wins over client bucketing. A dumb JSON backend
    /// supplies <see cref="None"/> and everything resolves from the document and client bucketing.
    /// </summary>
    public sealed class ServerOverrides
    {
        public static readonly ServerOverrides None = new ServerOverrides(
            new Dictionary<string, ConfigValue>(0), new Dictionary<string, string>(0));

        public IReadOnlyDictionary<string, ConfigValue> Values { get; }

        /// <summary>Experiment key → server-forced variant.</summary>
        public IReadOnlyDictionary<string, string> ExperimentVariants { get; }

        public ServerOverrides(
            IReadOnlyDictionary<string, ConfigValue> values,
            IReadOnlyDictionary<string, string> experimentVariants)
        {
            Values = values;
            ExperimentVariants = experimentVariants;
        }
    }
}
