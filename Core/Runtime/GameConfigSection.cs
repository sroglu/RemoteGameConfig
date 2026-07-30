using System.Collections.Generic;

namespace PFound.RemoteGameConfig.Core
{
    /// <summary>
    /// The sink a section writes its built-in defaults into. It qualifies every key with the section's
    /// name (<c>"section.key"</c>) so two sections can use the same short key without colliding in the
    /// flat resolved table.
    /// </summary>
    public sealed class SectionDefaultWriter
    {
        readonly Dictionary<string, ConfigValue> _sink;
        readonly string _prefix;

        internal SectionDefaultWriter(Dictionary<string, ConfigValue> sink, string prefix)
        {
            _sink = sink;
            _prefix = prefix;
        }

        public void Set(string key, bool value) => _sink[_prefix + key] = ConfigValue.Of(value);
        public void Set(string key, int value) => _sink[_prefix + key] = ConfigValue.Of(value);
        public void Set(string key, float value) => _sink[_prefix + key] = ConfigValue.Of(value);
        public void Set(string key, string value) => _sink[_prefix + key] = ConfigValue.Of(value);
    }

    /// <summary>
    /// Base for a strongly-typed slice of configuration. A subclass names its section, declares its
    /// built-in defaults (which seed the embedded-default trust tier so the game is fully configured
    /// offline / on first launch), and exposes typed properties that read the resolved values through
    /// the protected accessors. The same section instance is reused across refreshes: the resolver
    /// re-binds it to the newest resolved table, so a property read always reflects the current merge.
    /// Keys are section-qualified automatically; a subclass property passes only its short key.
    /// </summary>
    public abstract class GameConfigSection
    {
        ConfigValueMap _values = ConfigValueMap.Empty;
        string _prefix;

        /// <summary>The section's name; also the key prefix under which its values live in the flat table.</summary>
        public abstract string SectionName { get; }

        /// <summary>Declare the built-in defaults for this section. Called once to seed the embedded-default tier.</summary>
        public abstract void WriteDefaults(SectionDefaultWriter defaults);

        internal void Bind(ConfigValueMap values)
        {
            _values = values;
            _prefix = SectionName + ".";
        }

        protected bool GetBool(string key, bool fallback) => _values.GetBool(_prefix + key, fallback);
        protected int GetInt(string key, int fallback) => _values.GetInt(_prefix + key, fallback);
        protected float GetFloat(string key, float fallback) => _values.GetFloat(_prefix + key, fallback);
        protected string GetString(string key, string fallback) => _values.GetString(_prefix + key, fallback);
    }
}
