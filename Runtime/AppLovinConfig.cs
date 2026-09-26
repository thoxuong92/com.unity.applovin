using System;
using UnityEngine;
using Unity.Core.Logging;

namespace Unity.AppLovin
{
    public enum BannerPosition
    {
        TopLeft = 0,
        TopCenter = 1,
        TopRight = 2,
        Centered = 3,
        BottomLeft = 4,
        BottomCenter = 5,
        BottomRight = 6
    }

    public enum BannerColor
    {
        NoColor = 0,
        Black = 1,
        White = 2
    }

    [Serializable]
    public class AppLovinConfig
    {
        public string SdkKey = "";
        public string AppOpenId = "";
        public string BannerId = "";
        public string InterstitialId = "";
        public string RewardedId = "";
        public BannerPosition BannerPosition = BannerPosition.BottomCenter;
        public BannerColor BannerColor = BannerColor.NoColor;

        public static AppLovinConfig LoadFromResources()
        {
            var config = new AppLovinConfig();
            try
            {
                string platformSuffix = Application.platform == RuntimePlatform.Android ? "_Android" : (Application.platform == RuntimePlatform.IPhonePlayer ? "_iOS" : "");
                TextAsset data = Resources.Load<TextAsset>($"Info{platformSuffix}") ?? Resources.Load<TextAsset>("Info");

                if (data != null && !string.IsNullOrEmpty(data.text))
                {
                    string text = data.text;
                    config.SdkKey = ExtractJsonValue(text, "SDKMax", "SdkKey");
                    config.AppOpenId = ExtractJsonValue(text, "AppOpen", "AppOpenId");
                    config.BannerId = ExtractJsonValue(text, "Banner", "BannerId");
                    config.InterstitialId = ExtractJsonValue(text, "Interstitial", "InterstitialId");
                    config.RewardedId = ExtractJsonValue(text, "Rewarded", "RewardedId");

                    string posStr = ExtractJsonValue(text, "BannerPosition");
                    if (!string.IsNullOrEmpty(posStr) && Enum.TryParse<BannerPosition>(posStr, true, out var pos))
                    {
                        config.BannerPosition = pos;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"[AppLovinConfig] Lỗi nạp cấu hình từ Resources: {ex.Message}");
            }
            return config;
        }

        private static string ExtractJsonValue(string json, params string[] keys)
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
