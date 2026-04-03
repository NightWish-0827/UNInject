#if UNITY_EDITOR
/// <summary>
/// UNInject 에디터 전용 공유 유틸리티.
///
/// 배경:
///   UNInjectBakeValidator / UNInjectFallbackGuard / UNInjectGraphWindow 가
///   동일한 ShouldSkipAssembly 로직을 각자 보유하고 있었음.
///   필터 정책이 변경될 때 세 곳을 동시에 수정해야 하는 유지보수 부채를 제거하기 위해
///   이 클래스로 단일화함.
/// </summary>
internal static class UNInjectEditorUtility
{
    /// <summary>
    /// 어셈블리 이름을 보고 UNInject 진단 스캔에서 제외해야 하는 어셈블리인지 판별한다.
    ///
    /// 제외 대상: System.*, UnityEngine.*, UnityEditor.*, Unity.*,
    ///            mscorlib, netstandard, Mono.*, nunit.*, JetBrains.*, ExCSS.*, Bee.*
    /// (사용자 게임 코드가 아닌 런타임/에디터 인프라 어셈블리)
    /// </summary>
    internal static bool ShouldSkipAssembly(string name)
    {
        return name.StartsWith("System")      || name.StartsWith("Microsoft")
            || name.StartsWith("UnityEngine") || name.StartsWith("UnityEditor")
            || name.StartsWith("Unity.")      || name.StartsWith("mscorlib")
            || name.StartsWith("netstandard") || name.StartsWith("Mono.")
            || name.StartsWith("nunit.")      || name.StartsWith("JetBrains.")
            || name.StartsWith("ExCSS.")      || name.StartsWith("Bee.");
    }
}
#endif
