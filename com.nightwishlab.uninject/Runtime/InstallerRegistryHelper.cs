using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MasterInstaller / SceneInstaller 가 공유하는 레지스트리 등록 유틸리티.
///
/// 배경: 두 Installer 가 동일한 RegisterTypeMappings / IsMappableAbstraction / TryAdd
///       코드를 각자 보유하고 있었음 → 버그 수정이나 정책 변경 시 양쪽을 동시에 고쳐야 하는
///       유지보수 부채가 존재.
///       이 클래스는 두 Installer 의 공통 로직을 한 곳에 모아 단일 책임을 부여함.
///
/// 설계 원칙:
///   - internal : 공개 API 가 아니며, SDK 소비자가 직접 호출하지 않음.
///   - 순수 정적 메서드 : 상태를 보유하지 않으므로 테스트가 쉽고 사이드 이펙트가 없음.
///   - Attribute 해석은 각 Installer 가 담당 : 이 클래스는 이미 해석된 bindTypeOverride 만 받음.
///     (MasterInstaller → ReferralAttribute, SceneInstaller → SceneReferralAttribute)
/// </summary>
internal static class InstallerRegistryHelper
{
    // ─────────────────────────────────────────────────────────────────────────
    // Public Surface (internal)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 컴포넌트 하나를 레지스트리에 등록한다.
    ///
    /// bindTypeOverride 가 null 인 경우:
    ///   구체 타입 + 구현 인터페이스 전체 + 상속 체인을 모두 키로 등록.
    ///
    /// bindTypeOverride 가 null 이 아닌 경우 ([Referral(typeof(IFoo))] 등):
    ///   해당 타입 하나만 키로 등록하고 즉시 반환.
    ///   BindType 이 구체 타입과 호환되지 않으면 경고 후 전체 등록 경로로 폴백.
    /// </summary>
    internal static void RegisterTypeMappings(
        Component comp,
        Dictionary<Type, Component> registry,
        string ownerTag,
        Type bindTypeOverride = null)
    {
        if (comp == null || registry == null) return;

        var concrete = comp.GetType();

        if (bindTypeOverride != null)
        {
            if (!bindTypeOverride.IsAssignableFrom(concrete))
            {
                Debug.LogWarning(
                    $"[{ownerTag}] BindType '{bindTypeOverride.FullName}' is not assignable " +
                    $"from '{concrete.FullName}'. " +
                    $"Falling back to concrete type mapping for '{concrete.Name}'.");
                // 호환되지 않으므로 전체 매핑 경로로 진행
            }
            else
            {
                TryAdd(registry, bindTypeOverride, comp, ownerTag);
                return;
            }
        }

        // 1) 구체 타입
        TryAdd(registry, concrete, comp, ownerTag);

        // 2) 인터페이스 (Unity/System 공통 인터페이스 제외)
        foreach (var itf in concrete.GetInterfaces())
        {
            if (IsMappableAbstraction(itf))
                TryAdd(registry, itf, comp, ownerTag);
        }

        // 3) 베이스 타입 체인 (너무 광범위한 Unity 공통 베이스는 제외)
        var baseType = concrete.BaseType;
        while (baseType != null          &&
               baseType != typeof(object)      &&
               baseType != typeof(Component)   &&
               baseType != typeof(Behaviour)   &&
               baseType != typeof(MonoBehaviour))
        {
            TryAdd(registry, baseType, comp, ownerTag);
            baseType = baseType.BaseType;
        }
    }

    /// <summary>
    /// 레지스트리 키로 사용 가능한 타입인지 판별한다.
    ///
    /// 제외 대상:
    ///   - System.*      : IDisposable, IEnumerable 등 범용 인터페이스
    ///   - UnityEngine.* : IPointerDownHandler 등 Unity 이벤트 인터페이스
    ///   - UnityEditor.* : 에디터 전용 타입
    ///   - Unity.*       : Unity Collections 등 네임스페이스
    ///   - object        : 최상위 타입
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
    /// 이미 다른 컴포넌트가 같은 타입 키로 등록되어 있으면 먼저 등록된 쪽을 유지한다.
    /// </summary>
    internal static void TryAdd(
        Dictionary<Type, Component> registry,
        Type key,
        Component value,
        string ownerTag)
    {
        if (key == null || value == null) return;

        if (registry.TryGetValue(key, out var existing) && existing != null && existing != value)
        {
            Debug.LogWarning(
                $"[{ownerTag}] Duplicate mapping for type '{key.FullName}'. " +
                $"Keeping '{existing.name}' ({existing.GetType().Name}), " +
                $"ignoring '{value.name}' ({value.GetType().Name}).");
            return;
        }

        registry[key] = value;
    }
}