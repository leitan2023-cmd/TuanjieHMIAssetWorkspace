using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using HMI.Workspace.Runtime.Logic;

namespace HMI.Workspace.Editor.LogicFlowEditor
{
    /// <summary>
    /// 节点搜索窗口（右键菜单 / 按空格打开）。
    /// 按分类组织所有可用的逻辑节点。
    /// </summary>
    public class LogicNodeSearchWindow : ScriptableObject, ISearchWindowProvider
    {
        private LogicGraphView _graphView;
        private Texture2D _indentIcon;

        public void Initialize(LogicGraphView graphView)
        {
            _graphView = graphView;
            // SearchTreeEntry 必须有非空 Texture 才能注册点击
            _indentIcon = new Texture2D(1, 1);
            _indentIcon.SetPixel(0, 0, Color.clear);
            _indentIcon.Apply();
        }

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            var tree = new List<SearchTreeEntry>
            {
                new SearchTreeGroupEntry(new GUIContent("添加节点"), 0)
            };

            var groups = LogicNodeRegistry.GetAllGrouped();

            // 按固定顺序排列分类
            var order = new[] { NodeCategory.Trigger, NodeCategory.Action, NodeCategory.Flow, NodeCategory.Data };

            foreach (var category in order)
            {
                if (!groups.TryGetValue(category, out var nodeList)) continue;

                var groupName = category switch
                {
                    NodeCategory.Trigger => "触发器 (Trigger)",
                    NodeCategory.Action => "动作 (Action)",
                    NodeCategory.Flow => "流程控制 (Flow)",
                    NodeCategory.Data => "数据 (Data)",
                    _ => category.ToString()
                };

                tree.Add(new SearchTreeGroupEntry(new GUIContent(groupName), 1));

                foreach (var (typeName, displayName, description) in nodeList)
                {
                    tree.Add(new SearchTreeEntry(new GUIContent(displayName, _indentIcon))
                    {
                        level = 2,
                        userData = typeName
                    });
                }
            }

            return tree;
        }

        public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
        {
            if (entry.userData is string typeName)
            {
                // 将屏幕坐标转为 GraphView content 坐标
                // 兼容 Tuanjie 2022.3：用 worldBound + contentViewContainer.transform
                var graphRect = _graphView.worldBound;
                var contentOffset = _graphView.contentViewContainer.transform.position;
                var contentScale = _graphView.contentViewContainer.transform.scale;

                var relativePos = context.screenMousePosition - graphRect.position;
                var localPos = new Vector2(
                    (relativePos.x - contentOffset.x) / contentScale.x,
                    (relativePos.y - contentOffset.y) / contentScale.y
                );

                _graphView.CreateNode(typeName, localPos);
                return true;
            }

            return false;
        }
    }
}
