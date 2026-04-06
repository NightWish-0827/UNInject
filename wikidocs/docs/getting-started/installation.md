---
id: installation
title: 설치 및 환경 요구사항
sidebar_label: 설치 및 환경 요구사항
---

# 설치 및 환경 요구사항

:::info
개발 환경인 **Unity 2022.3 LTS**에서 동작 무결이 완벽하게 보장됩니다.  
해당 라이브러리는 **Unity 2021.3 LTS 미만 버전** 에서 동작 TC가 이뤄지지 않았습니다.  
:::

---

## 지원 환경 (필수)

| 항목 | 요구·전제 |
|------|-----------|
| **Unity 에디터** | **2021.3 LTS 이상** (이외 레거시 버전 미검증 상태입니다) |
| **Backend** | **Mono**, **IL2CPP** 모두 지원.  Roslyn 생성 경로는 **`Expression.Compile` 없이** 캐스트·대입만 사용하므로 **AOT(IL2CPP)에서도 생성 플랜 경로가 안전**합니다. |
| **C# / API 호환** | 런타임 어셈블리(`UNInject.Runtime`)는 **엔진 기본 설정**을 사용합니다. Unity 2021.3 계열에서 일반적인 **.NET Standard 2.1** 프로젝트와 호환됩니다. |
| **에디터 전용 코드** | `UNInject.Editor` 어셈블리는 **`includePlatforms: Editor`** 로 **플레이어 빌드에 포함되지 않습니다.** |

---

## 컴파일·도구 전제

| 항목 | 내용 |
|------|------|
| **Roslyn Source Generator** | 패키지에 포함된 **`UNInjectGenerator`** 가 컴파일 시점에 주입 플랜·팩토리를 생성합니다. **`[GlobalInject]` / `[SceneInject]`** (및 생성자 주입 대상) 타입은 **`public partial class`** 로 두어야 생성 코드와 병합됩니다. **VContainer**와 달리 **UNInject**는 이것을 옵션이 아닌, **필수 사용을 전제로 동작**합니다.|
| **UI Toolkit** | 의존성 그래프 등 일부 에디터 UI는 **GraphView / UI Toolkit** 을 사용합니다. 해당 Unity 버전에서 제공하는 범위 내에서 동작합니다. **특정 Unity 6 버전**. 혹은 **Graph IMGUI가 마이그레이션 된 버전**은, 제공되는 **Dependency Graph** 사용이 불가합니다. |

---

## Scripting Define Symbol 

빌드/진단용으로만 켭니다. **필수가 아닙니다.**

| 심볼 | 용도 |
|------|------|
| **`UNINJECT_STRICT_BUILD`** | 베이크 검증 실패 시 **빌드 중단** (`BuildFailedException`). |
| **`UNINJECT_PROFILING`** | 주입 시간 **`UNInjectProfiler`** 수집. **출시 빌드에서는 비활성화를 권장**합니다. |

---

## UPM으로 추가 (Git)

**Package Manager → Add package from git URL** 에 아래를 입력합니다.

```text
https://github.com/NightWish-0827/UNInject.git?path=/com.nightwishlab.uninject
```

### 로컬/서브 모듈로 넣는 경우

`Packages/manifest.json` 의 `dependencies` 에 **로컬 경로**를 지정하는 방식도 동일하게 작동합니다.  
이 경우에도 **`package.json` 이 있는 폴더**가 패키지 루트여야 합니다.

---

## 설치 직후 확인

1. **컴파일 오류 없음** — 첫 임포트 후 콘솔에 SDK 어셈블리 컴파일 에러가 없는지 확인합니다.   
   
2. **`partial` / UNI001** — 주입 필드를 둔 타입은 `partial` 이 없으면 IDE / 컴파일 단계에서 경고 진단이 발생합니다.  

3. **씬에 Installer 배치** — `MasterInstaller`, `SceneInstaller`, 필요 시 `ObjectInstaller` 를 씬에 두고,  
문서대로 **Registry Refresh / Bake** 를 수행합니다. (후술)

---

## 라이선스

:::info
MIT License

Copyright (c) 2026 NightWish

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
:::