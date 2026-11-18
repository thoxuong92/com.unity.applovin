using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

namespace Unity.AppLovin.Editor
{
    /// <summary>
    /// Đưa link.xml của gói (com.unity.applovin/link.xml) vào bước cắt code của IL2CPP. Unity chỉ tự đọc link.xml nằm trong
    /// Assets, nên không có lớp này thì một assembly không được game gọi trực tiếp (adapter tự đăng ký bằng
    /// RuntimeInitializeOnLoadMethod) bị bỏ hẳn khỏi bản build Android / iOS.
    /// </summary>
    internal class PackageLinkXml : IUnityLinkerProcessor
    {
        public int callbackOrder => 0;

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PackageLinkXml).Assembly);
            string path = info != null ? Path.Combine(info.resolvedPath, "link.xml") : null;
            return path != null && File.Exists(path) ? path : null;
        }
    }
}
