using System;
using System.Collections.Generic;
using System.Linq;

namespace HMI.Workspace.Runtime.Logic
{
    /// <summary>
    /// 节点类型注册表。自动扫描程序集中所有 LogicNodeBase 子类。
    /// </summary>
    public static class LogicNodeRegistry
    {
        private static Dictionary<string, Type> _registry;
        private static bool _initialized;

        /// <summary>所有已注册的节点类型名称</summary>
        public static IReadOnlyCollection<string> RegisteredTypes
        {
            get
            {
                EnsureInitialized();
                return _registry.Keys;
            }
        }

        /// <summary>
        /// 按类型名称创建节点实例。
        /// </summary>
        public static LogicNodeBase CreateNode(string typeName)
        {
            EnsureInitialized();
            if (_registry.TryGetValue(typeName, out var type))
                return (LogicNodeBase)Activator.CreateInstance(type);
            return null;
        }

        /// <summary>
        /// 获取指定分类的所有节点类型。
        /// </summary>
        public static List<(string typeName, NodeCategory category, string displayName)> GetNodesByCategory(NodeCategory category)
        {
            EnsureInitialized();
            var result = new List<(string, NodeCategory, string)>();
            foreach (var kvp in _registry)
            {
                var instance = (LogicNodeBase)Activator.CreateInstance(kvp.Value);
                if (instance.Category == category)
                    result.Add((kvp.Key, category, instance.DisplayName));
            }
            return result;
        }

        /// <summary>
        /// 获取所有节点类型，按分类分组。
        /// </summary>
        public static Dictionary<NodeCategory, List<(string typeName, string displayName, string description)>> GetAllGrouped()
        {
            EnsureInitialized();
            var groups = new Dictionary<NodeCategory, List<(string, string, string)>>();

            foreach (var kvp in _registry)
            {
                var instance = (LogicNodeBase)Activator.CreateInstance(kvp.Value);
                if (!groups.ContainsKey(instance.Category))
                    groups[instance.Category] = new List<(string, string, string)>();
                groups[instance.Category].Add((kvp.Key, instance.DisplayName, instance.Description));
            }

            return groups;
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _registry = new Dictionary<string, Type>();

            var baseType = typeof(LogicNodeBase);
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (!type.IsAbstract && baseType.IsAssignableFrom(type))
                        {
                            _registry[type.Name] = type;
                        }
                    }
                }
                catch
                {
                    // 跳过无法加载的程序集
                }
            }

            _initialized = true;
        }

        /// <summary>强制重新扫描（热重载后使用）</summary>
        public static void Refresh()
        {
            _initialized = false;
            EnsureInitialized();
        }
    }
}
