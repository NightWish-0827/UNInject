# UNInject – High-Performance Unity Dependency Injection SDK
## Current Version : 2.1.0

![](https://img.shields.io/badge/unity-2021.3%2B-black)
![](https://img.shields.io/badge/license-MIT-blue)
![](https://img.shields.io/badge/IL2CPP-supported-brightgreen)
![](https://img.shields.io/badge/[A.O.T]-supported-brightgreen)

> **UNInject** is a dependency injection framework for Unity built on **editor-time baking**, **Roslyn source generation**, and **three-tier scoping** with a **runtime path that avoids reflection** when types are declared `partial`.

<img width="797" height="86" alt="Image" src="https://github.com/user-attachments/assets/fbba5f3c-6803-41bc-84b9-3ecf957ec7d0" />

`https://github.com/NightWish-0827/UNInject.git?path=/com.nightwishlab.uninject`  
UPM: Add package from git URL

---

# Table of Contents

* [Core Features](#core-features)
* [Pure C# service layer](#pure-c-service-layer)
* [API Reference & Usage](#api-reference--usage)
* [Usage patterns: concrete types & MonoBehaviour](#usage-patterns-concrete-types--monobehaviour)
* [IScope, Registry & Named Bindings](#iscope-registry--named-bindings)
* [Per-installer API surface](#per-installer-api-surface)
* [Constructor Injection & `Create<T>()`](#constructor-injection--createt)
* [Tickables & Scope Teardown](#tickables--scope-teardown)
* [User Callbacks — Reference](#user-callbacks--reference)
* [Lifecycle & Internal Architecture](#lifecycle--internal-architecture)
* [Dynamic Object Support](#dynamic-object-support)
* [Performance & Injection Paths](#performance--injection-paths)
* [Editor Supports](#editor-supports)
* [Common Editor Warnings](#common-editor-warnings)

---

# Core Features

### Editor-backed bake

`ObjectInstaller` resolves **`[Inject]`** fields against the hierarchy at edit time (context menu **Bake Dependencies**). Connections are serialized; **no runtime hierarchy scan** for those fields.

### Roslyn source generator (IL2CPP-safe)

At compile time the generator emits in-memory artifacts (never written as user files):

* **`{TypeName}.UNInject.g.cs`** — `partial` class extension with `[MethodImpl(AggressiveInlining)]` setters  
* **`UNInjectPlanRegistry.g.cs`** — registers plans after assemblies load  

Generated code uses **casts and direct assignment only** — **no `Expression.Compile()`**.

**Requirement:** any type with `[GlobalInject]` / `[SceneInject]` (or generated constructor plans) must be declared **`public partial class`**.

**Diagnostics:** entering Play without `partial` triggers **`UNInjectFallbackGuard`** warnings; **`UNI001`** surfaces at compile time in the IDE.

### Runtime injection tiers

| Priority | Path | Notes |
|----------|------|--------|
| 1 | Roslyn plan | Dictionary lookup + inlined setter |
| 2 | Expression tree fallback | Cached delegate; **Mono-oriented** |
| 3 | `FieldInfo.SetValue` | Last resort; **IL2CPP risk** if hit hot |

### Three-tier scoping

| Scope | Component | Lifetime |
|--------|-----------|----------|
| Global | `MasterInstaller` | `DontDestroyOnLoad` |
| Scene | `SceneInstaller` | Current scene (policy below) |
| Local | `ObjectInstaller` | Subtree under the installer root |

Scene unload behavior is controlled by **`SceneExitPolicy`** on `SceneInstaller`:

* **`Clear`** — registry cleared on destroy (default).  
* **`Preserve`** — registry entries kept across unload (e.g. additive loading); ownership maps are still cleared so stale `UnregisterByOwner` paths do not strip preserved bindings.

### Play mode guards

* **`MasterInstallerPlayModeGuard`** — warns if the baked global referral list is empty before Play.  
* **`UNInjectFallbackGuard`** — lists types that will run on **non-generated** injection paths (missing `partial`, constructor fallback, etc.).

### Runtime registry shape

Registries use **`Dictionary<RegistryKey, Component>`** where **`RegistryKey` = (Type, Id)**.  
`Id == string.Empty` matches **v1.x keyless** registration. Named referrals use matching **`Id`** on **`[Referral]` / `[SceneReferral]`** and on **`[GlobalInject]` / `[SceneInject]`** (fields and constructor parameters).

---

# Pure C# service layer

UNInject distinguishes two runtime roles:

| Layer | What it is | How it is registered | Typical use |
|--------|------------|----------------------|-------------|
| **Manager layer** | `MonoBehaviour` with `[Referral]` / `[SceneReferral]` | Editor refresh → baked lists → `Awake` registry build | `UnityEngine.Object` lifetime, scene or DDOL |
| **Service layer** | **Plain C# `class`** (no `MonoBehaviour`), **`partial`** for inject fields | Not in hierarchy — created only through **`IScope.Create<T>()`** | Application/domain logic, facades, systems that consume managers via `[GlobalInject]` / `[SceneInject]` |

**Service layer** instances:

* Are **`ReferenceType`** objects constructed with **`[InjectConstructor]`** (or exactly **one** `public` constructor when no attribute) plus optional **Roslyn `TryGetGeneratedFactory`** path for zero-reflection creation.
* Resolve **`Component`** dependencies through the **same** `RegistryKey` rules as MonoBehaviours: constructor parameters may carry **`[GlobalInject]`** / **`[SceneInject]`** (including **named `Id`** and **`optional`**).
* **Do not** receive `UnityEngine.Object` messages — no `Update`/`OnDestroy` from Unity. Participation is explicit:
  * **`ITickable` / `IFixedTickable` / `ILateTickable`** → driven by the **`MonoBehaviour` installer** that called `Create` (`Update` / `FixedUpdate` / `LateUpdate` forwarding).
  * **`IScopeDestroyable`** → teardown when that installer’s `OnDestroy` runs (see [Tickables & Scope Teardown](#tickables--scope-teardown)).
* **Lifetime** is bound to the **owning scope’s** `GameObject`: when `MasterInstaller` / `SceneInstaller` / `ObjectInstaller` is destroyed, `TickableRegistry.ClearWithDestroy()` runs **`OnScopeDestroy()`** for registered destroyables, then clears tick lists.

**Choosing a scope for `Create<T>()`**

* **`MasterInstaller.Create<T>()`** — resolver sees **global** first, then **`SceneInstaller`** fallthrough. Use for game-wide services that must survive scene loads (installer is DDOL).
* **`SceneInstaller.Create<T>()`** — resolver sees **scene** first, then **`MasterInstaller`**. Use for session/scene-bound logic.
* **`ObjectInstaller.Create<T>()`** — resolver uses **local registry** → optional **`_parentScope`** chain → scene → global. Use for subtree-local services (e.g. under one UI root or pooled subsystem).

**Not supported on `Create<T>()`**

* **`MonoBehaviour`** — Unity forbids sensible constructor-driven construction here; use **`SpawnInjected`** / **`InjectTarget`** / **`InjectGameObject`**.

> **Note on `ScriptableObject`:** SDK summaries sometimes mention it beside “plain C#”. **`Create<T>()` uses constructor invocation (or generated factory)**. Unity **`ScriptableObject`** instances are normally created with **`ScriptableObject.CreateInstance`**, not a public `ctor` in the same way. Treat **`Create<T>()`** as targeting **non-`UnityEngine.Object`** service types unless you provide a **generator-produced factory** that correctly constructs your asset type.

---

# API Reference & Usage

Resolution always uses **`RegistryKey(Type, Id)`**: the **declared field** (or ctor parameter) **`Type`** must match a key the registry has. That key may come from a **concrete** `MonoBehaviour` registration (“wide” auto-mapping) or from a **narrow** **`[Referral(BindType)]`** (“expert” single key). **Beginners and experts hit the same `Resolve`** — only the registration shape differs.

---

## Usage patterns: concrete types & MonoBehaviour

### Pattern 1 — Global manager: concrete type only (no interface required)

Many teams reference **`AudioManager`** or **`GameSettings`** directly. You **do not** have to introduce an interface.

**Provider (baked into `MasterInstaller`):**

```csharp
// [Referral] with no BindType → wide registration: concrete + mappable interfaces + bases
[Referral]
public partial class AudioManager : MonoBehaviour
{
    public void PlaySfx() { /* ... */ }
}
```

**Consumer (`partial` + generator):**

```csharp
public partial class PlayerController : MonoBehaviour
{
    [GlobalInject] private AudioManager _audio;
}
```

**Equivalent “interface-first” style** (same `Resolve`, clearer seams for tests):

```csharp
public interface IAudioManager { void PlaySfx(); }

[Referral(typeof(IAudioManager))]
public partial class AudioManager : MonoBehaviour, IAudioManager
{
    public void PlaySfx() { /* ... */ }
}

public partial class PlayerController : MonoBehaviour
{
    [GlobalInject] private IAudioManager _audio;
}
```

After **`Refresh Global Registry`**, both consumers receive the same instance when a single **`AudioManager`** is in the scene.

---

### Pattern 2 — Scene manager: concrete vs `SceneReferral(BindType)`

```csharp
[SceneReferral]
public partial class WaveSpawner : MonoBehaviour { /* ... */ }

// Consumer under ObjectInstaller (same scene)
public partial class HordeDirector : MonoBehaviour
{
    [SceneInject] private WaveSpawner _waves;
}
```

Or with an abstraction:

```csharp
public interface IWaveSpawner { }

[SceneReferral(typeof(IWaveSpawner))]
public partial class WaveSpawner : MonoBehaviour, IWaveSpawner { }

public partial class HordeDirector : MonoBehaviour
{
    [SceneInject] private IWaveSpawner _waves;
}
```

Run **`Refresh Scene Registry`** on the active **`SceneInstaller`**.

---

### Pattern 3 — Local subtree: `[Inject]` + sibling / child `MonoBehaviour` (no global registry)

Typical “beginner” hierarchy: everything is **concrete** `MonoBehaviour` references under one **`ObjectInstaller`** root.

```csharp
public partial class HUD : MonoBehaviour
{
    [Inject] [SerializeField] private HealthBar _healthBar;   // child or sibling under same root
    [Inject] [SerializeField] private PlayerController _player; // SerializeField + Bake Dependencies
}
```

* **`[Inject]`** is resolved **in the editor** (**Bake Dependencies**); at runtime Unity deserializes the references — **no `[GlobalInject]`** involved.  
* Still declare **`public partial class`** if the same type also has `[GlobalInject]` / `[SceneInject]` fields elsewhere.

---

### Pattern 4 — Named bindings when multiple instances share a field type

Without **`Id`**, only **one** registration wins per key (duplicates log a warning). With **two** `AudioManager` objects you must use **named** referrals and matching inject **`Id`**.

```csharp
[Referral("music", typeof(AudioManager))]
public partial class MusicManager : AudioManager { }

[Referral("sfx", typeof(AudioManager))]
public partial class SfxManager : AudioManager { }

public partial class MixerHub : MonoBehaviour
{
    [GlobalInject("music")] private AudioManager _music;
    [GlobalInject("sfx")]  private AudioManager _sfx;
}
```

You can name **`BindType`** as an interface instead; the **`Id`** on **`[Referral]`** and **`[GlobalInject]`** must still match pairwise.

---

### Pattern 5 — Runtime-spawned `MonoBehaviour` (concrete `Register` vs `Register<IBind>`)

Spawning an enemy prefab that **does not** rely on bake-time global discovery:

```csharp
public partial class EnemyView : MonoBehaviour, IEnemyView { /* ... */ }

// After Instantiate —
var scope = GetComponent<ObjectInstaller>(); // or cached reference
scope.Register<EnemyView>(enemyView, owner: this);           // expert: bind as EnemyView only if you use that key
scope.Register(enemyView, owner: this);                      // beginner-friendly: same as inspector [Referral] on prefab type
```

If the prefab’s class carries **`[Referral(typeof(IEnemyView))]`**, **`Register(enemyView)`** picks up **`BindType`** automatically. If it is **only** a concrete class, registration still creates keys for **`typeof(EnemyView)`** (and interfaces/bases per helper rules), so **`[GlobalInject] private EnemyView _x`** on another type continues to work **without** an interface.

---

### Pattern 6 — `ObjectInstaller` reference (any access style)

All of the following are valid as long as you end up calling the **same** instance:

```csharp
[SerializeField] private ObjectInstaller _scope;                    // drag in inspector (common for beginners)

// or
private ObjectInstaller Scope => GetComponentInParent<ObjectInstaller>();

// or (scene-local singleton path)
SceneInstaller.Instance.Create<MyService>(); // services created from Scene scope — no ObjectInstaller field needed
```

Use **`ObjectInstaller`** when injection must respect **local registry + `_parentScope`**; use **`MasterInstaller` / `SceneInstaller`** when the service should use those installers’ native chains only.

---

### Pattern 7 — `Create<T>()` with concrete ctor parameters (service layer)

```csharp
public partial class SessionStats
{
    [GlobalInject] private IAnalytics _analytics; // injected after ctor body runs

    [InjectConstructor]
    public SessionStats([GlobalInject] AudioManager audio, WaveSpawner waves)
    {
        // Same instances you would get from [GlobalInject] / [SceneInject] fields on this type
    }
}

// var stats = sceneInstaller.Create<SessionStats>();
```

`AudioManager` / **`WaveSpawner`** are **concrete** `Component` types resolved from registries exactly like **`GlobalInject` fields**. Interfaces work the same if those types are registered under interface keys.

---

## Provider attributes (registration)

```csharp
// Expert: single bind type (only BindType + Id keys are registered)
[Referral(typeof(IInputService))]
public partial class DesktopInputManager : MonoBehaviour, IInputService { }

// Named expert binding
[Referral("profileA", typeof(IProfileService))]
public partial class ProfileServiceA : MonoBehaviour, IProfileService { }

// Scene expert binding
[SceneReferral(typeof(ILevelRules))]
public partial class LevelRules : MonoBehaviour, ILevelRules { }
```

```csharp
// Beginner-friendly: minimal attribute; Refresh Global Registry picks up this type
[Referral]
public partial class AudioManager : MonoBehaviour { /* ... */ }
```

(`Refresh` only sees types with **`[Referral]`** / **`[SceneReferral]`** on the **component class**. Purely runtime objects can skip attributes and use **`Register`** instead.)

Editor **Refresh Global Registry** / **Refresh Scene Registry** scans the scene and fills serialized lists; `Awake` rebuilds fast dictionaries.

## Consumer attributes

```csharp
public partial class PlayerController : MonoBehaviour
{
    [Inject] [SerializeField] private Animator _animator;

    [GlobalInject] private IInputService _input;
    [GlobalInject] private AudioManager _audioConcrete;   // valid when AudioManager is registered under that Type

    [GlobalInject("profileA")] private IProfileService _profile;

    [SceneInject(optional: true)] private LevelManager _level;
}
```

**Optional:** `[GlobalInject(optional: true)]` / `[SceneInject(optional: true)]` — missing bindings do **not** fail injection; warnings are suppressed for those fields.

Inspector draw policy: optional unbound fields render **gray**; missing **required** bindings render as errors/warnings depending on context.

---

# IScope, Registry & Named Bindings

**`IScope`** is implemented by **`MasterInstaller`**, **`SceneInstaller`**, and **`ObjectInstaller`**.

### Register

* **`Register(Component comp)`** — register using `[Referral]` / `[SceneReferral]` metadata on `comp`’s type (bind type + id).  
* **`Register(Component comp, MonoBehaviour owner)`** — same; when **`owner`** is destroyed, all components registered with that owner are unregistered (`ScopeOwnerTracker`).  
* **`Register<TBind>(Component comp, MonoBehaviour owner = null)`** — **code-first** binding: registers **`comp`** as **`TBind`** (attribute bind type optional).

Duplicate keys log a warning and keep the **first** registration.

### Unregister

* **`Unregister(Type type)`** — keyless (`Id == default`).  
* **`Unregister(Type type, string id)`** — named key removal.  
* **`Unregister(Component comp)`** — **`MasterInstaller`** / **`SceneInstaller` only**: removes **every registry key** whose value is **`comp`**. (`ObjectInstaller` has no overload; unregister by type/id or rely on owner-driven cleanup.)

### Resolve

Keyless:

* **`Resolve(Type type)`**, **`TryResolve(Type, out Component)`**, **`Resolve<T>() where T : Component`**, **`ResolveAs<T>() where T : class`**

Named:

* **`Resolve(Type type, string id)`**, **`TryResolve(Type, string id, out Component)`**, **`ResolveAs<T>(string id)`**

**`MasterInstaller`** also exposes **`ResolveStatic<T>()`** as a convenience over **`Instance`**.

### ObjectInstaller resolve chain

**`ObjectInstaller`** resolves in order:

1. Local registry (`RegistryKey`)  
2. If **`_parentScope`** is set — that installer’s chain (recursive)  
3. Else **`SceneInstaller.Instance`** then **`MasterInstaller.Instance`**

`_parentScope` is optional and **serialized** for nested installer graphs (pools, isolated subtrees).

---

# Per-installer API surface

Beyond **`IScope`**, these public entry points exist (editor APIs omitted except where noted):

### `MasterInstaller`

| Category | Members |
|----------|---------|
| Access | **`Instance`**, **`ResolveStatic<T>()`** |
| `IScope` | Full contract including **`Create<T>()`**, **`UnregisterTickable`** variants |
| Registry cleanup | **`Unregister(Component comp)`** (all keys for that instance) |
| Try patterns | **`TryResolve<T>(out T)`**, **`TryResolveAs<T>(out T)`** (not on `ObjectInstaller`) |
| Editor | **`RefreshRegistry()`**, **`GetGlobalComponent` / `GetGlobalComponent<T>`** |

### `SceneInstaller`

| Category | Members |
|----------|---------|
| Access | **`Instance`** |
| `IScope` | Full contract |
| Policy | **`SceneExitPolicy`** field (Clear / Preserve) |
| Registry cleanup | **`Unregister(Component comp)`** |
| Try patterns | **`TryResolve<T>(out T)`**, **`TryResolveAs<T>(out T)`** |
| Editor | **`RefreshSceneRegistry()`** |

### `ObjectInstaller`

| Category | Members |
|----------|---------|
| `IScope` | **`Register`**, **`Register<TBind>`**, **`Unregister(Type[, id])`**, **`Resolve` / `TryResolve`**, **`Create<T>()`**, **`UnregisterTickable`** — **no** **`Unregister(Component)`**, **no** generic **`TryResolve<T>(out T)`** |
| Hierarchy injection | **`InjectTarget`**, **`TryInjectTarget`**, **`InjectGameObject`**, **`SpawnInjected`** overloads |
| Pooling | **`InjectTargetFromPool`**, **`ReleaseTargetToPool`** |
| Scope graph | **`_parentScope`** (serialized) |
| Editor | **`BakeDependencies()`** |

### `ObjectInstaller` — injection, spawn, and pooling (behavioral reference)

These methods use the same **`TypeDataCache`** field plans as runtime injection elsewhere. **`[Inject]`** (local bake) is **not** applied here — only **`[SceneInject]`** then **`[GlobalInject]`** in that order.

| Method | Signature / notes |
|--------|-------------------|
| **`InjectTarget`** | **`void InjectTarget(MonoBehaviour target)`** — thin wrapper: **`TryInjectTarget(target, logWarnings: true, isReinjection: false)`**. |
| **`TryInjectTarget`** | **`bool TryInjectTarget(MonoBehaviour target, bool logWarnings = true, bool isReinjection = false)`** — resolves each inject field via **`Resolve(field.FieldType, field.Id)`** on this installer’s **chain** (local → parent / scene / master). **Return value:** `true` only if **every required** field resolved; optional misses do not force `false`. **`logWarnings`:** when `true`, required failures emit **`Debug.LogWarning`** with field/type/id context. **`isReinjection`:** when `true`, **`IInjected.OnInjected`** is skipped; **`IPoolInjectionTarget.OnPoolGet`** runs on success instead (if implemented). |
| **`InjectGameObject`** | **`void InjectGameObject(GameObject root, bool includeInactive = true)`** — **`GetComponentsInChildren<MonoBehaviour>`** then **`InjectTarget`** per instance. |
| **`SpawnInjected`** (prefab) | **`GameObject SpawnInjected(GameObject prefab)`** — `Instantiate` at identity; **`InjectGameObject(instance)`**. |
| | **`GameObject SpawnInjected(GameObject prefab, Transform parent)`** — same with parent. |
| | **`GameObject SpawnInjected(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)`** — positioned instantiate + **`InjectGameObject`**. |
| **`SpawnInjected`** (`MonoBehaviour`) | **`T SpawnInjected<T>(T prefab) where T : MonoBehaviour`** — `Instantiate(prefab)` + **`InjectTarget(instance)`** (single component, not whole hierarchy unless you add calls). |
| | **`T SpawnInjected<T>(T prefab, Vector3, Quaternion, Transform parent = null)`** — positioned variant + **`InjectTarget`**. |
| **`InjectTargetFromPool`** | **`void InjectTargetFromPool(MonoBehaviour target)`** → **`TryInjectTarget(target, logWarnings: true, isReinjection: true)`**. |
| **`ReleaseTargetToPool`** | **`void ReleaseTargetToPool(MonoBehaviour target)`** — invokes **`IPoolInjectionTarget.OnPoolRelease`** when present; then sets **all** cached **`[GlobalInject]` / `[SceneInject]`** fields on that type to **`null`** via **`TypeDataCache`** setters. |

**`Register` / `Register<TBind>` reminder (local scope):** mappings use **`ReferralAttribute`** / **`SceneReferralAttribute`** on the **concrete** `Component` type for **`BindType`** and **`Id`**; **`Register<TBind>`** overrides the bind type when supplied. **`InstallerRegistryHelper`** registers the concrete type, every **mappable** interface (skipping `System.*`, `UnityEngine.*`, etc.), and base types up to but **not including** **`MonoBehaviour`**, all under the same **`RegistryKey`**.

### `TypeDataCache` (diagnostics / warmup)

Public helpers you may call from startup or tests:

* **`Warmup(Type)` / `Warmup(params Type[])`** — pre-populate field and constructor caches.
* **`HasAnyInjectField`**, **`HasGeneratedGlobalPlan`**, **`HasGeneratedScenePlan`**, **`HasGeneratedFactory`** — introspection for tooling.
* **`GetGlobalInjectFields` / `GetSceneInjectFields`** — returns cached **`CachedInjectField`** lists.

`RegisterGenerated*` APIs are **for the generator only** (`[EditorBrowsable(Never)]`).

---

# Constructor Injection & `Create<T>()`

**`IScope.Create<T>() where T : class`** runs **`InstallerRegistryHelper.CreateAndInject<T>`** with this **exact order**:

1. **Construction** — `TypeDataCache.TryGetGeneratedFactory` **or** `[InjectConstructor]` / single public `ctor` + **`ConstructorInfo.Invoke`**. Unresolvable required ctor parameter → **`InvalidOperationException`**.
2. **Field injection** — all **`[SceneInject]`** fields, then **`[GlobalInject]`** fields (same optional/required rules as components). Partial failure sets internal `success` false and logs warnings for required misses.
3. **`IInjected.OnInjected()`** — invoked **inside** field injection when **all required** inject fields succeeded (**optional** misses do not block success).
4. **`RegisterTickables(object)`** — if `T` implements **`ITickable` / `IFixedTickable` / `ILateTickable` / `IScopeDestroyable`**, registrars on the **calling** installer’s **`TickableRegistry`** pick them up **after** `OnInjected`.

**Resolve fallthrough** for ctor/fields matches each installer (**Master** → scene; **Scene** → master; **Object** → local → parent → scene → master).

---

# Tickables & Scope Teardown

Plain C# services from **`Create<T>()`** can receive Unity frame callbacks **without** `PlayerLoop` manipulation:

| Interface | Invoked from |
|-----------|----------------|
| **`ITickable`** | Host **`Update`** → **`Tick()`** |
| **`IFixedTickable`** | **`FixedUpdate`** → **`FixedTick()`** |
| **`ILateTickable`** | **`LateUpdate`** → **`LateTick()`** |

**Rules**

* The **host** is always the **`MonoBehaviour`** **`IScope`** that invoked **`Create`**.  
* Ticks **do not propagate** to parent scopes.  
* **`UnregisterTickable(...)`** removes from that scope; **`Tick()`** uses a **snapshot** — removals during iteration take effect **next frame**.  
* One object may implement **several** of **`ITickable` / `IFixedTickable` / `ILateTickable`**; each matching list is populated.

**`IScopeDestroyable` during teardown**

On installer **`OnDestroy`**, **`TickableRegistry.ClearWithDestroy()`**:

1. Iterates **`_destroyables`** and calls **`OnScopeDestroy()`** ( **`try/catch` per item** — one failure does not skip others ).
2. Clears tick lists and internal snapshot state.

So **`OnScopeDestroy`** runs **before** tick lists are cleared for that scope; it does **not** run on ordinary per-frame tick shutdown.

---

# User Callbacks — Reference

| Callback | Target | When it runs | Caller / path |
|----------|--------|----------------|---------------|
| **`IInjected.OnInjected()`** | **`MonoBehaviour`** or **`Create<T>()`** instance that implements **`IInjected`** | After **required** `[GlobalInject]`/`[SceneInject]` fields are applied successfully for that pass | **`TryInjectTarget`** (`!isReinjection`) or **`InjectFields`** inside **`CreateAndInject`** |
| **`IPoolInjectionTarget.OnPoolGet()`** | **`MonoBehaviour`** with pool interface | After **`TryInjectTarget(..., isReinjection: true)`** succeeds | **`InjectTargetFromPool`** only — **not** combined with **`OnInjected`** on the same success branch |
| **`IPoolInjectionTarget.OnPoolRelease()`** | Same | **Start** of **`ReleaseTargetToPool`**, **before inject fields cleared to `null`** | **`ReleaseTargetToPool`** |
| **`IScopeDestroyable.OnScopeDestroy()`** | **`Create<T>()`** instance (not `MonoBehaviour`) | Installer **`OnDestroy`**, during **`ClearWithDestroy`** | **`TickableRegistry`** |
| **`IUnregistered.OnUnregistered()`** | Any (interface exists) | *Not invoked by current runtime* | Reserved; no automatic SDK dispatch today |

### `IInjected` — detail

* **Required vs optional:** Missing **optional** dependencies **do not** set `success` to false; **`OnInjected`** still runs when all **required** fields resolved.  
* **Re-injection:** **`isReinjection: true`** **skips** **`OnInjected`**. If the target implements **`IPoolInjectionTarget`**, **`OnPoolGet`** runs instead; otherwise **no** inject-completion callback fires for that pass.  
* **Re-entrancy:** Each successful **`TryInjectTarget`** / successful **`Create`** field phase can fire **`OnInjected`** again — not “once per instance globally”.

### `IPoolInjectionTarget` — field semantics

After **`OnPoolRelease`**, **`ReleaseTargetToPool`** assigns **`null`** to every cached **`[GlobalInject]`** and **`[SceneInject]`** field on that type via `TypeDataCache` setters — pool instances should not retain stale **`Component`** references.

### `IScopeDestroyable` — pairing with ticks

Use for **disposal** (event unsubscribe, closing handles) **without** relying on `UnityEngine.Object` destruction on a plain C#. Combine with **`ITickable`** on the same service if needed; destroy callbacks run in **`ClearWithDestroy`**, independent of per-frame tick snapshot rules.

---

# Lifecycle & Internal Architecture

```
[Compile]
UNInjectGenerator → partial setters + plan registry registration

[RuntimeInitialize SubsystemRegistration]
TypeDataCache static caches cleared

[AfterAssembliesLoaded]
Generated plans registered into TypeDataCache

[Order -1000] MasterInstaller.Awake → RebuildRuntimeRegistry from baked list
[Order -900]  SceneInstaller.Awake  → RebuildSceneRegistry from baked list
[Order -500]  ObjectInstaller.Awake  → InjectGlobalDependencies (TypeDataCache plan / fallback)
```

**Safety net (Master / Scene):** first lookup miss after a registry build may trigger **one** silent rebuild; flag arms again on explicit **`Register`** / editor refresh / **`Rebuild*Registry`**.

---

# Dynamic Object Support

Runtime-spawned objects use the same **`TypeDataCache`** path as static ones.

```csharp
ObjectInstaller scope = ...;

var instance = Instantiate(enemyPrefab);
scope.InjectTarget(instance);

scope.InjectGameObject(instanceRoot, includeInactive: true);
```

`partial` types keep the **generated** path at runtime (IL2CPP-safe if the generator ran).

---

# Performance & Injection Paths

**`[Inject]` / bake:** deserialization-only at runtime for those references.

| Path | When | IL2CPP |
|------|------|--------|
| Generated plan | `partial` + generator | Safe |
| Expression | no `partial`, Mono | Avoid for shipping |
| `FieldInfo` | fallback | Risk |

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
internal void __UNInject_Global_input(object __v) => _input = (IInputService)__v;
```

<img width="787" height="846" alt="image" src="https://github.com/user-attachments/assets/1d2c472b-cd44-4df9-9630-610be6236103" />

---

# Editor Supports

## Inspector tooling

Custom **`Editor`** scripts drive **`MasterInstaller`**, **`SceneInstaller`**, and **`ObjectInstaller`** inspectors (registry refresh buttons, dependency visualization, read-only **`[Inject]`** drawer, optional-field coloring).

| Color | Meaning |
|-------|---------|
| Green | Bound / registered |
| Gray | Optional, intentionally empty |
| Orange / red (context) | Required missing |

The dependency list is **not truncated** — long lists are treated as architectural signal, not UI noise.

Runtime **`ContextMenu`** items on installer components include **`Bake Dependencies`** (`ObjectInstaller`), **`Refresh Global Registry`** (`MasterInstaller`), **`Refresh Scene Registry`** (`SceneInstaller`) — same actions as inspector workflows.

<p align="center">
  <img src="https://github.com/user-attachments/assets/84a1af2b-aa68-4722-ad2e-e9a15ed6c0de" width="32%">
  <img src="https://github.com/user-attachments/assets/3858e28f-6332-4048-b80a-1bbcb7fa26d1" width="32%">
  <img src="https://github.com/user-attachments/assets/a9e36b9f-7d35-416c-a5e8-145f13e5ef59" width="32%">
</p>

## Dependency Graph (`UNInjectGraphWindow`)

**Menu:** **`Window > UNInject > Dependency Graph`**

**Class:** **`UNInjectGraphWindow`** — builds a **`GraphView`** (UI Toolkit) for the **current editor state** (not a saved asset).

**What it shows**

| Node / element | Meaning |
|----------------|---------|
| **Installer** | **`MasterInstaller [Global]`** and **`SceneInstaller [Scene]`** (purple header). |
| **Referral** | Components in a **valid scene** carrying **`[Referral]`** or **`[SceneReferral]`**. Cyan = global, green = scene. Label includes **`[id:"…"]`** when the referral declares a named **`Id`**. |
| **Inject target** | Types that declare at least one **`[GlobalInject]`** or **`[SceneInject]`** field. **Gold** if every required dependency edge resolves; **red** if any **`RegistryKey(fieldType, fieldId)`** is missing from the referral map. |
| **Edges** | **Installer → Referral** = registration edge. **Referral → Inject target** = dependency edge; **green** tint if that consumer type has a **Roslyn-generated** plan for that field (**`TypeDataCache.HasGeneratedGlobalPlan` / `HasGeneratedScenePlan`**), **yellow** if it will use **fallback** injection. |

**Toolbar:** **Refresh** rescans loaded assemblies and scene components (use after registry refresh or code changes).

Assembly scanning for inject targets uses the same **`ShouldSkipAssembly`** policy as other diagnostics (see **Bake Validator** below) so framework assemblies are ignored.

<img width="1232" height="623" alt="image" src="https://github.com/user-attachments/assets/a77b35c6-45e0-4277-a611-71929ff461d8" />

## Bake Validator (`UNInjectBakeValidator`)

**Menu:** **`Window > UNInject > Validate Bake`**

**Class:** **`UNInjectBakeValidator`** — implements **`IPreprocessBuildWithReport`** (`callbackOrder == 0`), so it also runs automatically **before each player build**.

**Algorithm (summary)**

1. Scan loaded **game** assemblies for **non-optional** **`[GlobalInject]`** / **`[SceneInject]`** fields.  
   * **Keyless** fields contribute **`field.FieldType`**.  
   * **Named** fields contribute **`RegistryKey(field.FieldType, id)`** (never mixed with the keyless pass).  
2. For **each scene enabled in `EditorBuildSettings`**, open it **additively** (or use the already-open scene), read **`MasterInstaller._globalReferrals`** / **`SceneInstaller._sceneReferrals`** via **`SerializedObject`**, and mark which **concrete** and **abstract/interface** keys are covered (mirrors runtime **`RegisterTypeMappings`** widening).  
3. Emit **`Debug.LogError`** lines for each **required** inject that has **no matching referral** in the baked lists.

**Outcomes**

* **Default:** errors are logged; the build **continues** unless you opt into strict mode.  
* **`UNINJECT_STRICT_BUILD`**: any validation error throws **`BuildFailedException`** and **aborts the build**.

The menu command shows a **`DisplayDialog`** summary; **`RunValidation()`** is **`public static`** if you want to invoke the same checks from CI or custom editor buttons.

## Play mode guards (editor-only)

| Type | Trigger | Role |
|------|---------|------|
| **`MasterInstallerPlayModeGuard`** | Exiting Edit Mode → Play | If a **`MasterInstaller`** exists in the scene and **`_globalReferrals.arraySize == 0`**, logs the **empty global registry** warning. |
| **`UNInjectFallbackGuard`** | Same transition | Lists types that rely on **Expression Tree / reflection** paths (missing **`partial`**, runtime-register candidates without generated plans, constructor fallback usage, etc.). |

These are independent hooks; both may fire in the same Play enter.

## Scripting define symbols (optional tooling)

| Symbol | Effect |
|--------|--------|
| **`UNINJECT_STRICT_BUILD`** | **`UNInjectBakeValidator`** throws **`BuildFailedException`** when required referrals are missing from baked lists. |
| **`UNINJECT_PROFILING`** | Compiles **`UNInjectProfiler`**. **`ObjectInstaller.TryInjectTarget`** records per-type **`Stopwatch`** timing; use **`UNInjectProfiler.PrintReport()`**, **`GetStats()`**, **`Reset()`** (stats also reset on **`SubsystemRegistration`** in Play). Omit from shipping players unless you intentionally measure injection cost. |

**Profiler public API** (only when the symbol is set): **`RecordInjection(Type, double)`** (invoked from **`TryInjectTarget`**), **`GetStats()`**, **`PrintReport()`**, **`Reset()`**.

---

# Common Editor Warnings

**Empty global registry (Play)**  
`[MasterInstaller] Global registry is empty...` — run **Refresh Global Registry**. (`MasterInstallerPlayModeGuard`)

**Missing `partial` (Play)**  
`[UNInject] ... Expression Tree fallback ...` — add `partial` to listed types before IL2CPP. (`UNInjectFallbackGuard`)

---

# Compiler & Platform Notes

**IL2CPP / AOT:** Roslyn-generated setters are **AOT-safe**. Relying on expression/`FieldInfo` fallbacks without `partial` is **not** a shipping configuration for hot paths.

**Build-time validation:** enable **`UNINJECT_STRICT_BUILD`** on release branches so **`UNInjectBakeValidator`** fails the build when **EditorBuildSettings** scenes contain consumers that reference referrals missing from serialized registry lists.

**Tests:** consumer solutions should keep **EditMode** tests in sync with the distributed `Tests` asmdef — CI should compile the test assembly that ships beside the package.

---
