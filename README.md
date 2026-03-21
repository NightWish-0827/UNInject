# UNInject – High-Performance Unity Dependency Injection SDK
## Current Version : 1.1.0

![](https://img.shields.io/badge/unity-2021.3%2B-black)
![](https://img.shields.io/badge/license-MIT-blue)
![](https://img.shields.io/badge/IL2CPP-supported-brightgreen)
![](https://img.shields.io/badge/[A.O.T]-supported-brightgreen)

> **UNInject** is a high-performance dependency injection framework designed for Unity.  
> Built around **zero-reflection runtime**, **editor-based dependency baking**,  
> and **strict multi-tier lifecycle control**.

UNInject combines **editor-time dependency baking**, **Roslyn Source Generation**,  
and **hierarchical scoping** to enable highly efficient runtime dependency injection  
without the **reflection bottlenecks** or **runtime lookup overhead** common in traditional DI implementations.

Unlike DI systems that rely on `FindObjectsOfType` calls or heavy reflection initialization,  
UNInject delivers a **complete injection architecture** built around **performance**,  
**deterministic dependency resolution**, and **developer ergonomics**.

<img width="797" height="86" alt="Image" src="https://github.com/user-attachments/assets/fbba5f3c-6803-41bc-84b9-3ecf957ec7d0" />

`https://github.com/NightWish-0827/UNInject.git?path=/com.nightwishlab.uninject`  
UPM Add package from git URL

---

# Table of Contents

* [Core Features](#core-features)
* [API Reference & Usage](#api-reference--usage)
* [Lifecycle & Internal Architecture](#lifecycle--internal-architecture)
* [Dynamic Object Support](#dynamic-object-support)
* [Performance Comparison](#performance-comparison)
* [Editor Supports](#editor-supports)
* [Editor Case](#editor-case)

---

# Core Features

### Editor-Backed Bake Architecture

UNInject introduces an **editor-time dependency scanning system**.

This allows developers to resolve local dependencies such as **sibling components**  
or **child hierarchy references** with a single inspector button click.

**No runtime lookup cost is incurred** during this process.

`ObjectInstaller` is designed to **fully resolve `[Inject]` attributes at development time**,  
before the game ever runs.

---

### ✦ Roslyn Source Generator — Full IL2CPP Support (New in v1.1.0)

The defining change of v1.1.0.

UNInject introduces a **Roslyn Source Generator** based code generation pipeline,  
completely resolving the **IL2CPP/AOT constraints** that the previous Expression Tree approach carried.

**How it works:**

The Generator automatically detects classes with `[GlobalInject]` / `[SceneInject]` fields at compile time  
and generates two source files **directly in memory, compiled alongside your code**.

```
Save file → Unity compilation begins
  → UNInjectGenerator runs (automatically on every save)
  → ① {TypeName}.UNInject.g.cs  — partial class extension + AggressiveInlining setter
  → ② UNInjectPlanRegistry.g.cs — AfterAssembliesLoaded registration entry
  → Compiled together, done
```

Generated code is never written to disk and makes **no use of `Expression.Compile()`**.  
It consists solely of simple casts and direct method calls, making it **safe in all AOT environments**.

**The only requirement from user code:**

Add the `partial` keyword to any class that has `[GlobalInject]` or `[SceneInject]` fields.

```csharp
// Before
public class PlayerController : MonoBehaviour { ... }

// After
public partial class PlayerController : MonoBehaviour { ... }
```

Entering Play mode without `partial` will cause `UNInjectFallbackGuard` to immediately emit a warning.  
A **UNI001 warning** is also issued at Roslyn compile time, making it visible at the IDE level as well.

---

### Zero Reflection Overhead at Runtime

**No reflection is used at runtime** during dependency injection.

As of v1.1.0, the injection path consists of two tiers.

**Priority 1 — Roslyn Generated Plan (IL2CPP safe)**  
Types with `partial` declarations are injected via Generator-produced `AggressiveInlining` setters,  
with **no boxing or reflection — identical in cost to native C# assignment**.

**Priority 2 — Expression Tree Fallback (Mono environments)**  
For types without `partial`, the original approach is used as a fallback.  
Expression Trees are compiled into **cached native `Action<object, object>` delegates**.

This design ensures that object injection produces  
**no initialization spikes or CPU bottlenecks**.

---

### Three-Tier Deterministic Scoping

Traditional DI systems frequently suffer from ambiguous lifecycle management.

UNInject resolves this by **enforcing three clearly defined scope tiers**.

* **Global Scope (`MasterInstaller`)**
  → Persists across scene transitions via `DontDestroyOnLoad`

* **Scene Scope (`SceneInstaller`)**
  → Exists only within the active scene; destroyed on scene transition

* **Local Scope (`ObjectInstaller`)**
  → Confined to a specific GameObject root and its children

No ambiguous bindings.  
No memory leaks.  
No runtime container traversal overhead.

---

### PlayMode Guard Protection

Two independent guards defend against potential issues in a bake-based system.

**① MasterInstallerPlayModeGuard** — Empty registry detection

Prevents the **empty registry problem** that occurs when a developer  
enters Play mode without refreshing the global registry.

* Checks the size of the `_globalReferrals` array
* Emits a warning immediately if empty

```
[MasterInstaller] Global registry is empty. Did you forget to click 'Refresh Global Registry' before Play?
```

**② UNInjectFallbackGuard** — IL2CPP fallback type detection (New in v1.1.0)

Detects types that will operate on Expression Tree fallback due to missing `partial` declarations.  
Explicitly lists types that may fall back to `FieldInfo.SetValue` in an IL2CPP build.

```
[UNInject] The following types are missing a 'partial' declaration and will use
Expression Tree fallback instead of a Roslyn generated plan.
  • MyGame.PlayerController
  • MyGame.EnemyAI
```

The two guards are kept in **separate files** because their concerns and detection mechanisms are entirely independent.

---

### Cache-Friendly Injection Execution

UNInject uses a **cached structural mapping** internally.

Global and scene registries are built from pre-baked list structures  
and converted into fast `Dictionary<Type, Component>` lookups at `Awake` time.

In v1.1.0, `TypeDataCache` manages the generated plan cache and reflection cache separately.  
Types with a registered generated plan complete their lookup in a single `Dictionary` access,  
making even `Warmup()` calls **effectively zero-cost**.

This **data-oriented structure** delivers significantly higher performance  
than approaches that traverse the scene hierarchy directly.

---

# API Reference & Usage

The UNInject API is built around **two core concepts**.

* **Providers (Referrals)**
* **Consumers (Injects)**

Most DI APIs require a large external Binder script.  
UNInject provides an **attribute-based declarative pipeline** instead.

This allows dependency resolution to be **expressive**, **safe**, and **extremely fast**.

In short: place the three core components in your Scene,  
then use attributes like `[Inject]`, `[Referral]`, and `[SceneReferral]` in code —  
no manual Resolve calls required.

---

## The Provider Attributes

Services are registered into the framework via referral attributes.

```csharp
[Referral(typeof(IInputService))]
public class DesktopInputManager : MonoBehaviour, IInputService { }
```

Provider attributes guarantee:

* **Automatic Editor Scanning**
* **Interface Binding support**
* **Duplicate registration prevention**

Supported provider markers:

* `[Referral]` → Global / Master scope
* `[SceneReferral]` → Scene-local scope

---

## The Consumer Attributes

Dependencies are injected into target classes via consumer markers.

```csharp
// v1.1.0: partial declaration required for Roslyn generated plan
public partial class PlayerController : MonoBehaviour
{
    // Resolved at editor-time (Baked)
    [Inject] [SerializeField] private Animator _animator;

    // Resolved at runtime from MasterInstaller
    [GlobalInject] private IInputService _input;

    // Resolved at runtime from SceneInstaller
    [SceneInject(optional: true)] private LevelManager _level;
}
```

This design means that **reading the field declarations alone** reveals:

* the **source** of each dependency
* the **lifecycle** of each dependency

The `partial` keyword is the **only requirement** — it allows the Generator to produce  
setter methods within the same class scope.  
No additional code needs to be written in your game logic.

---

## Optional Dependencies (MOAK Object Logic)

Both Global and Scene injection support the `Optional` property.

```csharp
[GlobalInject(optional: true)] 
private AnalyticsManager _analytics;
```

When a manager does not exist (e.g. in an isolated test environment),  
UNInject skips the injection without raising an error.

This is extremely useful in **large project environments** or **module testing** setups.

It provides the dependency flexibility and debugging capability that  
**major DI solutions (VContainer, Zenject, etc.)** required mock objects to achieve —  
now expressed cleanly through `[SceneInject(optional: true)]` and `[GlobalInject(optional: true)]`.

From v1.1.0, Optional fields in the Inspector are rendered in **gray (unregistered + intentional)**,  
while red warnings are reserved exclusively for **missing required dependencies**.

---

## Injected State Alarm

Just as **Start** and **Awake** are well-known lifecycle functions,  
**UNInject** provides its own explicit injection-complete callback.

```csharp
public partial class PlayerController : MonoBehaviour, IInjected
{
    [GlobalInject] private IInputManager _input;                 
    [SceneInject(true)] private IStageContext _stageContext;    

    public void OnInjected()
    {
        // Called automatically when ObjectInstaller has completed injection
        // for this component and all required dependencies were resolved successfully.

        // Caller: ObjectInstaller.TryInjectTarget() — invoked when success == true
        // and the target implements IInjected.
        
        // Condition: called only when all required (non-Optional) dependencies
        // are successfully injected.
        // (Missing Optional dependencies do not affect success or failure.)
        
        // Timing / call count: called each time injection is executed —
        // i.e. via InjectTarget(), InjectGameObject(), or Awake().
        // If injection is triggered multiple times, OnInjected() fires each time.
    }
}
```

(Currently `IInjected` must be explicitly implemented to use `OnInjected`.  
A future update is planned to extract this into a native layer,  
eliminating the need for the interface inheritance entirely.)

---

# Lifecycle & Internal Architecture

UNInject manages object lifecycle through a **strictly ordered execution pipeline**.

```
[Editor — Compile Time]
UNInjectGenerator runs
→ Detects [GlobalInject] / [SceneInject] fields
→ ① Generates {TypeName}.UNInject.g.cs  (partial setter)
→ ② Generates UNInjectPlanRegistry.g.cs (registration entry)

[Runtime — On Play Enter]

Step 0: Cache Initialization [SubsystemRegistration]
-----------------------------------------------------
TypeDataCache static caches fully cleared

Step 0.5: Plan Registration [AfterAssembliesLoaded]
----------------------------------------------------
UNInjectPlanRegistry.RegisterAll()
→ Generated plans registered into TypeDataCache

Step 1: Master Initialization [Order: -1000]
--------------------------------------------
MasterInstaller.Awake()
→ Rebuilds Global Registry from baked list

Step 2: Scene Initialization [Order: -900]
------------------------------------------
SceneInstaller.Awake()
→ Rebuilds Scene Registry from baked list

Step 3: Object Injection [Order: -500]
--------------------------------------
ObjectInstaller.Awake()
→ Collects all [GlobalInject], [SceneInject] targets
→ Queries TypeDataCache (generated plan first / Expression Tree second)
→ Executes setters
```

---

# Dynamic Object Support

One of the key limitations of bake-based DI systems is  
**handling dynamically instantiated objects**.

UNInject resolves this through an **explicit injection API**.

For **PowerPool integration** or **factory-based runtime spawn** scenarios,  
dependencies can be injected as follows.

```csharp
// 1. Spawn from PowerPool
var handle = PowerPool.Spawn(enemyPrefab).Rent();

// 2. Inject runtime dependencies instantly
ObjectInstaller.Instance.InjectTarget(handle.Instance);
```

Because `InjectTarget` uses the same `TypeDataCache` structure,  
**allocation-free, high-speed injection** is possible even for large volumes of dynamically created objects.

In v1.1.0, if the dynamically injected target is a `partial` class,  
the generated plan path is used as-is — **IL2CPP safety is guaranteed at spawn time**.

---

# Performance Comparison

**Zero Runtime GetComponent Lookup Cost**

UNInject is designed to fundamentally replace `GetComponent` chains.

All local dependencies are **resolved entirely at the Editor stage**.

During **"Bake Dependencies"**, the hierarchy is scanned and references are serialized  
directly into the scene file.

At runtime, Unity's native deserialization restores the connections automatically.  
**No CPU cycles are spent on traversal.**

---

**Roslyn Generated Plan vs Expression Tree vs Reflection**

A comparison of the three setter paths as of v1.1.0.

| Path | Condition | IL2CPP | Cost |
|---|---|---|---|
| Roslyn Generated Plan | `partial` declared | ✅ Safe | Dictionary lookup + direct call |
| Expression Tree | No `partial`, Mono | ⚠️ Limited | Compiled once, then cached |
| FieldInfo.SetValue | AOT compile failure | ⚠️ Fallback | Boxing on every call |

Generated setters are marked `[MethodImpl(AggressiveInlining)]`,  
making them **cast + assignment cost on both JIT and AOT**.

```csharp
// Code generated by the Generator
[MethodImpl(MethodImplOptions.AggressiveInlining)]
internal void __UNInject_Global_input(object __v) => _input = (IInputService)__v;
```

<img width="800" height="600" alt="Image" src="https://github.com/user-attachments/assets/964a75dc-0a7d-4c7c-9455-bec0fb48e0bd" />  

Once cached, injection of **10,000 objects is achievable in microseconds**.

This prevents **GC spikes and frame drops** that can occur during  
complex UI instantiation or large-scale level object creation.

---

# Editor Supports

> Intuitive Inspector tooling for managing and visualizing the dependency graph.

<p align="center">
  <img src="https://github.com/user-attachments/assets/84a1af2b-aa68-4722-ad2e-e9a15ed6c0de" width="32%">
  <img src="https://github.com/user-attachments/assets/3858e28f-6332-4048-b80a-1bbcb7fa26d1" width="32%">
  <img src="https://github.com/user-attachments/assets/a9e36b9f-7d35-416c-a5e8-145f13e5ef59" width="32%">
</p>  

UNInject provides a **visual diagnostic panel** in the `ObjectInstaller` Inspector  
via the `AttributeButton` extension.

* **🍩 Bake Dependencies Button**
  → Resolves all local dependencies in a single click

* **Registry Visualization**
  → Clearly displays tracked managers and any missing references

* **Read-Only Inject Drawer**
  → Prevents accidental manual modification of `[Inject]` fields

* **✦ 3-State Optional Status Display (New in v1.1.0)**  
  → Dependency status is distinguished by three colors

| Color | Meaning |
|---|---|
| 🟢 Green | Registered in registry (nominal) |
| ⚫ Gray | Optional — not registered (intentional) |
| 🔴 Orange | Required — not registered (needs attention) |

By rendering Optional dependencies in gray,  
**only genuine problems are immediately visually apparent**.

* **✦ Full Dependency List Display (New in v1.1.0)**  
  → The previous "… and N more" truncation has been removed.  
  Dependencies are architectural information — all entries are always shown.  
  (If the dependency field count reaches the dozens, that itself is a signal to revisit the design.)

---

# Editor Case

> **Entering Play mode with an un-baked master prefab** may produce the following warning.

`[MasterInstaller] Global registry is empty. Did you forget to click 'Refresh Global Registry' before Play?`

: **LogWarning** — `MasterInstallerPlayModeGuard`

This indicates a registry bake is required. The warning disappears after refreshing and re-entering Play.

---

> **New in v1.1.0 — Entering Play mode without `partial`** produces the following warning.

```
[UNInject] The following types are missing a 'partial' declaration and will use
Expression Tree fallback instead of a Roslyn generated plan.
FieldInfo.SetValue fallback may occur in IL2CPP builds.
  • MyGame.PlayerController
```

: **LogWarning** — `UNInjectFallbackGuard`

This is the **IL2CPP safety guard functioning as intended**.  
Adding `partial` to the listed types will resolve the warning.

The system performs **Expression Tree fallback** to prevent crashes,  
but this must be resolved before targeting IL2CPP builds.

---

# Notes & Roadmap

### Compiler Support

**IL2CPP / AOT environments are officially supported as of v1.1.0.**

Code generated by the Roslyn Source Generator does not use `Expression.Compile()`,  
making it **safe on all AOT platforms (iOS, consoles, etc.)**.

Expression Tree fallback applies only to types without a `partial` declaration.  
In those cases, `UNInjectFallbackGuard` emits an explicit warning on Play enter.

---

```
1.1.0 — Full IL2CPP Support (Released)
────────────────────────────────────────────────────────────
Roslyn Source Generator introduced.

Dependency matching plans are automatically collected and generated at compile time,
then supplied to the runtime.
IL2CPP-safe setters with direct private field access are auto-generated via partial classes.
Constant time O(1) lookup guaranteed for all resolutions.

UNInjectFallbackGuard added: explicitly warns on Play enter for types missing partial.
Inspector Optional 3-state display: visually separates intentional non-registration from actual errors.
Full dependency list display: displayLimit removed.

All cases verified — ObjectInstaller (Consumer) <-> Master/Scene Installer (Provider)

══════════════════════════════════════
       UNInject Test Results
══════════════════════════════════════
  Precondition: MasterInstaller present
  Precondition: SceneInstaller present
  Precondition: Consumer object present (Generated : Roslyn Source)
  Precondition: Consumer object present (Fallback)
  Precondition: Consumer object present (Optional)
  Precondition: Consumer object present (Callback)
  Precondition: Consumer object present (Mixed)

  MonoBehaviour Lifecycle
  ✓ [Provider] Awake - dependency provision successful
  ✓ [Provider] Start - dependency provision successful

  ✓ [Consumer] Awake - dependency injection successful
  ✓ [Consumer] Start - dependency injection successful
  Lifecycle fully compatible

  ✓ [A] Generated — Interface injection from scene-placed component successful
  ✓ [A] Generated — Correct implementation resolved successfully
  ✓ [A] Generated — Plain MonoBehaviour provider injection successful
  ✓ [A] Generated — Roslyn plan registration confirmed (HasGeneratedGlobalPlan) successful
  ✓ [B] Fallback — Interface injection via fallback path (no partial) successful
  ✓ [B] Fallback — Correct implementation resolved via fallback successful
  ✓ [C] Optional — Abstract interface injection with optional: true successful
  ✓ [C] Optional — Mock object test: absent interface injection successful
  ✓ [C] Optional — Required injection succeeds (optional absence does not cause failure)
  ✓ [D] Callback — Dependency injection successful
  ✓ [D] Callback — OnInjected() called exactly once successful
  ✓ [E] Mixed — GlobalInject + SceneInject combined injection successful
  ✓ [Dynamic] Manual InjectTarget — injection successful

  Goal: approach "SerializeField-level cost" on all runtimes
──────────────────────────────────────
  ALL PASSED  (24 + 51 tests)
══════════════════════════════════════
```

```
1.2 (Architecture Advancement: Scope / Lifetime)
Scope concept introduction (simplifying VContainer LifetimeScope into a Unity-native model)
Global (DDOL), Scene, Object (root)
Runtime Register API provided here alongside ownership / unregister / scene transition policy
```

```
2.0 (Enterprise Complete)
Validation / diagnostics system (graph, report, pre-build inspection)
Profiling tooling (injection cost, missing, duplicate visualization)
Factory pattern or Unity object creation pipeline integration
```

---

### In a word...

Having worked with numerous **DI Solution SDKs** and collected pain points that arose consistently,  
this SDK was built by incorporating feedback from developers to produce  
a DI solution that is `lightweight, approachable, and performance-maximized`.

The persistent problems with existing DI solutions were **steep learning curves**,  
**ambiguous lifecycle management**, and **high upfront architectural cost**.

UNInject preserves the strengths of major solutions while significantly reducing  
the learning curve and initial design overhead.  
The author actively uses this SDK in casual and mid-core game development.

v1.1.0 completes the first milestone toward enterprise readiness with full IL2CPP support.  
Adding a single `partial` keyword is all it takes to guarantee  
**reflection-free injection on mobile and console platforms**.

Give it a try — it's worth it.

The `OnInjected` member of the `IInjected` interface is particularly powerful.  
It is an exceptionally useful member in the context of object lifecycle design.

---
