using System.Runtime.CompilerServices;

// InstallerRegistryHelper (internal) 를 아래 어셈블리에서 접근할 수 있게 허용.
// 프로덕션 빌드에는 영향 없음 (런타임 어셈블리가 이들 어셈블리에 의존하지 않음).
[assembly: InternalsVisibleTo("UNInject.Tests.EditMode")]
[assembly: InternalsVisibleTo("UNInject.Editor")]