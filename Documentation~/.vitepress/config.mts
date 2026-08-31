import { defineConfig } from 'vitepress'

const pkgRepo = 'https://github.com/opoi375/com.opoi375.cartoon-rendering'
const docsRepo = pkgRepo // 文档源文件在包仓库的 Documentation~/ 下

// 侧边栏结构（中英文共用同一套路径，文案在各自 locale 里定义）
function sidebarZh() {
  return [
    {
      text: '指南',
      items: [
        { text: '概述', link: '/guide/overview' },
        { text: '安装', link: '/guide/installation' },
        { text: '快速上手', link: '/guide/quickstart' }
      ]
    },
    {
      text: '渲染模块',
      items: [
        { text: '卡通角色渲染（PBRToon）', link: '/shading/toon' },
        { text: '程序化卡通天空', link: '/sky/procedural-sky' },
        { text: '体积云', link: '/sky/volumetric-clouds' },
        { text: '卡通水面与水下效果', link: '/water/' },
        { text: '交互草地', link: '/grass/' },
        { text: '像素化后期', link: '/effects/' },
        { text: 'SDF UI 图形', link: '/ui-sdf/' }
      ]
    },
    {
      text: '工具',
      items: [{ text: '编辑器工具一览', link: '/tools/' }]
    },
    {
      text: '参考',
      items: [
        { text: '天空系统参数参考', link: '/reference/sky-parameters' },
        { text: '更新日志', link: '/changelog' }
      ]
    }
  ]
}

function sidebarEn() {
  return [
    {
      text: 'Guide',
      items: [
        { text: 'Overview', link: '/en/guide/overview' },
        { text: 'Installation', link: '/en/guide/installation' },
        { text: 'Quick Start', link: '/en/guide/quickstart' }
      ]
    },
    {
      text: 'Rendering Modules',
      items: [
        { text: 'Toon Characters (PBRToon)', link: '/en/shading/toon' },
        { text: 'Procedural Cartoon Sky', link: '/en/sky/procedural-sky' },
        { text: 'Volumetric Clouds', link: '/en/sky/volumetric-clouds' },
        { text: 'Cartoon Water & Underwater', link: '/en/water/' },
        { text: 'Interactive Grass', link: '/en/grass/' },
        { text: 'Pixelate Post Process', link: '/en/effects/' },
        { text: 'SDF UI Graphics', link: '/en/ui-sdf/' }
      ]
    },
    {
      text: 'Tools',
      items: [{ text: 'Editor Tools', link: '/en/tools/' }]
    },
    {
      text: 'Reference',
      items: [
        { text: 'Sky Parameter Reference', link: '/en/reference/sky-parameters' },
        { text: 'Changelog', link: '/en/changelog' }
      ]
    }
  ]
}

export default defineConfig({
  base: '/com.opoi375.cartoon-rendering/',
  title: 'Cartoon Rendering',
  description: 'Stylized cartoon rendering toolkit for Unity URP',
  lastUpdated: true,
  cleanUrls: true,

  locales: {
    root: {
      label: '简体中文',
      lang: 'zh-CN',
      themeConfig: {
        nav: [
          { text: '指南', link: '/guide/overview' },
          { text: '渲染模块', link: '/shading/toon' },
          { text: '工具', link: '/tools/' },
          { text: '参考', link: '/reference/sky-parameters' }
        ],
        sidebar: sidebarZh(),
        outline: { level: [2, 3], label: '本页目录' },
        docFooter: { prev: '上一页', next: '下一页' },
        lastUpdatedText: '最后更新',
        returnToTopLabel: '回到顶部',
        sidebarMenuLabel: '菜单',
        darkModeSwitchLabel: '深色模式',
        editLink: {
          pattern: `${docsRepo}/edit/main/Documentation~/:path`,
          text: '在 GitHub 上编辑此页'
        }
      }
    },
    en: {
      label: 'English',
      lang: 'en-US',
      link: '/en/',
      themeConfig: {
        nav: [
          { text: 'Guide', link: '/en/guide/overview' },
          { text: 'Modules', link: '/en/shading/toon' },
          { text: 'Tools', link: '/en/tools/' },
          { text: 'Reference', link: '/en/reference/sky-parameters' }
        ],
        sidebar: { '/en/': sidebarEn() },
        outline: { level: [2, 3], label: 'On this page' },
        editLink: {
          pattern: `${docsRepo}/edit/main/Documentation~/:path`,
          text: 'Edit this page on GitHub'
        }
      }
    }
  },

  themeConfig: {
    socialLinks: [{ icon: 'github', link: pkgRepo }],
    search: { provider: 'local' }
  }
})
