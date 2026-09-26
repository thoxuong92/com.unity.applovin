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
        public static readonly Dictionary<AdMediation, string> NetworkPackages = new Dictionary<AdMediation, string>()
        {
            { AdMediation.AdMob, "com.applovin.mediation.adapters.google" },
            { AdMediation.GoogleBidding, "com.applovin.mediation.adapters.google-ad-manager" },
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
            { AdMediation.Moloco, "com.applovin.mediation.adapters.moloco" },
            { AdMediation.Ogury, "com.applovin.mediation.adapters.ogury" }
        };
    }
}
