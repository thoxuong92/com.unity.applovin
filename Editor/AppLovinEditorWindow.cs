using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Unity.AppLovin.Editor
{
    /// <summary>
    /// Cửa sổ cấu hình AppLovin MAX Mediation trong Unity Editor:
    /// - Quản lý cài đặt Scoped Registries
    /// - Sao chép tài nguyên AAR native cho Android
    /// - Soạn thảo và xuất file cấu hình Ad Units (Assets/Resources/Info.json)
    /// </summary>
    public class AppLovinEditorWindow : EditorWindow
    {
        private string _sdkKey = "";
        private string _appOpenId = "";
        private string _bannerId = "";
        private string _interstitialId = "";
        private string _rewardedId = "";
        private BannerPosition _bannerPos = BannerPosition.BottomCenter;
        private string _targetExportPath = "Assets/Resources/Info.json";
        private Vector2 _scrollPos;

        [MenuItem("Unity Core/AppLovin MAX/Integration & Mediation Setup", false, 14)]
        public static void ShowWindow()
        {
            var window = GetWindow<AppLovinEditorWindow>("AppLovin MAX Setup");
            window.minSize = new Vector2(520, 560);
            window.Show();
        }

        private void OnEnable()
        {
            LoadCurrentSettings();
        }

        private void LoadCurrentSettings()
        {
            try
            {
                string fullPath = Path.Combine(Application.dataPath, "..", _targetExportPath);
                if (File.Exists(fullPath))
                {
                    string json = File.ReadAllText(fullPath);
                    _sdkKey = ExtractValue(json, "SDKMax", "SdkKey");
                    _appOpenId = ExtractValue(json, "AppOpen", "AppOpenId");
                    _bannerId = ExtractValue(json, "Banner", "BannerId");
                    _interstitialId = ExtractValue(json, "Interstitial", "InterstitialId");
                    _rewardedId = ExtractValue(json, "Rewarded", "RewardedId");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AppLovinEditor] Lỗi đọc file config: {ex.Message}");
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            GUILayout.Label("Unity AppLovin MAX Mediation Setup", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Quản lý cấu hình Ad Unit IDs, cài đặt Scoped Registry UPM và Plugin Native cho dự án.", MessageType.Info);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            // 1. Cấu hình Scoped Registries & Native Plugins
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("1. Cài Đặt SDK & Native Dependencies", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Cấu hình registry UPM để Unity tự động tải gói AppLovin MAX Mediation và External Dependency Manager.", MessageType.None);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Thêm Scoped Registries", GUILayout.Height(30)))
            {
                AppLovinUpmManifest.EnsureAppLovinScopedRegistries();
                EditorUtility.DisplayDialog("Thành công", "Đã thêm AppLovin & OpenUPM Scoped Registries vào Packages/manifest.json!", "OK");
            }

            if (GUILayout.Button("Copy Android AAR Plugins", GUILayout.Height(30)))
            {
                CopyAndroidAar();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            // 2. Cấu hình Ad Unit IDs
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("2. Cấu Hình Ad Unit IDs & SDK Key", EditorStyles.boldLabel);

            _sdkKey = EditorGUILayout.TextField("AppLovin SDK Key:", _sdkKey);
            _appOpenId = EditorGUILayout.TextField("App Open Ad Unit ID:", _appOpenId);
            _bannerId = EditorGUILayout.TextField("Banner Ad Unit ID:", _bannerId);
            _interstitialId = EditorGUILayout.TextField("Interstitial Ad Unit ID:", _interstitialId);
            _rewardedId = EditorGUILayout.TextField("Rewarded Ad Unit ID:", _rewardedId);
            _bannerPos = (BannerPosition)EditorGUILayout.EnumPopup("Vị trí Banner mặc định:", _bannerPos);

            EditorGUILayout.Space(4);
            _targetExportPath = EditorGUILayout.TextField("Export File:", _targetExportPath);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Info.json (Mặc định)")) _targetExportPath = "Assets/Resources/Info.json";
            if (GUILayout.Button("Info_Android.json")) _targetExportPath = "Assets/Resources/Info_Android.json";
            if (GUILayout.Button("Info_iOS.json")) _targetExportPath = "Assets/Resources/Info_iOS.json";
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);
            if (GUILayout.Button("💾 Lưu Cấu Hình Vào Resources", GUILayout.Height(38)))
            {
                SaveConfigToResources();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndScrollView();
        }

        private void CopyAndroidAar()
        {
            try
            {
                string targetDir = Path.Combine(Application.dataPath, "Plugins", "Android");
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                string srcFile = Path.GetFullPath("Packages/com.unity.applovin/LibAar~/ads_resource.aar"); if (!File.Exists(srcFile)) srcFile = Path.Combine(Application.dataPath, "wasd", "Packages", "com.wasd.applovin", "LibAar~", "ads_resource.aar");
                string dstFile = Path.Combine(targetDir, "ads_resource.aar");

                if (File.Exists(srcFile))
                {
                    File.Copy(srcFile, dstFile, true);
                    AssetDatabase.Refresh();
                    EditorUtility.DisplayDialog("Thành công", "Đã sao chép ads_resource.aar vào Assets/Plugins/Android/!", "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Thông báo", "Không tìm thấy file nguồn ads_resource.aar trong package.", "OK");
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Lỗi", $"Lỗi sao chép AAR: {ex.Message}", "OK");
            }
        }

        private void SaveConfigToResources()
        {
            try
            {
                string fullPath = Path.Combine(Application.dataPath, "..", _targetExportPath);
                string dir = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var sb = new StringBuilder("{\n");
                sb.AppendLine($"  \"SDKMax\": \"{_sdkKey}\",");
                sb.AppendLine($"  \"AppOpen\": \"{_appOpenId}\",");
                sb.AppendLine($"  \"Banner\": \"{_bannerId}\",");
                sb.AppendLine($"  \"Interstitial\": \"{_interstitialId}\",");
                sb.AppendLine($"  \"Rewarded\": \"{_rewardedId}\",");
                sb.AppendLine($"  \"BannerPosition\": \"{_bannerPos}\"");
                sb.AppendLine("}");

                File.WriteAllText(fullPath, sb.ToString());
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Thành công", $"Đã lưu cấu hình vào: {_targetExportPath}", "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Lỗi", $"Lỗi ghi file cấu hình: {ex.Message}", "OK");
            }
        }

        private string ExtractValue(string json, params string[] keys)
        {
            if (string.IsNullOrEmpty(json)) return "";
            foreach (var key in keys)
            {
                string searchKey = $"\"{key}\"";
                int idx = json.IndexOf(searchKey, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    int colon = json.IndexOf(':', idx + searchKey.Length);
                    if (colon >= 0)
                    {
                        int q1 = json.IndexOf('"', colon + 1);
                        if (q1 >= 0)
                        {
                            int q2 = json.IndexOf('"', q1 + 1);
                            if (q2 > q1)
                            {
                                return json.Substring(q1 + 1, q2 - q1 - 1);
                            }
                        }
                    }
                }
            }
            return "";
        }
    }
}
