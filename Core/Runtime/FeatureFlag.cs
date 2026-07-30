using System.Collections.Generic;

namespace PFound.RemoteGameConfig.Core
{
    /// <summary>
    /// A named on/off (or typed) switch the game reads by key. A flag can be resolved three ways, in
    /// priority: gated by an experiment variant (on iff the install landed on <see cref="RequiredVariant"/>
    /// of <see cref="ExperimentKey"/>), a percentage rollout (on iff the install's stable bucket for this
    /// flag falls under <see cref="RolloutPercent"/>), or a plain boolean that a remote document can flip.
    /// The plain boolean carries <see cref="DefaultOn"/> as its built-in value until a remote overrides it.
    /// </summary>
    public sealed class FeatureFlagDefinition
    {
        public string Key { get; }
        public bool DefaultOn { get; }

        /// <summary>Non-null ⇒ this flag is on only when the install is on <see cref="RequiredVariant"/> of this experiment.</summary>
        public string ExperimentKey { get; }
        public string RequiredVariant { get; }

        /// <summary>0..100; non-negative ⇒ this flag rolls out to that percentage of installs by stable bucket.</summary>
        public int RolloutPercent { get; }

        public FeatureFlagDefinition(string key, bool defaultOn, string experimentKey = null, string requiredVariant = null, int rolloutPercent = -1)
        {
            Key = key;
            DefaultOn = defaultOn;
            ExperimentKey = experimentKey;
            RequiredVariant = requiredVariant;
            RolloutPercent = rolloutPercent;
        }

        public bool IsExperimentGated => ExperimentKey != null;
        public bool IsRollout => RolloutPercent >= 0;
    }

    /// <summary>An immutable set of flag definitions keyed by flag key, as parsed from a config document.</summary>
    public sealed class FeatureFlagSet
    {
        public static readonly FeatureFlagSet Empty = new FeatureFlagSet(new Dictionary<string, FeatureFlagDefinition>(0));

        readonly Dictionary<string, FeatureFlagDefinition> _flags;

        public FeatureFlagSet(Dictionary<string, FeatureFlagDefinition> flags)
        {
            _flags = flags;
        }

        public int Count => _flags.Count;

        public IEnumerable<FeatureFlagDefinition> All => _flags.Values;

        public bool TryGet(string key, out FeatureFlagDefinition definition) => _flags.TryGetValue(key, out definition);
    }
}
