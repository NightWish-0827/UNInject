#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Play 버튼을 누르기 전에 MasterInstaller 의 전역 레지스트리가 비어 있으면
/// 경고를 출력하여 Refresh Global Registry 호출을 유도하는 에디터 전용 가드입니다.
/// </summary>
[InitializeOnLoad]
public static class MasterInstallerPlayModeGuard
{
    static MasterInstallerPlayModeGuard()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.ExitingEditMode) return;

        // 현재 씬에서 MasterInstaller 가 있는 경우만 검사
        var installer = Object.FindObjectOfType<MasterInstaller>();
        if (installer == null) return;

        // SerializedObject 는 대상 Object 만 전달하면 됩니다 (named parameter 사용 불가)
        var so = new SerializedObject(installer);
        var prop = so.FindProperty("_globalReferrals");
        if (prop != null && prop.arraySize == 0)
        {
            Debug.LogWarning("[MasterInstaller] Global registry is empty. Did you forget to click 'Refresh Global Registry' before Play?");
        }
    }
}
#endif

