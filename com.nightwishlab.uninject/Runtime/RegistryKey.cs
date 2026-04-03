using System;

/// <summary>
/// Named/Keyed 바인딩을 지원하기 위한 레지스트리 복합 키.
///
/// v2.0 에서 레지스트리가 Dictionary&lt;Type, Component&gt; 에서
/// Dictionary&lt;RegistryKey, Component&gt; 로 변경됨에 따라 도입됨.
///
/// 설계 원칙:
///   - Id == string.Empty 이면 v1.x 방식의 무키 바인딩과 동일하게 동작함. (하위 호환)
///   - 동일 타입에 서로 다른 Id 를 가진 복수 등록이 가능함.
///   - 불변(readonly struct) 으로 값 의미론을 가지며, Dictionary 키로 안전하게 사용됨.
///
/// 사용 예:
///   [Referral("enemyA")] public class EnemyManagerA : IEnemyManager { ... }
///   [Referral("enemyB")] public class EnemyManagerB : IEnemyManager { ... }
///   → RegistryKey(typeof(IEnemyManager), "enemyA") / RegistryKey(typeof(IEnemyManager), "enemyB") 로 각각 등록
/// </summary>
internal readonly struct RegistryKey : IEquatable<RegistryKey>
{
    /// <summary>Id 미지정 시 사용되는 기본 키 값. v1.x 무키 바인딩과 동일.</summary>
    internal static readonly string DefaultId = string.Empty;

    internal readonly Type   Type;
    internal readonly string Id;

    internal RegistryKey(Type type, string id = null)
    {
        Type = type;
        Id   = id ?? DefaultId;
    }

    public bool Equals(RegistryKey other)
        => Type == other.Type && string.Equals(Id, other.Id, StringComparison.Ordinal);

    public override bool Equals(object obj)
        => obj is RegistryKey k && Equals(k);

    public override int GetHashCode()
    {
        // .NET Standard 2.0 호환 해시 결합 (HashCode.Combine 불필요)
        unchecked
        {
            return ((Type?.GetHashCode() ?? 0) * 397) ^ (Id?.GetHashCode() ?? 0);
        }
    }

    public override string ToString()
        => string.IsNullOrEmpty(Id) ? (Type?.Name ?? "null") : $"{Type?.Name ?? "null"}[{Id}]";
}
