using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using HMI.Workspace.Runtime.Logic;

namespace HMI.Workspace.Editor.LogicFlowEditor
{
    /// <summary>
    /// 单个逻辑节点在 GraphView 中的可视化表示。
    /// </summary>
    public class LogicNodeView : Node
    {
        public string NodeId { get; private set; }
        public LogicNodeBase NodeInstance { get; private set; }
        public LogicNodeData Data { get; private set; }

        private readonly Dictionary<string, Port> _inputPorts = new();
        private readonly Dictionary<string, Port> _outputPorts = new();

        // ── 分类颜色映射 ──
        private static readonly Dictionary<NodeCategory, Color> CategoryColors = new()
        {
            { NodeCategory.Trigger, new Color(0.2f, 0.7f, 0.3f) },  // 绿色
            { NodeCategory.Action,  new Color(0.3f, 0.5f, 0.8f) },  // 蓝色
            { NodeCategory.Flow,    new Color(0.9f, 0.6f, 0.2f) },  // 橙色
            { NodeCategory.Data,    new Color(0.6f, 0.3f, 0.8f) },  // 紫色
        };

        public LogicNodeView(LogicNodeData data, LogicNodeBase nodeInstance)
        {
            Data = data;
            NodeId = data.Id;
            NodeInstance = nodeInstance;

            title = nodeInstance.DisplayName;
            tooltip = nodeInstance.Description;

            // 设置节点颜色标记
            if (CategoryColors.TryGetValue(nodeInstance.Category, out var color))
            {
                var titleContainer = this.Q("title");
                if (titleContainer != null)
                {
                    titleContainer.style.backgroundColor = new StyleColor(color);
                }
            }

            // 添加分类标签
            var categoryLabel = new Label(GetCategoryLabel(nodeInstance.Category));
            categoryLabel.AddToClassList("logic-node-category");
            categoryLabel.style.fontSize = 9;
            categoryLabel.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.8f, 0.7f));
            categoryLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            titleContainer.Add(categoryLabel);

            // 创建端口
            foreach (var portDef in nodeInstance.GetPorts())
            {
                CreatePort(portDef);
            }

            // 添加目标对象字段（如果节点需要）
            AddTargetObjectField();

            // 添加节点特定参数 UI
            AddNodeParameterFields();

            RefreshExpandedState();
            RefreshPorts();
        }

        private void CreatePort(PortDefinition portDef)
        {
            var direction = portDef.Direction == PortDirection.Input
                ? Direction.Input : Direction.Output;
            var capacity = portDef.Capacity == PortCapacity.Single
                ? Port.Capacity.Single : Port.Capacity.Multi;

            // Flow 端口使用 bool 类型，Data 端口使用实际类型
            var portType = portDef.Kind == PortKind.Flow ? typeof(bool) : (portDef.DataType ?? typeof(object));

            var port = InstantiatePort(Orientation.Horizontal, direction, capacity, portType);
            port.portName = portDef.Name;

            // Flow 端口用不同颜色
            if (portDef.Kind == PortKind.Flow)
            {
                port.portColor = new Color(0.9f, 0.9f, 0.9f);
            }
            else
            {
                port.portColor = new Color(0.4f, 0.8f, 1f);
            }

            if (direction == Direction.Input)
            {
                inputContainer.Add(port);
                _inputPorts[portDef.Name] = port;
            }
            else
            {
                outputContainer.Add(port);
                _outputPorts[portDef.Name] = port;
            }
        }

        private void AddTargetObjectField()
        {
            var container = new VisualElement();
            container.style.paddingLeft = 8;
            container.style.paddingRight = 8;
            container.style.paddingTop = 4;
            container.style.paddingBottom = 4;

            var label = new Label("目标对象路径:");
            label.style.fontSize = 10;
            label.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            container.Add(label);

            var pathField = new TextField();
            pathField.value = Data.TargetObjectPath ?? "";
            pathField.RegisterValueChangedCallback(evt =>
            {
                Data.TargetObjectPath = evt.newValue;
            });
            pathField.style.fontSize = 10;
            container.Add(pathField);

            extensionContainer.Add(container);
        }

        private void AddNodeParameterFields()
        {
            // 根据节点类型动态添加参数 UI
            var typeName = NodeInstance.GetType().Name;

            switch (typeName)
            {
                case "HighlightAction":
                    AddColorField("高亮颜色", new Color(1f, 0.8f, 0f));
                    break;
                case "SetActiveAction":
                    AddToggleField("激活", true);
                    break;
                case "PlayAnimationAction":
                    AddTextField("动画片段名", "");
                    break;
                case "FocusCameraAction":
                    AddFloatField("距离", 3f);
                    AddFloatField("高度偏移", 1f);
                    break;
                case "DelayAction":
                    AddFloatField("延迟(秒)", 1f);
                    break;
                case "DebugLogAction":
                    AddTextField("消息", "Logic 调试信息");
                    break;
            }
        }

        private void AddTextField(string label, string defaultVal)
        {
            var container = CreateParamContainer();
            var field = new TextField(label) { value = defaultVal };
            field.style.fontSize = 10;
            container.Add(field);
            extensionContainer.Add(container);
        }

        private void AddFloatField(string label, float defaultVal)
        {
            var container = CreateParamContainer();
            var field = new FloatField(label) { value = defaultVal };
            field.style.fontSize = 10;
            container.Add(field);
            extensionContainer.Add(container);
        }

        private void AddToggleField(string label, bool defaultVal)
        {
            var container = CreateParamContainer();
            var field = new Toggle(label) { value = defaultVal };
            container.Add(field);
            extensionContainer.Add(container);
        }

        private void AddColorField(string label, Color defaultVal)
        {
            var container = CreateParamContainer();
            var l = new Label(label);
            l.style.fontSize = 10;
            l.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            container.Add(l);
            // ColorField 在 GraphView 中使用受限，用文本显示代替
            var display = new Label($"R:{defaultVal.r:F1} G:{defaultVal.g:F1} B:{defaultVal.b:F1}");
            display.style.fontSize = 9;
            display.style.backgroundColor = new StyleColor(defaultVal);
            display.style.color = new StyleColor(Color.white);
            display.style.paddingLeft = 4;
            display.style.paddingRight = 4;
            container.Add(display);
            extensionContainer.Add(container);
        }

        private VisualElement CreateParamContainer()
        {
            var c = new VisualElement();
            c.style.paddingLeft = 8;
            c.style.paddingRight = 8;
            c.style.paddingBottom = 2;
            return c;
        }

        public Port GetInputPort(string name) => _inputPorts.TryGetValue(name, out var p) ? p : null;
        public Port GetOutputPort(string name) => _outputPorts.TryGetValue(name, out var p) ? p : null;

        /// <summary>
        /// 将当前视图状态导出为序列化数据。
        /// </summary>
        public LogicNodeData ToData()
        {
            var rect = GetPosition();
            return new LogicNodeData
            {
                Id = NodeId,
                TypeName = NodeInstance.GetType().Name,
                Position = new Vector2(rect.x, rect.y),
                JsonData = NodeInstance.Serialize(),
                TargetObjectPath = Data.TargetObjectPath
            };
        }

        private string GetCategoryLabel(NodeCategory cat) => cat switch
        {
            NodeCategory.Trigger => "触发",
            NodeCategory.Action => "动作",
            NodeCategory.Flow => "流程",
            NodeCategory.Data => "数据",
            _ => ""
        };
    }
}
