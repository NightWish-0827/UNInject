using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 리플렉션 비용을 줄이기 위해
/// [GlobalInject], [SceneInject] 필드 정보를 타입별로 캐싱하는 헬퍼
///
/// v1.1: Roslyn Source Generator 가 생성한 플랜을 1순위로 조회함.
///       생성된 플랜이 없으면 기존 Expression Tree / FieldInfo 경로로 풀백 동작함.
/// </summary>
public static class TypeDataCache
{
    public readonly struct CachedInjectField
    {
        public readonly string Name;
        public readonly Type FieldType;
        public readonly Action<object, object> Setter;
        public readonly bool Optional;

        public CachedInjectField(string name, Type fieldType, Action<object, object> setter, bool optional)
        {
            Name = name;
            FieldType = fieldType;
            Setter = setter;
            Optional = optional;
        }
    }

    // ── v1.1: 생성된 플랜 캐시 ───────────────────────────────────────────────
    // Roslyn Source Generator 가 생성하는 UNInjectPlanRegistry.g.cs 의
    // [RuntimeInitializeOnLoadMethod(AfterAssembliesLoaded)] 에서 채워짐.
    // SubsystemRegistration(캐시 초기화) 이후 실행이 보장됨.

    private static readonly Dictionary<Type, List<CachedInjectField>> _generatedGlobalCache
        = new Dictionary<Type, List<CachedInjectField>>();

    private static readonly Dictionary<Type, List<CachedInjectField>> _generatedSceneCache
        = new Dictionary<Type, List<CachedInjectField>>();

    // ── 기존: Reflection 캐시 (폴백, 원본 유지) ──────────────────────────────

    private static readonly Dictionary<Type, List<CachedInjectField>> _globalInjectCache
        = new Dictionary<Type, List<CachedInjectField>>();

    private static readonly Dictionary<Type, List<CachedInjectField>> _sceneInjectCache
        = new Dictionary<Type, List<CachedInjectField>>();

    /// <summary>
    /// 도메인 리로드 비활성 상태에서도 정적 캐시 오염을 방지함.
    /// (플레이 모드 진입 시점에 정적 딕셔너리를 초기화)
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearStaticCaches()
    {
        _generatedGlobalCache.Clear();
        _generatedSceneCache.Clear();
        _globalInjectCache.Clear();
        _sceneInjectCache.Clear();
    }

    // ── v1.1: 생성된 플랜 등록 API ──────────────────────────────────────────
    // 생성된 UNInjectPlanRegistry.g.cs 에서만 호출됨. Do not call this method directly in user code.

    public static void RegisterGeneratedGlobalFields(Type type, List<CachedInjectField> fields)
    {
        if (type != null && fields != null)
            _generatedGlobalCache[type] = fields;
    }

    public static void RegisterGeneratedSceneFields(Type type, List<CachedInjectField> fields)
    {
        if (type != null && fields != null)
            _generatedSceneCache[type] = fields;
    }

    /// <summary>
    /// 해당 타입에 Roslyn 생성 플랜이 등록되어 있는지 확인함.
    /// 테스트 및 진단 용도로 사용함.
    /// </summary>
    public static bool HasGeneratedGlobalPlan(Type type)
        => type != null && _generatedGlobalCache.ContainsKey(type);

    public static bool HasGeneratedScenePlan(Type type)
        => type != null && _generatedSceneCache.ContainsKey(type);

    // ── 조회 API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 해당 타입에서 [GlobalInject] 가 붙은 필드 목록을 반환함. (캐싱됨)
    ///
    /// v1.1 조회 우선순위:
    ///   1) Roslyn 생성 플랜 (_generatedGlobalCache) — IL2CPP 안전, zero-reflection
    ///   2) Expression Tree / FieldInfo 폴백 (_globalInjectCache) — 기존 동작 그대로
    /// </summary>
    public static List<CachedInjectField> GetGlobalInjectFields(Type type)
    {
        if (type == null) return new List<CachedInjectField>();

        // 1순위: 생성된 플랜
        if (_generatedGlobalCache.TryGetValue(type, out var generated))
            return generated;

        // 2순위: 기존 Reflection 경로 (원본 코드 그대로)
        if (_globalInjectCache.TryGetValue(type, out var cached))
            return cached;

        var result = new List<CachedInjectField>();
        var fields = GetAllInstanceFields(type);

        foreach (var field in fields)
        {
            var attr = field.GetCustomAttribute<GlobalInjectAttribute>();
            if (attr != null)
            {
                // FieldInfo.SetValue() 오버헤드 제거를 위해 setter 델리게이트를 컴파일해 캐싱함.
                result.Add(new CachedInjectField(field.Name, field.FieldType, CreateSetter(field), attr.Optional));
            }
        }

        _globalInjectCache[type] = result;
        return result;
    }

    /// <summary>
    /// 해당 타입에서 [SceneInject] 가 붙은 필드 목록을 반환함. (캐싱됨)
    ///
    /// v1.1 조회 우선순위:
    ///   1) Roslyn 생성 플랜 (_generatedSceneCache) — IL2CPP 안전, zero-reflection
    ///   2) Expression Tree / FieldInfo 폴백 (_sceneInjectCache) — 기존 동작 그대로
    /// </summary>
    public static List<CachedInjectField> GetSceneInjectFields(Type type)
    {
        if (type == null) return new List<CachedInjectField>();

        // 1순위: 생성된 플랜
        if (_generatedSceneCache.TryGetValue(type, out var generated))
            return generated;

        // 2순위: 기존 Reflection 경로 (원본 코드 그대로)
        if (_sceneInjectCache.TryGetValue(type, out var cached))
            return cached;

        var result = new List<CachedInjectField>();
        var fields = GetAllInstanceFields(type);

        foreach (var field in fields)
        {
            var attr = field.GetCustomAttribute<SceneInjectAttribute>();
            if (attr != null)
            {
                // FieldInfo.SetValue() 오버헤드 제거를 위해 setter 델리게이트를 컴파일해 캐싱함.
                result.Add(new CachedInjectField(field.Name, field.FieldType, CreateSetter(field), attr.Optional));
            }
        }

        _sceneInjectCache[type] = result;
        return result;
    }

    /// <summary>
    /// 첫 주입 스파이크를 줄이기 위해, 지정 타입의 주입 메타데이터/세터를 미리 컴파일해 둠.
    /// 생성된 플랜이 등록된 타입은 이 호출이 사실상 무비용임.
    /// </summary>
    public static void Warmup(Type type)
    {
        if (type == null) return;
        GetGlobalInjectFields(type);
        GetSceneInjectFields(type);
    }

    public static void Warmup(params Type[] types)
    {
        if (types == null) return;
        for (int i = 0; i < types.Length; i++)
        {
            Warmup(types[i]);
        }
    }

    /// <summary>
    /// (object target, object value) 형태의 필드 세터를 생성함.
    /// - 최초 1회만 컴파일하고 캐싱하여 이후 주입 루프에서 리플렉션 호출을 제거함.
    /// - 일부 런타임(AOT/IL2CPP 등)에서 Expression.Compile 이 제한될 수 있어,
    ///   실패 시에는 안전하게 FieldInfo.SetValue 로 폴백함.
    /// - Roslyn 플랜이 등록된 타입에서는 이 메서드가 호출되지 않음
    /// </summary>
    private static Action<object, object> CreateSetter(FieldInfo field)
    {
        if (field == null)
            return (t, v) => { };

        try
        {
            var targetParam = Expression.Parameter(typeof(object), "target");
            var valueParam = Expression.Parameter(typeof(object), "value");

            var declaring = field.DeclaringType;
            if (declaring == null)
                return (t, v) => { };

            var typedTarget = Expression.Convert(targetParam, declaring);
            var typedValue = Expression.Convert(valueParam, field.FieldType);

            var assign = Expression.Assign(Expression.Field(typedTarget, field), typedValue);
            var lambda = Expression.Lambda<Action<object, object>>(assign, targetParam, valueParam);
            return lambda.Compile();
        }
        catch (Exception)
        {
            // 폴백: 기능 보장 (성능은 일반 Reflection과과 유사함)
            return (target, value) => field.SetValue(target, value);
        }
    }

    /// <summary>
    /// 상속 체인을 따라 private 필드까지 포함하여 Instance 필드를 열거함.
    /// (기본 GetFields는 베이스 타입의 private 필드를 반환하지 않음. DeclaredOnly 플래그 사용)
    /// </summary>
    private static IEnumerable<FieldInfo> GetAllInstanceFields(Type type)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        var current = type;
        while (current != null && current != typeof(object))
        {
            foreach (var f in current.GetFields(Flags))
            {
                yield return f;
            }
            current = current.BaseType;
        }
    }
}