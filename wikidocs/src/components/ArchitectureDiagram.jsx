import React, { useState, useEffect } from 'react';

const VW = 1100, VH = 680;
const BW = 188, BH = 60, BRX = 13;
const hw = BW / 2, hh = BH / 2;

// 패널 중앙 = 패널 x + w/2
// Pure C#: 28+286/2=171  Mono: 378+286/2=521  DI: 758+312/2=914
const CX = { p: 171, m: 521, d: 914 };

const ARROW_COLORS = [
  '#6366F1', // service
  '#22C55E', // create
  '#cc9a1e', // inject
  '#a93e52', // unity
];

const NODES = {
  p: [
    { id: 'service', label: 'Tickable Service', sub: 'ITickable · IFixedTickable',   y: 220 },
    { id: 'domain',  label: 'Domain Logic',      sub: '비즈니스 규칙 · Pure C#',      y: 375 },
    { id: 'repo',    label: 'Repository',         sub: '데이터 접근 · 외부 소스',      y: 530 },
  ],
  m: [
    // 수직 중앙(364) 기준으로 간격 재조정
    { id: 'view',   label: 'View Component',   sub: 'MonoBehaviour · UI · 렌더링',   y: 284 },
    { id: 'bridge', label: 'Mono Data Bridge', sub: 'Transform · Unity 데이터 전달', y: 444 },
  ],
  d: [
    { id: 'global', label: 'MasterInstaller',  sub: 'Global Scope · DontDestroyOnLoad', y: 180, ck: 'di'    },
    { id: 'scene',  label: 'SceneInstaller',   sub: 'Scene Scope · 씬 생명주기',         y: 320, ck: 'di'    },
    { id: 'ticks',  label: 'TickableRegistry', sub: 'Update · FixedUpdate · LateUpdate',  y: 455, ck: 'ticks' },
    { id: 'obj',    label: 'ObjectInstaller',  sub: 'Local Scope · 계층 내 주입',         y: 590, ck: 'obj'   },
  ],
};

const DESCS = {
  service: 'ITickable / IFixedTickable / ILateTickable 를 구현하는 순수 C# 서비스 클래스입니다. IScope.Create<T>() 로 생성되며, TickableRegistry 를 통해 스코프 Installer 의 Update / FixedUpdate / LateUpdate 에 자동 등록됩니다. MonoBehaviour 없이 단위 테스트가 가능합니다.',
  domain:  '게임의 핵심 비즈니스 규칙을 순수 C# 클래스로 작성합니다. [InjectConstructor] 생성자 주입으로 의존성을 받으며, 엔진 의존성이 없어 서버 이식과 테스트가 용이합니다.',
  repo:    '파일·네트워크·DB 등 외부 데이터 소스에 대한 접근을 추상화합니다. 인터페이스로 분리되어 Named 바인딩(RegistryKey) 으로 구체 구현을 런타임에 교체할 수 있습니다.',
  global:  'MasterInstaller — DontDestroyOnLoad 전역 스코프입니다. [Referral] 어트리뷰트 컴포넌트를 에디터 Bake 로 등록하고, RegistryKey(Type, Id) 기반 Named 바인딩을 지원합니다. Safety Net 패턴으로 레지스트리 미초기화 케이스를 1회 자동 복구합니다.',
  scene:   'SceneInstaller — 씬 단위 생명주기 스코프입니다. [SceneReferral] 컴포넌트를 관리하며 씬 전환 시 자동 해제됩니다. SceneExitPolicy.Preserve 로 레지스트리를 전환 과도기 동안 보존할 수 있습니다.',
  ticks:   'TickableRegistry — 세 Installer 가 각자 인스턴스를 소유합니다. 분리 리스트 + 스냅샷 배열 패턴으로 매 프레임 GC 없이 실행되며, PlayerLoop 조작 없이 Installer MonoBehaviour 의 Update 를 통해 위임합니다.',
  obj:     'ObjectInstaller — GameObject 계층 로컬 스코프입니다. _parentScope 로 명시적 상위 스코프 계층을 구성합니다. Resolve 체인: 로컬 → SceneInstaller → MasterInstaller 순으로 폴백합니다.',
  view:    'MonoBehaviour 기반 UI·렌더링 컴포넌트입니다. ObjectInstaller.Awake() 시점에 [GlobalInject] / [SceneInject] 필드가 Roslyn 플랜(1순위) → Expression Tree → FieldInfo 순으로 주입됩니다.',
  bridge:  'Unity 엔진 데이터(Transform, Collider, UI 파츠 등)를 Pure C# 계층으로 전달하는 브릿지입니다. MonoBehaviour 가 데이터를 노출하고 Domain Logic 이 소비해 엔진 의존성을 분리합니다.',
};

const LEGEND = [
  { ck: 'p',     label: 'Pure C# Service' },
  { ck: 'm',     label: 'MonoBehaviour' },
  { ck: 'di',    label: 'Installer / Scope' },
  { ck: 'ticks', label: 'TickableRegistry' },
  { ck: 'obj',   label: 'ObjectInstaller' },
];

// 노드 자체 색상만 라이트/다크 분기, 배경은 CSS 변수로 테마에 위임
const NODE_COLORS = {
  light: {
    p:     { bg: '#EEEDF8', bdr: '#534AB7', t: '#1e1a4a', s: '#534AB7' },
    m:     { bg: '#F5EDEB', bdr: '#993C1D', t: '#5a1e0a', s: '#993C1D' },
    di:    { bg: '#E8F3EF', bdr: '#0F6E56', t: '#063b2e', s: '#0F6E56' },
    ticks: { bg: '#FFF7ED', bdr: '#F97316', t: '#7C2D12', s: '#C2410C' },
    obj:   { bg: '#FFFBEB', bdr: '#D97706', t: '#78350F', s: '#92400E' },
  },
  dark: {
    p:     { bg: '#1a1840', bdr: '#7B72D4', t: '#cdc9f0', s: '#a09ae0' },
    m:     { bg: '#3a1208', bdr: '#c45a30', t: '#f0c4b0', s: '#d4805a' },
    di:    { bg: '#052e20', bdr: '#18a87a', t: '#a0e8cc', s: '#5ecfaa' },
    ticks: { bg: '#431407', bdr: '#fb923c', t: '#fed7aa', s: '#fdba74' },
    obj:   { bg: '#1c1004', bdr: '#fbbf24', t: '#fef3c7', s: '#fde68a' },
  },
};

// 배경/UI 색상은 PerformGraph와 동일하게 CSS 변수 사용
const CSS = {
  panel:    'var(--ifm-color-emphasis-100)',
  panelBdr: 'var(--ifm-color-emphasis-300)',
  ct:       'var(--ifm-color-emphasis-600)',
  arrow:    'var(--ifm-color-emphasis-400)',
  hdrBg:    'var(--ifm-background-surface-color)',
  hdrBdr:   'var(--ifm-color-emphasis-200)',
  hdrText:  'var(--ifm-font-color-base)',
  descBg:   'var(--ifm-background-surface-color)',
  descBdr:  'var(--ifm-color-emphasis-200)',
  descText: 'var(--ifm-font-color-base)',
  descTitle:'var(--ifm-font-color-base)',
  btnBg:    'var(--ifm-color-emphasis-100)',
  btnBdr:   'var(--ifm-color-emphasis-300)',
  btnText:  'var(--ifm-color-emphasis-700)',
  btnOn:    'var(--ifm-color-emphasis-200)',
};

function NodeBox({ id, cx, y, label, sub, c, active, toggle }) {
  const on = active === id;
  return (
    <g style={{ cursor: 'pointer' }} onClick={() => toggle(id)}>
      <rect x={cx - hw} y={y - hh} width={BW} height={BH} rx={BRX}
        fill={c.bg} stroke={c.bdr} strokeWidth={on ? 2.8 : 1.5}
        style={{ filter: on ? `drop-shadow(0 0 8px ${c.bdr}99)` : 'none' }}
      />
      <text x={cx} y={y - 9}  textAnchor="middle" fontSize={13} fontWeight="700"
        fill={c.t} fontFamily="system-ui,sans-serif" pointerEvents="none">{label}</text>
      <text x={cx} y={y + 12} textAnchor="middle" fontSize={11}
        fill={c.s} fontFamily="system-ui,sans-serif" pointerEvents="none">{sub}</text>
    </g>
  );
}

// 둥근 모서리를 가진 직교 라우팅(Orthogonal Routing) 헬퍼 함수
function getRoundedPath(points, r = 16) {
  if (points.length < 2) return '';
  let path = `M ${points[0][0]},${points[0][1]}`;

  for (let i = 1; i < points.length - 1; i++) {
    const prev = points[i - 1], curr = points[i], next = points[i + 1];
    const dx1 = curr[0] - prev[0], dy1 = curr[1] - prev[1];
    const dx2 = next[0] - curr[0], dy2 = next[1] - curr[1];
    const len1 = Math.sqrt(dx1*dx1 + dy1*dy1);
    const len2 = Math.sqrt(dx2*dx2 + dy2*dy2);
    
    const actualR = Math.min(r, len1/2, len2/2);
    const p1x = curr[0] - (dx1 === 0 ? 0 : (dx1 / len1) * actualR);
    const p1y = curr[1] - (dy1 === 0 ? 0 : (dy1 / len1) * actualR);
    const p2x = curr[0] + (dx2 === 0 ? 0 : (dx2 / len2) * actualR);
    const p2y = curr[1] + (dy2 === 0 ? 0 : (dy2 / len2) * actualR);

    path += ` L ${p1x},${p1y} Q ${curr[0]},${curr[1]} ${p2x},${p2y}`;
  }
  const last = points[points.length - 1];
  path += ` L ${last[0]},${last[1]}`;
  return path;
}

export default function ArchitectureDiagram() {
  const [active,     setActive]     = useState(null);
  const [showArrows, setShowArrows] = useState(false);
  const [dark,       setDark]       = useState(false);

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const mq = window.matchMedia('(prefers-color-scheme: dark)');
    setDark(mq.matches);
    const h = (e) => setDark(e.matches);
    mq.addEventListener('change', h);
    return () => mq.removeEventListener('change', h);
  }, []);

  const nc     = dark ? NODE_COLORS.dark : NODE_COLORS.light;
  const pn     = (ck) => nc[ck];
  const toggle = (id) => setActive(p => p === id ? null : id);
  const ac     = CSS.arrow;

  const dp = NODES.d, pp = NODES.p, mp = NODES.m;
  const allNodes   = [...NODES.p, ...NODES.m, ...NODES.d];
  const activeNode = allNodes.find(n => n.id === active);

  // 라우팅용 포트 좌표 및 중간 지점(Gap) 정의
  const port = {
    right:  (cx, y) => [cx + hw, y],
    left:   (cx, y) => [cx - hw, y],
    top:    (cx, y) => [cx, y - hh],
    bottom: (cx, y) => [cx, y + hh],
  };
  
  const gap1X = 346; // Pure C# 패널과 Mono 패널 사이 중앙 공간
  const gap2X = 711; // Mono 패널과 DI 패널 사이 중앙 공간

  const solidArrows = [
    // 1. 상단 아치 — PureC# Service top → MasterInstaller top
    getRoundedPath([
      port.top(CX.p, pp[0].y),
      [CX.p, 40],
      [CX.d, 40],
      port.top(CX.d, dp[0].y)
    ]),

    // 2. Create<T>() : SceneInstaller 좌포트 → 상단 통로 → Tickable Service 우포트
    getRoundedPath([
      port.left(CX.d, dp[1].y),
      [gap2X, dp[1].y],
      [gap2X, 130],          // Mono 패널(y=174) 위로 이동
      [gap1X, 130],          // 좌측으로 횡단
      [gap1X, pp[0].y],      // 목표 높이로 하강
      port.right(CX.p, pp[0].y)
    ]),

    // 3. Inject Deps : ObjectInstaller 좌포트 → View Component 우포트
    getRoundedPath([
      port.left(CX.d, dp[3].y),
      [gap2X, dp[3].y],
      [gap2X, mp[0].y],      // View Component 높이로 수직 상승
      port.right(CX.m, mp[0].y)
    ]),

    // 4. Unity Data : Mono Bridge 좌포트 → Domain Logic 우포트
    getRoundedPath([
      port.left(CX.m, mp[1].y),
      [gap1X, mp[1].y],
      [gap1X, pp[1].y],      // Domain Logic 높이로 수직 이동
      port.right(CX.p, pp[1].y)
    ]),
  ];

  const dashedArrows = [
    // DI 내부 수직 체인 (직선 연결)
    `M${CX.d},${dp[0].y+hh} L${CX.d},${dp[1].y-hh}`,
    `M${CX.d},${dp[1].y+hh} L${CX.d},${dp[2].y-hh}`,
    `M${CX.d},${dp[2].y+hh} L${CX.d},${dp[3].y-hh}`,
  ];

  const btnSt = {
    padding: '7px 16px', borderRadius: 8, cursor: 'pointer',
    fontSize: 12, fontWeight: 600, fontFamily: 'system-ui,sans-serif',
    border: `1px solid ${CSS.btnBdr}`,
    background: showArrows ? CSS.btnOn : CSS.btnBg,
    color: CSS.btnText,
    whiteSpace: 'nowrap', flexShrink: 0,
  };

  return (
    <div style={{ fontFamily: 'system-ui,sans-serif', width: '100%' }}>
      {/* 헤더: 범례(중앙) + 토글 버튼(우) */}
      <div style={{
        display: 'flex', alignItems: 'center',
        background: CSS.hdrBg, border: `1px solid ${CSS.hdrBdr}`,
        borderRadius: 10, padding: '8px 14px', marginBottom: 8,
      }}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 22, flex: 1, flexWrap: 'wrap' }}>
          {LEGEND.map(item => {
            const c = pn(item.ck);
            return (
              <span key={item.ck} style={{ display: 'flex', alignItems: 'center', gap: 7, fontSize: 12, fontWeight: 600, color: CSS.hdrText }}>
                <span style={{ width: 14, height: 14, borderRadius: 3, flexShrink: 0, background: c.bg, border: `1.5px solid ${c.bdr}`, display: 'inline-block' }} />
                {item.label}
              </span>
            );
          })}
        </div>
        <button style={btnSt} onClick={() => setShowArrows(s => !s)}>
          {showArrows ? 'Match' : 'Match'}
        </button>
      </div>

      {/* SVG */}
      <svg width="100%" viewBox={`0 0 ${VW} ${VH}`} style={{ display: 'block' }}>
        <defs>
  {ARROW_COLORS.map((c, i) => (
    <marker
      key={i}
      id={`ah-${i}`}
      viewBox="0 0 10 10"
      refX="9"
      refY="5"
      markerWidth="6"
      markerHeight="6"
      orient="auto-start-reverse"
    >
      <path
        d="M1,1 L9,5 L1,9"
        fill="none"
        stroke={c}
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </marker>
  ))}
</defs>

        {/* 컬럼 패널 */}
        {[
          { x: 28,  y: 80,  w: 286, h: 568, label: 'Pure C# Layer',         cx: CX.p },
          { x: 378, y: 174, w: 286, h: 380, label: 'MonoBehaviour',         cx: CX.m }, // 수직 중앙 정렬됨
          { x: 758, y: 80,  w: 312, h: 568, label: 'DI & Scope Management', cx: CX.d },
        ].map(p => (
          <g key={p.label}>
            <rect x={p.x} y={p.y} width={p.w} height={p.h} rx={18} fill={CSS.panel} stroke={CSS.panelBdr} strokeWidth={1} strokeDasharray="5 4" />
            <text x={p.cx} y={p.y + 28} textAnchor="middle" fontSize={13} fontWeight="700" fill={CSS.ct} fontFamily="system-ui,sans-serif">{p.label}</text>
          </g>
        ))}

        {/* 화살표 렌더링 */}
        {showArrows && (
          <g>
           {solidArrows.map((d, i) => (
  <path
    key={`solid-${i}`}
    d={d}
    fill="none"
    stroke={ARROW_COLORS[i]}
    strokeWidth={1.6}
    markerEnd={`url(#ah-${i})`}
  />
))}
            {dashedArrows.map((d, i) => (
              <path key={`dash-${i}`} d={d} fill="none" stroke={ac} strokeWidth={1.6} strokeDasharray="6 4" markerEnd="url(#ah)" />
            ))}
          </g>
        )}

        {/* 노드 */}
        {NODES.p.map(n => <NodeBox key={n.id} id={n.id} cx={CX.p} y={n.y} label={n.label} sub={n.sub} c={pn('p')} active={active} toggle={toggle} />)}
        {NODES.m.map(n => <NodeBox key={n.id} id={n.id} cx={CX.m} y={n.y} label={n.label} sub={n.sub} c={pn('m')} active={active} toggle={toggle} />)}
        {NODES.d.map(n => <NodeBox key={n.id} id={n.id} cx={CX.d} y={n.y} label={n.label} sub={n.sub} c={pn(n.ck)} active={active} toggle={toggle} />)}
      </svg>

      {/* 설명 패널 */}
      {active && activeNode && (
        <div style={{ marginTop: 10, padding: '12px 18px', borderRadius: 12, border: `1px solid ${CSS.descBdr}`, background: CSS.descBg, fontSize: 14, lineHeight: 1.75, color: CSS.descText }}>
          <strong style={{ display: 'block', marginBottom: 5, fontSize: 15, color: CSS.descTitle }}>
            {activeNode.label}
          </strong>
          {DESCS[active]}
        </div>
      )}
    </div>
  );
}