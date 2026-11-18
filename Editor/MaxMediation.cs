using System;
using System.Collections.Generic;

namespace Unity.AppLovin.Editor
{
    public enum AdMediation
    {
        AdMob,
        GoogleBidding,
        Mintegral,
        UnityAds,
        IronSource,
        Pangle,
        Liftoff_Vungle,
        InMobi,
        Chartboost,
        Facebook,
        BidMachine,
        Fyber,
        LINE,
        Maio,
        MobileFuse,
        Moloco,
        Ogury,
        Smaato,
        Tencent,
        Verve,
        VKAdNetwork,
        Yandex,
        YSONetwork
    }

    public static class MaxMediation
    {
        /// <summary>
        /// Tên adapter trong registry của AppLovin (https://unity.packages.applovin.com/), không có đuôi nền tảng:
        /// gói thật là "&lt;tên&gt;.android" và "&lt;tên&gt;.ios" (xem <see cref="PackageName"/>).
        /// Tencent và VK Ad Network không có gói UPM.
        /// </summary>
        public static readonly Dictionary<AdMediation, string> NetworkPackages = new Dictionary<AdMediation, string>()
        {
            { AdMediation.AdMob, "com.applovin.mediation.adapters.google" },
            { AdMediation.GoogleBidding, "com.applovin.mediation.adapters.googleadmanager" },
            { AdMediation.Mintegral, "com.applovin.mediation.adapters.mintegral" },
            { AdMediation.UnityAds, "com.applovin.mediation.adapters.unityads" },
            { AdMediation.IronSource, "com.applovin.mediation.adapters.ironsource" },
            { AdMediation.Pangle, "com.applovin.mediation.adapters.bytedance" },
            { AdMediation.Liftoff_Vungle, "com.applovin.mediation.adapters.vungle" },
            { AdMediation.InMobi, "com.applovin.mediation.adapters.inmobi" },
            { AdMediation.Chartboost, "com.applovin.mediation.adapters.chartboost" },
            { AdMediation.Facebook, "com.applovin.mediation.adapters.facebook" },
            { AdMediation.BidMachine, "com.applovin.mediation.adapters.bidmachine" },
            { AdMediation.Fyber, "com.applovin.mediation.adapters.fyber" },
            { AdMediation.LINE, "com.applovin.mediation.adapters.line" },
            { AdMediation.Maio, "com.applovin.mediation.adapters.maio" },
            { AdMediation.MobileFuse, "com.applovin.mediation.adapters.mobilefuse" },
            { AdMediation.Moloco, "com.applovin.mediation.adapters.moloco" },
            { AdMediation.Ogury, "com.applovin.mediation.adapters.ogurypresage" },
            { AdMediation.Smaato, "com.applovin.mediation.adapters.smaato" },
            { AdMediation.Verve, "com.applovin.mediation.adapters.verve" },
            { AdMediation.Yandex, "com.applovin.mediation.adapters.yandex" },
            { AdMediation.YSONetwork, "com.applovin.mediation.adapters.ysonetwork" }
        };

        /// <summary>
        /// Mã mạng trong Info.json của trang quản lý ("mediationNetworks": [{ "id": "admob" }, ...]) -> mạng.
        /// </summary>
        public static readonly Dictionary<string, AdMediation> InfoIds = new Dictionary<string, AdMediation>(StringComparer.OrdinalIgnoreCase)
        {
            { "admob", AdMediation.AdMob },
            { "google", AdMediation.AdMob },
            { "google_ad_manager", AdMediation.GoogleBidding },
            { "google_bidding", AdMediation.GoogleBidding },
            { "unity", AdMediation.UnityAds },
            { "unityads", AdMediation.UnityAds },
            { "ironsource", AdMediation.IronSource },
            { "vungle", AdMediation.Liftoff_Vungle },
            { "liftoff", AdMediation.Liftoff_Vungle },
            { "mintegral", AdMediation.Mintegral },
            { "inmobi", AdMediation.InMobi },
            { "pangle", AdMediation.Pangle },
            { "bytedance", AdMediation.Pangle },
            { "chartboost", AdMediation.Chartboost },
            { "facebook", AdMediation.Facebook },
            { "meta", AdMediation.Facebook },
            { "bidmachine", AdMediation.BidMachine },
            { "fyber", AdMediation.Fyber },
            { "dt_exchange", AdMediation.Fyber },
            { "line", AdMediation.LINE },
            { "maio", AdMediation.Maio },
            { "mobilefuse", AdMediation.MobileFuse },
            { "moloco", AdMediation.Moloco },
            { "ogury", AdMediation.Ogury },
            { "smaato", AdMediation.Smaato },
            { "verve", AdMediation.Verve },
            { "yandex", AdMediation.Yandex },
            { "yso", AdMediation.YSONetwork },
            { "ysonetwork", AdMediation.YSONetwork }
        };

        /// <summary>gói UPM của một mạng cho một nền tảng ("android" / "ios"); null khi mạng đó không có gói</summary>
        public static string PackageName(AdMediation network, string platform)
        {
            return NetworkPackages.TryGetValue(network, out var p) ? p + "." + platform : null;
        }
    }
}
