using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;
using Component = UnityEngine.Component;

/// <summary>
/// 리플렉션 비용을 줄이기 위해
/// [GlobalInject], [SceneInject] 필드 정보 및 생성자 정보를 타입별로 캐싱하는 헬퍼.
///
/// v1.1: Roslyn Source Generator 가 생성한 플랜을 1순위로 조회함.
///       생성된 플랜이 없으면 기존 Expression Tree / FieldInfo 경로로 폴백 동작함.
///
/// v2.0:
///   - CachedInjectField.Id: Named 바인딩 키 (하위 호환 4-인자 생성자 유지).
///   - GetInjectableConstructor(Type): [InjectConstructor] 또는 단일 public 생성자를 캐싱.
///   - RegisterGeneratedFactory / TryGetGeneratedFactory: [InjectConstructor] non-reflective 팩토리.
///   - Roslyn Generator 등록 API 에 EditorBrowsable(Never) 적용 (내부 전용 명시).
/// </summary>
public static class TypeDataCache
{
    // ── CachedInjectField ─────────────────────────────────────────────────────

    public readonly struct CachedInjectField
    {
        public readonly string Name;
        public readonly Type FieldType;
        public readonly Action<object, object> Setter;
        public readonly bool Optional;

        /// <summary>
        /// v2.0: Named 바인딩 키. string.Empty 이면 무키(v1.x 기본) 바인딩.
        /// Roslyn Generator 는 기존 4-인자 생성자를 사용하므로 Id = string.Empty 로 처리됨.
        /// Roslyn Generator 업데이트 시 5-인자 생성자 경로로 전환할 것.
        /// </summary>
        public readonly string Id;

        /// <summary>
        /// 하위 호환 생성자. v2.0 이전 Roslyn Generator 가 생성하는 코드와의 하위 호환을 위해 유지.
        /// v2.0 Generator 는 Id 를 포함하는 5-인자 생성자를 직접 호출함.
        /// </summary>
        public CachedInjectField(string name, Type fieldType, Action<object, object> setter, bool optional)
            : this(name, fieldType, setter, optional, string.Empty) { }

        /// <summary>v2.0: Id 를 포함하는 Named 바인딩 전용 생성자.</summary>
        public CachedInjectField(string name, Type fieldType, Action<object, object> setter, bool optional, string id)
        {
            Name = name;
            FieldType = fieldType;
            Setter = setter;
            Optional = optional;
            Id = id ?? string.Empty;
        }
    }

    // ── v1.1: 생성된 플랜 캐시 ───────────────────────────────────────────────
    // 모든 캐시는 Unity 메인 스레드 전용이다.
    // Roslyn 생성 코드의 정적 초기화(Register* 메서드)는 어셈블리 로드 시 메인 스레드에서
    // 실행되므로 읽기/쓰기 경쟁이 발생하지 않는다.

    private static readonly Dictionary<Type, List<CachedInjectField>> _generatedGlobalCache
        = new Dictionary<Type, List<CachedInjectField>>();

    private static readonly Dictionary<Type, List<CachedInjectField>> _generatedSceneCache
        = new Dictionary<Type, List<CachedInjectField>>();

    // ── 기존: Reflection 캐시 (폴백) ─────────────────────────────────────────

    private static readonly Dictionary<Type, List<CachedInjectField>> _globalInjectCache
        = new Dictionary<Type, List<CachedInjectField>>();

    private static readonly Dictionary<Type, List<CachedInjectField>> _sceneInjectCache
        = new Dictionary<Type, List<CachedInjectField>>();

    // ── v2.0: 생성자 캐시 ────────────────────────────────────────────────────
    // null 항목: 주입 가능한 생성자가 없음을 나타냄 (반복 탐색 방지용 센티넬)
    private static readonly Dictionary<Type, ConstructorInfo> _constructorCache
        = new Dictionary<Type, ConstructorInfo>();

    // ── v2.0: Roslyn 생성 팩토리 캐시 ────────────────────────────────────────
    // Func 시그니처: Func< resolver(Type,string→Component), instance(object) >
    // Roslyn Generator 가 [InjectConstructor] 를 감지하면 RegisterGeneratedFactory 로 등록함.
    // CreateAndInject<T> 에서 reflection 경로보다 우선 사용됨.
    //
    // [스레드 안전성] 이 딕셔너리는 잠금을 사용하지 않는다.
    // 근거: RegisterGeneratedFactory 는 Roslyn 생성 코드의 정적 초기화에서,
    //       또는 RuntimeInitializeOnLoad 로 등록된 메서드에서 호출된다.
    //       두 경로 모두 Unity 공식 문서에 따라 메인 스레드 실행이 보장된다.
    //       (Unity Scripting API — RuntimeInitializeOnLoadMethod 항목 참조)
    //       읽기(TryGetGeneratedFactory)도 같은 메인 스레드에서 호출되므로
    //       동시 읽기-쓰기 경쟁이 발생하지 않는다.
    //       외부 코드에서 백그라운드 스레드로 이 API 를 호출하는 것은 지원하지 않는다.
    private static readonly Dictionary<Type, Func<Func<Type, string, Component>, object>>
        _generatedFactoryCache
            = new Dictionary<Type, Func<Func<Type, string, Component>, object>>();

    // ── v2.1: HasAnyInjectField 음성 캐시 ────────────────────────────────────
    // 한 번이라도 "주입 필드 없음"으로 확인된 타입을 기억하여
    // InjectGlobalDependencies 의 반복 조회를 O(1) 로 단축함.
    private static readonly HashSet<Type> _noInjectTypes = new HashSet<Type>();

    /// <summary>
    /// 도메인 리로드 비활성 상태에서도 정적 캐시 오염을 방지함.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearStaticCaches()
    {
        _generatedGlobalCache.Clear();
        _generatedSceneCache.Clear();
        _globalInjectCache.Clear();
        _sceneInjectCache.Clear();
        _constructorCache.Clear();
        _generatedFactoryCache.Clear();
        _noInjectTypes.Clear();
    }

    // ── v1.1: 생성된 플랜 등록 API ───────────────────────────────────────────

    /// <summary>
    /// [UNInject 내부 전용] Roslyn Source Generator 가 생성한 전역 주입 플랜을 등록한다.
    /// 사용자 코드에서 직접 호출하지 않는다.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RegisterGeneratedGlobalFields(Type type, List<CachedInjectField> fields)
    {
        if (type != null && fields != null)
            _generatedGlobalCache[type] = fields;
    }

    /// <summary>
    /// [UNInject 내부 전용] Roslyn Source Generator 가 생성한 씬 주입 플랜을 등록한다.
    /// 사용자 코드에서 직접 호출하지 않는다.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RegisterGeneratedSceneFields(Type type, List<CachedInjectField> fields)
    {
        if (type != null && fields != null)
            _generatedSceneCache[type] = fields;
    }

    /// <summary>
    /// [UNInject 내부 전용] Roslyn Source Generator 가 생성한 [InjectConstructor] 팩토리를 등록한다.
    /// 사용자 코드에서 직접 호출하지 않는다.
    ///
    /// 호출 스레드: 메인 스레드 전용.
    /// 이 메서드는 잠금을 사용하지 않는다 — 위 _generatedFactoryCache 주석 참조.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RegisterGeneratedFactory(
        Type type,
        Func<Func<Type, string, Component>, object> factory)
    {
        if (type == null || factory == null) return;
        _generatedFactoryCache[type] = factory;
    }

    /// <summary>
    /// v2.0: [InjectConstructor] non-reflective 팩토리를 조회한다.
    /// InstallerRegistryHelper.CreateAndInject 에서 reflection 경로보다 우선 호출됨.
    ///
    /// 호출 스레드: 메인 스레드 전용. 잠금 없음 — _generatedFactoryCache 주석 참조.
    /// </summary>
    public static bool TryGetGeneratedFactory(
        Type type,
        out Func<Func<Type, string, Component>, object> factory)
    {
        factory = null;
        if (type == null) return false;
        return _generatedFactoryCache.TryGetValue(type, out factory);
    }

    /// <summary>해당 타입에 Roslyn 생성 전역 플랜이 등록되어 있는지 확인함.</summary>
    public static bool HasGeneratedGlobalPlan(Type type)
        => type != null && _generatedGlobalCache.ContainsKey(type);

    /// <summary>해당 타입에 Roslyn 생성 씬 플랜이 등록되어 있는지 확인함.</summary>
    public static bool HasGeneratedScenePlan(Type type)
        => type != null && _generatedSceneCache.ContainsKey(type);

    /// <summary>v2.0: 해당 타입에 Roslyn 생성 팩토리가 등록되어 있는지 확인함.</summary>
    public static bool HasGeneratedFactory(Type type)
        => type != null && _generatedFactoryCache.ContainsKey(type);

    /// <summary>
    /// v2.1: 타입에 [GlobalInject] 또는 [SceneInject] 필드가 하나라도 있으면 true.
    ///
    /// ObjectInstaller.InjectGlobalDependencies 의 Hot Path 에서 호출됨.
    /// 결과는 내부적으로 두 단계로 캐싱됨:
    ///   - 양성(필드 있음): GetGlobal/SceneInjectFields 의 반환 리스트가 캐싱됨.
    ///   - 음성(필드 없음): _noInjectTypes HashSet 에 추가되어 즉시 반환됨.
    /// 두 경우 모두 첫 호출 이후 O(1) 조회임.
    /// </summary>
    public static bool HasAnyInjectField(Type type)
    {
        if (type == null) return false;
        if (_noInjectTypes.Contains(type)) return false;

        if (GetGlobalInjectFields(type).Count > 0) return true;
        if (GetSceneInjectFields(type).Count > 0) return true;

        _noInjectTypes.Add(type);
        return false;
    }

    // ── 조회 API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 해당 타입에서 [GlobalInject] 가 붙은 필드 목록을 반환함. (캐싱됨)
    ///
    /// v1.1 조회 우선순위:
    ///   1) Roslyn 생성 플랜 — IL2CPP 안전, zero-reflection
    ///   2) Expression Tree / FieldInfo 폴백
    ///
    /// v2.0: CachedInjectField.Id 에 GlobalInjectAttribute.Id 값을 포함.
    /// </summary>
    public static List<CachedInjectField> GetGlobalInjectFields(Type type)
    {
        if (type == null) return new List<CachedInjectField>();

        if (_generatedGlobalCache.TryGetValue(type, out var generated))
            return generated;

        if (_globalInjectCache.TryGetValue(type, out var cached))
            return cached;

        var result = new List<CachedInjectField>();

        foreach (var field in GetAllInstanceFields(type))
        {
            var attr = field.GetCustomAttribute<GlobalInjectAttribute>();
            if (attr != null)
            {
                result.Add(new CachedInjectField(
                    field.Name,
                    field.FieldType,
                    CreateSetter(field),
                    attr.Optional,
                    attr.Id));   // v2.0: Id 포함
            }
        }

        _globalInjectCache[type] = result;
        return result;
    }

    /// <summary>
    /// 해당 타입에서 [SceneInject] 가 붙은 필드 목록을 반환함. (캐싱됨)
    ///
    /// v2.0: CachedInjectField.Id 에 SceneInjectAttribute.Id 값을 포함.
    /// </summary>
    public static List<CachedInjectField> GetSceneInjectFields(Type type)
    {
        if (type == null) return new List<CachedInjectField>();

        if (_generatedSceneCache.TryGetValue(type, out var generated))
            return generated;

        if (_sceneInjectCache.TryGetValue(type, out var cached))
            return cached;

        var result = new List<CachedInjectField>();

        foreach (var field in GetAllInstanceFields(type))
        {
            var attr = field.GetCustomAttribute<SceneInjectAttribute>();
            if (attr != null)
            {
                result.Add(new CachedInjectField(
                    field.Name,
                    field.FieldType,
                    CreateSetter(field),
                    attr.Optional,
                    attr.Id));   // v2.0: Id 포함
            }
        }

        _sceneInjectCache[type] = result;
        return result;
    }

    /// <summary>
    /// v2.0: [InjectConstructor] 가 붙은 생성자 또는 단일 public 생성자를 반환함. (캐싱됨)
    ///
    /// 탐색 우선순위:
    ///   1) [InjectConstructor] 가 붙은 public 생성자
    ///   2) public 생성자가 단 하나인 경우 자동 선택
    ///   3) 해당 없으면 null 반환
    ///
    /// ※ IL2CPP 빌드 주의: 리플렉션 기반 생성자 호출을 사용함.
    ///   해당 타입이 코드 스트리핑되지 않도록 link.xml 보존 규칙 추가 필요.
    ///   partial 선언 시 Roslyn Generator 가 RegisterGeneratedFactory 로 non-reflective 플랜을 등록하며,
    ///   CreateAndInject<T> 는 해당 플랜을 reflection 경로보다 우선 사용함.
    /// </summary>
    public static ConstructorInfo GetInjectableConstructor(Type type)
    {
        if (type == null) return null;

        if (_constructorCache.TryGetValue(type, out var cached))
            return cached;

        ConstructorInfo found = null;
        var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        // 1순위: [InjectConstructor] 마킹된 생성자
        foreach (var ctor in ctors)
        {
            if (ctor.IsDefined(typeof(InjectConstructorAttribute), false))
            {
                found = ctor;
                break;
            }
        }

        // 2순위: 단일 public 생성자 자동 선택
        if (found == null && ctors.Length == 1)
            found = ctors[0];

        // null 도 캐싱하여 반복 탐색을 방지함 (null = 주입 불가 타입 센티넬)
        _constructorCache[type] = found;
        return found;
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
        GetInjectableConstructor(type);  // v2.0: 생성자 캐시 미리 빌드
    }

    public static void Warmup(params Type[] types)
    {
        if (types == null) return;
        for (int i = 0; i < types.Length; i++)
            Warmup(types[i]);
    }

    /// <summary>
    /// (object target, object value) 형태의 필드 세터를 생성함.
    /// - 최초 1회만 컴파일하고 캐싱하여 이후 주입 루프에서 리플렉션 호출을 제거함.
    /// - 일부 런타임(AOT/IL2CPP 등)에서 Expression.Compile 이 제한될 수 있어,
    ///   실패 시에는 안전하게 FieldInfo.SetValue 로 폴백함.
    /// - Roslyn 플랜이 등록된 타입에서는 이 메서드가 호출되지 않음.
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
            // 폴백: 기능 보장 (성능은 일반 Reflection 과 유사함)
            return (target, value) => field.SetValue(target, value);
        }
    }

    /// <summary>
    /// 상속 체인을 따라 private 필드까지 포함하여 Instance 필드를 열거함.
    /// </summary>
    private static IEnumerable<FieldInfo> GetAllInstanceFields(Type type)
    {
        const BindingFlags Flags = BindingFlags.Instance
                                 | BindingFlags.Public
                                 | BindingFlags.NonPublic
                                 | BindingFlags.DeclaredOnly;

        var current = type;
        while (current != null && current != typeof(object))
        {
            foreach (var f in current.GetFields(Flags))
                yield return f;

            current = current.BaseType;
        }
    }
}
