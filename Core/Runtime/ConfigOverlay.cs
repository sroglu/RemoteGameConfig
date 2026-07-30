using System.Collections.Generic;

namespace PFound.RemoteGameConfig.Core
{
    /// <summary>
    /// Which trust tier a set of values came from. The enum order IS the trust order: a later tier's
    /// value for a key wins over an earlier tier's, key-by-key. Embedded defaults ship in the binary
    /// and always exist; cached and fresh remote documents overlay them; a server-assigned override
    /// (per-key or a forced experiment variant) is the most authoritative.
    /// </summary>
    public enum ConfigTrustTier
    {
        EmbeddedDefault = 0,
        CachedRemote = 1,
        FreshRemote = 2,
        ServerOverride = 3
    }

    /// <summary>One trust tier's contribution to the merge: a tier tag plus the keys it supplies.</summary>
    public sealed class ConfigLayer
    {
        public ConfigTrustTier Tier { get; }
        public IReadOnlyDictionary<string, ConfigValue> Values { get; }

        public ConfigLayer(ConfigTrustTier tier, IReadOnlyDictionary<string, ConfigValue> values)
        {
            Tier = tier;
            Values = values;
        }
    }

    /// <summary>
    /// Merges trust tiers into one resolved table. The merge is key-by-key: start empty, then apply
    /// each layer from lowest trust to highest, letting a higher tier overwrite only the keys it
    /// actually carries. A key present only in a low tier survives; a key a high tier omits falls
    /// through to whatever lower tier last set it. This guarantees a complete config even when the
    /// higher (remote) tiers are absent or partial.
    /// </summary>
    public static class ConfigOverlay
    {
        /// <summary>
        /// Apply <paramref name="layers"/> in ascending trust order. Callers pass layers already
        /// ordered low → high (<see cref="ConfigTrustTier"/> ascending); the merge does not reorder,
        /// so the caller controls exactly which tiers participate.
        /// </summary>
        public static ConfigValueMap Merge(IReadOnlyList<ConfigLayer> layers)
        {
            var merged = new Dictionary<string, ConfigValue>();
            for (int i = 0; i < layers.Count; i++)
            {
                foreach (KeyValuePair<string, ConfigValue> pair in layers[i].Values)
                {
                    merged[pair.Key] = pair.Value;
                }
            }
            return new ConfigValueMap(merged);
        }
    }
}
