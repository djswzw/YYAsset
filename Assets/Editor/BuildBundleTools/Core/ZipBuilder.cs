using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace YY.Build.Core
{
    public static class ZipBuilder
    {
        public static bool CreateZip(string outputDir, string zipFilename, List<AssetBuildInfo> assets, string password = "")
        {
            if (assets == null || assets.Count == 0)
            {
                Debug.LogError("[ZipBuilder] No assets to zip.");
                return false;
            }

            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            string fullZipPath = Path.Combine(outputDir, zipFilename);

            // 如果文件存在，先删除
            if (File.Exists(fullZipPath)) File.Delete(fullZipPath);

            try
            {
                // 1. 创建 Zip 文件
                using (FileStream zipToOpen = new FileStream(fullZipPath, FileMode.Create))
                using (ZipArchive archive = new ZipArchive(zipToOpen, ZipArchiveMode.Create))
                {
                    foreach (var asset in assets)
                    {
                        // 2. 确定源文件路径
                        // asset.AssetPath 是 "Assets/..." 格式
                        // 我们需要转为系统绝对路径来读取文件
                        string sourceFilePath = Path.GetFullPath(asset.AssetPath); // Unity 工程根目录下的绝对路径

                        if (!File.Exists(sourceFilePath))
                        {
                            Debug.LogWarning($"[ZipBuilder] File not found: {sourceFilePath}");
                            continue;
                        }

                        // 3. 确定 Zip 内的相对路径 (Entry Name)
                        // 通常我们希望去掉 "Assets/" 前缀，保持目录结构
                        // 例如: "Assets/Lua/Main.lua" -> "Lua/Main.lua"
                        string entryName = asset.AssetPath;
                        if (entryName.StartsWith("Assets/")) entryName = entryName.Substring(7);

                        // 4. 写入 Zip Entry
                        ZipArchiveEntry entry = archive.CreateEntry(entryName, System.IO.Compression.CompressionLevel.Optimal);
                        using (Stream writer = entry.Open())
                        using (FileStream reader = File.OpenRead(sourceFilePath))
                        {
                            reader.CopyTo(writer);
                        }
                    }
                }

                // 5. (可选) 加密处理
                // 如果需要加密，通常是在 Zip 生成后，对整个文件进行 AES 加密或异或混淆
                if (!string.IsNullOrEmpty(password))
                {
                    EncryptFile(fullZipPath, password);
                    Debug.Log($"[ZipBuilder] Zip created and Encrypted: {fullZipPath}");
                }
                else
                {
                    Debug.Log($"[ZipBuilder] Zip created: {fullZipPath}");
                }

                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ZipBuilder] Failed: {ex.Message}");
                // 失败时清理残缺文件
                if (File.Exists(fullZipPath)) File.Delete(fullZipPath);
                return false;
            }
        }

        // 一个极其简单的异或加密示例 (实际项目建议用 AES)
        private static void EncryptFile(string path, string key)
        {
            byte[] fileBytes = File.ReadAllBytes(path);
            byte[] keyBytes = System.Text.Encoding.UTF8.GetBytes(key);

            for (int i = 0; i < fileBytes.Length; i++)
            {
                fileBytes[i] ^= keyBytes[i % keyBytes.Length];
            }

            File.WriteAllBytes(path, fileBytes);
        }
    }
}