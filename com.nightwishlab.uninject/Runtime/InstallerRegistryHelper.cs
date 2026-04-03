using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// MasterInstaller / SceneInstaller 가 공유하는 레지스트리 등록 유틸리티.
///
/// v1.1: MasterInstaller / SceneInstaller 의 공통 등록 로직을 단일화.
///
/// v2.0:
///   - 레지스트리 타입: Dictionary&lt;Type, Component&gt; → Dictionary&lt;RegistryKey, Component&gt;.
///     RegistryKey = (Type, string Id) 복합 키로 Named/Keyed 바인딩을 지원함.
///     Id = string.Empty 이면 v1.x 와 동일한 무키 동작.
///   - RegisterTypeMappings 에 id 파라미터 추가.
///   - CreateAndInject&lt;T&gt;: 생성자 주입 + 필드 주입을 수행하는 공유 팩토리.
///     세 Installer 모두 이 메서드로 Create&lt;T&gt;() 를 구현함.
///
/// 설계 원칙:
///   - internal: 공개 API 가 아니며 SDK 소비자가 직접 호출하지 않음.
///   - 순수 정적 메서드: 상태를 보유하지 않으므로 테스트가 쉽고 사이드 이펙트가 없음.
/// </summary>
internal static class InstallerRegistryHelper
{
    // ─────────────────────────────────────────────────────────────────────────
    // 등록 (Register)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 컴포넌트 하나를 레지스트리에 등록한다.
    ///
    /// bindTypeOverride 가 null 인 경우:
    ///   구체 타입 + 구현 인터페이스 전체 + 상속 체인을 모두 키로 등록. (id 포함)
    ///
    /// bindTypeOverride 가 null 이 아닌 경우 ([Referral(typeof(IFoo))] 등):
    ///   해당 타입 하나만 키로 등록하고 즉시 반환.
    ///   BindType 이 구체 타입과 호환되지 않으면 경고 후 전체 등록 경로로 폴백.
    ///
    /// id: Named 바인딩 키. string.Empty 이면 v1.x 무키 동작과 동일.
    /// </summary>
    internal static void RegisterTypeMappings(
        Component                          comp,
        Dictionary<RegistryKey, Component> registry,
        string                             ownerTag,
        Type                               bindTypeOverride = null,
        string                             id               = null)
    {
        if (comp == null || registry == null) return;

        id = id ?? RegistryKey.DefaultId;
        var concrete = comp.GetType();

        if (bindTypeOverride != null)
        {
            if (!bindTypeOverride.IsAssignableFrom(concrete))
            {
                Debug.LogWarning(
                    $"[{ownerTag}] BindType '{bindTypeOverride.FullName}' is not assignable " +
                    $"from '{concrete.FullName}'. " +
                    $"Falling back to concrete type mapping for '{concrete.Name}'.");
                // 호환 불가 → 전체 매핑 경로로 진행
            }
            else
            {
                TryAdd(registry, new RegistryKey(bindTypeOverride, id), comp, ownerTag);
                return;
            }
        }

        // 1) 구체 타입
        TryAdd(registry, new RegistryKey(concrete, id), comp, ownerTag);

        // 2) 인터페이스 (Unity/System 공통 인터페이스 제외)
        foreach (var itf in concrete.GetInterfaces())
        {
            if (IsMappableAbstraction(itf))
                TryAdd(registry, new RegistryKey(itf, id), comp, ownerTag);
        }

        // 3) 베이스 타입 체인 (너무 광범위한 Unity 공통 베이스는 제외)
        var baseType = concrete.BaseType;
        while (baseType != null          &&
               baseType != typeof(object)      &&
               baseType != typeof(Component)   &&
               baseType != typeof(Behaviour)   &&
               baseType != typeof(MonoBehaviour))
        {
            TryAdd(registry, new RegistryKey(baseType, id), comp, ownerTag);
            baseType = baseType.BaseType;
        }
    }

    /// <summary>
    /// 레지스트리 키로 사용 가능한 타입인지 판별한다.
    /// System.*, UnityEngine.*, UnityEditor.*, Unity.* 를 제외함.
    /// </summary>
    internal static bool IsMappableAbstraction(Type type)
    {
        if (type == null || type == typeof(object)) return false;

        var ns = type.Namespace ?? string.Empty;
        return !(ns == "System"      || ns.StartsWith("System.")      ||
                 ns == "UnityEngine" || ns.StartsWith("UnityEngine.") ||
                 ns == "UnityEditor" || ns.StartsWith("UnityEditor.") ||
                 ns == "Unity"       || ns.StartsWith("Unity."));
    }

    /// <summary>
    /// 중복 키 충돌을 경고와 함께 처리하며 레지스트리에 값을 추가한다.
    /// 이미 다른 컴포넌트가 같은 키로 등록되어 있으면 먼저 등록된 쪽을 유지한다.
    /// </summary>
    internal static void TryAdd(
        Dictionary<RegistryKey, Component> registry,
        RegistryKey                        key,
        Component                          value,
        string                             ownerTag)
    {
        if (key.Type == null || value == null) return;

        if (registry.TryGetValue(key, out var existing) && existing != null && existing != value)
        {
            Debug.LogWarning(
                $"[{ownerTag}] Duplicate mapping for '{key}'. " +
                $"Keeping '{existing.name}' ({existing.GetType().Name}), " +
                $"ignoring '{value.name}' ({value.GetType().Name}).");
            return;
        }

        registry[key] = value;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 해제 (Unregister)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 레지스트리에서 해당 컴포넌트가 값으로 등록된 모든 키를 제거한다.
    /// RegisterTypeMappings 는 하나의 컴포넌트를 다수의 키에 등록하므로
    /// 언레지스터도 동일하게 전체 키를 제거한다.
    /// </summary>
    internal static List<RegistryKey> Unregister(
        Dictionary<RegistryKey, Component> registry,
        Component                          comp,
        string                             ownerTag)
    {
        var removed = new List<RegistryKey>();
        if (comp == null || registry == null) return removed;

        foreach (var key in new List<RegistryKey>(registry.Keys))
        {
            if (registry.TryGetValue(key, out var existing) && existing == comp)
            {
                registry.Remove(key);
                removed.Add(key);
            }
        }

        if (removed.Count > 0)
        {
            Debug.Log(
                $"[{ownerTag}] Unregistered '{comp.name}' ({comp.GetType().Name}): " +
                $"{removed.Count} key(s) removed.");
        }

        return removed;
    }

    /// <summary>
    /// 소유권 맵에서 특정 owner 가 소유한 컴포넌트를 모두 언레지스터한다.
    /// ScopeOwnerTracker.OnDestroy 경로에서 자동으로 호출됨.
    /// </summary>
    internal static void UnregisterByOwner(
        Dictionary<RegistryKey, Component>         registry,
        Dictionary<MonoBehaviour, List<Component>> ownershipMap,
        MonoBehaviour                              owner,
        string                                     ownerTag)
    {
        if (owner == null || ownershipMap == null) return;
        if (!ownershipMap.TryGetValue(owner, out var ownedComps)) return;

        foreach (var comp in ownedComps)
            Unregister(registry, comp, ownerTag);

        ownershipMap.Remove(owner);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // v2.0: 생성자 주입 팩토리 (Create<T>)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// [InjectConstructor] 또는 단일 public 생성자를 통해 T 의 인스턴스를 생성하고,
    /// [GlobalInject] / [SceneInject] 필드 주입도 함께 수행한다.
    ///
    /// resolver: (Type type, string id) → Component.
    ///   - MasterInstaller.Create 는 MasterInstaller.Resolve + SceneInstaller.Resolve fallthrough 를 전달.
    ///   - SceneInstaller.Create 는 SceneInstaller.Resolve + MasterInstaller.Resolve fallthrough 를 전달.
    ///   - ObjectInstaller.Create 는 ObjectInstaller.Resolve (이미 전체 체인) 를 전달.
    ///
    /// onCreated: 생성 + 필드 주입 완료 직후 호출되는 콜백.
    ///   IScope.Create&lt;T&gt;() 에서 ITickable 자동 등록에 활용됨.
    ///   IInjected.OnInjected() 이후에 호출됨 (서비스가 완전히 초기화된 상태).
    ///
    /// 생성 우선순위:
    ///   1) Roslyn Generator 가 등록한 non-reflective 팩토리 (TypeDataCache.TryGetGeneratedFactory)
    ///      IL2CPP 안전, zero-reflection, 가장 빠름.
    ///   2) ConstructorInfo.Invoke 폴백 — partial 선언이 없거나 Generator 가 실행되지 않은 경우.
    ///      IL2CPP 빌드 시 해당 타입이 코드 스트리핑되지 않도록 link.xml 보존 규칙 추가 필요.
    /// </summary>
    internal static T CreateAndInject<T>(
        Func<Type, string, Component> resolver,
        string                        ownerTag,
        Action<object>                onCreated = null) where T : class
    {
        var type = typeof(T);
        T instance;

        // ── 1순위: Roslyn Generator 생성 non-reflective 팩토리 ─────────────────
        if (TypeDataCache.TryGetGeneratedFactory(type, out var factory))
        {
            instance = (T)factory(resolver);
        }
        else
        {
            // ── 2순위: Reflection 폴백 ──────────────────────────────────────────
            var ctor = TypeDataCache.GetInjectableConstructor(type);

            if (ctor == null)
                throw new InvalidOperationException(
                    $"[{ownerTag}] No injectable constructor found for '{type.Name}'. " +
                    "Ensure there is exactly one public constructor, " +
                    "or mark the target constructor with [InjectConstructor]. " +
                    "If the class is partial, verify the Generator DLL is referenced.");

            var parameters = ctor.GetParameters();
            var args       = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var param      = parameters[i];
                var globalAttr = param.GetCustomAttribute<GlobalInjectAttribute>();
                var sceneAttr  = param.GetCustomAttribute<SceneInjectAttribute>();
                string id      = globalAttr?.Id ?? sceneAttr?.Id ?? RegistryKey.DefaultId;

                var resolved = resolver(param.ParameterType, id);

                if (resolved == null)
                    throw new InvalidOperationException(
                        $"[{ownerTag}] Cannot resolve constructor parameter '{param.Name}' " +
                        $"(type: '{param.ParameterType.Name}', id: '{id}') for '{type.Name}'.");

                args[i] = resolved;
            }

            instance = (T)ctor.Invoke(args);
        }

        // ── 필드 주입 ([SceneInject] 먼저, [GlobalInject] 후) ─────────────────
        InjectFields(instance, type, resolver, ownerTag);

        // ── 생성 후 콜백 (Tickable 자동 등록 등) ────────────────────────────
        onCreated?.Invoke(instance);

        return instance;
    }

    /// <summary>
    /// 이미 생성된 인스턴스에 [SceneInject] / [GlobalInject] 필드 주입을 수행한다.
    /// CreateAndInject 내부에서 생성자 주입 이후에 호출됨.
    /// </summary>
    private static void InjectFields<T>(
        T                             instance,
        Type                          type,
        Func<Type, string, Component> resolver,
        string                        ownerTag) where T : class
    {
        bool success = true;

        foreach (var field in TypeDataCache.GetSceneInjectFields(type))
        {
            var resolved = resolver(field.FieldType, field.Id);
            if (resolved != null)
                field.Setter(instance, resolved);
            else if (!field.Optional)
            {
                success = false;
                Debug.LogWarning(
                    $"[{ownerTag}] Create<{type.Name}>: " +
                    $"Failed to resolve [SceneInject] '{field.Name}' " +
                    $"({field.FieldType.Name}, id: '{field.Id}').");
            }
        }

        foreach (var field in TypeDataCache.GetGlobalInjectFields(type))
        {
            var resolved = resolver(field.FieldType, field.Id);
            if (resolved != null)
                field.Setter(instance, resolved);
            else if (!field.Optional)
            {
                success = false;
                Debug.LogWarning(
                    $"[{ownerTag}] Create<{type.Name}>: " +
                    $"Failed to resolve [GlobalInject] '{field.Name}' " +
                    $"({field.FieldType.Name}, id: '{field.Id}').");
            }
        }

        if (success && instance is IInjected injected)
            injected.OnInjected();
    }
}
