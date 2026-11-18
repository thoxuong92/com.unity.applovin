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
        public string MrecId = "";
        public BannerPosition BannerPosition = BannerPosition.BottomCenter;
        public BannerColor BannerColor = BannerColor.NoColor;

        /// <summary>AdMob App ID ("ca-app-pub-...~..."): bắt buộc khi có adapter AdMob, Editor ghi nó vào AppLovinSettings khi build</summary>
        public string AdMobAppId = "";

        /// <summary>
        /// Info_Android.json / Info_iOS.json / Info.json trong Resources, ở một trong hai dạng:
        ///   - của cửa sổ Setup: { "SDKMax", "AppOpen", "Banner", "Interstitial", "Rewarded", "MREC", "BannerPosition" };
        ///   - của trang quản lý app: { "data": { "max": { "sdkKey", "admobAppId", "aoa", "inter", "banner", "rewarded", "mrec" } } }.
        /// </summary>
        public static AppLovinConfig LoadFromResources()
        {
            string platformSuffix = Application.platform == RuntimePlatform.Android ? "_Android" : (Application.platform == RuntimePlatform.IPhonePlayer ? "_iOS" : "");
            return LoadFromResources(platformSuffix);
        }

        /// <summary>cấu hình của một nền tảng ("_Android", "_iOS" hoặc ""): Editor dùng khi build cho nền tảng đó</summary>
        public static AppLovinConfig LoadFromResources(string platformSuffix)
        {
            try
            {
                TextAsset data = Resources.Load<TextAsset>($"Info{platformSuffix}") ?? Resources.Load<TextAsset>("Info");
                if (data != null) return Parse(data.text);
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"[AppLovinConfig] Lỗi nạp cấu hình từ Resources: {ex.Message}");
            }
            return new AppLovinConfig();
        }

        public static AppLovinConfig Parse(string text)
        {
            var config = new AppLovinConfig();
            if (string.IsNullOrEmpty(text)) return config;
            config.SdkKey = ExtractJsonValue(text, "SDKMax", "SdkKey");
            config.AppOpenId = ExtractJsonValue(text, "AppOpen", "AppOpenId", "aoa");
            config.BannerId = ExtractJsonValue(text, "Banner", "BannerId");
            config.InterstitialId = ExtractJsonValue(text, "Interstitial", "InterstitialId", "inter");
            config.RewardedId = ExtractJsonValue(text, "Rewarded", "RewardedId");
            config.MrecId = ExtractJsonValue(text, "MREC", "MRec", "MrecId");
            config.AdMobAppId = ExtractJsonValue(text, "AdMobAppId", "AdMobApplicationId");

            string posStr = ExtractJsonValue(text, "BannerPosition");
            if (!string.IsNullOrEmpty(posStr) && Enum.TryParse<BannerPosition>(posStr, true, out var pos))
            {
                config.BannerPosition = pos;
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
