using System.Collections.Generic;
using PFound.RemoteGameConfig.Core;
using UnityEngine;

namespace PFound.RemoteGameConfig
{
    /// <summary>
    /// Unity-side wiring that assembles a <see cref="RemoteGameConfigService"/> from the game's sections
    /// and a document URL: a persisted install identity, the StaticJson backend over RemoteResourceCache,
    /// and the app's version / platform / locale as fetch context. The game owns its section list (they
    /// are code types), calls <see cref="Build"/> once during boot, then awaits
    /// <see cref="RemoteGameConfigService.RefreshAsync"/> to pull the remote document. Reads are valid
    /// immediately after Build — before the fetch completes — because the service starts on embedded
    /// defaults (and the optional baked manifest).
    /// </summary>
    public static class RemoteGameConfigInstaller
    {
        public static RemoteGameConfigService Build(
            IReadOnlyList<GameConfigSection> sections,
            string documentUrl,
            TextAsset embeddedManifest = null)
        {
            var identity = new PersistentInstallIdentity();
            var source = new StaticJsonConfigSource(documentUrl);
            ConfigDocument manifest = ParseManifest(embeddedManifest);

            return new RemoteGameConfigService(
                sections,
                source,
                identity,
                Application.version,
                Application.platform.ToString(),
                Application.systemLanguage.ToString(),
                manifest);
        }

        /// <summary>
        /// Assemble a service from already-constructed parts (custom source/identity, e.g. a self-host
        /// backend or a test double). The app's version/platform/locale still supply the fetch context.
        /// </summary>
        public static RemoteGameConfigService Build(
            IReadOnlyList<GameConfigSection> sections,
            IGameConfigSource source,
            IInstallIdentity identity,
            ConfigDocument embeddedManifest = null)
        {
            return new RemoteGameConfigService(
                sections,
                source,
                identity,
                Application.version,
                Application.platform.ToString(),
                Application.systemLanguage.ToString(),
                embeddedManifest);
        }

        static ConfigDocument ParseManifest(TextAsset embeddedManifest)
        {
            if (embeddedManifest == null) return null;
            return ConfigDocumentReader.TryParse(embeddedManifest.text, out ConfigDocument document) ? document : null;
        }
    }
}
