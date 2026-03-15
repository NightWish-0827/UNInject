# UNInject

**UNInject**는 Unity 프로젝트의 객체 간 결합도를 낮추고 의존성 관리를 효율적으로 처리하기 위해 설계된 경량 의존성 주입(Dependency Injection) 프레임워크입니다.

## 개요

많은 리소스를 사용하는 무거운 DI 컨테이너 대신, Unity의 워크플로우를 존중하면서도 성능과 명확성을 동시에 확보하는 것을 목표로 제작되었습니다. 에디터 타임의 의존성 베이킹(Baking)과 런타임의 동적 해소(Resolution)를 결합하여 유연한 구조를 제공합니다.

## 주요 특징

- **계층적 주입 구조**: `Local`, `Scene`, `Global` 세 가지 범위를 통해 의존성의 생명주기와 범위를 명확하게 관리합니다.
- **에디터 기반 베이킹**: `Local` 의존성의 경우 에디터 타임에 미리 연결하여 런타임 오버헤드를 최소화합니다.
- **Attribute 기반 인터페이스**: `[Inject]`, `[GlobalInject]`, `[SceneInject]` 등 직관적인 어트리뷰트를 통해 손쉽게 의존성을 정의할 수 있습니다.
- **자동화된 관리**: `Installer` 시리즈(`Master`, `Scene`, `Object`)를 통해 중앙 집중식 또는 분산식 의존성 관리가 가능합니다.

## 1.0.0 (최초 출시) 패치 노트

- **수동 주입 API (풀링/동적 생성 호환)**: `ObjectInstaller.InjectTarget()`, `TryInjectTarget()`, `InjectGameObject()`
- **극단적 런타임 최적화**: `TypeDataCache`에서 주입 세터를 Expression Tree로 컴파일/캐싱하여 런타임 주입 루프의 리플렉션 호출을 최소화
- **인터페이스 기반 느슨한 결합**: `[Referral(typeof(IMyService))]`, `[SceneReferral(typeof(IMyService))]` 형태의 `BindType` 지원
- **도메인 리로드 OFF 방어**: 플레이 모드 진입 시 `TypeDataCache` 정적 캐시 초기화(SubsystemRegistration) + Installer 싱글톤 안전 정리
- **엔터프라이즈 운영 품질**: 선택 의존성(Optional Inject) + 주입 완료 콜백(`IInjected`) + `TryResolve*` 계열 API 추가

## 빠른 사용법 (요약)

### 1) 전역/씬 매니저 등록(바인딩)

```csharp
// 구체 타입 그대로 등록
[Referral]
public class GameManager : MonoBehaviour { }

// 인터페이스로 바인딩(이 경우 Resolve 키는 IInputManager)
[Referral(typeof(IInputManager))]
public class InputManager : MonoBehaviour, IInputManager { }
```

### 2) 필드 주입

```csharp
public class PlayerController : MonoBehaviour, IInjected
{
    [GlobalInject] private IInputManager _input;                 // 필수
    [SceneInject(true)] private IStageContext _stageContext;     // 선택(Optional)

    public void OnInjected()
    {
        // ObjectInstaller가 이 컴포넌트에 대해 주입을 수행했고,
        // "필수 의존성" 주입이 모두 성공했을 때 자동으로 호출됩니다.
    }
}
```

### 3) 풀링/런타임 생성 오브젝트 주입

```csharp
// Spawn 직후 한 번 호출하는 패턴을 권장합니다.
// (ObjectInstaller가 있는 루트/씬 컨텍스트를 기준으로 주입)
objectInstaller.InjectTarget(spawnedMonoBehaviour);
// 또는
objectInstaller.InjectGameObject(spawnedRootGameObject);
```

## 주의사항

- **IL2CPP/AOT 환경**: 일부 환경에서 Expression Compile이 제한될 수 있어, 이 경우 기능은 유지되지만 성능 최적화가 폴백될 수 있습니다.
- **에디터 워크플로우**: `MasterInstaller`/`SceneInstaller`는 인스펙터의 `Refresh * Registry`로 매니저 목록을 갱신하는 흐름을 권장합니다.

## 권리 고지 및 소유권

본 소프트웨어 및 포함된 모든 소스 코드는 **Team. Waleuleu LAB**에 의해 설계 및 구현되었으며, 모든 창작적 권리는 작성자에게 귀속됩니다. 

- **개발자**: Nightwish0827 황준혁
- **목적**: Unity 프로젝트 내 효율적인 아키텍처 구축 및 의존성 관리 자동화

---

*Usage documentation and detailed guides will be updated in the future.*
