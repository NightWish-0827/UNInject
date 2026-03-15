using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 리플렉션 비용을 줄이기 위해
/// [GlobalInject], [SceneInject] 필드 정보를 타입별로 캐싱하는 헬퍼입니다.
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

    private static readonly Dictionary<Type, List<CachedInjectField>> _globalInjectCache
        = new Dictionary<Type, List<CachedInjectField>>();

    private static readonly Dictionary<Type, List<CachedInjectField>> _sceneInjectCache
        = new Dictionary<Type, List<CachedInjectField>>();

    /// <summary>
    /// 도메인 리로드 비활성 상태에서도 정적 캐시 오염을 방지합니다.
    /// (플레이 모드 진입 시점에 정적 딕셔너리를 초기화)
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearStaticCaches()
    {
        _globalInjectCache.Clear();
        _sceneInjectCache.Clear();
    }

    /// <summary>
    /// 해당 타입에서 [GlobalInject] 가 붙은 필드 목록을 반환합니다. (캐싱됨)
    /// </summary>
    public static List<CachedInjectField> GetGlobalInjectFields(Type type)
    {
        if (type == null) return new List<CachedInjectField>();

        if (_globalInjectCache.TryGetValue(type, out var cached))
        {
            return cached;
        }

        var result = new List<CachedInjectField>();
        var fields = GetAllInstanceFields(type);

        foreach (var field in fields)
        {
            var attr = field.GetCustomAttribute<GlobalInjectAttribute>();
            if (attr != null)
            {
                // FieldInfo.SetValue() 오버헤드 제거를 위해 setter 델리게이트를 컴파일해 캐싱합니다.
                result.Add(new CachedInjectField(field.Name, field.FieldType, CreateSetter(field), attr.Optional));
            }
        }

        _globalInjectCache[type] = result;
        return result;
    }

    /// <summary>
    /// 해당 타입에서 [SceneInject] 가 붙은 필드 목록을 반환합니다. (캐싱됨)
    /// </summary>
    public static List<CachedInjectField> GetSceneInjectFields(Type type)
    {
        if (type == null) return new List<CachedInjectField>();

        if (_sceneInjectCache.TryGetValue(type, out var cached))
        {
            return cached;
        }

        var result = new List<CachedInjectField>();
        var fields = GetAllInstanceFields(type);

        foreach (var field in fields)
        {
            var attr = field.GetCustomAttribute<SceneInjectAttribute>();
            if (attr != null)
            {
                // FieldInfo.SetValue() 오버헤드 제거를 위해 setter 델리게이트를 컴파일해 캐싱합니다.
                result.Add(new CachedInjectField(field.Name, field.FieldType, CreateSetter(field), attr.Optional));
            }
        }

        _sceneInjectCache[type] = result;
        return result;
    }

    /// <summary>
    /// 첫 주입 스파이크를 줄이기 위해, 지정 타입의 주입 메타데이터/세터를 미리 컴파일해 둡니다.
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
    /// (object target, object value) 형태의 필드 세터를 생성합니다.
    /// - 최초 1회만 컴파일하고 캐싱하여 이후 주입 루프에서 리플렉션 호출을 제거합니다.
    /// - 일부 런타임(AOT/IL2CPP 등)에서 Expression.Compile 이 제한될 수 있어,
    ///   실패 시에는 안전하게 FieldInfo.SetValue 로 폴백합니다.
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
            // 폴백: 기능 보장 (성능은 낮음)
            return (target, value) => field.SetValue(target, value);
        }
    }

    /// <summary>
    /// 상속 체인을 따라 private 필드까지 포함하여 Instance 필드를 열거합니다.
    /// (기본 GetFields는 베이스 타입의 private 필드를 반환하지 않음)
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

