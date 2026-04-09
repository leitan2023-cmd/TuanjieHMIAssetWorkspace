using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Rendering;

namespace HMI.Workspace.Editor.Services
{
    public sealed class PackageService : IPackageService
    {
        // ── 四包注册表：包名 → (显示名, 最低版本) ──
        private static readonly Dictionary<string, (string displayName, string minVersion)> RequiredPackages = new()
        {
            ["com.unity.render-pipelines.universal"]                    = ("HMIRP Core",            "1.0.3"),
            ["com.tuanjie.hmirp.shaderlibrary"]                        = ("HMIRP Shader Library",   "1.0.3"),
            ["com.tuanjie.hmirp.materiallibrary"]                      = ("HMIRP Material Library",  "1.0.4"),
            ["com.tuanjie.render-pipelines.hmirp.staterendersystem"]   = ("HMIRP State Render",      "1.0.4"),
        };

        // ═══════════════════════════════════════════════════════════
        // 原有能力：渲染管线检测（保持不变）
        // ═══════════════════════════════════════════════════════════

        public string DetectPipeline()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null) return "内置";

            var typeName = pipeline.GetType().Name;
            if (typeName.Contains("Universal") || typeName.Contains("URP"))
                return "HMIRP";
            if (typeName.Contains("HDRenderPipeline") || typeName.Contains("HDRP"))
                return "HDRP";
            if (typeName.Contains("Lightweight") || typeName.Contains("LWRP"))
                return "LWRP";

            return typeName
                .Replace("RenderPipelineAsset", "")
                .Replace("PipelineAsset", "")
                .Replace("Asset", "");
        }

        // ═══════════════════════════════════════════════════════════
        // 新增能力：HMIRP 四包检测
        // ═══════════════════════════════════════════════════════════

        public HMIRPDependencyReport CheckHMIRPPackages()
        {
            // 先读一次 manifest，所有包共用
            var installedPackages = ReadInstalledPackages();

            var core     = CheckSingle("com.unity.render-pipelines.universal", installedPackages);
            var shader   = CheckSingle("com.tuanjie.hmirp.shaderlibrary", installedPackages);
            var material = CheckSingle("com.tuanjie.hmirp.materiallibrary", installedPackages);
            var state    = CheckSingle("com.tuanjie.render-pipelines.hmirp.staterendersystem", installedPackages);

            return new HMIRPDependencyReport(core, shader, material, state);
        }

        // ── 单包检测 ──
        private static PackageCheckResult CheckSingle(
            string packageName,
            Dictionary<string, string> installed)
        {
            var (displayName, minVersion) = RequiredPackages[packageName];
            var packagePath = $"Packages/{packageName}";

            // 优先按真实可访问包目录判断，而不是只看 manifest 声明。
            // 这样可以避免 git 包拉取失败但 manifest 仍残留、或本地包已存在却版本字符串解析失败的误判。
            var packageFolderExists = AssetDatabase.IsValidFolder(packagePath);

            if (!packageFolderExists)
            {
                return new PackageCheckResult(packageName, displayName, "", minVersion, PackageHealth.Missing);
            }

            // 对关键包增加内容级校验，确保不是“包名存在但内容不可用”。
            if (packageName == "com.tuanjie.hmirp.materiallibrary")
            {
                var materialsFolder = $"{packagePath}/Runtime/Materials";
                var hasMaterialsFolder = AssetDatabase.IsValidFolder(materialsFolder);
                var hasMaterials = hasMaterialsFolder && AssetDatabase.FindAssets("t:Material", new[] { materialsFolder }).Length > 0;
                if (!hasMaterials)
                    return new PackageCheckResult(packageName, displayName, "", minVersion, PackageHealth.Missing);
            }

            string installedVersion = null;

            // 1. 用 PackageInfo 取 Unity 已解析成功的真实版本
            try
            {
                var pkgInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(packagePath);
                if (pkgInfo != null && !string.IsNullOrEmpty(pkgInfo.version))
                    installedVersion = pkgInfo.version;
            }
            catch
            {
                // 某些异常状态下 PackageInfo 可能拿不到，继续走后备逻辑
            }

            // 2. 回退到 manifest/package.json 解析
            if (string.IsNullOrEmpty(installedVersion))
            {
                if (installed.TryGetValue(packageName, out var manifestVersion) && !string.IsNullOrEmpty(manifestVersion))
                    installedVersion = manifestVersion;
            }

            // 3. 再回退到包目录内 package.json
            if (string.IsNullOrEmpty(installedVersion))
            {
                var packageJsonPath = Path.Combine(Application.dataPath, "..", packagePath, "package.json");
                if (File.Exists(packageJsonPath))
                {
                    try
                    {
                        var json = File.ReadAllText(packageJsonPath);
                        installedVersion = ExtractJsonStringField(json, "version");
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }

            if (string.IsNullOrEmpty(installedVersion))
            {
                // 目录真实存在时，将其视为已安装；版本未知则按最低版本处理，避免误报“未安装”
                installedVersion = minVersion;
            }

            var health = CompareVersions(installedVersion, minVersion) >= 0
                ? PackageHealth.Installed
                : PackageHealth.VersionMismatch;

            return new PackageCheckResult(packageName, displayName, installedVersion, minVersion, health);
        }

        // ═══════════════════════════════════════════════════════════
        // 工具方法
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// 从 Packages/manifest.json 读取所有已安装包及版本。
        /// 如果某个包的 value 是 file: 或 git: 路径，会尝试读取其 package.json 拿真实版本。
        /// </summary>
        private static Dictionary<string, string> ReadInstalledPackages()
        {
            var result = new Dictionary<string, string>();

            // 路径 1：Packages/manifest.json（所有项目都有）
            var manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
            if (!File.Exists(manifestPath))
                return result;

            try
            {
                var json = File.ReadAllText(manifestPath);
                ParseManifestDependencies(json, result);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HMI Workspace] 读取 manifest.json 失败: {e.Message}");
            }

            // 路径 2：对 file: 引用的包，读取其 package.json 拿真实版本
            ResolveLocalPackageVersions(result);

            return result;
        }

        /// <summary>
        /// 极简 JSON 解析 manifest.json 的 dependencies 段。
        /// 不引入第三方 JSON 库，只做字符串匹配。
        /// </summary>
        private static void ParseManifestDependencies(string json, Dictionary<string, string> output)
        {
            // 找到 "dependencies" : { ... } 块
            var depsIndex = json.IndexOf("\"dependencies\"", StringComparison.Ordinal);
            if (depsIndex < 0) return;

            var braceStart = json.IndexOf('{', depsIndex);
            if (braceStart < 0) return;

            // 找到匹配的右花括号
            int depth = 0;
            int braceEnd = -1;
            for (int i = braceStart; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}') { depth--; if (depth == 0) { braceEnd = i; break; } }
            }
            if (braceEnd < 0) return;

            var block = json.Substring(braceStart + 1, braceEnd - braceStart - 1);

            // 逐行提取 "key": "value"
            var lines = block.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim().TrimEnd(',');
                // 格式: "com.xxx.yyy": "1.0.4" 或 "com.xxx.yyy": "file:..."
                var colonIdx = trimmed.IndexOf(':');
                if (colonIdx < 0) continue;

                var key = ExtractQuotedString(trimmed.Substring(0, colonIdx));
                var val = ExtractQuotedString(trimmed.Substring(colonIdx + 1));

                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(val))
                    output[key] = val;
            }
        }

        /// <summary>
        /// 对 file: / git URL 引用的包，解析出真实版本号。
        /// - file: 引用 → 读取其 package.json 中的 "version" 字段
        /// - git URL (#branch/tag) → 从 URL 尾部提取版本号
        /// </summary>
        private static void ResolveLocalPackageVersions(Dictionary<string, string> packages)
        {
            var packagesDir = Path.Combine(Application.dataPath, "..", "Packages");
            var keysToUpdate = new List<(string key, string version)>();

            foreach (var kvp in packages)
            {
                var val = kvp.Value;

                // ── git URL：https://xxx.git#release/1.0.4 或 #v1.0.4 ──
                if (val.Contains(".git#") || val.Contains(".git?"))
                {
                    var hashIdx = val.LastIndexOf('#');
                    if (hashIdx >= 0)
                    {
                        var fragment = val.Substring(hashIdx + 1); // "release/1.0.4"
                        var version = ExtractVersionFromFragment(fragment);
                        if (!string.IsNullOrEmpty(version))
                            keysToUpdate.Add((kvp.Key, version));
                    }
                    continue;
                }

                // ── file: 本地引用 ──
                if (!val.StartsWith("file:")) continue;

                var relativePath = val.Substring("file:".Length);
                var fullPath = Path.GetFullPath(Path.Combine(packagesDir, relativePath));
                var pkgJsonPath = Path.Combine(fullPath, "package.json");

                if (!File.Exists(pkgJsonPath)) continue;

                try
                {
                    var pkgJson = File.ReadAllText(pkgJsonPath);
                    var version = ExtractJsonStringField(pkgJson, "version");
                    if (!string.IsNullOrEmpty(version))
                        keysToUpdate.Add((kvp.Key, version));
                }
                catch
                {
                    // 读不到就保留 file: 原值
                }
            }

            foreach (var (key, version) in keysToUpdate)
                packages[key] = version;
        }

        /// <summary>
        /// 从 git fragment 中提取版本号。
        /// 支持: "release/1.0.4", "v1.0.4", "1.0.4", "release/v1.0.4-preview.1"
        /// </summary>
        private static string ExtractVersionFromFragment(string fragment)
        {
            // 取最后一个 '/' 之后的部分: "release/1.0.4" → "1.0.4"
            var slashIdx = fragment.LastIndexOf('/');
            var tail = slashIdx >= 0 ? fragment.Substring(slashIdx + 1) : fragment;

            // 去掉 'v' 前缀: "v1.0.4" → "1.0.4"
            if (tail.Length > 0 && (tail[0] == 'v' || tail[0] == 'V'))
                tail = tail.Substring(1);

            // 验证至少包含一个数字
            if (tail.Length > 0 && char.IsDigit(tail[0]))
                return tail;

            return "";
        }

        /// <summary>从引号包裹的字符串中提取内容。</summary>
        private static string ExtractQuotedString(string s)
        {
            var start = s.IndexOf('"');
            if (start < 0) return "";
            var end = s.IndexOf('"', start + 1);
            if (end < 0) return "";
            return s.Substring(start + 1, end - start - 1);
        }

        /// <summary>从 JSON 中提取指定字段的字符串值（极简实现）。</summary>
        private static string ExtractJsonStringField(string json, string fieldName)
        {
            var key = $"\"{fieldName}\"";
            var idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return "";
            var colonIdx = json.IndexOf(':', idx + key.Length);
            if (colonIdx < 0) return "";
            return ExtractQuotedString(json.Substring(colonIdx, Math.Min(100, json.Length - colonIdx)));
        }

        /// <summary>
        /// 语义化版本比较。返回 >0 表示 a 更大，=0 相等，<0 表示 b 更大。
        /// 只比较主版本号.次版本号.修订号，忽略 prerelease 标签。
        /// </summary>
        private static int CompareVersions(string a, string b)
        {
            var partsA = ParseVersionParts(a);
            var partsB = ParseVersionParts(b);

            for (int i = 0; i < 3; i++)
            {
                var va = i < partsA.Length ? partsA[i] : 0;
                var vb = i < partsB.Length ? partsB[i] : 0;
                if (va != vb) return va.CompareTo(vb);
            }
            return 0;
        }

        private static int[] ParseVersionParts(string version)
        {
            // 去掉 prerelease 后缀: "1.0.3-preview.1" → "1.0.3"
            var dashIdx = version.IndexOf('-');
            if (dashIdx >= 0) version = version.Substring(0, dashIdx);

            var segments = version.Split('.');
            var result = new int[segments.Length];
            for (int i = 0; i < segments.Length; i++)
                int.TryParse(segments[i], out result[i]);
            return result;
        }
    }
}
