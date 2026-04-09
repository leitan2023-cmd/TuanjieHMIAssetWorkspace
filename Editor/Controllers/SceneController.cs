using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;
using HMI.Workspace.Editor.Services;

namespace HMI.Workspace.Editor.Controllers
{
    /// <summary>
    /// 场景构建控制器 — SceneBuilder 工作区的真实控制入口。
    ///
    /// 职责：
    /// 1. 管理场景模板列表和当前选中模板
    /// 2. 维护 SceneBuilderState（灯光/相机/天气/地面/天空配置）
    /// 3. 判断 State Render System 可用性，标记高级特性的降级状态
    /// 4. 执行场景生成（创建灯光、相机、环境）
    /// 5. 发布上下文事件，驱动 InspectorPanel 联动
    /// 6. 追踪当前活跃场景信息
    /// </summary>
    public sealed class SceneController : IController
    {
        private readonly WorkspaceState _state;
        private readonly SceneBuilderState _sb = new();
        private bool _suppressContextPublish;

        /// <summary>需要 State Render System 的高级天气 ID</summary>
        private static readonly HashSet<string> AdvancedWeatherIds = new() { "rainy", "snowy", "foggy" };

        /// <summary>需要 State Render System 的高级天空 ID</summary>
        private static readonly HashSet<string> AdvancedSkyIds = new() { "procedural" };

        /// <summary>SceneBuilder 状态，供 View 绑定。</summary>
        public SceneBuilderState BuilderState => _sb;

        public SceneController(WorkspaceState state)
        {
            _state = state;
            InitTemplates();
        }

        public void Initialize()
        {
            UpdateSceneInfo();
            EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;

            // 配置变化 → 重新发布上下文（InspectorPanel 消费）
            _sb.LightingPresetId.Changed += (_, _) => PublishContext();
            _sb.CameraPresetId.Changed   += (_, _) => PublishContext();
            _sb.WeatherId.Changed        += (_, _) => PublishContext();
            _sb.FloorId.Changed          += (_, _) => PublishContext();
            _sb.SkyId.Changed            += (_, _) => PublishContext();

            // 依赖状态变化 → 重新发布（高级特性可用性可能改变）
            _state.StateRenderHealth.Changed += (_, _) => PublishContext();

            // 选中默认模板 → 触发级联更新
            if (_sb.Templates.Value.Count > 0)
                SelectTemplate(_sb.Templates.Value[0]);
        }

        // ════════════════════════════════════════════════════════════
        // State Render 感知
        // ════════════════════════════════════════════════════════════

        /// <summary>State Render System 是否已安装。</summary>
        public bool IsStateRenderAvailable =>
            _state.StateRenderHealth.Value == PackageHealth.Installed;

        /// <summary>指定天气 ID 是否需要 State Render System。</summary>
        public bool IsAdvancedWeather(string weatherId) =>
            AdvancedWeatherIds.Contains(weatherId);

        /// <summary>指定天空 ID 是否需要 State Render System。</summary>
        public bool IsAdvancedSky(string skyId) =>
            AdvancedSkyIds.Contains(skyId);

        // ════════════════════════════════════════════════════════════
        // 模板管理
        // ════════════════════════════════════════════════════════════

        /// <summary>选择场景模板并级联应用默认配置。</summary>
        public void SelectTemplate(SceneTemplate template)
        {
            if (template == null) return;

            // 批量设置默认值时抑制逐条上下文发布，最后统一发布一次
            _suppressContextPublish = true;
            ApplyTemplateDefaults(template);
            _sb.SelectedTemplate.Value = template;
            _suppressContextPublish = false;

            PublishContext();
        }

        private void ApplyTemplateDefaults(SceneTemplate t)
        {
            if (!string.IsNullOrEmpty(t.DefaultLighting))  _sb.LightingPresetId.Value = t.DefaultLighting;
            if (!string.IsNullOrEmpty(t.DefaultCamera))    _sb.CameraPresetId.Value   = t.DefaultCamera;
            if (!string.IsNullOrEmpty(t.DefaultWeather))   _sb.WeatherId.Value         = t.DefaultWeather;
            if (!string.IsNullOrEmpty(t.DefaultFloor))     _sb.FloorId.Value           = t.DefaultFloor;
            if (!string.IsNullOrEmpty(t.DefaultSky))       _sb.SkyId.Value             = t.DefaultSky;
        }

        // ════════════════════════════════════════════════════════════
        // 配置摘要
        // ════════════════════════════════════════════════════════════

        /// <summary>返回当前配置的格式化摘要文本。</summary>
        public string GetConfigSummary()
        {
            var t = _sb.SelectedTemplate.Value;
            if (t == null) return "";

            var summary = $"\u5206\u7C7B\uFF1A{t.Category}\n\u73AF\u5883\uFF1A{t.EnvironmentLabel ?? "\u672A\u6307\u5B9A"}";
            summary += $"\n\u706F\u5149\uFF1A{GetLightingName(_sb.LightingPresetId.Value)}";
            summary += $"\n\u89C6\u89D2\uFF1A{GetCameraName(_sb.CameraPresetId.Value)}";

            var weather = GetWeatherName(_sb.WeatherId.Value);
            if (IsAdvancedWeather(_sb.WeatherId.Value) && !IsStateRenderAvailable)
                weather += " \u26A0";
            summary += $"\n\u5929\u6C14\uFF1A{weather}";

            summary += $"\n\u5730\u9762\uFF1A{GetFloorName(_sb.FloorId.Value)}";

            var sky = GetSkyName(_sb.SkyId.Value);
            if (IsAdvancedSky(_sb.SkyId.Value) && !IsStateRenderAvailable)
                sky += " \u26A0";
            summary += $"\n\u5929\u7A7A\uFF1A{sky}";

            if (t.Features != null && t.Features.Length > 0)
                summary += $"\n\u7279\u6027\uFF1A{string.Join(", ", t.Features)}";

            return summary;
        }

        /// <summary>返回 State Render 相关的状态描述。</summary>
        public string GetStateRenderSummary()
        {
            if (IsStateRenderAvailable)
                return "\u2713 State Render System \u5DF2\u5C31\u7EEA\uFF0C\u6240\u6709\u6548\u679C\u53EF\u7528";
            return "\u26A0 State Render System \u672A\u5B89\u88C5\uFF0C\u9AD8\u7EA7\u5929\u6C14/\u5929\u7A7A\u6548\u679C\u964D\u7EA7\u663E\u793A";
        }

        // ════════════════════════════════════════════════════════════
        // 上下文发布 — 驱动 InspectorPanel
        // ════════════════════════════════════════════════════════════

        private void PublishContext()
        {
            if (_suppressContextPublish) return;

            var t = _sb.SelectedTemplate.Value;
            if (t == null)
            {
                SelectionEvents.ContextCleared.Publish(new SelectionContextClearedEvent(
                    "SceneBuilder", "\u4ECE\u5DE6\u4FA7\u9009\u62E9\u573A\u666F\u6A21\u677F\u5F00\u59CB\u914D\u7F6E"));
                return;
            }

            SelectionEvents.ContextChanged.Publish(new SelectionContextEvent(
                "SceneBuilder",
                t.Name,
                $"{t.Category}  \u2022  {t.UsageHint ?? "\u573A\u666F\u6A21\u677F"}",
                GetConfigSummary(),
                $"\u914D\u7F6E\u53C2\u6570\u540E\u70B9\u51FB\u300C\u751F\u6210\u573A\u666F\u300D\n{GetStateRenderSummary()}"));
        }

        // ════════════════════════════════════════════════════════════
        // 场景生成
        // ════════════════════════════════════════════════════════════

        /// <summary>根据当前模板和配置生成场景。</summary>
        public void GenerateScene()
        {
            var t = _sb.SelectedTemplate.Value;
            if (t == null)
            {
                _state.StatusMessage.Value = "\u8BF7\u5148\u9009\u62E9\u4E00\u4E2A\u573A\u666F\u6A21\u677F";
                return;
            }

            _sb.IsGenerating.Value = true;
            _sb.GenerateStatus.Value = "\u6B63\u5728\u751F\u6210\u573A\u666F\u2026";

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            scene.name = $"HMI_{t.Id}";

            SetupLighting();
            SetupCamera();
            SetupEnvironment();

            _sb.IsGenerating.Value = false;
            var msg = $"\u2713 \u5DF2\u751F\u6210\u300C{t.Name}\u300D\u573A\u666F";
            _sb.GenerateStatus.Value = msg;
            _state.StatusMessage.Value = msg;
            ActionEvents.Executed.Publish(new ActionExecutedEvent("SceneGenerate", msg));

            UpdateSceneInfo();
        }

        private void SetupLighting()
        {
            var lights = Object.FindObjectsOfType<Light>();
            foreach (var light in lights)
                Object.DestroyImmediate(light.gameObject);

            var pid = _sb.LightingPresetId.Value;
            switch (pid)
            {
                case "studio":
                    CreateLight("Key",  LightType.Spot, new Vector3(0, 4, -3),  Quaternion.Euler(45, 0, 0),     Color.white, 800);
                    CreateLight("Fill", LightType.Spot, new Vector3(-3, 3, 2),  Quaternion.Euler(30, 60, 0),    new Color(0.9f, 0.95f, 1f), 400);
                    CreateLight("Rim",  LightType.Spot, new Vector3(3, 3, 3),   Quaternion.Euler(30, -120, 0),  new Color(1f, 0.95f, 0.9f), 300);
                    break;
                case "three-point":
                    CreateLight("Key",  LightType.Directional, new Vector3(0, 5, -5), Quaternion.Euler(50, -30, 0),  Color.white, 1.2f);
                    CreateLight("Fill", LightType.Directional, new Vector3(0, 3, 5),  Quaternion.Euler(30, 150, 0),  new Color(0.7f, 0.8f, 1f), 0.5f);
                    CreateLight("Rim",  LightType.Directional, new Vector3(0, 4, 0),  Quaternion.Euler(70, 180, 0),  new Color(1f, 0.95f, 0.85f), 0.8f);
                    break;
                case "ring-light":
                    for (int i = 0; i < 8; i++)
                    {
                        float angle = i * 45f;
                        float rad = angle * Mathf.Deg2Rad;
                        var pos = new Vector3(Mathf.Sin(rad) * 3f, 3f, Mathf.Cos(rad) * 3f);
                        CreateLight($"Ring_{i}", LightType.Point, pos, Quaternion.identity, Color.white, 200);
                    }
                    break;
                default:
                    CreateLight("Sun", LightType.Directional, Vector3.zero,
                        Quaternion.Euler(pid == "hdri-night" ? 10 : pid == "hdri-sunset" ? 15 : 50, -30, 0),
                        pid == "hdri-night" ? new Color(0.4f, 0.5f, 0.7f) : pid == "hdri-sunset" ? new Color(1f, 0.7f, 0.4f) : Color.white,
                        pid == "hdri-night" ? 0.3f : 1f);
                    break;
            }
        }

        private static void CreateLight(string name, LightType type, Vector3 pos, Quaternion rot, Color col, float intensity)
        {
            var go = new GameObject($"SB_{name}");
            go.transform.position = pos;
            go.transform.rotation = rot;
            var l = go.AddComponent<Light>();
            l.type = type;
            l.color = col;
            l.intensity = intensity;
            if (type == LightType.Spot) l.spotAngle = 60;
        }

        private void SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            switch (_sb.CameraPresetId.Value)
            {
                case "orbit-60":
                    cam.fieldOfView = 60; cam.transform.position = new Vector3(3, 2, -4); cam.transform.LookAt(Vector3.zero); break;
                case "orbit-35":
                    cam.fieldOfView = 35; cam.transform.position = new Vector3(5, 2.5f, -6); cam.transform.LookAt(Vector3.zero); break;
                case "front-hero":
                    cam.fieldOfView = 40; cam.transform.position = new Vector3(0, 1.2f, -5); cam.transform.LookAt(new Vector3(0, 0.8f, 0)); break;
                case "three-quarter":
                    cam.fieldOfView = 50; cam.transform.position = new Vector3(4, 2, -3); cam.transform.LookAt(new Vector3(0, 0.5f, 0)); break;
                case "top-down":
                    cam.fieldOfView = 60; cam.transform.position = new Vector3(0, 8, 0); cam.transform.rotation = Quaternion.Euler(90, 0, 0); break;
                case "interior":
                    cam.fieldOfView = 75; cam.transform.position = new Vector3(-0.3f, 1.2f, 0.2f); cam.transform.rotation = Quaternion.Euler(5, 10, 0); break;
            }
        }

        private void SetupEnvironment()
        {
            var floorId = _sb.FloorId.Value;
            if (floorId != "transparent")
            {
                var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.name = "SB_Floor";
                floor.transform.position = Vector3.zero;
                floor.transform.localScale = new Vector3(3, 1, 3);

                var r = floor.GetComponent<Renderer>();
                if (r != null)
                {
                    var mat = new Material(Shader.Find("Standard"));
                    switch (floorId)
                    {
                        case "dark":     mat.color = new Color(0.1f, 0.1f, 0.12f);   mat.SetFloat("_Glossiness", 0.85f); break;
                        case "light":    mat.color = new Color(0.85f, 0.85f, 0.87f);  mat.SetFloat("_Glossiness", 0.7f);  break;
                        case "asphalt":  mat.color = new Color(0.2f, 0.2f, 0.22f);    mat.SetFloat("_Glossiness", 0.3f);  break;
                        case "concrete": mat.color = new Color(0.5f, 0.5f, 0.48f);    mat.SetFloat("_Glossiness", 0.15f); break;
                        case "grass":    mat.color = new Color(0.2f, 0.45f, 0.15f);   mat.SetFloat("_Glossiness", 0.1f);  break;
                    }
                    r.sharedMaterial = mat;
                }
            }

            var weatherId = _sb.WeatherId.Value;
            var ambient = weatherId switch
            {
                "cloudy" => new Color(0.4f, 0.42f, 0.48f),
                "rainy"  => new Color(0.3f, 0.32f, 0.38f),
                "snowy"  => new Color(0.6f, 0.65f, 0.7f),
                "foggy"  => new Color(0.45f, 0.48f, 0.5f),
                "sunset" => new Color(0.5f, 0.35f, 0.25f),
                _        => new Color(0.5f, 0.52f, 0.56f),
            };
            RenderSettings.ambientLight = ambient;

            // 高级天气效果（雾）— State Render 缺失时降级强度
            if (weatherId == "foggy" || weatherId == "rainy")
            {
                RenderSettings.fog = true;
                RenderSettings.fogColor = ambient;
                if (IsStateRenderAvailable)
                    RenderSettings.fogDensity = weatherId == "foggy" ? 0.05f : 0.02f;
                else
                    RenderSettings.fogDensity = weatherId == "foggy" ? 0.02f : 0.01f;
            }

            var skyId = _sb.SkyId.Value;
            var cam = Camera.main;
            if (cam != null && (skyId == "solid" || skyId == "gradient"))
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = skyId == "solid"
                    ? new Color(0.12f, 0.12f, 0.14f)
                    : new Color(0.15f, 0.18f, 0.25f);
            }

            // procedural sky 需要 State Render，降级为渐变
            if (skyId == "procedural" && cam != null)
            {
                if (!IsStateRenderAvailable)
                {
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.15f, 0.18f, 0.25f);
                    _sb.GenerateStatus.Value = "\u26A0 \u7A0B\u5E8F\u5316\u4E91\u5C42\u9700\u8981 State Render System\uFF0C\u5DF2\u964D\u7EA7\u4E3A\u6E10\u53D8\u80CC\u666F";
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        // 场景追踪
        // ════════════════════════════════════════════════════════════

        private void OnSceneChanged(Scene previous, Scene next)
        {
            UpdateSceneInfo();
        }

        private void UpdateSceneInfo()
        {
            var scene = EditorSceneManager.GetActiveScene();
            _state.ActiveScene.Value = new SceneInfo(scene.name, scene.rootCount, scene.isDirty);
        }

        // ════════════════════════════════════════════════════════════
        // 名称映射 — 供 View 调用
        // ════════════════════════════════════════════════════════════

        public static string GetLightingName(string id) => id switch
        {
            "studio" => "\u6444\u5F71\u68DA\u706F\u5149", "hdri-day" => "\u81EA\u7136\u65E5\u5149",
            "hdri-sunset" => "\u65E5\u843D\u6696\u5149", "hdri-night" => "\u591C\u95F4\u51B7\u5149",
            "three-point" => "\u4E09\u70B9\u5E03\u5149", "ring-light" => "\u73AF\u5F62\u706F",
            _ => id,
        };

        public static string GetCameraName(string id) => id switch
        {
            "orbit-60" => "\u73AF\u7ED5\u5E7F\u89D2", "orbit-35" => "\u73AF\u7ED5\u957F\u7126",
            "front-hero" => "\u6B63\u9762\u82F1\u96C4", "three-quarter" => "3/4 \u7ECF\u5178",
            "top-down" => "\u4FEF\u89C6", "interior" => "\u8F66\u5185\u89C6\u89D2",
            _ => id,
        };

        public static string GetWeatherName(string id) => id switch
        {
            "sunny" => "\u6674\u5929", "cloudy" => "\u9634\u5929", "rainy" => "\u96E8\u5929",
            "snowy" => "\u96EA\u5929", "foggy" => "\u96FE\u5929", "sunset" => "\u9EC4\u660F",
            _ => id,
        };

        public static string GetFloorName(string id) => id switch
        {
            "dark" => "\u6DF1\u8272\u9AD8\u5149", "light" => "\u6D45\u8272\u54D1\u5149",
            "asphalt" => "\u67CF\u6CB9\u8DEF\u9762", "concrete" => "\u6DF7\u51DD\u571F",
            "grass" => "\u8349\u5730", "transparent" => "\u900F\u660E",
            _ => id,
        };

        public static string GetSkyName(string id) => id switch
        {
            "gradient" => "\u6E10\u53D8\u5929\u7A7A", "solid" => "\u7EAF\u8272\u80CC\u666F",
            "hdri" => "HDRI \u5929\u7A7A\u7403", "procedural" => "\u7A0B\u5E8F\u5316\u4E91\u5C42",
            _ => id,
        };

        // ════════════════════════════════════════════════════════════
        // 模板数据 — 效果导向语言
        // ════════════════════════════════════════════════════════════

        private void InitTemplates()
        {
            _sb.Templates.Value = new List<SceneTemplate>
            {
                new SceneTemplate
                {
                    Id = "showroom", Name = "\u9AD8\u7AEF\u5C55\u5385", Category = "\u5BA4\u5185", Icon = "\u25C8",
                    Description = "\u4E13\u4E1A\u8F66\u8F86\u5C55\u793A\u7A7A\u95F4\uFF0C\u805A\u5149\u706F\u7167\u4EAE\u4E3B\u4F53\uFF0C\u53CD\u5C04\u5730\u9762\u8425\u9020\u9AD8\u7EA7\u8D28\u611F\u3002",
                    UsageHint = "\u9002\u7528\u4E8E\u4EA7\u54C1\u53D1\u5E03\u4E0E\u5BA2\u6237\u5C55\u793A",
                    EnvironmentLabel = "\u9AD8\u53CD\u5C04\u73AF\u5883",
                    Features = new[] { "3 \u70B9\u5E03\u5149", "\u53CD\u5C04\u5730\u9762", "\u6E10\u53D8\u80CC\u666F", "\u73AF\u5883\u5149\u5E26" },
                    DefaultLighting = "three-point", DefaultCamera = "orbit-60",
                    DefaultWeather = "sunny", DefaultFloor = "dark", DefaultSky = "gradient",
                },
                new SceneTemplate
                {
                    Id = "outdoor-road", Name = "\u6237\u5916\u516C\u8DEF", Category = "\u5BA4\u5916", Icon = "\u2261",
                    Description = "\u65E5\u95F4\u516C\u8DEF\u73AF\u5883\uFF0C\u771F\u5B9E\u5929\u7A7A\u7167\u660E\u4E0E\u8FDC\u666F\u914D\u5408\uFF0C\u5448\u73B0\u52A8\u6001\u9A7E\u9A76\u6C1B\u56F4\u3002",
                    UsageHint = "\u9002\u7528\u4E8E\u884C\u9A76\u6548\u679C\u5C55\u793A",
                    EnvironmentLabel = "\u81EA\u7136\u5149\u7167\u73AF\u5883",
                    Features = new[] { "\u771F\u5B9E\u5149\u7167\u6548\u679C", "HDRI \u5929\u7A7A", "\u516C\u8DEF\u6A21\u578B", "\u8FDC\u666F\u690D\u88AB" },
                    DefaultLighting = "hdri-day", DefaultCamera = "three-quarter",
                    DefaultWeather = "sunny", DefaultFloor = "asphalt", DefaultSky = "hdri",
                },
                new SceneTemplate
                {
                    Id = "studio", Name = "\u6444\u5F71\u68DA", Category = "\u5BA4\u5185", Icon = "\u25CB",
                    Description = "\u4E13\u4E1A\u4E2D\u6027\u6444\u5F71\u7A7A\u95F4\uFF0C\u67D4\u548C\u6F2B\u5C04\u5149\uFF0C\u7A81\u51FA\u6750\u8D28\u4E0E\u8272\u5F69\u7684\u771F\u5B9E\u8868\u73B0\u3002",
                    UsageHint = "\u9002\u7528\u4E8E\u6750\u8D28\u4E0E\u8272\u5F69\u8BC4\u5BA1",
                    EnvironmentLabel = "\u4E2D\u6027\u6F2B\u5C04\u73AF\u5883",
                    Features = new[] { "\u65E0\u7F1D\u80CC\u666F", "\u67D4\u5149\u7BB1", "\u53CD\u5C04\u677F", "\u4E2D\u6027\u8272\u8C03" },
                    DefaultLighting = "studio", DefaultCamera = "front-hero",
                    DefaultWeather = "sunny", DefaultFloor = "light", DefaultSky = "solid",
                },
                new SceneTemplate
                {
                    Id = "night-city", Name = "\u57CE\u5E02\u591C\u666F", Category = "\u5BA4\u5916", Icon = "\u2605",
                    Description = "\u9713\u8679\u706F\u6620\u5C04\u4E0B\u7684\u90FD\u5E02\u591C\u95F4\u573A\u666F\uFF0C\u6E7F\u6DA6\u8DEF\u9762\u589E\u5F3A\u5149\u5F71\u5C42\u6B21\u3002",
                    UsageHint = "\u9002\u7528\u4E8E\u706F\u5149\u6548\u679C\u6F14\u793A",
                    EnvironmentLabel = "\u57CE\u5E02\u706F\u5149\u73AF\u5883",
                    Features = new[] { "\u70B9\u5149\u6E90\u9635\u5217", "\u6E7F\u5730\u53CD\u5C04", "\u4F53\u79EF\u96FE", "\u9713\u8679\u53D1\u5149" },
                    DefaultLighting = "hdri-night", DefaultCamera = "three-quarter",
                    DefaultWeather = "rainy", DefaultFloor = "asphalt", DefaultSky = "hdri",
                },
                new SceneTemplate
                {
                    Id = "turntable", Name = "360\u00B0 \u65CB\u8F6C\u53F0", Category = "\u5C55\u793A", Icon = "\u21BB",
                    Description = "\u81EA\u52A8\u65CB\u8F6C\u5C55\u793A\u53F0\uFF0C\u73AF\u5F62\u706F\u4FDD\u8BC1\u65E0\u6B7B\u89D2\u7167\u660E\uFF0C\u7B80\u6D01\u80CC\u666F\u7A81\u51FA\u4E3B\u4F53\u3002",
                    UsageHint = "\u9002\u7528\u4E8E\u4EA7\u54C1\u5168\u89D2\u5EA6\u5C55\u793A",
                    EnvironmentLabel = "\u5747\u5300\u7167\u660E\u73AF\u5883",
                    Features = new[] { "\u73AF\u5F62\u706F", "\u81EA\u52A8\u65CB\u8F6C", "\u7EAF\u8272\u80CC\u666F", "\u65E0\u6B7B\u89D2\u7167\u660E" },
                    DefaultLighting = "ring-light", DefaultCamera = "orbit-35",
                    DefaultWeather = "sunny", DefaultFloor = "transparent", DefaultSky = "solid",
                },
                new SceneTemplate
                {
                    Id = "parking", Name = "\u5730\u4E0B\u8F66\u5E93", Category = "\u5BA4\u5185", Icon = "\u25A0",
                    Description = "\u771F\u5B9E\u5730\u4E0B\u505C\u8F66\u73AF\u5883\uFF0C\u8367\u5149\u706F\u7BA1\u7167\u660E\uFF0C\u6DF7\u51DD\u571F\u6750\u8D28\u4E0E\u6807\u7EBF\u589E\u6DFB\u573A\u666F\u611F\u3002",
                    UsageHint = "\u9002\u7528\u4E8E\u573A\u666F\u878D\u5408\u6F14\u793A",
                    EnvironmentLabel = "\u4F4E\u7167\u5EA6\u73AF\u5883",
                    Features = new[] { "\u7EBF\u6027\u706F\u7BA1", "\u6DF7\u51DD\u571F\u6750\u8D28", "\u5730\u9762\u6807\u7EBF", "\u81EA\u7136\u5165\u5149" },
                    DefaultLighting = "studio", DefaultCamera = "three-quarter",
                    DefaultWeather = "cloudy", DefaultFloor = "concrete", DefaultSky = "gradient",
                },
            };
        }

        public void Dispose()
        {
            EditorSceneManager.activeSceneChangedInEditMode -= OnSceneChanged;
        }
    }
}
