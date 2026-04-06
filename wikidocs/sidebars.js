/** @type {import('@docusaurus/plugin-content-docs').SidebarsConfig} */
const sidebars = {
  docs: [
    {
      type: 'category',
      label: 'ABOUT',
      collapsed: false,
      items: ['intro', 'whatsdi', 'uninjectsummary', 'about/glossary'],
    },
    {
      type: 'category',
      label: 'Getting Started',
      collapsed: false,
      items: [
        'getting-started/installation',
        'getting-started/quickstart',
        'getting-started/first-scene',
        'getting-started/checklist',
      ],
    },
    {
      type: 'category',
      label: 'Editor & code generation',
      collapsed: true,
      items: [
        'editor/roslyn-and-partial',
        'editor/uninject-generator',
        'editor/bake-local-inject',
        'editor/refresh-registries',
        'editor/inspector-installers',
      ],
    },
    {
      type: 'category',
      label: 'Scoping',
      collapsed: true,
      items: [
        'scoping/overview',
        'scoping/master-installer',
        'scoping/scene-installer',
        'scoping/object-installer',
        'scoping/scene-exit-policy',
        'scoping/resolve-chains',
        'scoping/execution-order',
      ],
    },
    {
      type: 'category',
      label: 'Registering',
      collapsed: true,
      items: [
        'registering/referral-attributes',
        'registering/registry-keys',
        'registering/mapping-rules',
        'registering/duplicates',
        'registering/runtime-register',
        'registering/scope-owner-tracker',
        'registering/unregister',
      ],
    },
    {
      type: 'category',
      label: 'Resolving',
      collapsed: true,
      items: [
        'resolving/inject-attributes',
        'resolving/try-inject-target',
        'resolving/inject-gameobject-and-spawn',
        'resolving/create-t',
        'resolving/safety-net',
        'resolving/has-any-inject-field',
      ],
    },
    {
      type: 'category',
      label: 'Lifecycle callbacks',
      collapsed: true,
      items: [
        'lifecycle/iinjected',
        'lifecycle/pool',
        'lifecycle/scope-destroyable',
        'lifecycle/tickables',
        'lifecycle/iunregistered-note',
      ],
    },
    {
      type: 'category',
      label: 'Integrations',
      collapsed: true,
      items: ['integrations/pooling-patterns', 'integrations/dynamic-content'],
    },
    {
      type: 'category',
      label: 'Diagnostics',
      collapsed: true,
      items: [
        'diagnostics/master-play-mode-guard',
        'diagnostics/fallback-guard',
        'diagnostics/bake-validator',
        'diagnostics/dependency-graph',
        'diagnostics/troubleshooting',
      ],
    },
    {
      type: 'category',
      label: 'Optimization',
      collapsed: true,
      items: [
        'optimization/hot-path',
        'optimization/fallback-paths',
        'optimization/il2cpp',
        'optimization/profiling',
        'optimization/typedatacache',
      ],
    },
    {
      type: 'category',
      label: 'Appendix',
      collapsed: true,
      items: ['appendix/internal-types'],
    },
  ],
};

module.exports = sidebars;
