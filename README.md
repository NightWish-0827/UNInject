# UNInject – High-Performance Unity Dependency Injection SDK

![](https://img.shields.io/badge/unity-2021.3%2B-black)
![](https://img.shields.io/badge/license-MIT-blue)

> **UNInject** is a high-performance dependency injection framework for Unity designed for
> **zero-reflection runtime**,
> **editor-baked resolution**, and **strict multi-tier lifecycle control**.

By combining **editor-time dependency baking**, **expression-tree caching**,
and **hierarchical scoping**, UNInject enables extremely efficient
runtime dependency injection without the traditional reflection bottlenecks or lookup overhead found
in typical DI implementations.

Unlike conventional DI systems that rely heavily on runtime `FindObjectsOfType` or heavy reflection initialization,
UNInject provides a **fully engineered injection architecture** focused on
**performance**, **deterministic resolution**, and **developer usability**.

[upm link image]

`https://github.com/NightWish-0827/UNInject.git?path=/com.nightwishlab.uninject`  
UPM Add package from git URL

---

# Table of Contents

* [Core Features](https://www.google.com/search?q=%23core-features)
* [API Reference & Usage](https://www.google.com/search?q=%23api-reference--usage)
* [Lifecycle & Internal Architecture](https://www.google.com/search?q=%23lifecycle--internal-architecture)
* [Dynamic Object Support](https://www.google.com/search?q=%23dynamic-object-support)
* [Performance Comparison](https://www.google.com/search?q=%23performance-comparison)
* [Editor Supports](https://www.google.com/search?q=%23editor-supports)
* [Editor Case](https://www.google.com/search?q=%23editor-case)

---

# Core Features

### Editor-Backed Bake Architecture

UNInject introduces an **editor-time dependency scanning system**.

This allows developers to configure local dependencies such as **sibling components** and
**child hierarchy dependencies** using a simple inspector button, without generating **any runtime lookup costs**.
The `ObjectInstaller` completely resolves `[Inject]` attributes during development time.

---

### Zero Reflection Overhead at Runtime

Dependency injection operations are completely **reflection-free during assignment**.

Instead of using slow `FieldInfo.SetValue` methods, UNInject utilizes `TypeDataCache` to compile
Expression Trees into native `Action<object, object>` delegates.

This ensures that object injection operations never trigger **initialization spikes or CPU bottlenecks**.

---

### Three-Tier Deterministic Scoping

Traditional DI systems often struggle with confusing lifecycle management.

UNInject eliminates this ambiguity by enforcing three explicit layers of scope:

* **Global Scope (`MasterInstaller`)**: Preserved across scenes via `DontDestroyOnLoad`.
* **Scene Scope (`SceneInstaller`)**: Strictly bound to the active scene and destroyed upon transition.
* **Local Scope (`ObjectInstaller`)**: Confined to a specific GameObject root and its children.

No ambiguous bindings.
No memory leaks.
No runtime container discovery overhead.

---

### PlayMode Guard Protection

One of the most common and dangerous bugs in baked systems is the
**empty registry problem** — when developers forget to update the global registry before entering Play Mode.

UNInject prevents this entirely using an **Editor-only validation system**.

When the Play button is pressed:

* `MasterInstallerPlayModeGuard` intercepts the state change
* The global registry `_globalReferrals` array size is validated
* If empty, a warning is immediately logged to prompt the developer

---

### Cache-Friendly Injection Execution

UNInject uses **cached structural mappings** internally.

Both global and scene registries use pre-baked lists that are converted to fast `Dictionary<Type, Component>` lookups during `Awake`.
This **data-oriented structure** significantly improves performance compared to traversing the scene hierarchy.

---

# API Reference & Usage

UNInject’s API is designed around **two core concepts**:

* **Providers (Referrals)**
* **Consumers (Injects)**

Unlike traditional DI APIs that require massive external "Binder" scripts,
UNInject provides a **declarative attribute-based pipeline** that keeps dependency resolution
**expressive, safe, and lightning-fast**.

---

## The Provider Attributes

Services are registered into the framework using referral attributes.

```csharp
[Referral(typeof(IInputService))]
public class DesktopInputManager : MonoBehaviour, IInputService { }
```

The provider attributes guarantee:

* **Automatic Editor Scanning**
* **Interface Binding Support**
* **Duplicate Protection**

Supported provider markers include:

* `[Referral]` for Global/Master scope
* `[SceneReferral]` for Scene-local scope

---

## The Consumer Attributes

Dependencies are injected into target classes using specialized markers.

```csharp
public class PlayerController : MonoBehaviour
{
    // Resolved at editor-time (Baked)
    [Inject] [SerializeField] private Animator _animator;

    // Resolved at runtime from MasterInstaller
    [GlobalInject] private IInputService _input;

    // Resolved at runtime from SceneInstaller
    [SceneInject(optional: true)] private LevelManager _level;
}
```

This design ensures that **the exact origin and lifetime of a dependency** is instantly recognizable
just by reading the field declaration.

---

## Optional Dependencies

Global and Scene injections support the `Optional` property.

```csharp
[GlobalInject(optional: true)] 
private AnalyticsManager _analytics;

```

If the manager is missing (e.g., during isolated testing), UNInject will bypass the injection without throwing errors,
making it ideal for enterprise environments and modular testing.

---

# Lifecycle & Internal Architecture

UNInject manages object lifecycles using a strictly ordered execution pipeline.

```
Step 1: Master Initialization [Order: -1000]
------------------------------------------
MasterInstaller.Awake()
→ Rebuild Global Registry from baked list

Step 2: Scene Initialization [Order: -900]
------------------------------------------
SceneInstaller.Awake()
→ Rebuild Scene Registry from baked list

Step 3: Object Injection [Order: -500]
--------------------------------------
ObjectInstaller.Awake()
→ Collect all [GlobalInject] and [SceneInject] targets
→ Execute pre-compiled lambda setters
```

---

# Dynamic Object Support

A major limitation of baked DI systems is handling **dynamically instantiated or pooled objects**.
UNInject natively solves this with its explicit injection API.

For delayed spawn scenarios such as **PowerPool integrations or dynamic factory methods**,
developers can manually push dependencies to newly created objects.

```csharp
// 1. Spawn from PowerPool
var handle = PowerPool.Spawn(enemyPrefab).Rent();

// 2. Inject runtime dependencies instantly
ObjectInstaller.Instance.InjectTarget(handle.Instance);
```

Because the `InjectTarget` method leverages the same `TypeDataCache` architecture,
injecting dependencies into high-frequency spawned entities remains **allocation-free and highly performant**.

---

# Performance Comparison

**Zero Runtime GetComponent Lookup Cost**

UNInject fundamentally replaces `GetComponent` chains. Local dependencies are resolved entirely in the Editor.
The **"Bake Dependencies"** process scans the hierarchy and serializes the references directly into the scene file.

At runtime, Unity's native deserialization handles the linkage, resulting in **Zero CPU cycles spent on lookup**.

**Expression Tree Compilation vs Reflection**

Typical injection systems use `FieldInfo.SetValue(target, value)` which creates massive boxing allocations and CPU overhead.

In contrast, UNInject's **TypeDataCache** compiles an expression tree once per type:

```csharp
var assign = Expression.Assign(Expression.Field(typedTarget, field), typedValue);
var lambda = Expression.Lambda<Action<object, object>>(assign, targetParam, valueParam);
```

[perform images space] 

This compiled lambda executes at the speed of native C# code.
Once cached, injecting 10,000 objects takes microseconds, preventing the GC spikes (frame drops) that occur during complex UI or level instantiation.

---

# Editor Supports

> Provide intuitive Inspector tools to manage and visualize dependency graphs.

<img width="447" height="276" alt="Bake Button UI" src="[https://github.com/user-attachments/assets/579b2a3c-4eba-4977-8eb7-5bcac7c38541](https://github.com/user-attachments/assets/579b2a3c-4eba-4977-8eb7-5bcac7c38541)" />

UNInject features the `AttributeButton` extension, bringing a highly visual diagnostic panel to the `ObjectInstaller`.

* **🍩 Bake Dependencies Button**: One-click local dependency resolution.
* **Registry Visualization**: Clearly displays all tracked managers and missing references.
* **Read-Only Inject Drawer**: Prevents developers from accidentally overwriting managed `[Inject]` fields manually.

---

# Editor Case

> When entering **PlayMode** with an unbaked master prefab, the following warning may occur.

`[MasterInstaller] Global registry is empty. Did you forget to click 'Refresh Global Registry' before Play?`

: **LogWarning**

This is the safety net from the `MasterInstallerPlayModeGuard`, **so it is functioning as intended**.

However, if this occurs, you must ensure you have baked the registry. The system will gracefully fallback to null injection to prevent crashes, but your dependencies will not be met.

### In a word...

UNInject is not just another bulky IoC container.

It is a **lightweight, heavily optimized injection pipeline** designed to make
Unity architecture **scalable**, **deterministic**, and **extremely fast**.

By combining **editor-time baking**, **expression tree compilation**,
and **strict scoping rules**, UNInject delivers a DI
architecture capable of supporting even the most demanding mobile or AAA workloads.

**High performance injection should be invisible.**

UNInject makes that possible.
