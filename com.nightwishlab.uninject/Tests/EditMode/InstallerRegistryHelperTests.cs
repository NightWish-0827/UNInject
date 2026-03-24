using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// InstallerRegistryHelper 의 순수 로직을 검증하는 EditMode 단위 테스트.
///
/// 커버리지 대상:
///   - IsMappableAbstraction : 필터 정책 검증 (System.*, UnityEngine.*, 사용자 정의 타입)
///   - TryAdd               : 정상 추가 / 중복 키 시 선착순 유지 / null 방어
///   - RegisterTypeMappings : 구체 타입 / 인터페이스 / 상속 체인 등록, BindType 오버라이드
/// </summary>
public class InstallerRegistryHelperTests
{
    // ─── 테스트용 더미 타입 ────────────────────────────────────────────────

    private interface IUserDefined { }
    private interface IAnotherUserDefined { }

    // MonoBehaviour 는 에디터에서도 AddComponent 로만 생성 가능.
    // Component 가 필요한 테스트는 setUp/tearDown 에서 GameObject 를 관리.
    private class FakeManager : MonoBehaviour, IUserDefined { }
    private class FakeManagerChild : FakeManager, IAnotherUserDefined { }
    private class FakeManagerUnrelated : MonoBehaviour { }

    private GameObject _go;

    [SetUp]
    public void SetUp()
    {
        _go = new GameObject("__TestGO__");
    }

    [TearDown]
    public void TearDown()
    {
        if (_go != null)
            UnityEngine.Object.DestroyImmediate(_go);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IsMappableAbstraction
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void IsMappableAbstraction_Null_ReturnsFalse()
    {
        Assert.IsFalse(InstallerRegistryHelper.IsMappableAbstraction(null));
    }

    [Test]
    public void IsMappableAbstraction_Object_ReturnsFalse()
    {
        Assert.IsFalse(InstallerRegistryHelper.IsMappableAbstraction(typeof(object)));
    }

    [Test]
    public void IsMappableAbstraction_SystemInterface_ReturnsFalse()
    {
        Assert.IsFalse(InstallerRegistryHelper.IsMappableAbstraction(typeof(IDisposable)));
        Assert.IsFalse(InstallerRegistryHelper.IsMappableAbstraction(typeof(System.Collections.IEnumerable)));
    }

    [Test]
    public void IsMappableAbstraction_UnityEngineInterface_ReturnsFalse()
    {
        // UnityEngine 네임스페이스 인터페이스는 매핑 불가
        Assert.IsFalse(InstallerRegistryHelper.IsMappableAbstraction(
            typeof(UnityEngine.EventSystems.IPointerDownHandler)));
    }

    [Test]
    public void IsMappableAbstraction_UserDefinedInterface_ReturnsTrue()
    {
        Assert.IsTrue(InstallerRegistryHelper.IsMappableAbstraction(typeof(IUserDefined)));
        Assert.IsTrue(InstallerRegistryHelper.IsMappableAbstraction(typeof(IAnotherUserDefined)));
    }

    [Test]
    public void IsMappableAbstraction_UserDefinedClass_ReturnsTrue()
    {
        // 네임스페이스 없는 사용자 정의 클래스
        Assert.IsTrue(InstallerRegistryHelper.IsMappableAbstraction(typeof(FakeManager)));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TryAdd
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void TryAdd_NewKey_AddsSuccessfully()
    {
        var registry = new Dictionary<Type, Component>();
        var comp = _go.AddComponent<FakeManager>();

        InstallerRegistryHelper.TryAdd(registry, typeof(FakeManager), comp, "Test");

        Assert.IsTrue(registry.ContainsKey(typeof(FakeManager)));
        Assert.AreEqual(comp, registry[typeof(FakeManager)]);
    }

    [Test]
    public void TryAdd_DuplicateKey_KeepsFirst()
    {
        var go2 = new GameObject("__TestGO2__");
        try
        {
            var registry = new Dictionary<Type, Component>();
            var first  = _go.AddComponent<FakeManager>();
            var second = go2.AddComponent<FakeManager>();

            InstallerRegistryHelper.TryAdd(registry, typeof(FakeManager), first,  "Test");
            InstallerRegistryHelper.TryAdd(registry, typeof(FakeManager), second, "Test");

            // 먼저 등록된 컴포넌트가 유지되어야 함
            Assert.AreEqual(first, registry[typeof(FakeManager)]);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go2);
        }
    }

    [Test]
    public void TryAdd_SameInstance_DoesNotDuplicate()
    {
        var registry = new Dictionary<Type, Component>();
        var comp = _go.AddComponent<FakeManager>();

        InstallerRegistryHelper.TryAdd(registry, typeof(FakeManager), comp, "Test");
        InstallerRegistryHelper.TryAdd(registry, typeof(FakeManager), comp, "Test");

        Assert.AreEqual(1, registry.Count);
    }

    [Test]
    public void TryAdd_NullKey_DoesNotAdd()
    {
        var registry = new Dictionary<Type, Component>();
        var comp = _go.AddComponent<FakeManager>();

        InstallerRegistryHelper.TryAdd(registry, null, comp, "Test");

        Assert.AreEqual(0, registry.Count);
    }

    [Test]
    public void TryAdd_NullValue_DoesNotAdd()
    {
        var registry = new Dictionary<Type, Component>();

        InstallerRegistryHelper.TryAdd(registry, typeof(FakeManager), null, "Test");

        Assert.AreEqual(0, registry.Count);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RegisterTypeMappings — bindTypeOverride = null (전체 매핑)
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void RegisterTypeMappings_NoOverride_RegistersConcreteType()
    {
        var registry = new Dictionary<Type, Component>();
        var comp = _go.AddComponent<FakeManager>();

        InstallerRegistryHelper.RegisterTypeMappings(comp, registry, "Test", null);

        Assert.IsTrue(registry.ContainsKey(typeof(FakeManager)),
            "구체 타입(FakeManager)이 등록되어야 함");
    }

    [Test]
    public void RegisterTypeMappings_NoOverride_RegistersUserDefinedInterface()
    {
        var registry = new Dictionary<Type, Component>();
        var comp = _go.AddComponent<FakeManager>();

        InstallerRegistryHelper.RegisterTypeMappings(comp, registry, "Test", null);

        Assert.IsTrue(registry.ContainsKey(typeof(IUserDefined)),
            "사용자 정의 인터페이스(IUserDefined)가 등록되어야 함");
        Assert.AreEqual(comp, registry[typeof(IUserDefined)]);
    }

    [Test]
    public void RegisterTypeMappings_NoOverride_DoesNotRegisterSystemInterfaces()
    {
        var registry = new Dictionary<Type, Component>();
        var comp = _go.AddComponent<FakeManager>();

        InstallerRegistryHelper.RegisterTypeMappings(comp, registry, "Test", null);

        Assert.IsFalse(registry.ContainsKey(typeof(IDisposable)),
            "System.IDisposable 은 등록되지 않아야 함");
    }

    [Test]
    public void RegisterTypeMappings_NoOverride_RegistersInheritedInterfaces()
    {
        // FakeManagerChild 는 FakeManager 를 상속하며 IAnotherUserDefined 도 구현
        var registry = new Dictionary<Type, Component>();
        var comp = _go.AddComponent<FakeManagerChild>();

        InstallerRegistryHelper.RegisterTypeMappings(comp, registry, "Test", null);

        Assert.IsTrue(registry.ContainsKey(typeof(IUserDefined)),
            "부모 타입에서 상속된 IUserDefined 가 등록되어야 함");
        Assert.IsTrue(registry.ContainsKey(typeof(IAnotherUserDefined)),
            "자신의 IAnotherUserDefined 가 등록되어야 함");
        Assert.IsTrue(registry.ContainsKey(typeof(FakeManager)),
            "부모 클래스 FakeManager 가 등록되어야 함");
        Assert.IsTrue(registry.ContainsKey(typeof(FakeManagerChild)),
            "구체 타입 FakeManagerChild 가 등록되어야 함");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RegisterTypeMappings — bindTypeOverride 지정
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void RegisterTypeMappings_WithValidOverride_RegistersOnlyOverrideType()
    {
        var registry = new Dictionary<Type, Component>();
        var comp = _go.AddComponent<FakeManager>();

        InstallerRegistryHelper.RegisterTypeMappings(comp, registry, "Test", typeof(IUserDefined));

        Assert.IsTrue(registry.ContainsKey(typeof(IUserDefined)),
            "BindType 으로 지정된 IUserDefined 가 등록되어야 함");
        Assert.IsFalse(registry.ContainsKey(typeof(FakeManager)),
            "BindType 지정 시 구체 타입은 등록되지 않아야 함");
    }

    [Test]
    public void RegisterTypeMappings_WithIncompatibleOverride_FallsBackToFullMapping()
    {
        // FakeManagerUnrelated 는 IUserDefined 를 구현하지 않음 → 호환 불가 → 전체 매핑 폴백
        var registry = new Dictionary<Type, Component>();
        var comp = _go.AddComponent<FakeManagerUnrelated>();

        InstallerRegistryHelper.RegisterTypeMappings(comp, registry, "Test", typeof(IUserDefined));

        // 폴백 시 구체 타입은 등록되어야 함
        Assert.IsTrue(registry.ContainsKey(typeof(FakeManagerUnrelated)),
            "호환 불가 BindType 지정 시 폴백으로 구체 타입이 등록되어야 함");
        // BindType 자체는 등록되지 않아야 함
        Assert.IsFalse(registry.ContainsKey(typeof(IUserDefined)),
            "호환 불가 BindType 은 등록되지 않아야 함");
    }

    [Test]
    public void RegisterTypeMappings_NullComponent_DoesNotThrow()
    {
        var registry = new Dictionary<Type, Component>();
        Assert.DoesNotThrow(() =>
            InstallerRegistryHelper.RegisterTypeMappings(null, registry, "Test", null));
        Assert.AreEqual(0, registry.Count);
    }

    [Test]
    public void RegisterTypeMappings_NullRegistry_DoesNotThrow()
    {
        var comp = _go.AddComponent<FakeManager>();
        Assert.DoesNotThrow(() =>
            InstallerRegistryHelper.RegisterTypeMappings(comp, null, "Test", null));
    }
}