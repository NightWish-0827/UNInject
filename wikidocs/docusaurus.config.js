// @ts-check

/** @type {import('@docusaurus/types').Config} */
const config = {
  title: 'UNInject',           // ← SDK 이름으로 변경
  tagline: 'NightWish',
  url: 'https://NightWish-0827.github.io',
  baseUrl: '/UNInject/',
  onBrokenLinks: 'throw',
  onBrokenMarkdownLinks: 'warn',
  favicon: 'img/uninjectlogo.ico',

  // 번역 설정
  i18n: {
    defaultLocale: 'ko',
    locales: ['ko', 'en'],
    localeConfigs: {
      ko: { label: '한국어' },
      en: { label: 'English' },
    },
  },

  presets: [
    [
      'classic',
      /** @type {import('@docusaurus/preset-classic').Options} */
      ({
        docs: {
          sidebarPath: './sidebars.js',
          routeBasePath: '/',          // 루트가 바로 docs로 이동
        },
        blog: false,                   // blog 비활성화
        theme: {
          customCss: './src/css/custom.css',
        },
      }),
    ],
  ],

  themeConfig:
    /** @type {import('@docusaurus/preset-classic').ThemeConfig} */
    ({
      navbar: {
        title: 'UNInject',             // ← SDK 이름으로 변경
        // logo 있으면 추가
        logo: { alt: 'UNInject Logo', src: 'img/uninjectlogo.png' },
        items: [
          /*{
            type: 'localeDropdown',    // 번역 드롭다운
            position: 'right',
          },*/
          {
            type: 'doc',
            docId: 'getting-started/installation',  // Getting Started 이동
            label: 'Getting Started',
            position: 'right',
          },
        ],
      },
      colorMode: {
        defaultMode: 'light',
        disableSwitch: false,          // 라이트/다크 토글 활성화
      },
      prism: {
        additionalLanguages: ['bash', 'java', 'csharp'],
      },
    }),

  // 로컬 검색 플러그인
  plugins: [
    [
      '@easyops-cn/docusaurus-search-local',
      {
        hashed: true,
        language: ['ko', 'en'],
        docsRouteBasePath: '/',
      },
    ],
  ],
};

module.exports = config;

