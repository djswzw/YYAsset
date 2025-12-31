using System;

namespace YY
{
    public class BundleLoadException : Exception
    {
        public string BundleName { get; }

        public BundleLoadException(string bundleName, string message)
            : base($"[Bundle: {bundleName}] {message}")
        {
            BundleName = bundleName;
        }

        public BundleLoadException(string bundleName, string message, Exception innerException)
            : base($"[Bundle: {bundleName}] {message}", innerException)
        {
            BundleName = bundleName;
        }
    }
}