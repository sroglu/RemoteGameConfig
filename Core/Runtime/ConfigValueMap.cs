using System.Collections.Generic;

namespace PFound.RemoteGameConfig.Core
{
    /// <summary>
    /// An immutable, resolved key → <see cref="ConfigValue"/> table with the typed accessors the game
    /// reads at runtime. Keys are the flat, dotted <c>"section.key"</c> form the overlay produces.
    /// A read for a missing key returns the caller's fallback: a config document legitimately omits
    /// keys (the game then keeps its built-in default), so an absent key is normal input, not a bug —
    /// this is the one sanctioned fallback that does not violate the project's fail-fast rule.
    /// </summary>
    public sealed class ConfigValueMap
    {
        public static readonly ConfigValueMap Empty = new ConfigValueMap(new Dictionary<string, ConfigValue>(0));

        readonly Dictionary<string, ConfigValue> _entries;

        public ConfigValueMap(Dictionary<string, ConfigValue> entries)
        {
            _entries = entries;
        }

        public int Count => _entries.Count;

        public IEnumerable<string> Keys => _entries.Keys;

        public bool TryGetValue(string key, out ConfigValue value) => _entries.TryGetValue(key, out value);

        public bool ContainsKey(string key) => _entries.ContainsKey(key);

        public bool GetBool(string key, bool fallback)
            => _entries.TryGetValue(key, out ConfigValue v) ? v.AsBool(fallback) : fallback;

        public int GetInt(string key, int fallback)
            => _entries.TryGetValue(key, out ConfigValue v) ? v.AsInt(fallback) : fallback;

        public float GetFloat(string key, float fallback)
            => _entries.TryGetValue(key, out ConfigValue v) ? v.AsFloat(fallback) : fallback;

        public string GetString(string key, string fallback)
            => _entries.TryGetValue(key, out ConfigValue v) ? v.AsString(fallback) : fallback;

        /// <summary>
        /// True when this table differs from <paramref name="other"/> in any key or value — the signal
        /// a refresh actually changed something (used to decide whether to raise a change event).
        /// </summary>
        public bool DiffersFrom(ConfigValueMap other)
        {
            if (_entries.Count != other._entries.Count) return true;
            foreach (KeyValuePair<string, ConfigValue> pair in _entries)
            {
                if (!other._entries.TryGetValue(pair.Key, out ConfigValue theirs)) return true;
                if (!pair.Value.SameValueAs(theirs)) return true;
            }
            return false;
        }
    }
}
