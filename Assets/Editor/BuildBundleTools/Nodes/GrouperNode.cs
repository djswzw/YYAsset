using UnityEngine;
using UnityEngine.UIElements;
using System.IO;
using System.Collections.Generic;

namespace YY.Build.Graph.Nodes
{
    public class GrouperNode : BaseBuildNode
    {
        public enum GroupingMode { OneBundle, ByFolder, ByFile, TopDirectory }

        public GroupingMode Mode = GroupingMode.OneBundle;
        public string MainKey = "assets"; // 在 OneBundle模式下是包名，在其他模式下可能是前缀
        public string Suffix = ".bundle"; // 后缀可配置

        private EnumField _modeField;
        private TextField _mainKeyField;
        private TextField _suffixField;

        public override void Initialize()
        {
            base.Initialize();
            title = "Strategy: Grouper";
            AddInputPort("Input");
            AddOutputPort("Output", UnityEditor.Experimental.GraphView.Port.Capacity.Multi);

            _modeField = new EnumField("Mode:", Mode);
            _modeField.RegisterValueChangedCallback(e =>
            {
                Mode = (GroupingMode)e.newValue;
                UpdateUIState();
                NotifyChange();
            });

            _mainKeyField = new TextField("Name/Prefix:");
            _mainKeyField.value = MainKey;
            _mainKeyField.RegisterValueChangedCallback(e => { MainKey = e.newValue; NotifyChange(); });

            _suffixField = new TextField("Suffix:");
            _suffixField.value = Suffix;
            _suffixField.RegisterValueChangedCallback(e => { Suffix = e.newValue; NotifyChange(); });

            mainContainer.Add(_modeField);
            mainContainer.Add(_mainKeyField);
            mainContainer.Add(_suffixField);

            UpdateUIState();
        }

        private void UpdateUIState()
        {
            if (Mode == GroupingMode.OneBundle) _mainKeyField.label = "Bundle Name:";
            else _mainKeyField.label = "Prefix (Optional):";
        }

        // --- 执行逻辑 ---
        public override Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            context.Logs.AppendLine($"[GrouperNode] Mode: {Mode}");

            foreach (var asset in context.Assets)
            {
                string bundleName = "";

                switch (Mode)
                {
                    case GroupingMode.OneBundle:
                        bundleName = MainKey;
                        break;

                    case GroupingMode.ByFile:
                        // Prefix + Filename
                        bundleName = MainKey + Path.GetFileNameWithoutExtension(asset.AssetPath).ToLower();
                        break;

                    case GroupingMode.ByFolder:
                        // 逻辑：Assets/Res/UI/Login/bg.png -> "res_ui_login"
                        // 移除 Assets/ 前缀
                        string dir = Path.GetDirectoryName(asset.AssetPath).Replace("\\", "/");
                        if (dir.StartsWith("Assets/")) dir = dir.Substring(7);
                        // 获取最后一级目录
                        int lastSlashIndex = dir.LastIndexOf('/');
                        if (lastSlashIndex >= 0)
                        {
                            dir = dir.Substring(lastSlashIndex + 1); // 提取最后一级目录
                        }
                        // 替换斜杠为下划线
                        bundleName = MainKey + dir.Replace("/", "_").ToLower();
                        break;

                    case GroupingMode.TopDirectory:
                        // 逻辑：Assets/Res/UI/Login/bg.png -> "res" (第一层目录)
                        string relative = asset.AssetPath.Replace("\\", "/");
                        if (relative.StartsWith("Assets/")) relative = relative.Substring(7);

                        int slashIdx = relative.IndexOf('/');
                        if (slashIdx > 0) bundleName = MainKey + relative.Substring(0, slashIdx).ToLower();
                        else bundleName = MainKey + "root";
                        break;
                }

                // 添加后缀
                if (!string.IsNullOrEmpty(Suffix) && !bundleName.EndsWith(Suffix))
                {
                    bundleName += Suffix;
                }

                asset.BundleName = bundleName;
            }

            context.Logs.AppendLine($"  Processed {context.Assets.Count} assets.");
            return base.Execute(context);
        }

        // --- 序列化 ---
        [System.Serializable]
        class NodeData { public GroupingMode mode; public string key; public string suffix; }

        public override string SaveToJSON()
        {
            return JsonUtility.ToJson(new NodeData { mode = Mode, key = MainKey, suffix = Suffix });
        }

        public override void LoadFromJSON(string json)
        {
            var data = JsonUtility.FromJson<NodeData>(json);
            if (data != null)
            {
                Mode = data.mode;
                MainKey = data.key;
                Suffix = data.suffix;

                if (_modeField != null) _modeField.value = Mode;
                if (_mainKeyField != null) _mainKeyField.value = MainKey;
                if (_suffixField != null) _suffixField.value = Suffix;
                UpdateUIState();
            }
        }
    }
}