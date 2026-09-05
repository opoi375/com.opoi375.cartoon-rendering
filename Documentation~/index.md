---
layout: home

hero:
  name: Cartoon Rendering
  text: Unity URP 卡通渲染工具包
  tagline: 角色 Toon 着色 · 程序化天空 · 体积云 · 卡通水面 · 交互草地 —— 一站式风格化渲染方案
  image:
    src: /logo.png
    alt: Cartoon Rendering
  actions:
    - theme: brand
      text: 快速上手
      link: /guide/quickstart
    - theme: alt
      text: GitHub
      link: https://github.com/opoi375/com.opoi375.cartoon-rendering

features:
  - icon: 🎨
    title: 卡通角色渲染
    details: PBRToon 家族（身体 / 脸 / 眼 / 发），阴影色阶、边缘光、SDF 面部阴影，完整支持烘焙光照与 Forward+
    link: /shading/toon
  - icon: 🌅
    title: 程序化卡通天空
    details: 五段式渐变 + 昼 / 黄昏 / 夜三套调色板，太阳、星星、2D 云层，跟随方向光自动昼夜循环
    link: /sky/procedural-sky
  - icon: ☁️
    title: 体积云
    details: 光线步进 + 128³ Perlin-Worley 噪声，卡通明暗分区、银边、时序累积抗条带，半分辨率渲染
    link: /sky/volumetric-clouds
  - icon: 🌊
    title: 卡通水面
    details: 距离 LOD 曲面细分 + 解析波形 + 深度泡沫，另有水下后期雾效
    link: /water/
  - icon: 🌿
    title: 交互草地
    details: GPU 实例化草场，角色踩踏压弯、脚印恢复，编辑期烘焙工具
    link: /grass/
  - icon: 🌍
    title: 世界弯曲
    details: 动森式小星球视角，远处顶点按距离平方下沉，全 Pass 接入阴影不脱节，曲率 / 死区 / 法线修正可调
    link: /world-bend/
  - icon: 🧩
    title: SDF 工具链
    details: UI SDF 图形材质 + GPU SDF 生成器（遮罩→SDF、多帧→渐变），溶解 / 燃烧 / 生长效果
    link: /ui-sdf/
---
