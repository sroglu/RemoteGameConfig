using System.Collections.Generic;
using System.Text;

namespace PFound.RemoteGameConfig.Core
{
    /// <summary>
    /// Turns a raw JSON config document into the typed <see cref="ConfigDocument"/> model. The document
    /// shape is a dumb-backend-friendly layout any CDN or file server can host:
    /// <code>
    /// {
    ///   "version": "2026-07-01",
    ///   "config":  { "gameplay": { "maxLives": 5 }, "economy": { "coinMultiplier": 1.5 } },
    ///   "flags":   { "newShop": true,
    ///                "halloween": { "default": false, "rollout": 25 },
    ///                "tutorialV2": { "default": false, "experiment": "onboarding", "variant": "treatment" } },
    ///   "experiments": [
    ///     { "key": "onboarding", "salt": "s1",
    ///       "variants": [ { "name": "control", "min": 0, "max": 50 }, { "name": "treatment", "min": 50, "max": 100 } ],
    ///       "assigned": "treatment" }
    ///   ]
    /// }
    /// </code>
    /// <c>config</c> is flattened to dotted <c>"section.key"</c> entries. Parsing is all-or-nothing:
    /// malformed JSON (or an experiment entry missing its key) yields false and no document, so the
    /// caller keeps the last good document instead of applying a broken one.
    /// </summary>
    public static class ConfigDocumentReader
    {
        public static bool TryParse(byte[] utf8, out ConfigDocument document)
        {
            document = null;
            if (utf8 == null || utf8.Length == 0) return false;
            return TryParse(Encoding.UTF8.GetString(utf8), out document);
        }

        public static bool TryParse(string json, out ConfigDocument document)
        {
            document = null;
            if (!JsonParser.TryParse(json, out JsonValue root)) return false;
            if (!root.IsObject) return false;

            string version = null;
            if (root.TryMember("version", out JsonValue versionNode) && versionNode.Kind == JsonKind.String)
            {
                version = versionNode.Text;
            }

            var values = new Dictionary<string, ConfigValue>();
            if (root.TryMember("config", out JsonValue configNode) && configNode.IsObject)
            {
                foreach (KeyValuePair<string, JsonValue> section in configNode.Members)
                {
                    FlattenInto(values, section.Key, section.Value);
                }
            }

            var flags = new Dictionary<string, FeatureFlagDefinition>();
            if (root.TryMember("flags", out JsonValue flagsNode) && flagsNode.IsObject)
            {
                foreach (KeyValuePair<string, JsonValue> flag in flagsNode.Members)
                {
                    flags[flag.Key] = ReadFlag(flag.Key, flag.Value, values);
                }
            }

            var experiments = new List<ExperimentDefinition>();
            if (root.TryMember("experiments", out JsonValue experimentsNode) && experimentsNode.IsArray)
            {
                foreach (JsonValue entry in experimentsNode.Items)
                {
                    if (!TryReadExperiment(entry, out ExperimentDefinition experiment)) return false;
                    experiments.Add(experiment);
                }
            }

            document = new ConfigDocument(values, new FeatureFlagSet(flags), experiments, version);
            return true;
        }

        static void FlattenInto(Dictionary<string, ConfigValue> sink, string prefix, JsonValue node)
        {
            if (node.IsObject)
            {
                foreach (KeyValuePair<string, JsonValue> member in node.Members)
                {
                    FlattenInto(sink, prefix + "." + member.Key, member.Value);
                }
                return;
            }
            if (node.IsArray || node.Kind == JsonKind.Null) return; // scalars only
            sink[prefix] = node.ToConfigValue();
        }

        static FeatureFlagDefinition ReadFlag(string key, JsonValue node, Dictionary<string, ConfigValue> values)
        {
            if (node.Kind == JsonKind.Bool)
            {
                // A plain boolean flag also becomes an overridable value under "flags.<key>".
                values["flags." + key] = ConfigValue.Of(node.Boolean);
                return new FeatureFlagDefinition(key, node.Boolean);
            }

            bool defaultOn = node.TryMember("default", out JsonValue def) && def.Kind == JsonKind.Bool && def.Boolean;
            string experimentKey = node.TryMember("experiment", out JsonValue exp) && exp.Kind == JsonKind.String ? exp.Text : null;
            string requiredVariant = node.TryMember("variant", out JsonValue variant) && variant.Kind == JsonKind.String ? variant.Text : null;
            int rollout = node.TryMember("rollout", out JsonValue roll) && roll.Kind == JsonKind.Number ? roll.ToConfigValue().AsInt(-1) : -1;

            values["flags." + key] = ConfigValue.Of(defaultOn);
            return new FeatureFlagDefinition(key, defaultOn, experimentKey, requiredVariant, rollout);
        }

        static bool TryReadExperiment(JsonValue node, out ExperimentDefinition experiment)
        {
            experiment = null;
            if (!node.IsObject) return false;
            if (!node.TryMember("key", out JsonValue keyNode) || keyNode.Kind != JsonKind.String) return false;

            string salt = node.TryMember("salt", out JsonValue saltNode) && saltNode.Kind == JsonKind.String ? saltNode.Text : keyNode.Text;
            string assigned = node.TryMember("assigned", out JsonValue assignedNode) && assignedNode.Kind == JsonKind.String ? assignedNode.Text : null;

            var ranges = new List<VariantRange>();
            if (node.TryMember("variants", out JsonValue variantsNode) && variantsNode.IsArray)
            {
                foreach (JsonValue v in variantsNode.Items)
                {
                    if (!v.IsObject) continue;
                    string name = v.TryMember("name", out JsonValue n) && n.Kind == JsonKind.String ? n.Text : null;
                    if (name == null) continue;
                    int min = v.TryMember("min", out JsonValue mn) && mn.Kind == JsonKind.Number ? mn.ToConfigValue().AsInt(0) : 0;
                    int max = v.TryMember("max", out JsonValue mx) && mx.Kind == JsonKind.Number ? mx.ToConfigValue().AsInt(100) : 100;
                    ranges.Add(new VariantRange(name, min, max));
                }
            }

            experiment = new ExperimentDefinition(keyNode.Text, salt, ranges, assigned);
            return true;
        }
    }
}
