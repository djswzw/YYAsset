using System.IO;
using UnityEngine;

public static class FileUtils
{
    public static string NativePath { get; private set; }
    public static string PersistentPath { get; private set; }

    public static void CreateDirectoryForFile(string filePath)
    {
        string path = Path.GetDirectoryName(filePath);
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    public static void CreateDirectory(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    public static void CreateAssetsPath()
    {
        if (!Directory.Exists(PersistentPath))
        {
            Directory.CreateDirectory(PersistentPath);
        }
    }

    public static void ResetPath()
    {
        NativePath = string.Format("{0}{1}", Application.streamingAssetsPath, "/assets/");
#if UNITY_EDITOR
        PersistentPath = Application.dataPath.Replace("/Assets", "/PersistentRes/");
        NativePath = string.Format("{0}/", Application.dataPath.Replace("/Assets", "/StreamingRes/") + GetLoadUrl());
#else
#if UNITY_STANDALONE_WIN
        PersistentPath = Application.streamingAssetsPath.Replace("/StreamingAssets", "/PersistentRes/");
#elif UNITY_ANDROID
        PersistentPath = string.Format("{0}{1}", Application.persistentDataPath, "/assets/");
#else
        PersistentPath = string.Format("{0}{1}", Application.persistentDataPath, "/assets/");
#endif
        
#endif
    }

#if UNITY_EDITOR
    static string GetLoadUrl()
    {
        return UnityEditor.EditorPrefs.GetString("bundleurl");
    }
#endif
}