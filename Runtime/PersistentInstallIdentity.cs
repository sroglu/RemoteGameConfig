using System;
using PFound.RemoteGameConfig.Core;
using UnityEngine;

namespace PFound.RemoteGameConfig
{
    /// <summary>
    /// A stable per-install identity backed by <see cref="PlayerPrefs"/>. On first launch it generates a
    /// fresh GUID and persists it; every later session returns that same value, so an install keeps its
    /// experiment bucket and analytics identity across sessions. It deliberately does NOT use
    /// <see cref="SystemInfo.deviceUniqueIdentifier"/> — that is unstable across reinstalls/OS updates on
    /// iOS and is privacy-sensitive. A generated, app-owned GUID is both stable for this install and
    /// disposable if the app is removed.
    /// </summary>
    public sealed class PersistentInstallIdentity : IInstallIdentity
    {
        public const string DefaultStorageKey = "PFound.RemoteGameConfig.InstallId";

        public string InstallId { get; }

        public PersistentInstallIdentity(string storageKey = DefaultStorageKey)
        {
            string existing = PlayerPrefs.GetString(storageKey, string.Empty);
            if (existing.Length == 0)
            {
                existing = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(storageKey, existing);
                PlayerPrefs.Save();
            }
            InstallId = existing;
        }
    }
}
