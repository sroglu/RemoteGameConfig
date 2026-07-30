using System;
using System.Collections.Generic;

namespace PFound.RemoteGameConfig.Core
{
    /// <summary>
    /// The resolved, in-memory configuration the game reads synchronously. It is an immutable snapshot
    /// of one merge across the trust tiers plus the install's identity, exposing typed value accessors,
    /// feature-flag evaluation, and deterministic experiment assignment. Reads never touch the network:
    /// everything is already resolved. Experiment assignment is memoized per key and raises
    /// <see cref="ExperimentExposed"/> the first time a variant is read, so analytics can record that
    /// this install was actually exposed to the experiment.
    /// </summary>
    public sealed class GameConfig
    {
        readonly ConfigValueMap _values;
        readonly FeatureFlagSet _flags;
        readonly Dictionary<string, ExperimentDefinition> _experiments;
        readonly string _installId;
        readonly HashSet<string> _exposed = new HashSet<string>();
        readonly Dictionary<string, ExperimentAssignment> _assignments = new Dictionary<string, ExperimentAssignment>();

        /// <summary>Raised once per experiment key, the first time this install reads that experiment's variant.</summary>
        public event Action<ExperimentExposure> ExperimentExposed;

        public string InstallId => _installId;
        public string Version { get; }
        public ConfigValueMap Values => _values;

        public GameConfig(
            ConfigValueMap values,
            FeatureFlagSet flags,
            IReadOnlyList<ExperimentDefinition> experiments,
            string installId,
            string version)
        {
            _values = values;
            _flags = flags;
            _installId = installId;
            Version = version;

            _experiments = new Dictionary<string, ExperimentDefinition>(experiments.Count);
            for (int i = 0; i < experiments.Count; i++)
            {
                _experiments[experiments[i].Key] = experiments[i];
            }
        }

        // ---- typed value access ----

        public bool GetBool(string key, bool fallback) => _values.GetBool(key, fallback);
        public int GetInt(string key, int fallback) => _values.GetInt(key, fallback);
        public float GetFloat(string key, float fallback) => _values.GetFloat(key, fallback);
        public string GetString(string key, string fallback) => _values.GetString(key, fallback);

        /// <summary>Bind a typed section to this snapshot's resolved values and return it for reading.</summary>
        public T Section<T>(T section) where T : GameConfigSection
        {
            section.Bind(_values);
            return section;
        }

        // ---- feature flags ----

        /// <summary>
        /// Resolve a feature flag. Priority: an experiment gate (on iff the install is on the required
        /// variant), then a percentage rollout (on iff the install's stable bucket for this flag is under
        /// the percentage), then the plain boolean value (built-in default, overridable by a remote
        /// document). An unknown flag key is off.
        /// </summary>
        public bool IsEnabled(string flagKey)
        {
            if (!_flags.TryGet(flagKey, out FeatureFlagDefinition definition))
            {
                return _values.GetBool("flags." + flagKey, false);
            }

            if (definition.IsExperimentGated)
            {
                ExperimentAssignment assignment = GetVariant(definition.ExperimentKey);
                return string.Equals(assignment.Variant, definition.RequiredVariant);
            }

            if (definition.IsRollout)
            {
                return StableHash.Bucket(_installId + ".flag." + flagKey, 100) < definition.RolloutPercent;
            }

            return _values.GetBool("flags." + flagKey, definition.DefaultOn);
        }

        // ---- experiments ----

        /// <summary>
        /// The install's variant for an experiment, assigned deterministically (server override wins,
        /// else client bucketing). Memoized, and raises <see cref="ExperimentExposed"/> exactly once per
        /// experiment key with the final variant. An unknown experiment key resolves to an unassigned
        /// result (null variant) and does not raise exposure.
        /// </summary>
        public ExperimentAssignment GetVariant(string experimentKey)
        {
            if (_assignments.TryGetValue(experimentKey, out ExperimentAssignment cached))
            {
                return cached;
            }

            if (!_experiments.TryGetValue(experimentKey, out ExperimentDefinition definition))
            {
                var unknown = new ExperimentAssignment(experimentKey, null, 0, false);
                _assignments[experimentKey] = unknown;
                return unknown;
            }

            ExperimentAssignment assignment = ExperimentBucketing.Assign(definition, _installId);
            _assignments[experimentKey] = assignment;

            if (assignment.IsAssigned && _exposed.Add(experimentKey))
            {
                ExperimentExposed?.Invoke(new ExperimentExposure(experimentKey, assignment.Variant, _installId));
            }

            return assignment;
        }

        /// <summary>The experiment keys this snapshot carries (for cohort reporting in the next fetch context).</summary>
        public IEnumerable<string> ExperimentKeys => _experiments.Keys;

        /// <summary>
        /// The experiment cohorts already resolved for this install (memoized assignments with a variant),
        /// as key → variant. Reads only what was already assigned, so building a fetch context does not
        /// trigger a new exposure.
        /// </summary>
        public IReadOnlyDictionary<string, string> AssignedCohorts()
        {
            var cohorts = new Dictionary<string, string>(_assignments.Count);
            foreach (KeyValuePair<string, ExperimentAssignment> pair in _assignments)
            {
                if (pair.Value.IsAssigned) cohorts[pair.Key] = pair.Value.Variant;
            }
            return cohorts;
        }

        // ---- change detection ----

        /// <summary>True when a newer snapshot's values or version differ from this one — the change signal.</summary>
        public bool DiffersFrom(GameConfig other)
        {
            if (!string.Equals(Version, other.Version)) return true;
            return _values.DiffersFrom(other._values);
        }
    }
}
