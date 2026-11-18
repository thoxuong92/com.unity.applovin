using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Unity.AppLovin.Editor
{
    /// <summary>
    /// Info.json (Resources/Info_Android.json, Info_iOS.json hoặc Info.json) -> cài đặt của AppLovin MAX:
    ///   - trước mỗi bản build: SDK Key (Quality Service cần nó) và AdMob App ID (adapter AdMob cần nó, thiếu thì app
    ///     dừng ngay khi mở) ghi vào AppLovinSettings của MAX;
    ///   - menu "Install Networks From Info.json": cài adapter của các mạng trong "mediationNetworks".
    /// </summary>
    internal class AppLovinInfoSync : IPreprocessBuildWithReport
    {
        // trước các bước của MAX (chúng đọc AppLovinSettings)
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            var target = report.summary.platform;
            if (target != BuildTarget.Android && target != BuildTarget.iOS) return;
            Apply(target);
        }

        static string Suffix(BuildTarget t) { return t == BuildTarget.iOS ? "_iOS" : "_Android"; }
        static string Platform(BuildTarget t) { return t == BuildTarget.iOS ? "ios" : "android"; }

        /// <summary>chữ của Info của một nền tảng (Info_Android / Info_iOS, không có thì Info); null khi không có</summary>
        static string InfoText(BuildTarget t)
        {
            var data = Resources.Load<TextAsset>("Info" + Suffix(t)) ?? Resources.Load<TextAsset>("Info");
            return data ? data.text : null;
        }

        /// <summary>ghi SDK Key và AdMob App ID của Info vào AppLovinSettings; false khi chưa cài MAX SDK</summary>
        public static bool Apply(BuildTarget target)
        {
            var config = AppLovinConfig.LoadFromResources(Suffix(target));
            var settingsType = FindType("AppLovinSettings");
            var instance = settingsType?.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
            if (instance == null)
            {
                Debug.Log("[AppLovinInfoSync] AppLovin MAX SDK chưa được cài: bỏ qua AppLovinSettings.");
                return false;
            }

            bool changed = false;
            changed |= Set(instance, "SdkKey", config.SdkKey);
            string admobProp = target == BuildTarget.iOS ? "AdMobIosAppId" : "AdMobAndroidAppId";
            changed |= Set(instance, admobProp, config.AdMobAppId);
            if (changed)
            {
                settingsType.GetMethod("SaveAsync")?.Invoke(instance, null);
                AssetDatabase.SaveAssets();
                Debug.Log("[AppLovinInfoSync] Đã ghi SDK Key" + (string.IsNullOrEmpty(config.AdMobAppId) ? "" : " và AdMob App ID") + " từ Info vào AppLovinSettings.");
            }

            string admobId = settingsType.GetProperty(admobProp)?.GetValue(instance) as string;
            if (string.IsNullOrEmpty(admobId) && IsInstalled(MaxMediation.PackageName(AdMediation.AdMob, Platform(target))))
                Debug.LogError("[AppLovinInfoSync] Đã cài adapter AdMob nhưng chưa có AdMob App ID (\"admobAppId\" trong Info.json): app sẽ dừng ngay khi mở.");
            return true;
        }

        static bool Set(object instance, string prop, string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            var p = instance.GetType().GetProperty(prop);
            if (p == null || (p.GetValue(instance) as string) == value) return false;
            p.SetValue(instance, value);
            return true;
        }

        static Type FindType(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
        }

        static bool IsInstalled(string package)
        {
            if (string.IsNullOrEmpty(package)) return false;
            return UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Any(p => p.name == package);
        }

        // ---------------------------------------------------------------- mạng quảng cáo

        /// <summary>các mạng trong "mediationNetworks" của Info (theo "id"); mã lạ ghi vào danh sách thứ hai</summary>
        public static List<AdMediation> InfoNetworks(string text, List<string> unknown = null)
        {
            var list = new List<AdMediation>();
            if (string.IsNullOrEmpty(text)) return list;
            int at = text.IndexOf("\"mediationNetworks\"", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return list;
            int open = text.IndexOf('[', at), close = open < 0 ? -1 : text.IndexOf(']', open);
            if (open < 0 || close < 0) return list;
            foreach (Match m in Regex.Matches(text.Substring(open, close - open), "\"id\"\\s*:\\s*\"([^\"]+)\""))
            {
                string id = m.Groups[1].Value;
                if (MaxMediation.InfoIds.TryGetValue(id, out var n)) { if (!list.Contains(n)) list.Add(n); }
                else unknown?.Add(id);
            }
            return list;
        }

        [MenuItem("Unity Core/AppLovin MAX/Install Networks From Info.json", false, 15)]
        static void InstallNetworksMenu()
        {
            var target = EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS ? BuildTarget.iOS : BuildTarget.Android;
            InstallNetworks(target);
        }

        /// <summary>
        /// cài adapter (bản mới nhất trong registry của AppLovin) của mọi mạng trong Info cho một nền tảng.
        /// Dòng lệnh: -executeMethod Unity.AppLovin.Editor.AppLovinInfoSync.InstallNetworksAndroid
        /// </summary>
        public static void InstallNetworks(BuildTarget target)
        {
            var unknown = new List<string>();
            var networks = InfoNetworks(InfoText(target), unknown);
            foreach (var id in unknown) Debug.LogWarning("[AppLovinInfoSync] Không biết gói của mạng \"" + id + "\".");
            var add = networks.Select(n => MaxMediation.PackageName(n, Platform(target)))
                .Where(p => p != null && !IsInstalled(p)).ToArray();
            if (add.Length == 0)
            {
                Debug.Log("[AppLovinInfoSync] Các mạng trong Info đã được cài đủ (" + networks.Count + ").");
                return;
            }
            AppLovinUpmManifest.EnsureAppLovinScopedRegistries();
            Debug.Log("[AppLovinInfoSync] Cài adapter: " + string.Join(", ", add));
            var req = Client.AddAndRemove(add, null);
            while (!req.IsCompleted) System.Threading.Thread.Sleep(100);
            if (req.Status == StatusCode.Success) Debug.Log("[AppLovinInfoSync] Đã cài " + add.Length + " adapter.");
            else Debug.LogError("[AppLovinInfoSync] Không cài được adapter: " + req.Error?.message);
        }

        public static void InstallNetworksAndroid() { InstallNetworks(BuildTarget.Android); }
        public static void InstallNetworksIos() { InstallNetworks(BuildTarget.iOS); }
    }
}
