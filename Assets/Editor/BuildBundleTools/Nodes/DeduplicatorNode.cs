using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.Experimental.GraphView;
using UnityEngine.U2D;

namespace YY.Build.Graph.Nodes
{
    /// <summary>
    /// 依赖去重节点
    /// 功能：计算Source的依赖，排除Reserved中的资源，输出去重后的结果
    /// 
    /// 输出端口：
    /// - Combined (Unique): Source + 唯一依赖（适合打整包）
    /// - Deps Only (Unique): 仅唯一依赖，不含Source本身（适合分离打包）
    /// </summary>
    public class DeduplicatorNode : BaseBuildNode
    {
        #region Settings

        /// <summary>
        /// 是否递归计算依赖
        /// </summary>
        public bool Recursive = true;

        /// <summary>
        /// 是否包含Shader变体（通常不需要打包进Bundle）
        /// </summary>
        public bool ExcludeShaders = true;

        #endregion

        #region Constants

        /// <summary>
        /// 默认排除的扩展名（脚本、DLL、预设等不应打包的资源）
        /// </summary>
        private static readonly HashSet<string> kDefaultExcludedExtensions = new HashSet<string>
        {
            ".cs", ".dll", ".exe", ".bat", ".sh", ".meta", ".asmdef"
        };

        /// <summary>
        /// 默认排除的资源目录（Editor、Plugins等）
        /// </summary>
        private static readonly HashSet<string> kDefaultExcludedDirectories = new HashSet<string>
        {
            "Assets/Editor", "Assets/Editor Default Resources", "Assets/Plugins/Editor"
        };

        #endregion

        #region UI Elements

        private Toggle _recursiveToggle;
        private Toggle _excludeShadersToggle;

        #endregion

        #region Node Lifecycle

        public override void Initialize()
        {
            base.Initialize();
            title = "Process: Deduplicator";

            // --- 输入端口 ---
            AddInputPort("Source", Port.Capacity.Multi);
            var reservedPort = AddInputPort("Reserved (Exclude)", Port.Capacity.Multi);
            reservedPort.portColor = new Color(1f, 0.4f, 0.4f); // 红色提示排除

            // --- 输出端口 ---
            AddOutputPort("Combined (Unique)", Port.Capacity.Multi);
            AddOutputPort("Deps Only (Unique)", Port.Capacity.Multi);

            // --- UI ---
            DrawUI();
        }

        private void DrawUI()
        {
            var container = new VisualElement
            {
                style = { paddingTop = 5, paddingBottom = 5, paddingLeft = 5, paddingRight = 5 }
            };

            // 递归选项
            _recursiveToggle = new Toggle("Recursive Check") { value = Recursive };
            _recursiveToggle.RegisterValueChangedCallback(e =>
            {
                Recursive = e.newValue;
                NotifyChange();
            });

            // Shader排除选项
            _excludeShadersToggle = new Toggle("Exclude Shaders") { value = ExcludeShaders };
            _excludeShadersToggle.RegisterValueChangedCallback(e =>
            {
                ExcludeShaders = e.newValue;
                NotifyChange();
            });

            // 说明文字
            var description = new Label(
                "Calculates Source dependencies,\n" +
                "removes 'Reserved' items,\n" +
                "and splits Roots vs Deps."
            )
            {
                style = { color = Color.gray, fontSize = 10, marginTop = 5 }
            };

            container.Add(_recursiveToggle);
            container.Add(_excludeShadersToggle);
            container.Add(description);
            mainContainer.Add(container);
        }

        #endregion

        #region Execution

        public override Dictionary<string, BuildContext> Execute(BuildContext ignoredContext)
        {
            // 1. 拉取输入数据
            var sourceCtx = GetInputContext("Source");
            var reservedCtx = GetInputContext("Reserved (Exclude)");

            var combinedCtx = new BuildContext();
            var depsOnlyCtx = new BuildContext();

            // 合并日志
            combinedCtx.Logs.Append(sourceCtx.Logs);
            depsOnlyCtx.Logs.Append(sourceCtx.Logs);
            combinedCtx.Logs.AppendLine("[Deduplicator] Processing...");

            // 2. 构建源文件集合 (Root Set)
            var rootSet = new HashSet<string>();
            var rootAssetPaths = new List<string>();

            foreach (var asset in sourceCtx.Assets)
            {
                var normalizedPath = NormalizePath(asset.AssetPath);
                rootSet.Add(normalizedPath);
                rootAssetPaths.Add(normalizedPath);
            }

            combinedCtx.Logs.AppendLine($"  Source assets: {rootSet.Count}");

            // 3. 构建黑名单 (Reserved Set)
            var blackList = BuildBlackList(reservedCtx.Assets);
            combinedCtx.Logs.AppendLine($"  Reserved list size: {blackList.Count}");

            // 4. 计算依赖（带进度显示）
            var dependencies = CalculateDependencies(rootAssetPaths, combinedCtx.Logs);

            // 5. 过滤与分发
            ProcessDependencies(
                dependencies,
                rootSet,
                blackList,
                combinedCtx,
                depsOnlyCtx,
                out int addedCount,
                out int skippedCount,
                out int excludedDirCount
            );

            combinedCtx.Logs.AppendLine($"  Total Unique Assets: {addedCount}");
            combinedCtx.Logs.AppendLine($"  Skipped (Reserved): {skippedCount}");
            combinedCtx.Logs.AppendLine($"  Skipped (Editor/Plugins): {excludedDirCount}");

            return new Dictionary<string, BuildContext>
            {
                { "Combined (Unique)", combinedCtx },
                { "Deps Only (Unique)", depsOnlyCtx }
            };
        }

        /// <summary>
        /// 构建黑名单（排除列表）
        /// </summary>
        private HashSet<string> BuildBlackList(List<AssetBuildInfo> reservedAssets)
        {
            var blackList = new HashSet<string>();

            foreach (var asset in reservedAssets)
            {
                var normalizedPath = NormalizePath(asset.AssetPath);
                blackList.Add(normalizedPath);

                // 智能扩展：SpriteAtlas v2 自动包含子图
                if (normalizedPath.EndsWith(".spriteatlasv2"))
                {
                    var packedSprites = GetSpritesInAtlas(normalizedPath);
                    foreach (var spritePath in packedSprites)
                    {
                        blackList.Add(spritePath);
                    }
                }
            }

            return blackList;
        }

        /// <summary>
        /// 计算依赖关系
        /// </summary>
        private HashSet<string> CalculateDependencies(List<string> rootPaths, System.Text.StringBuilder logs)
        {
            if (rootPaths.Count == 0)
            {
                return new HashSet<string>();
            }

            logs.AppendLine($"  Calculating dependencies (Recursive={Recursive})...");

            // AssetDatabase.GetDependencies 是同步阻塞调用
            // 对于大型项目，这里可能需要优化（缓存、异步等）
            var dependencies = AssetDatabase.GetDependencies(rootPaths.ToArray(), Recursive);

            return new HashSet<string>(dependencies.Select(NormalizePath));
        }

        /// <summary>
        /// 处理依赖：过滤与分发到输出端口
        /// </summary>
        private void ProcessDependencies(
            HashSet<string> dependencies,
            HashSet<string> rootSet,
            HashSet<string> blackList,
            BuildContext combinedCtx,
            BuildContext depsOnlyCtx,
            out int addedCount,
            out int skippedCount,
            out int excludedDirCount)
        {
            addedCount = 0;
            skippedCount = 0;
            excludedDirCount = 0;

            // 用于去重输出
            var processedOutputs = new HashSet<string>();

            foreach (var path in dependencies)
            {
                // A. 基础过滤：脚本、DLL等
                if (IsExcludedByExtension(path))
                {
                    continue;
                }

                // B. 目录过滤：Editor、Plugins等
                if (IsInExcludedDirectory(path))
                {
                    excludedDirCount++;
                    continue;
                }

                // C. Shader过滤
                if (ExcludeShaders && path.EndsWith(".shader"))
                {
                    continue;
                }

                // D. 防止重复处理
                if (processedOutputs.Contains(path))
                {
                    continue;
                }
                processedOutputs.Add(path);

                // E. 黑名单检查
                if (blackList.Contains(path))
                {
                    skippedCount++;
                    continue;
                }

                // F. 分发到输出端口
                var newAsset = new AssetBuildInfo(path);

                // Combined: Source + 依赖
                combinedCtx.Assets.Add(newAsset);

                // Deps Only: 仅依赖（不含Source本身）
                if (!rootSet.Contains(path))
                {
                    depsOnlyCtx.Assets.Add(newAsset);
                }

                addedCount++;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// 标准化路径（统一使用正斜杠）
        /// </summary>
        private static string NormalizePath(string path)
        {
            return path.Replace("\\", "/");
        }

        /// <summary>
        /// 检查是否为默认排除的扩展名
        /// </summary>
        private static bool IsExcludedByExtension(string path)
        {
            var ext = Path.GetExtension(path).ToLower();
            return kDefaultExcludedExtensions.Contains(ext);
        }

        /// <summary>
        /// 检查是否在排除的目录中
        /// </summary>
        private static bool IsInExcludedDirectory(string path)
        {
            foreach (var dir in kDefaultExcludedDirectories)
            {
                if (path.StartsWith(dir + "/") || path.StartsWith(dir + "\\"))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 获取SpriteAtlas中包含的Sprite路径
        /// </summary>
        private static List<string> GetSpritesInAtlas(string atlasPath)
        {
            var results = new List<string>();

            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            if (atlas == null)
            {
                return results;
            }

            // 使用SerializedObject获取packables
            using (var so = new SerializedObject(atlas))
            {
                var packables = so.FindProperty("m_EditorData.packables");
                if (packables == null)
                {
                    return results;
                }

                for (int i = 0; i < packables.arraySize; i++)
                {
                    var element = packables.GetArrayElementAtIndex(i);
                    var obj = element.objectReferenceValue;

                    if (obj == null)
                    {
                        continue;
                    }

                    var objPath = AssetDatabase.GetAssetPath(obj);

                    if (Directory.Exists(objPath))
                    {
                        // 目录：遍历所有文件
                        var files = Directory.GetFiles(objPath, "*.*", SearchOption.AllDirectories);
                        foreach (var file in files)
                        {
                            if (!file.EndsWith(".meta"))
                            {
                                results.Add(NormalizePath(file));
                            }
                        }
                    }
                    else
                    {
                        results.Add(NormalizePath(objPath));
                    }
                }
            }

            return results;
        }

        #endregion

        #region Serialization

        [System.Serializable]
        private class NodeData
        {
            public bool recursive;
            public bool excludeShaders;
        }

        public override string SaveToJSON()
        {
            return JsonUtility.ToJson(new NodeData
            {
                recursive = Recursive,
                excludeShaders = ExcludeShaders
            });
        }

        public override void LoadFromJSON(string json)
        {
            var data = JsonUtility.FromJson<NodeData>(json);
            if (data == null)
            {
                return;
            }

            Recursive = data.recursive;
            ExcludeShaders = data.excludeShaders;

            if (_recursiveToggle != null)
            {
                _recursiveToggle.value = Recursive;
            }
            if (_excludeShadersToggle != null)
            {
                _excludeShadersToggle.value = ExcludeShaders;
            }
        }

        #endregion
    }
}