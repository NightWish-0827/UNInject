using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 런타임 Register 의 소유권 자동 해제를 위해 오너 GameObject 에 부착되는 내부 헬퍼.
///
/// 사용 흐름:
///   MasterInstaller.Register(comp, owner) 또는 SceneInstaller.Register(comp, owner) 호출 시
///   owner.gameObject 에 이 컴포넌트가 없으면 자동으로 추가됨.
///   owner 가 파괴될 때 OnDestroy 가 등록된 모든 콜백을 실행하여 해당 스코프에서 언레지스터됨.
///
/// 설계 원칙:
///   - internal : SDK 소비자가 직접 생성/참조하지 않음.
///   - 하나의 GameObject 에 여러 스코프에서 동시에 소유될 수 있으므로 콜백 목록을 유지함.
/// </summary>
[AddComponentMenu("")]   // 컴포넌트 메뉴에 노출하지 않음
internal sealed class ScopeOwnerTracker : MonoBehaviour
{
    private readonly List<Action> _destroyCallbacks = new List<Action>();

    /// <summary>
    /// owner 가 파괴될 때 실행할 콜백을 등록한다.
    /// 동일한 스코프에서 여러 번 Register 되는 경우 콜백이 누적됨.
    /// </summary>
    internal void AddDestroyCallback(Action callback)
    {
        if (callback != null)
            _destroyCallbacks.Add(callback);
    }

    private void OnDestroy()
    {
        foreach (var cb in _destroyCallbacks)
        {
            try { cb(); }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
        _destroyCallbacks.Clear();
    }
}
