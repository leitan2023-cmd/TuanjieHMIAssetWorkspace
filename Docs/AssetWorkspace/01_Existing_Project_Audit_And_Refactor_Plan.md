# Asset Workspace 现有工程审查与增量改造计划

> 审查日期：2026-04-02
> 审查范围：TuanjieHMIAssetWorkspace + HMIRP 四包
> 核心原则：**不推翻重做，基于现状增量改造**

---

## 一、当前工程保留项（不应推翻的部分）

### 1.1 架构层 — 保留

| 组件 | 文件 | 保留理由 |
|------|------|----------|
| Observable\<T\> 响应式状态 | `Core/Observable.cs` | 干净、轻量、无依赖，所有页面已统一使用 |
| EventChannel\<T\> 事件总线 | `Core/EventChannel.cs` | 发布-订阅解耦，跨页面通信已落地 |
| IController 生命周期接口 | `Core/IController.cs` | Initialize/Dispose 规范，所有 Controller 已遵循 |
| 四层架构 | Views/Controllers/Services/Data | 职责划分清晰，Service 接口+实现分离 |

**结论：Core 层零改动，直接复用。**

### 1.2 Controller 层 — 保留

| Controller | 状态 | 保留理由 |
|------------|------|----------|
| SelectionController | ✅ 完成 | 三优先级选择同步（UI > External > Auto），已稳定 |
| ActionController | ✅ 基础完成 | Apply 流程已打通 Material → Renderer |
| AssetBrowserController | ✅ 完成 | 扫描/过滤/搜索，已被 AssetGridView 使用 |
| PreviewController | ✅ 完成 | 异步轮询 + LRU 缓存，体验流畅 |
| SceneController | ✅ 完成 | 场景切换跟踪 |
| DependencyController | 🔶 基础完成 | 管线检测逻辑可复用，缺 UI |

### 1.3 Service 层 — 保留

所有 Service（AssetService、SelectionService、PreviewService、PrefabService、UndoService、PackageService）接口设计合理，实现简洁。**不需要改接口，只需补充实现。**

### 1.4 View 层 — 按页面评估

| 页面 | View 文件 | 保留程度 | 说明 |
|------|-----------|----------|------|
| HomeView | `HomeView.cs` | ✅ 保留 | 4 卡片入口，方向正确 |
| AssetGridView | `AssetGridView.cs` | ✅ 保留 | 卡片/列表切换、slider、收藏、分类 tabs 均已实现 |
| InspectorPanelView | `InspectorPanelView.cs` | ✅ 保留 | 三步操作面板方向正确 |
| SidebarView | `SidebarView.cs` | ✅ 保留 | 资产类型筛选 + 工具导航 |
| TopBarView | `TopBarView.cs` | ✅ 保留 | 搜索 + 面包屑 |
| BottomBarView | `BottomBarView.cs` | ✅ 保留 | 状态消息 |
| BatchReplaceView | `BatchReplaceView.cs` | 🔶 保留结构 | 上下文条+候选+预览闭环已重构，需补后端 |
| VehicleSetupView | `VehicleSetupView.cs` | 🔶 保留结构 | 3D 预览+零件树已有，需接入真实数据 |
| SceneBuilderView | `SceneBuilderView.cs` | 🔶 保留结构 | 模板+预设系统已有，需接入 StateRenderSystem |
| AIContextView | `AIContextView.cs` | ❌ 需重做 | 当前仅硬编码 mock，无实际功能 |
| ScenePreviewView | `ScenePreviewView.cs` | ❌ 空壳 | 纯占位符 |
| CompareView | `CompareView.cs` | ❌ 空壳 | 纯占位符 |

### 1.5 数据层 — 保留

| 数据 | 文件 | 状态 |
|------|------|------|
| WorkspaceState | `Data/WorkspaceState.cs` | ✅ 核心状态容器，需拆分 UI/Domain |
| AssetEntry | `Data/AssetEntry.cs` | ✅ 资产元数据模型 |
| AssetRegistry | `Data/AssetRegistry.cs` | ✅ 内存资产集合 |
| PreviewCache | `Data/PreviewCache.cs` | ✅ LRU 缓存 |
| BatchReplaceState | `Data/BatchReplaceState.cs` | ✅ 数据模型完整 |
| SceneBuilderState | `Data/SceneBuilderState.cs` | ✅ 模板+预设模型 |
| VehicleSetupState | `Data/VehicleSetupState.cs` | ✅ 零件+校验模型 |

---

## 二、当前工程问题项

### 2.1 P0 — 必须先改

#### 问题 1：完全没有 HMIRP 依赖感知

**现状**：
- `DependencyController` 能检测管线类型（URP/HDRP/Built-in）
- 但 **没有任何 UI** 展示依赖状态
- 当 HMIRP 四包缺失时，工具不会提示，也不会降级
- `PackageService.DetectPipeline()` 只返回字符串，不检查四包版本

**影响**：用户装了 Workspace 但没装 HMIRP → 看到一堆空白 → 不知道为什么

**建议改造**：
- 在 `PackageService` 中增加四包检测方法（检查 `Packages/` 或 `manifest.json`）
- 在 `WorkspaceState` 中增加 `Observable<DependencyStatus> PipelineStatus`
- HomeView 在依赖缺失时显示降级卡片："需要安装 HMIRP 渲染管线"
- 右面板在依赖缺失时 Step 3 显示："当前环境不支持此操作，请先安装…"

#### 问题 2：资产扫描路径硬编码

**现状**：
```csharp
private const string ScanRoot = "Assets/HMIWorkspaceTest";
```

**影响**：
- 用户必须手动创建 `HMIWorkspaceTest` 目录
- 无法扫描 HMIRP MaterialLibrary 包里的 50+ 材质
- 无法扫描 ShaderLibrary 包里的 17+ ShaderGraph

**建议改造**：
- 支持多根路径扫描：`Assets/` + `Packages/com.tuanjie.hmirp.materiallibrary/`
- `AssetService` 增加 `AddScanRoot(string path)` 方法
- 自动检测已安装包的资产路径

#### 问题 3：ActiveTool 枚举完全未使用

**现状**：
```csharp
public enum ActiveTool { None, VehicleSetup, BatchReplace, SceneBuilder }
// 从未被 set/get，代码全部使用 ViewMode
```

**建议**：删除 `ActiveTool`，或明确它与 `ViewMode` 的区别（如果有的话）。

### 2.2 P1 — 流程和结构改造

#### 问题 4：WorkspaceState 混合了 UI 状态和领域状态

**现状**：11 个 Observable 混在一起
```csharp
// 领域状态
Observable<AssetEntry> SelectedAsset;
Observable<Object> UnitySelection;
Observable<ViewMode> CurrentViewMode;

// UI 状态（不应在领域层）
Observable<string> CurrentSidebarTab;
Observable<string> CurrentRightTab;
Observable<float> ProgressValue;
```

**建议**：拆分为 `WorkspaceState`（领域）+ `UIState`（视图层面），或至少在注释中分区。

#### 问题 5：ModeToLabel 重复定义

**现状**：`WorkspaceController.ModeToLabel()` 和 `HMIWorkspaceWindow.ModeToLabel()` 各有一份 switch 表达式。

**建议**：统一到一处（`Enums.cs` 中的扩展方法）。

#### 问题 6：AssetKind → 分类字符串二次转换

**现状**：
```
AssetKind.Material → KindToCategory() → "材质库" → SidebarView 过滤
```
两步转换，中间用字符串匹配。

**建议**：`AssetEntry` 直接存储 `Category` 枚举，消除字符串中间态。

#### 问题 7：BatchReplace 缺少 ActionController 后端

**现状**：
- `BatchReplaceView` 有完整 UI（上下文条 + 候选 + 预览 + 操作按钮）
- `BatchReplaceState` 有完整数据模型
- 但 `ActionController` 只有 `ApplyToSelection()`
- 没有 `ReplaceAsset()` 或 `BatchReplaceAll()` 方法
- `BatchReplaceView` 内部自己实现了 `OnApplySingle()` 和 `OnApplyAll()`

**问题**：操作逻辑在 View 层，违反架构约定。

**建议**：将 `OnApplySingle`/`OnApplyAll` 的 Renderer 操作逻辑提取到 `ActionController`。

#### 问题 8：三大工作区（Vehicle/Batch/Scene）各自独立建设

**现状**：三个 View 各自包含完整的左中右三栏布局，与全局的 Sidebar + CenterView + InspectorPanel 体系并行。

**影响**：
- VehicleSetupView 有自己的右面板（绑定+校验），同时全局右面板也在显示
- 用户看到两个右面板信息，不知道哪个是"当前操作"
- 三个工作区与主工作流（选目标→选资产→应用）没有收敛

**建议**：
- 三大工作区只负责左+中区域，右面板统一用 InspectorPanelView
- 通过 `SelectionEvents.ContextChanged` 将工作区状态同步到右面板（已部分实现）
- VehicleSetupView 右面板内容合并到 InspectorPanelView 的 Step 显示中

#### 问题 9：PreviewRenderUtility 生命周期管理不完善

**现状**：已部分修复（添加了 Dispose），但 `Bind()` 被多次调用时不清理旧实例。

**建议**：在 `Bind()` 开头调用 `CleanupPreview()`。

### 2.3 P2 — 视觉和表现提升（暂不改）

| 问题 | 说明 | 优先级 |
|------|------|--------|
| ScenePreviewView 是空壳 | 需要实现场景 3D 预览 | P2 |
| CompareView 是空壳 | 需要实现材质对比 | P2 |
| AI Service 是 mock | 需要接入真实 AI API | P2 |
| 国际化 | 所有字符串硬编码中文 | P2 |
| 无 EditorPrefs 持久化 | 用户偏好（slider、tab）不保存 | P2 |

---

## 三、四个 HMIRP 包对 Workspace 的角色定位

### 3.1 依赖关系图

```
┌────────────────────────────────────────────────────────┐
│                 Asset Workspace（壳层）                  │
│  发现 / 索引 / 预览 / 对比 / 应用 / 批量替换              │
├────────────┬──────────┬──────────┬──────────────────────┤
│            │          │          │                      │
│   展示依赖  │  展示依赖  │  软依赖   │     强依赖            │
│            │          │          │                      │
▼            ▼          ▼          ▼                      │
materiallibrary  shaderlibrary  staterender   HMIRP Pipeline
(50+ .mat)       (17 .shadergraph)  system      (核心渲染管线)
                                 (天气/昼夜)              │
└────────────┴──────────┴──────────┴──────────────────────┘
```

### 3.2 各包角色定义

| 包 | 角色 | 对 Workspace 的意义 | 当前工程是否正确使用 |
|----|------|-------------------|-------------------|
| **HMIRP Pipeline** | 强依赖 | 渲染管线决定材质能否正确预览、Shader 能否编译 | ❌ 未检测，未降级 |
| **materiallibrary** | 展示依赖 | 提供 50+ 预置材质，是资产浏览的核心数据源 | ❌ 未扫描包内资产 |
| **shaderlibrary** | 展示依赖 | 提供 17+ ShaderGraph，决定材质类型（车漆/皮革/织物等） | ❌ 未索引，侧边栏不识别 |
| **staterendersystem** | 软依赖 | SceneBuilder 的灯光/天气/昼夜预设需要它 | ❌ SceneBuilder 用 mock 数据 |

### 3.3 当前工程的越界问题

**问题：当前工程没有越界实现底层能力，但也没有正确接入底层能力。**

具体：
- ✅ 没有重复实现 Shader 编辑（正确）
- ✅ 没有重复实现材质创建（正确）
- ✅ 没有重复实现天气系统（正确）
- ❌ 没有扫描 materiallibrary 的 50+ 材质（数据源断裂）
- ❌ 没有根据 shaderlibrary 的 Shader 类型分类资产（分类断裂）
- ❌ SceneBuilderView 的预设（天气/昼夜/灯光）是硬编码字符串，未接入 StateRenderSystem 的 Profile
- ❌ VehicleSetupView 的"车漆"分类未与 shaderlibrary 的 CarPaint shader 关联

### 3.4 具体断裂点

| 断裂点 | 当前实现 | 应该接入的包 | 影响 |
|--------|---------|------------|------|
| 材质来源 | 只扫描 `Assets/HMIWorkspaceTest/` | materiallibrary 50+ .mat | 用户看不到预置材质 |
| Shader 分类 | `AssetKind.Shader` 枚举值存在但侧边栏不显示 | shaderlibrary 17 .shadergraph | 无法按车漆/皮革/织物筛选 |
| 天气预设 | SceneBuilder 硬编码 "晴天/阴天/雨天" | staterendersystem ProceduralSkyProfile | 预设无法真正应用 |
| 灯光预设 | SceneBuilder 硬编码 "日光/黄昏/夜晚" | staterendersystem TimeProfile | 预设无法真正应用 |
| 管线检测 | `PackageService.DetectPipeline()` 仅返回字符串 | HMIRP Pipeline package.json | 不知道是否安装、版本是否兼容 |

---

## 四、建议的改造优先级

### P0：必须先改（依赖感知 + 数据源接通）

| # | 改造项 | 预计改动量 |
|---|--------|-----------|
| P0-1 | PackageService 增加四包检测 | 新增 ~80 行 |
| P0-2 | WorkspaceState 增加 DependencyStatus | 新增 ~30 行 |
| P0-3 | HomeView 依赖缺失时降级显示 | 改动 ~40 行 |
| P0-4 | AssetService 支持多根路径扫描 | 改动 ~50 行 |
| P0-5 | 删除 ActiveTool 枚举 | 删除 ~5 行 |

### P1：流程和结构改造

| # | 改造项 | 预计改动量 |
|---|--------|-----------|
| P1-1 | BatchReplace 操作逻辑提取到 ActionController | 移动 ~60 行 |
| P1-2 | ModeToLabel 统一到 Enums 扩展方法 | 改动 ~20 行 |
| P1-3 | WorkspaceState 分区（领域 vs UI） | 重排 ~15 行 |
| P1-4 | SceneBuilder 接入 StateRenderSystem Profile | 改动 ~100 行 |
| P1-5 | VehicleSetup 右面板合并到 InspectorPanelView | 改动 ~80 行 |

### P2：视觉和表现提升（后续轮次）

| # | 改造项 |
|---|--------|
| P2-1 | CompareView 实现材质 A/B 对比 |
| P2-2 | ScenePreviewView 实现场景 3D 预览 |
| P2-3 | AI Service 接入真实 API |
| P2-4 | EditorPrefs 持久化用户偏好 |

---

## 五、最建议先动的文件（按改造顺序）

### 第 1 轮：依赖感知（P0）

| # | 文件 | 改什么 | 为什么先改它 |
|---|------|--------|------------|
| 1 | `Editor/Services/PackageService.cs` | 增加 `CheckHMIRPPackages()` 方法，检测四包安装状态和版本 | **基础设施**：所有降级逻辑的前提 |
| 2 | `Editor/Services/IPackageService.cs` | 增加接口方法声明 | 与 PackageService 配套 |
| 3 | `Editor/Data/WorkspaceState.cs` | 增加 `DependencyStatus` observable；分区注释 | 状态感知的数据载体 |
| 4 | `Editor/Controllers/DependencyController.cs` | 在 Initialize 中调用四包检测，更新状态 | 将检测结果写入状态 |
| 5 | `Editor/Views/HomeView.cs` | 依赖缺失时卡片显示降级提示 | **用户第一眼看到的入口** |

### 第 2 轮：数据源接通（P0）

| # | 文件 | 改什么 | 为什么先改它 |
|---|------|--------|------------|
| 6 | `Editor/Services/AssetService.cs` | 多根路径扫描（Assets/ + Packages/materiallibrary/） | 50+ 预置材质是核心数据 |
| 7 | `Editor/Services/IAssetService.cs` | 增加 `AddScanRoot()` 接口 | 与 AssetService 配套 |
| 8 | `Editor/Data/Enums.cs` | 删除 `ActiveTool`；增加 `ModeToLabel` 扩展方法 | 清理冗余 + 统一转换 |

### 第 3 轮：操作闭环（P1）

| # | 文件 | 改什么 | 为什么改它 |
|---|------|--------|----------|
| 9 | `Editor/Controllers/ActionController.cs` | 增加 `ReplaceAsset()` 和 `BatchReplaceAll()` 方法 | BatchReplace 的操作逻辑不应在 View 层 |
| 10 | `Editor/Views/BatchReplaceView.cs` | `OnApplySingle/OnApplyAll` 改为调用 ActionController | 架构归位 |

---

## 六、增量改造顺序总结

```
第 1 轮（P0 依赖感知）     第 2 轮（P0 数据接通）     第 3 轮（P1 操作闭环）
┌──────────────────┐   ┌──────────────────┐   ┌──────────────────┐
│ PackageService    │   │ AssetService     │   │ ActionController │
│ IPackageService   │   │ IAssetService    │   │ BatchReplaceView │
│ WorkspaceState    │──▶│ Enums.cs         │──▶│                  │
│ DependencyCtrl    │   │                  │   │                  │
│ HomeView          │   │                  │   │                  │
└──────────────────┘   └──────────────────┘   └──────────────────┘
       ~200 行                ~100 行               ~140 行
```

**总计改动量：约 440 行（新增+修改），0 文件删除，0 页面推翻。**

---

## 附录 A：四包资产清单摘要

### materiallibrary（com.tuanjie.hmirp.materiallibrary v1.0.4）
- 50+ 材质文件（.mat），按类型分类：Wood、CarbonFiber、Reflector、Lamp、Floor
- 配套 Mesh、Prefab、Texture 资产
- **Workspace 应扫描 → 在资产浏览中展示**

### shaderlibrary（com.tuanjie.hmirp.shaderlibrary v1.0.3）
- 17 ShaderGraph：PBRStandard、CarPaint（含 VertexColor 变体）、Leather、Fabric（Cotton/Silk）、RainDecal、EmissionMaster、TintMaster、Billboard、UI-Blur、MapStandard、Tree_SimpleWind、WindowsPOM
- 8 个 SubGraph 子图
- **Workspace 应按 Shader 类型分类材质（车漆/皮革/织物/标准 PBR）**

### staterendersystem（com.tuanjie.hmirp.staterendersystem v1.0.4）
- StateRenderController：天气/昼夜/灯光状态管理
- ProceduralSkyProfile：天空/云/雾配置
- TimeProfile：时间段配置
- TransitionAnimation：状态切换动画
- WeatherController：天气粒子管理
- **SceneBuilder 应接入 Profile 而非硬编码字符串**

### HMIRP Pipeline（com.unity.render-pipelines.universal v1.0.4 HMI 定制版）
- 基于 URP 14.1.0 定制
- HMICore 扩展：Sky、StateRender、Weather、PostProcessing
- 依赖：Mathematics 1.2.1、Burst 1.8.9、ShaderGraph 14.1.0
- **Workspace 必须检测此包是否安装，否则材质预览将不正确**

---

## 附录 B：当前文件树（完整）

```
TuanjieHMIAssetWorkspace/
├── package.json
├── Docs/AssetWorkspace/
│   └── 01_Existing_Project_Audit_And_Refactor_Plan.md  (本文件)
├── Editor/
│   ├── com.hmi.workspace.Editor.asmdef
│   ├── Core/
│   │   ├── IController.cs
│   │   ├── Observable.cs
│   │   ├── EventChannel.cs
│   │   └── Events.cs
│   ├── Data/
│   │   ├── WorkspaceState.cs
│   │   ├── Enums.cs
│   │   ├── AssetEntry.cs
│   │   ├── AssetRegistry.cs
│   │   ├── BatchReplaceState.cs
│   │   ├── SceneBuilderState.cs
│   │   ├── VehicleSetupState.cs
│   │   ├── CommandHistory.cs
│   │   ├── PreviewCache.cs
│   │   ├── DependencyGraph.cs
│   │   └── SceneInfo.cs
│   ├── Controllers/
│   │   ├── WorkspaceController.cs
│   │   ├── SelectionController.cs
│   │   ├── ActionController.cs
│   │   ├── AssetBrowserController.cs
│   │   ├── PreviewController.cs
│   │   ├── SceneController.cs
│   │   ├── AIController.cs
│   │   ├── DependencyController.cs
│   │   └── ViewInterfaces/
│   │       ├── IInspectorView.cs
│   │       ├── ITopBarView.cs
│   │       ├── IBottomBarView.cs
│   │       └── IAssetGridView.cs
│   ├── Services/
│   │   ├── IAssetService.cs / AssetService.cs
│   │   ├── ISelectionService.cs / SelectionService.cs
│   │   ├── IPreviewService.cs / PreviewService.cs
│   │   ├── IPrefabService.cs / PrefabService.cs
│   │   ├── IUndoService.cs / UndoService.cs
│   │   ├── IAIService.cs / AIService.cs
│   │   └── IPackageService.cs / PackageService.cs
│   ├── Views/
│   │   ├── HMIWorkspaceWindow.cs
│   │   ├── HomeView.cs
│   │   ├── AssetGridView.cs
│   │   ├── InspectorPanelView.cs
│   │   ├── SidebarView.cs
│   │   ├── TopBarView.cs
│   │   ├── BottomBarView.cs
│   │   ├── VehicleSetupView.cs
│   │   ├── BatchReplaceView.cs
│   │   ├── SceneBuilderView.cs
│   │   ├── AIContextView.cs
│   │   ├── ScenePreviewView.cs
│   │   └── CompareView.cs
│   ├── USS/
│   │   └── HMIWorkspace.uss
│   └── UXML/
│       └── HMIWorkspace.uxml
└── Runtime/
    └── com.hmi.workspace.Runtime.asmdef
```
