using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ITickable / IFixedTickable / ILateTickable 등록·해제·틱 위임을 캡슐화.
/// MasterInstaller / SceneInstaller / ObjectInstaller 가 각각 인스턴스를 소유함.
///
/// IScopeDestroyable 도 함께 추적하여 ClearWithDestroy() 시 OnScopeDestroy() 를 호출함.
///
/// 분리 리스트 설계: 틱 타입별 리스트를 분리하여 해당 틱이 없는 프레임에서
///                   불필요한 순회 비용을 제거함.
///
/// 스냅샷 패턴: Tick 실행 중 Unregister 가 호출되어도 현재 프레임의 순회에는
///              영향을 주지 않으며, 변경은 다음 프레임부터 반영됨.
/// </summary>
internal sealed class TickableRegistry
{
    private readonly List<ITickable>         _tickables      = new List<ITickable>();
    private readonly List<IFixedTickable>    _fixedTickables = new List<IFixedTickable>();
    private readonly List<ILateTickable>     _lateTickables  = new List<ILateTickable>();

    // IScopeDestroyable 은 스냅샷 불필요 — ClearWithDestroy() 한 번만 순회함.
    private readonly List<IScopeDestroyable> _destroyables   = new List<IScopeDestroyable>();

    private ITickable[]      _tickSnap;
    private IFixedTickable[] _fixedSnap;
    private ILateTickable[]  _lateSnap;
    private bool _tickDirty, _fixedDirty, _lateDirty;

    // ─── 등록 / 해제 ──────────────────────────────────────────────────────────

    /// <summary>
    /// obj 가 구현하는 틱/수명 인터페이스를 모두 자동 감지하여 등록함.
    /// Create&lt;T&gt;() 내부에서 생성 직후 호출된다.
    /// </summary>
    internal void Register(object obj)
    {
        if (obj is ITickable t)         { _tickables.Add(t);       _tickDirty  = true; }
        if (obj is IFixedTickable ft)   { _fixedTickables.Add(ft); _fixedDirty = true; }
        if (obj is ILateTickable lt)    { _lateTickables.Add(lt);  _lateDirty  = true; }
        if (obj is IScopeDestroyable d) { _destroyables.Add(d); }
    }

    internal void Unregister(ITickable tickable)
    {
        if (_tickables.Remove(tickable)) _tickDirty = true;
    }

    internal void Unregister(IFixedTickable tickable)
    {
        if (_fixedTickables.Remove(tickable)) _fixedDirty = true;
    }

    internal void Unregister(ILateTickable tickable)
    {
        if (_lateTickables.Remove(tickable)) _lateDirty = true;
    }

    // ─── 틱 (MonoBehaviour 생명주기에서 호출) ────────────────────────────────

    internal void Tick()
    {
        if (_tickDirty) { _tickSnap = _tickables.Count > 0 ? _tickables.ToArray() : null; _tickDirty = false; }
        if (_tickSnap == null) return;
        var snap = _tickSnap;
        for (int i = 0; i < snap.Length; i++) snap[i].Tick();
    }

    internal void FixedTick()
    {
        if (_fixedDirty) { _fixedSnap = _fixedTickables.Count > 0 ? _fixedTickables.ToArray() : null; _fixedDirty = false; }
        if (_fixedSnap == null) return;
        var snap = _fixedSnap;
        for (int i = 0; i < snap.Length; i++) snap[i].FixedTick();
    }

    internal void LateTick()
    {
        if (_lateDirty) { _lateSnap = _lateTickables.Count > 0 ? _lateTickables.ToArray() : null; _lateDirty = false; }
        if (_lateSnap == null) return;
        var snap = _lateSnap;
        for (int i = 0; i < snap.Length; i++) snap[i].LateTick();
    }

    // ─── 소멸 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 모든 IScopeDestroyable.OnScopeDestroy() 를 먼저 호출한 뒤 전체 리스트를 비운다.
    /// 스코프의 OnDestroy() 에서 호출된다.
    ///
    /// 개별 OnScopeDestroy() 예외는 다른 서비스의 정리를 막지 않도록 로그 후 계속됨.
    /// </summary>
    internal void ClearWithDestroy()
    {
        foreach (var d in _destroyables)
        {
            try { d.OnScopeDestroy(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        _tickables.Clear();
        _fixedTickables.Clear();
        _lateTickables.Clear();
        _destroyables.Clear();

        _tickSnap  = null;
        _fixedSnap = null;
        _lateSnap  = null;
        _tickDirty = _fixedDirty = _lateDirty = false;
    }
}
