# HMI Asset Studio

面向智能座舱 HMI 团队的 Tuanjie / Unity Editor 工作区，把材质、模型、特效、场景模板和车辆配置等核心资产收敛到统一界面，提供浏览、批量操作、场景搭建和车辆装配等一站式能力。

> Package: `com.hmi.workspace` · Tuanjie / Unity 2022.3+

## 功能模块

| 模块 | 说明 |
|------|------|
| 欢迎页 | 品牌化入口，介绍能力范围并引导进入工作区 |
| 工作台首页 | 资产规模、依赖状态与下一步操作总览 |
| 资产浏览 | 按材质 / 模型 / 特效分类，支持搜索、网格列表切换、缩放 |
| 批量替换 | 选中目标后预览候选材质，一键批量替换并支持撤销 |
| 材质对比 | A/B 双栏对比材质属性差异 |
| 场景搭建 | 基于模板配置照明、相机、天气、地面、天空，一键生成场景 |
| 车辆配置 | 导入 FBX、解析零件结构、绑定材质并导出 Schema |
| 逻辑流编辑 | 节点式交互流程编排（点击 / 高亮 / 切换材质 / 播放动画等） |
| 上下文面板 | 跟随当前工作区切换的状态说明与下一步引导 |

## 环境要求

- **Tuanjie Engine** (Unity China) 2022.3 或更高
- **HMIRP Core** `com.unity.render-pipelines.universal` ≥ 1.0.3

完整功能建议安装以下四个 HMIRP 包，工具会自动识别 git URL / 本地路径 / Package Manager 安装的版本：

| 包名 | 最低版本 | 影响范围 |
|------|---------|---------|
| `com.unity.render-pipelines.universal` | 1.0.3 | 材质预览、场景搭建、批量替换 |
| `com.tuanjie.hmirp.shaderlibrary` | 1.0.3 | Shader 浏览与替换 |
| `com.tuanjie.hmirp.materiallibrary` | 1.0.4 | 材质库内容 |
| `com.tuanjie.render-pipelines.hmirp.staterendersystem` | 1.0.4 | 高级状态效果 |

依赖未完整安装时工具会进入降级模式，仍可使用资产浏览、车辆配置等不依赖渲染管线的功能。

## 安装

将 `com.hmi.workspace` 添加到项目的 `Packages/manifest.json`：

```json
"com.hmi.workspace": "file:../path/to/TuanjieHMIAssetWorkspace"
```

或通过 git URL：

```json
"com.hmi.workspace": "https://github.com/leitan2023-cmd/TuanjieHMIAssetWorkspace.git"
```

## 打开

Unity 菜单栏：**Window → HMI Asset Studio**

首次进入时显示欢迎页，点击「打开工作区」进入主界面，「阅读文档」会打开使用手册。

## 目录结构

```
Editor/
  Controllers/        # 各模块控制器（资产、场景、车辆、逻辑等）
  Core/               # Observable / EventChannel / PerfTrace 基础设施
  Data/               # 状态与数据模型
  Services/           # 资产、依赖、预览、撤销等服务实现
  Views/              # 工作区各面板视图
  LogicFlowEditor/    # 节点式逻辑编辑器
  USS/  UXML/         # 界面样式与布局
  Images/             # 欢迎页 hero / 卡片图
Runtime/
  Logic/              # 运行时逻辑节点（触发器 / 动作）
Docs/
  HMI-Asset-Studio-Guide.md       # 使用文档
  HMI-Asset-Studio-PRD-v0.1.docx  # 产品需求文档
```

## 文档

- 使用文档：[`Docs/HMI-Asset-Studio-Guide.md`](Docs/HMI-Asset-Studio-Guide.md)
- 产品需求文档（PRD v0.1）：[`Docs/HMI-Asset-Studio-PRD-v0.1.docx`](Docs/HMI-Asset-Studio-PRD-v0.1.docx)
