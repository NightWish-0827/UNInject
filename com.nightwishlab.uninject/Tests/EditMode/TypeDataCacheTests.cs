using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// TypeDataCache 의 캐싱·조회 로직을 검증하는 EditMode 단위 테스트.
///
/// 커버리지 대상:
///   - GetGlobalInjectFields  : [GlobalInject] 필드 수집, Optional 플래그, 상속 체인
///   - GetSceneInjectFields   : [SceneInject] 필드 수집, Optional 플래그
///   - 캐싱 동작              : 두 번째 호출은 동일 인스턴스 반환
///   - Setter 동작            : 컴파일된 setter 가 실제 값을 쓰는지 검증
///   - Warmup                 : 예외 없이 완료
///   - HasGeneratedPlan       : 등록 전은 false, 등록 후는 true
///   - 방어 코드              : null 타입 입력
/// </summary>
public class TypeDataCacheTests
{
    // ─── 테스트용 더미 타입 ────────────────────────────────────────────────

    private class DummyManager : MonoBehaviour { }
    private class DummySceneManager : MonoBehaviour { }

    /// <summary>[GlobalInject] 필드 1개를 가진 타입</summary>
    private class TargetWithGlobalInject : MonoBehaviour
    {
        [GlobalInject]
        private DummyManager _manager = default;

        // 테스트에서 setter 검증을 위해 값을 읽어오는 accessor
        public DummyManager Manager => _manager;
    }

    /// <summary>[GlobalInject(optional:true)] 필드를 가진 타입</summary>
    private class TargetWithOptionalGlobalInject : MonoBehaviour
    {
        // CS0414: 구조 검증(Optional 플래그)만 사용. 값은 Setter 경로로만 쓰임.
#pragma warning disable CS0414
        [GlobalInject(true)]
        private DummyManager _optManager = default;
#pragma warning restore CS0414
    }

    /// <summary>[SceneInject] 필드 1개를 가진 타입</summary>
    private class TargetWithSceneInject : MonoBehaviour
    {
        [SceneInject]
        private DummySceneManager _sceneManager = default;

        public DummySceneManager SceneManager => _sceneManager;
    }

    /// <summary>두 종류를 모두 가진 혼합 타입</summary>
    private class TargetWithBothInjects : MonoBehaviour
    {
        // CS0414: 필드명·종류 구분 검증만 사용. 값은 Setter 경로로만 쓰임.
#pragma warning disable CS0414
        [GlobalInject]
        private DummyManager _global = default;

        [SceneInject]
        private DummySceneManager _scene = default;
#pragma warning restore CS0414
    }

    /// <summary>inject 필드 없는 타입</summary>
    private class TargetWithNoInject : MonoBehaviour { }

    // ── HasGeneratedPlan 전용 센티넬 타입 ──────────────────────────────────
    // AfterManualRegister 테스트가 정적 캐시에 등록한 결과가
    // NotRegistered 테스트의 조회 결과에 간섭하는 것을 방지하기 위해
    // 등록(write)과 미등록 확인(read)에 서로 다른 타입을 사용한다.
    //
    // TypeDataCache 는 정적 딕셔너리를 사용하므로 테스트 실행 순서에 관계없이
    // 한 번 등록된 타입은 프로세스가 살아있는 동안 등록 상태를 유지한다.
    // SubsystemRegistration 초기화는 PlayMode 진입 시에만 동작하므로
    // EditMode Test Runner 연속 실행 중에는 캐시가 초기화되지 않는다.
    private class SentinelForGlobalPlanRegister  : MonoBehaviour { }
    private class SentinelForScenePlanRegister   : MonoBehaviour { }

    /// <summary>부모에 [GlobalInject] 가 있고 자식에도 있는 타입 (상속 체인)</summary>
    private class ParentWithGlobalInject : MonoBehaviour
    {
        // CS0414: 상속 체인 필드 수집 검증만 사용. 값은 Setter 경로로만 쓰임.
#pragma warning disable CS0414
        [GlobalInject]
        private DummyManager _parentManager = default;
#pragma warning restore CS0414
    }

    private class ChildWithGlobalInject : ParentWithGlobalInject
    {
        // CS0414: 상속 체인 필드 수집 검증만 사용. 값은 Setter 경로로만 쓰임.
#pragma warning disable CS0414
        [GlobalInject]
        private DummyManager _childManager = default;
#pragma warning restore CS0414
    }

    private GameObject _go;

    [SetUp]
    public void SetUp()
    {
        _go = new GameObject("__TypeDataCacheTestGO__");
    }

    [TearDown]
    public void TearDown()
    {
        if (_go != null)
            UnityEngine.Object.DestroyImmediate(_go);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetGlobalInjectFields
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void GetGlobalInjectFields_SingleField_ReturnsOneEntry()
    {
        var fields = TypeDataCache.GetGlobalInjectFields(typeof(TargetWithGlobalInject));
        Assert.AreEqual(1, fields.Count);
    }

    [Test]
    public void GetGlobalInjectFields_FieldName_MatchesDeclaredName()
    {
        var fields = TypeDataCache.GetGlobalInjectFields(typeof(TargetWithGlobalInject));
        Assert.AreEqual("_manager", fields[0].Name);
    }

    [Test]
    public void GetGlobalInjectFields_FieldType_MatchesDeclaredType()
    {
        var fields = TypeDataCache.GetGlobalInjectFields(typeof(TargetWithGlobalInject));
        Assert.AreEqual(typeof(DummyManager), fields[0].FieldType);
    }

    [Test]
    public void GetGlobalInjectFields_DefaultOptional_IsFalse()
    {
        var fields = TypeDataCache.GetGlobalInjectFields(typeof(TargetWithGlobalInject));
        Assert.IsFalse(fields[0].Optional);
    }

    [Test]
    public void GetGlobalInjectFields_OptionalTrue_IsTrue()
    {
        var fields = TypeDataCache.GetGlobalInjectFields(typeof(TargetWithOptionalGlobalInject));
        Assert.AreEqual(1, fields.Count);
        Assert.IsTrue(fields[0].Optional);
    }

    [Test]
    public void GetGlobalInjectFields_NoInjectField_ReturnsEmpty()
    {
        var fields = TypeDataCache.GetGlobalInjectFields(typeof(TargetWithNoInject));
        Assert.AreEqual(0, fields.Count);
    }

    [Test]
    public void GetGlobalInjectFields_InheritanceChain_CollectsAllFields()
    {
        // 부모 1개 + 자식 1개 = 2개 수집
        var fields = TypeDataCache.GetGlobalInjectFields(typeof(ChildWithGlobalInject));
        Assert.AreEqual(2, fields.Count, "상속 체인의 모든 [GlobalInject] 필드가 수집되어야 함");
    }

    [Test]
    public void GetGlobalInjectFields_DoesNotReturnSceneInjectFields()
    {
        var fields = TypeDataCache.GetGlobalInjectFields(typeof(TargetWithBothInjects));
        Assert.AreEqual(1, fields.Count, "[GlobalInject] 만 반환해야 함");
        Assert.AreEqual("_global", fields[0].Name);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetSceneInjectFields
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void GetSceneInjectFields_SingleField_ReturnsOneEntry()
    {
        var fields = TypeDataCache.GetSceneInjectFields(typeof(TargetWithSceneInject));
        Assert.AreEqual(1, fields.Count);
    }

    [Test]
    public void GetSceneInjectFields_FieldType_MatchesDeclaredType()
    {
        var fields = TypeDataCache.GetSceneInjectFields(typeof(TargetWithSceneInject));
        Assert.AreEqual(typeof(DummySceneManager), fields[0].FieldType);
    }

    [Test]
    public void GetSceneInjectFields_DoesNotReturnGlobalInjectFields()
    {
        var fields = TypeDataCache.GetSceneInjectFields(typeof(TargetWithBothInjects));
        Assert.AreEqual(1, fields.Count, "[SceneInject] 만 반환해야 함");
        Assert.AreEqual("_scene", fields[0].Name);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 캐싱 동작
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void GetGlobalInjectFields_CalledTwice_ReturnsSameListInstance()
    {
        var first  = TypeDataCache.GetGlobalInjectFields(typeof(TargetWithGlobalInject));
        var second = TypeDataCache.GetGlobalInjectFields(typeof(TargetWithGlobalInject));

        Assert.AreSame(first, second, "두 번째 호출은 캐시된 동일 인스턴스를 반환해야 함");
    }

    [Test]
    public void GetSceneInjectFields_CalledTwice_ReturnsSameListInstance()
    {
        var first  = TypeDataCache.GetSceneInjectFields(typeof(TargetWithSceneInject));
        var second = TypeDataCache.GetSceneInjectFields(typeof(TargetWithSceneInject));

        Assert.AreSame(first, second, "두 번째 호출은 캐시된 동일 인스턴스를 반환해야 함");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Setter 동작 검증
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void CachedInjectField_Setter_WritesValueToTarget()
    {
        var fields = TypeDataCache.GetGlobalInjectFields(typeof(TargetWithGlobalInject));
        Assert.AreEqual(1, fields.Count);

        var target  = _go.AddComponent<TargetWithGlobalInject>();
        var manager = _go.AddComponent<DummyManager>();

        // 컴파일된 setter 호출
        fields[0].Setter(target, manager);

        Assert.AreEqual(manager, target.Manager,
            "Setter 가 실제 필드에 값을 써야 함");
    }

    [Test]
    public void CachedInjectField_Setter_WritesSceneValue()
    {
        var fields = TypeDataCache.GetSceneInjectFields(typeof(TargetWithSceneInject));
        Assert.AreEqual(1, fields.Count);

        var target  = _go.AddComponent<TargetWithSceneInject>();
        var manager = _go.AddComponent<DummySceneManager>();

        fields[0].Setter(target, manager);

        Assert.AreEqual(manager, target.SceneManager,
            "SceneInject Setter 가 실제 필드에 값을 써야 함");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Warmup
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void Warmup_SingleType_DoesNotThrow()
    {
        Assert.DoesNotThrow(() =>
            TypeDataCache.Warmup(typeof(TargetWithGlobalInject)));
    }

    [Test]
    public void Warmup_MultipleTypes_DoesNotThrow()
    {
        Assert.DoesNotThrow(() =>
            TypeDataCache.Warmup(
                typeof(TargetWithGlobalInject),
                typeof(TargetWithSceneInject),
                typeof(TargetWithBothInjects)));
    }

    [Test]
    public void Warmup_NullType_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => TypeDataCache.Warmup((System.Type)null));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HasGeneratedPlan
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void HasGeneratedGlobalPlan_NotRegistered_ReturnsFalse()
    {
        // 테스트 환경에서는 Source Generator 가 실행되지 않으므로 항상 false
        Assert.IsFalse(TypeDataCache.HasGeneratedGlobalPlan(typeof(TargetWithGlobalInject)));
    }

    [Test]
    public void HasGeneratedScenePlan_NotRegistered_ReturnsFalse()
    {
        Assert.IsFalse(TypeDataCache.HasGeneratedScenePlan(typeof(TargetWithSceneInject)));
    }

    [Test]
    public void HasGeneratedGlobalPlan_AfterManualRegister_ReturnsTrue()
    {
        // NotRegistered 테스트와 타입을 분리해 정적 캐시 간섭을 방지함.
        var dummy = new List<TypeDataCache.CachedInjectField>();
        TypeDataCache.RegisterGeneratedGlobalFields(typeof(SentinelForGlobalPlanRegister), dummy);

        Assert.IsTrue(TypeDataCache.HasGeneratedGlobalPlan(typeof(SentinelForGlobalPlanRegister)));
    }

    [Test]
    public void HasGeneratedScenePlan_AfterManualRegister_ReturnsTrue()
    {
        // NotRegistered 테스트와 타입을 분리해 정적 캐시 간섭을 방지함.
        var dummy = new List<TypeDataCache.CachedInjectField>();
        TypeDataCache.RegisterGeneratedSceneFields(typeof(SentinelForScenePlanRegister), dummy);

        Assert.IsTrue(TypeDataCache.HasGeneratedScenePlan(typeof(SentinelForScenePlanRegister)));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 방어 코드
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void GetGlobalInjectFields_NullType_ReturnsEmptyList()
    {
        var fields = TypeDataCache.GetGlobalInjectFields(null);
        Assert.IsNotNull(fields);
        Assert.AreEqual(0, fields.Count);
    }

    [Test]
    public void GetSceneInjectFields_NullType_ReturnsEmptyList()
    {
        var fields = TypeDataCache.GetSceneInjectFields(null);
        Assert.IsNotNull(fields);
        Assert.AreEqual(0, fields.Count);
    }
}