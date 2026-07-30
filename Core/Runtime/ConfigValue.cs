using System.Globalization;

namespace PFound.RemoteGameConfig.Core
{
    /// <summary>Which primitive a <see cref="ConfigValue"/> carries.</summary>
    public enum ConfigValueKind
    {
        Bool,
        Int,
        Float,
        String
    }

    /// <summary>
    /// One resolved configuration value: a small tagged union over the four primitives a remote
    /// config document can express. Numbers are stored once and cross-read (an int reads as a float
    /// and vice versa) so a document author who writes <c>5</c> where the game expects a float still
    /// resolves. Reading a value as the wrong family (a string as a bool) yields the caller's
    /// fallback rather than throwing — a config document is external input, not a program invariant.
    /// </summary>
    public readonly struct ConfigValue
    {
        public ConfigValueKind Kind { get; }

        readonly long _int;
        readonly double _float;
        readonly bool _bool;
        readonly string _text;

        ConfigValue(ConfigValueKind kind, long asInt, double asFloat, bool asBool, string text)
        {
            Kind = kind;
            _int = asInt;
            _float = asFloat;
            _bool = asBool;
            _text = text;
        }

        public static ConfigValue Of(bool value) => new ConfigValue(ConfigValueKind.Bool, value ? 1 : 0, value ? 1d : 0d, value, null);
        public static ConfigValue Of(int value) => Of((long)value);
        public static ConfigValue Of(long value) => new ConfigValue(ConfigValueKind.Int, value, value, value != 0, null);
        public static ConfigValue Of(float value) => Of((double)value);
        public static ConfigValue Of(double value) => new ConfigValue(ConfigValueKind.Float, (long)value, value, value != 0d, null);
        public static ConfigValue Of(string value) => new ConfigValue(ConfigValueKind.String, 0, 0d, false, value);

        public bool AsBool(bool fallback)
        {
            switch (Kind)
            {
                case ConfigValueKind.Bool: return _bool;
                case ConfigValueKind.Int: return _int != 0;
                case ConfigValueKind.Float: return _float != 0d;
                case ConfigValueKind.String:
                    if (bool.TryParse(_text, out bool parsed)) return parsed;
                    return fallback;
                default: return fallback;
            }
        }

        public int AsInt(int fallback)
        {
            switch (Kind)
            {
                case ConfigValueKind.Int: return (int)_int;
                case ConfigValueKind.Float: return (int)_float;
                case ConfigValueKind.Bool: return _bool ? 1 : 0;
                case ConfigValueKind.String:
                    if (int.TryParse(_text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) return parsed;
                    return fallback;
                default: return fallback;
            }
        }

        public float AsFloat(float fallback)
        {
            switch (Kind)
            {
                case ConfigValueKind.Float: return (float)_float;
                case ConfigValueKind.Int: return _int;
                case ConfigValueKind.Bool: return _bool ? 1f : 0f;
                case ConfigValueKind.String:
                    if (float.TryParse(_text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)) return parsed;
                    return fallback;
                default: return fallback;
            }
        }

        public string AsString(string fallback)
        {
            switch (Kind)
            {
                case ConfigValueKind.String: return _text;
                case ConfigValueKind.Bool: return _bool ? "true" : "false";
                case ConfigValueKind.Int: return _int.ToString(CultureInfo.InvariantCulture);
                case ConfigValueKind.Float: return _float.ToString(CultureInfo.InvariantCulture);
                default: return fallback;
            }
        }

        /// <summary>Value equality across the stored primitive — used to detect a changed refresh.</summary>
        public bool SameValueAs(ConfigValue other)
        {
            if (Kind != other.Kind) return false;
            switch (Kind)
            {
                case ConfigValueKind.Bool: return _bool == other._bool;
                case ConfigValueKind.Int: return _int == other._int;
                case ConfigValueKind.Float: return _float.Equals(other._float);
                case ConfigValueKind.String: return string.Equals(_text, other._text);
                default: return false;
            }
        }

        public override string ToString() => AsString(string.Empty);
    }
}
