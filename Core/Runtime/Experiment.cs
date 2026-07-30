using System.Collections.Generic;

namespace PFound.RemoteGameConfig.Core
{
    /// <summary>
    /// One variant's slice of the 0..100 bucket space, half-open <c>[Start, End)</c>. A rollout of
    /// control 0–50 / treatment 50–100 is two ranges; leaving a gap (0–10) is a partial rollout where
    /// the remaining buckets fall into no variant (the install stays unassigned / on control-by-default).
    /// </summary>
    public readonly struct VariantRange
    {
        public string Variant { get; }
        public int StartInclusive { get; }
        public int EndExclusive { get; }

        public VariantRange(string variant, int startInclusive, int endExclusive)
        {
            Variant = variant;
            StartInclusive = startInclusive;
            EndExclusive = endExclusive;
        }

        public bool Contains(int bucket) => bucket >= StartInclusive && bucket < EndExclusive;
    }

    /// <summary>
    /// A backend-served experiment: a stable key, a salt that decorrelates this experiment's bucketing
    /// from every other, its variant rollout ranges, and an optional server-assigned variant that
    /// overrides client-side bucketing (authoritative targeting). Client bucketing works with a dumb
    /// JSON backend that only serves the ranges; a smart backend may additionally pin the variant.
    /// </summary>
    public sealed class ExperimentDefinition
    {
        public string Key { get; }
        public string Salt { get; }
        public IReadOnlyList<VariantRange> Variants { get; }

        /// <summary>Non-null when the backend authoritatively assigned a variant for this install.</summary>
        public string ServerAssignedVariant { get; }

        public ExperimentDefinition(string key, string salt, IReadOnlyList<VariantRange> variants, string serverAssignedVariant = null)
        {
            Key = key;
            Salt = salt;
            Variants = variants;
            ServerAssignedVariant = serverAssignedVariant;
        }

        public bool HasServerAssignment => ServerAssignedVariant != null;
    }

    /// <summary>The variant an install resolved to, and whether the backend pinned it or the client bucketed it.</summary>
    public readonly struct ExperimentAssignment
    {
        public string ExperimentKey { get; }
        public string Variant { get; }
        public int Bucket { get; }
        public bool FromServer { get; }

        public ExperimentAssignment(string experimentKey, string variant, int bucket, bool fromServer)
        {
            ExperimentKey = experimentKey;
            Variant = variant;
            Bucket = bucket;
            FromServer = fromServer;
        }

        public bool IsAssigned => Variant != null;
    }

    /// <summary>Payload of the first-exposure signal: which experiment, which variant, which install.</summary>
    public readonly struct ExperimentExposure
    {
        public string ExperimentKey { get; }
        public string Variant { get; }
        public string InstallId { get; }

        public ExperimentExposure(string experimentKey, string variant, string installId)
        {
            ExperimentKey = experimentKey;
            Variant = variant;
            InstallId = installId;
        }
    }

    /// <summary>
    /// Deterministic client-side A/B bucketing. A server-assigned variant wins outright; otherwise the
    /// install is hashed into a 0..99 bucket (run-stable FNV-1a over <c>installId + "." + salt</c>) and
    /// mapped to the variant whose range contains it. Same install + same salt ⇒ same variant on every
    /// session and platform.
    /// </summary>
    public static class ExperimentBucketing
    {
        public static ExperimentAssignment Assign(ExperimentDefinition experiment, string installId)
        {
            int bucket = StableHash.Bucket(installId + "." + experiment.Salt, 100);

            if (experiment.HasServerAssignment)
            {
                return new ExperimentAssignment(experiment.Key, experiment.ServerAssignedVariant, bucket, true);
            }

            IReadOnlyList<VariantRange> ranges = experiment.Variants;
            for (int i = 0; i < ranges.Count; i++)
            {
                if (ranges[i].Contains(bucket))
                {
                    return new ExperimentAssignment(experiment.Key, ranges[i].Variant, bucket, false);
                }
            }

            // Bucket fell in a rollout gap: the install is not in any variant.
            return new ExperimentAssignment(experiment.Key, null, bucket, false);
        }
    }
}
