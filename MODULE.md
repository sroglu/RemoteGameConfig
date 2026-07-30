# RemoteGameConfig

> **Module group — LiveOps** (`RemoteGameConfig`, `PlayerDataSync`, `Commerce`; analytics via HubApp's
> `IAnalyticsProvider`). Grouped by purpose. They are separately publishable, but the group is **not
> dependency-free**: `Commerce` uses `RemoteGameConfig` (product catalog), `PlayerDataSync` uses it
> (install-id), and `RemoteGameConfig` builds on `RemoteResourceCache`; optional/gated integrations are
> Firebase, Unity IAP, BestHTTP. See each module's **Dependencies** for exact edges.

## Purpose

Read tunable game values from a remote backend without shipping an app update — feature flags, A/B
experiments, and typed numbers/strings/bools grouped into sections — keyed by a stable per-install
identity. It **never blocks or crashes the game**: there is always a value, resolved in trust order
`embedded default → cached remote → fresh remote → server override`, key-by-key. The network fetch,
disk cache, TTL, retry/backoff, single-flight dedupe, and offline-serve-last are **reused from
`RemoteResourceCache`**, not reimplemented. The backend is pluggable behind a provider interface
(StaticJson today, Firebase gated, self-host later), so game code never names a backend.

## Scope boundary

Sharp separation from the two neighbouring modules (canonical rule, DESIGN D15):

- **RemoteGameConfig ships small tunable VALUES you READ** — feature flags, A/B experiments,
  numbers/strings/bools, cadences, small JSON tuning tables (e.g. a product catalog). Typed config: a
  knob you read.
- **ContentDelivery ships ASSETS you LOAD BY ADDRESS** — AssetBundles, prefabs, textures, audio,
  scenes, ScriptableObject assets, the content catalog.
- **RemoteResourceCache** is the low-level generic "fetch bytes by URL/key + tiered cache" primitive.
  RemoteGameConfig builds its config-document fetch **on** RemoteResourceCache.
- **Handoff:** config carries **addresses/ids**; ContentDelivery resolves them to assets. Config
  never ships an asset; ContentDelivery never ships a tuning value. Example — RemoteGameConfig serves
  `{ "summerEventEnabled": true, "bannerAddress": "banners/summer2026" }`; ContentDelivery then loads
  `banners/summer2026`.
- **Grey area (a level):** tuning numbers = config; the scene/prefab/art = content.
- **Localization tables** belong to LocalizationService, not here.
- The RemoteGameConfig **document itself** is fetched via RemoteResourceCache, **not** ContentDelivery.

## Assemblies

| Assembly | Folder | Engine refs | Purpose |
|----------|--------|-------------|---------|
| `PFound.RemoteGameConfig.Core` | `Core/Runtime/` | `noEngineReferences: true` | Engine-free: value model, sections, overlay, bucketing, JSON, provider seam, install-id contract, the boot/refresh orchestrator. |
| `PFound.RemoteGameConfig` | `Runtime/` | Unity | Unity glue: persisted install id, StaticJson source over RemoteResourceCache, installer. |
| `PFound.RemoteGameConfig.Firebase` | `Firebase/` | Unity | Firebase Remote Config provider, `defineConstraints: ["PFOUND_FIREBASE"]`. |
| `PFound.RemoteGameConfig.Core.Tests` | `Core/Tests/` | `noEngineReferences: true` | Standalone csc/mono runner for Core + the orchestrator. |
| `PFound.RemoteGameConfig.Tests` | `Tests/` | Editor | EditMode tests for the Unity glue (identity persistence, StaticJson end-to-end). |

Every assembly is `autoReferenced: false` — a consumer references it explicitly.

## Dependencies

- **Core** — none. No PFound modules, no third-party packages, engine-free (testable under mono/csc).
- **Runtime** — `PFound.RemoteGameConfig.Core`, `PFound.RemoteResourceCache`,
  `PFound.RemoteResourceCache.Core`, UnityEngine. Transport is the `PFOUND_BESTHTTP`-gated one supplied
  by RemoteResourceCache (settled default), or a caller-injected `IResourceTransport`.
- **Firebase** — `PFound.RemoteGameConfig.Core` + the Firebase SDK; compiled only when `PFOUND_FIREBASE`
  is defined. With the define absent it does not compile and the module still builds; StaticJson stays usable.

## Public API

**Core — value model & access**
- `ConfigValue` (`ConfigValueKind`) — tagged union of bool/int/float/string with cross-reads and
  `AsBool/AsInt/AsFloat/AsString(fallback)`.
- `ConfigValueMap` — resolved key → value table; `GetBool/GetInt/GetFloat/GetString(key, fallback)`,
  `TryGetValue`, `DiffersFrom`.
- `GameConfigSection` (base, pre-chosen name) + `SectionDefaultWriter` — a typed section names itself,
  `WriteDefaults(...)` to seed the embedded-default tier, and reads section-qualified keys via protected
  typed getters.

**Core — resolution & runtime read surface**
- `ConfigTrustTier`, `ConfigLayer`, `ConfigOverlay.Merge(...)` — key-by-key trust-order merge.
- `GameConfig` — the resolved snapshot: `GetBool/GetInt/GetFloat/GetString`, `Section<T>(...)`,
  `IsEnabled(flag)`, `GetVariant(experiment)`, `event ExperimentExposed`, `AssignedCohorts()`,
  `DiffersFrom`, `Version`, `InstallId`.
- `GameConfigResolver.Resolve(...)` — assembles a `GameConfig` from sections + document tiers + overrides.

**Core — experiments & flags**
- `StableHash.Of/Bucket` — FNV-1a (64-bit) over UTF-8 bytes; run-stable, cross-platform (never
  `GetHashCode`).
- `ExperimentDefinition`, `VariantRange`, `ExperimentAssignment`, `ExperimentExposure`,
  `ExperimentBucketing.Assign(...)` — deterministic bucketing, server-override wins.
- `FeatureFlagDefinition`, `FeatureFlagSet` — plain / percentage-rollout / experiment-gated flags.

**Core — document, provider seam, orchestration**
- `ConfigDocument`, `ConfigDocumentReader.TryParse(...)` — parse a JSON document; all-or-nothing (corrupt
  ⇒ false, no throw).
- `IGameConfigSource.FetchAsync(context, knownVersion, forceRefresh, ct)` — the pluggable backend seam;
  `ConfigFetchContext`, `ConfigFetchResult` (`Fetched/NotModified/Failed`), `ServerOverrides`.
- `IInstallIdentity` — stable per-install id contract.
- `RemoteGameConfigService` — holds `Current`, `RefreshAsync(force)`, `event Changed`,
  `event ExperimentExposed`; boot on defaults, offline-tolerant, change-detecting.

**Runtime (Unity)**
- `PersistentInstallIdentity : IInstallIdentity` — PlayerPrefs-backed GUID, generated once (not
  `deviceUniqueIdentifier`).
- `StaticJsonConfigSource : IGameConfigSource` — JSON-over-URL backend built on `ResourceCache<byte[]>`.
- `RemoteGameConfigInstaller.Build(...)` — wires sections + URL (+ optional baked `TextAsset` manifest)
  into a `RemoteGameConfigService` using `Application.version/platform/systemLanguage`.

**Firebase (gated)**
- `FirebaseGameConfigSource : IGameConfigSource` — reads the config document from a Remote Config
  parameter; brings its own fetch/cache (does not layer RemoteResourceCache).

## Document shape (StaticJson / Firebase)

```json
{
  "version": "2026-07-01",
  "config":  { "gameplay": { "maxLives": 5 }, "economy": { "coinMultiplier": 1.5 } },
  "flags":   { "newShop": true,
               "halloween":  { "default": false, "rollout": 25 },
               "tutorialV2": { "default": false, "experiment": "onboarding", "variant": "treatment" } },
  "experiments": [
    { "key": "onboarding", "salt": "s1",
      "variants": [ { "name": "control", "min": 0, "max": 50 }, { "name": "treatment", "min": 50, "max": 100 } ],
      "assigned": "treatment" }
  ]
}
```
`config` flattens to dotted `"section.key"` entries. A missing key falls through to the lower trust
tier (ultimately the section's built-in default).

## Usage (boot)

```csharp
var sections = new GameConfigSection[] { new GameplaySection(), new EconomySection() };
RemoteGameConfigService config = RemoteGameConfigInstaller.Build(sections, "https://cdn.example.com/game.json");

config.Changed += cfg => RefreshFlags(cfg);
config.ExperimentExposed += e => analytics.RecordExposure(e.ExperimentKey, e.Variant, e.InstallId);

await config.RefreshAsync();               // boot fetch; TTL-served afterwards. Reads valid before it returns.
int lives = config.Current.Section(new GameplaySection()).MaxLives;
bool shop = config.Current.IsEnabled("newShop");
// LiveOps event start:
await config.RefreshAsync(forceRefresh: true);
```

## Verification

- **Core (mono/csc):** `csc -nologo -warn:0 -out:/tmp/pf_rgc.exe Assets/PFound/RemoteGameConfig/Core/Runtime/*.cs Assets/PFound/RemoteGameConfig/Core/Tests/*.cs && mono /tmp/pf_rgc.exe` — 61 tests green.
- **EditMode (Unity):** `Tests/` covers identity persistence + StaticJson end-to-end (boot / offline / change event).

## Deviations from the build spec

- **Orchestrator lives in Core, not Runtime.** `RemoteGameConfigService` (boot fetch + TTL + offline +
  change detection + exposure forwarding) is engine-free, so the whole boot/refresh path is unit-tested
  under mono. The Unity layer only supplies a concrete source, the persisted identity, and app context.
- **Defaults declared, not cloned.** The spec suggested a `CreateDefault()` factory; sections instead
  declare defaults via `WriteDefaults(SectionDefaultWriter)`. Same guarantee (a complete embedded-default
  tier), one source of truth, and no reflection/round-trip. An optional baked `TextAsset` manifest can
  overlay the code defaults for a richer first-launch/offline configuration.
- **Conditional fetch is document-version based, not HTTP ETag** — see the RemoteResourceCache gap below.

## RemoteResourceCache gaps hit

- **No HTTP ETag / `If-None-Match` conditional GET.** RemoteResourceCache keys by URL and expires by TTL
  and content-version; it has no per-request conditional-fetch header hook. `StaticJsonConfigSource`
  therefore does conditional refresh at the **document** level: it compares the parsed document's
  `version` to the caller's known version and answers `NotModified`. Adding an ETag/If-None-Match hook
  to the transport seam would let an unchanged document be short-circuited before the body is downloaded
  — a minimal extension worth considering rather than a fork.
- **No single-key force-evict.** Force refresh clears the whole config cache (`ClearMemory` +
  `ClearDiskAsync`) before re-fetching; acceptable because a config cache instance holds one document
  key. A per-key evict on `ResourceCache<T>` would make this exact.
