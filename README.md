# UNInject – High-Performance Unity Dependency Injection SDK
## Current Version : 2.1.0

![](https://img.shields.io/badge/unity-2021.3%2B-black)
![](https://img.shields.io/badge/license-MIT-blue)
![](https://img.shields.io/badge/IL2CPP-supported-brightgreen)
![](https://img.shields.io/badge/[A.O.T]-supported-brightgreen)
[![Wiki](https://img.shields.io/badge/📖%20Wiki-blue?style=for-the-badge)](https://nightwish-0827.github.io/UNInject/)

> **UNInject** is a dependency injection framework for Unity built on **editor-time baking**, **Roslyn source generation**, **three-tier scoping**, and a `partial`-based **reflection-free runtime path**.

<img width="797" height="86" alt="Image" src="https://github.com/user-attachments/assets/fbba5f3c-6803-41bc-84b9-3ecf957ec7d0" />

`https://github.com/NightWish-0827/UNInject.git?path=/com.nightwishlab.uninject`  
UPM: Add package from git URL

---

# Core Features

### Editor-Bake Architecture

`ObjectInstaller` resolves `[Inject]` fields against the hierarchy at **edit time** (context menu **Bake Dependencies**). Connections are serialized — **no runtime hierarchy scan** occurs for those fields.

---

### Roslyn Source Generation — Full IL2CPP Support

The Roslyn pipeline eliminates IL2CPP/AOT limitations of Expression Tree approaches.

Classes with `[GlobalInject]` / `[SceneInject]` fields are detected at compile time; `partial` class extension code and plan registration code are auto-generated. Generated code uses **no `Expression.Compile()`** — **safe on all AOT platforms**.

The only change required is adding the `partial` keyword.

```csharp
// Before
public class PlayerController : MonoBehaviour { ... }

// After
public partial class PlayerController : MonoBehaviour { ... }
```

Entering Play without `partial` triggers `UNInjectFallbackGuard` warnings; **UNI001** surfaces at compile time in the IDE.

---

### Three-Tier Deterministic Scoping

| Scope | Component | Lifetime |
|-------|-----------|----------|
| Global | `MasterInstaller` | `DontDestroyOnLoad` |
| Scene | `SceneInstaller` | Current scene |
| Local | `ObjectInstaller` | Subtree under installer root |

Scene unload behavior is controlled by **`SceneExitPolicy`** on `SceneInstaller`:

* **`Clear`** — registry cleared on destroy (default).
* **`Preserve`** — registry entries kept across unload (e.g. additive loading).

---

### Runtime Injection Tiers

| Priority | Path | Notes |
|----------|------|-------|
| 1 | Roslyn plan | Dictionary lookup + `AggressiveInlining` setter |
| 2 | Expression tree fallback | Cached delegate; **Mono-only** |
| 3 | `FieldInfo.SetValue` | Last resort; **IL2CPP risk** |

Generated setters use `[MethodImpl(AggressiveInlining)]` — cost equivalent to a cast + assignment.

---

### Play Mode Guards

* **`MasterInstallerPlayModeGuard`** — warns if the global registry is empty before Play.
* **`UNInjectFallbackGuard`** — lists types that will use non-generated injection paths (missing `partial`).

---

# API Reference & Usage

UNInject is centered around pairs of **registration attributes (Provider)** and **injection attributes (Consumer)**.

| Attribute | Role | Scope |
|-----------|------|-------|
| `[Referral]` | Register to global registry | Global |
| `[SceneReferral]` | Register to scene registry | Scene |
| `[GlobalInject]` | Inject from global registry | Global |
| `[SceneInject]` | Inject from scene registry | Scene |
| `[Inject]` | Local injection via editor bake | Local |

---

## Global Scope

Managed by `MasterInstaller`. Persists across scenes via `DontDestroyOnLoad`.  
Run **Refresh Global Registry** in the editor to auto-register components marked `[Referral]`.

```csharp
// Registration — Refresh Global Registry on MasterInstaller
[Referral]
public partial class AudioManager : MonoBehaviour
{
    public void PlaySfx(string key) { /* ... */ }
}

// Injection — classes with GlobalInject fields must be declared partial
public partial class PlayerController : MonoBehaviour
{
    [GlobalInject] private AudioManager _audio;

    private void Start()
    {
        _audio.PlaySfx("jump");
    }
}
```

For interface abstraction, specify `BindType`. Easier to swap in tests.

```csharp
public interface IAudioManager { void PlaySfx(string key); }

[Referral(typeof(IAudioManager))]
public partial class AudioManager : MonoBehaviour, IAudioManager { /* ... */ }

public partial class PlayerController : MonoBehaviour
{
    [GlobalInject] private IAudioManager _audio;
}
```

---

## Scene Scope

Managed by `SceneInstaller` — scoped to the current scene.  
Run **Refresh Scene Registry** in the editor to register `[SceneReferral]` components.

```csharp
// Registration — Refresh Scene Registry on SceneInstaller
[SceneReferral]
public partial class WaveSpawner : MonoBehaviour
{
    public void StartWave(int level) { /* ... */ }
}

// Injection
public partial class HordeDirector : MonoBehaviour
{
    [SceneInject] private WaveSpawner _waves;

    private void OnEnable()
    {
        _waves.StartWave(1);
    }
}
```

---

## Local Scope

Local dependencies scoped to the subtree under the `ObjectInstaller` root.  
`[Inject]` fields are resolved at edit time via **Bake Dependencies**; at runtime Unity deserializes the references.

```csharp
// Components under the ObjectInstaller root
public partial class HUD : MonoBehaviour
{
    [Inject] [SerializeField] private HealthBar _healthBar;
    [Inject] [SerializeField] private PlayerController _player;
}
```

`partial` is not required if there are no `[GlobalInject]` / `[SceneInject]` fields.

---

## Optional Injection

Use `optional: true` to allow injection to succeed even when a binding is missing.

```csharp
public partial class PlayerController : MonoBehaviour
{
    [GlobalInject]                private IInputService _input;  // required
    [SceneInject(optional: true)] private IStageContext _stage;  // optional
}
```

---

## Named Bindings

Use `Id` to distinguish multiple instances of the same type.

```csharp
[Referral("music", typeof(AudioManager))]
public partial class MusicManager : AudioManager { }

[Referral("sfx", typeof(AudioManager))]
public partial class SfxManager : AudioManager { }

public partial class MixerHub : MonoBehaviour
{
    [GlobalInject("music")] private AudioManager _music;
    [GlobalInject("sfx")]   private AudioManager _sfx;
}
```

---

## Injection Callback — `IInjected`

Like `Start` / `Awake`, UNInject provides an explicit callback when injection is complete.  
Called only when all required dependencies have been resolved.

```csharp
public partial class PlayerController : MonoBehaviour, IInjected
{
    [GlobalInject] private IInputService _input;
    [SceneInject(optional: true)] private IStageContext _stage;

    public void OnInjected()
    {
        // Called automatically after all required dependencies are injected
        _input.Enable();
    }
}
```

---

## Pure C# Services — `Create<T>()`

Plain C# classes (no `MonoBehaviour`) can receive injection via `IScope.Create<T>()`.  
The method runs: constructor injection → field injection → tick registration, in that order.

```csharp
public partial class SessionStats
{
    [GlobalInject] private IAnalytics _analytics;

    [InjectConstructor]
    public SessionStats([GlobalInject] AudioManager audio)
    {
        // Constructor parameters are also automatically injected from the registry
    }
}

// Usage — dependencies resolved through the scope of the calling installer
var stats = sceneInstaller.Create<SessionStats>();
```

For frame callbacks, implement `ITickable` / `IFixedTickable` / `ILateTickable`.  
These are automatically driven by the installer that called `Create`.  
`IScopeDestroyable.OnScopeDestroy()` is invoked when the installer is destroyed.

```csharp
public partial class EnemyAIService : ITickable, IScopeDestroyable
{
    [SceneInject] private IWaveSpawner _spawner;

    public void Tick()           { /* runs every Update */ }
    public void OnScopeDestroy() { /* cleanup on scope end */ }
}
```

---

## Runtime Object Injection

Objects spawned at runtime receive injection the same way.

```csharp
// Instantiate a prefab and inject in one call
GameObject instance = objectInstaller.SpawnInjected(enemyPrefab, spawnPos, Quaternion.identity);

// Inject an already-instantiated object
objectInstaller.InjectTarget(existingMonoBehaviour);

// Inject all MonoBehaviours in an object hierarchy
objectInstaller.InjectGameObject(rootGameObject);
```

Runtime registration and unregistration can also be handled in code.

```csharp
// Register — automatically reflects BindType from [Referral] attribute
objectInstaller.Register(enemyView, owner: this);

// Or specify the type explicitly
objectInstaller.Register<IEnemyView>(enemyView, owner: this);

// When owner is destroyed, entries registered under that owner are automatically removed
```

---

## Object Pooling Support

Re-injects dependencies when pulling from pool; automatically clears references on return.

```csharp
public partial class EnemyView : MonoBehaviour, IPoolInjectionTarget
{
    [SceneInject] private IWaveContext _wave;

    public void OnPoolGet()     { /* called when retrieved from pool */ }
    public void OnPoolRelease() { /* called before return; inject fields auto-nulled after */ }
}

// Pull from pool with injection
objectInstaller.InjectTargetFromPool(enemyView);

// Return to pool — inject fields set to null
objectInstaller.ReleaseTargetToPool(enemyView);
```

---

# Performance Benchmark

Benchmark comparing Zenject (Reflection), VContainer (Expression Tree), and UNInject (Roslyn) under identical conditions. Lower is better.

- Target: `BenchmarkTarget` (5 global fields)
- Conditions: 30 iterations, averaged
- Measurement: 100,000 injections (after JIT warmup)

[Image #1]

| Path | Cold Start (ms/call) | Hot 100,000× (ms total) |
|------|---------------------|------------------------|
| Reflection — **Zenject** | 0.0107 ms | 96.67 ms |
| Expression Tree — **VContainer** | 0.4799 ms | 6.61 ms |
| Roslyn — **UNInject** | 0.0059 ms | 6.02 ms |

| Metric | Result |
|--------|--------|
| Hot pass — Roslyn vs Reflection | **16.1×** faster |
| Cold speed — Roslyn vs Expression Tree | **81.9×** faster |
| IL2CPP safe | Roslyn ✓ / Expression Tree ✗ |
| VContainer note | Roslyn option available → UNInject includes it by default |

On Cold Start, Expression Tree is 81.9× slower than Roslyn due to lambda compilation cost.  
The Roslyn path has setters already generated at compile time — zero initial cost.

---

# Editor Tools

Provides intuitive inspector tooling for managing and visualizing the dependency graph.

| Color | Meaning |
|-------|---------|
| Green | Registered (healthy) |
| Gray | Optional — not bound (intentional) |
| Orange / Red | Required — not bound (attention needed) |

<p align="center">
  <img src="https://github.com/user-attachments/assets/84a1af2b-aa68-4722-ad2e-e9a15ed6c0de" width="32%">
  <img src="https://github.com/user-attachments/assets/3858e28f-6332-4048-b80a-1bbcb7fa26d1" width="32%">
  <img src="https://github.com/user-attachments/assets/a9e36b9f-7d35-416c-a5e8-145f13e5ef59" width="32%">
</p>

### Dependency Graph (`UNInjectGraphWindow`)

**Menu:** `Window > UNInject > Dependency Graph`

Visualizes dependency relationships in the scene as a GraphView.  
Edges from Roslyn-generated plans appear green; fallback-path edges appear yellow.

<img width="1232" height="623" alt="image" src="https://github.com/user-attachments/assets/a77b35c6-45e0-4277-a611-71929ff461d8" />

### Bake Validator (`UNInjectBakeValidator`)

**Menu:** `Window > UNInject > Validate Bake`

Runs automatically before each player build. Detects required dependencies missing from the serialized registry.  
Set `UNINJECT_STRICT_BUILD` to abort the build on validation failure.

---
