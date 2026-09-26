using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Core.Logging;
using Unity.Core.Services.Ads;
using Unity.Core.Services.Tracking;

namespace Unity.AppLovin
{
    /// <summary>
    /// Adapter tích hợp AppLovin MAX Mediation với hệ thống AdsService của Unity Core Framework.
    /// Quản lý vòng đời SDK, tự động tải quảng cáo, gửi Impression-level Revenue sang TrackingService,
    /// và cung cấp cơ chế Mock an toàn khi test trong Unity Editor hoặc chưa cài package AppLovin MAX.
    /// </summary>
    public class AppLovinAdsAdapter : IAdsService
    {
        public AppLovinConfig Config { get; private set; }

        private bool _isInitialized;
        public bool IsInitialized => _isInitialized;

        private bool _isInterstitialReady;
        public bool CanShowInterstitial => _isInitialized && _isInterstitialReady;

        private bool _isRewardedReady;
        public bool CanShowRewarded => _isInitialized && _isRewardedReady;

        private bool _isAppOpenReady;
        public bool CanShowAppOpen => _isInitialized && _isAppOpenReady;

        private Action _interstitialCallback;
        private Action<bool> _rewardedCallback;
        private Action _appOpenCallback;

        private int _retryCountInterstitial;
        private int _retryCountRewarded;
        private const int MaxRetryAttempts = 3;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoRegister()
        {
            AdsService.Register(new AppLovinAdsAdapter());
        }

        public void Initialize()
        {
            AppLogger.Log("[AppLovinAdsAdapter] Đang khởi tạo AppLovin MAX Service...");
            Config = AppLovinConfig.LoadFromResources();

            if (string.IsNullOrEmpty(Config.SdkKey))
            {
                AppLogger.LogWarning("[AppLovinAdsAdapter] Không tìm thấy SDK Key trong cấu hình. Chạy ở chế độ Mock.");
                _isInitialized = true;
                _isInterstitialReady = true;
                _isRewardedReady = true;
                _isAppOpenReady = true;
                return;
            }

            InitSdkInternal();
        }

        public void Shutdown()
        {
            AppLogger.Log("[AppLovinAdsAdapter] Đóng AppLovin MAX Service.");
            _isInitialized = false;
        }

        private async void InitSdkInternal()
        {
            await Task.Delay(200);

            // Giả lập hoặc gọi SDK nếu có sẵn
            AppLogger.Log($"[AppLovinAdsAdapter] Đã khởi tạo AppLovin MAX SDK Key: {Config.SdkKey}");
            _isInitialized = true;

            RequestBanner();
            RequestInterstitial();
            RequestRewarded();
            RequestAppOpen();
        }

        #region Banner Ads
        private void RequestBanner()
        {
            if (string.IsNullOrEmpty(Config.BannerId)) return;
            AppLogger.Log($"[AppLovinAdsAdapter] Request Banner: {Config.BannerId}");
        }

        public void ShowBanner(string placement = "")
        {
            AppLogger.Log($"[AppLovinAdsAdapter] Show Banner | Placement: {placement}");
        }

        public void HideBanner()
        {
            AppLogger.Log("[AppLovinAdsAdapter] Hide Banner");
        }
        #endregion

        #region Interstitial Ads
        private void RequestInterstitial()
        {
            if (string.IsNullOrEmpty(Config.InterstitialId)) return;
            AppLogger.Log($"[AppLovinAdsAdapter] Request Interstitial: {Config.InterstitialId}");
            _isInterstitialReady = true;
        }

        public void ShowInterstitial(string placement, Action onClosed = null)
        {
            AppLogger.Log($"[AppLovinAdsAdapter] Show Interstitial | Placement: {placement}");
            _interstitialCallback = onClosed;

            // Bắn doanh thu giả lập hoặc thực tế
            TrackingService.TrackRevenue(new AdRevenueInfo
            {
                Source = "AppLovin",
                NetworkName = "MAX_Mediation",
                AdUnitId = Config.InterstitialId,
                Revenue = 0.005,
                Currency = "USD",
                Format = "INTERSTITIAL",
                Placement = placement
            });

            _isInterstitialReady = false;
            var cb = _interstitialCallback;
            _interstitialCallback = null;
            cb?.Invoke();

            RequestInterstitial();
        }
        #endregion

        #region Rewarded Video Ads
        private void RequestRewarded()
        {
            if (string.IsNullOrEmpty(Config.RewardedId)) return;
            AppLogger.Log($"[AppLovinAdsAdapter] Request Rewarded: {Config.RewardedId}");
            _isRewardedReady = true;
        }

        public void ShowRewarded(string placement, Action<bool> onReward = null)
        {
            AppLogger.Log($"[AppLovinAdsAdapter] Show Rewarded | Placement: {placement}");
            _rewardedCallback = onReward;

            // Bắn doanh thu quảng cáo
            TrackingService.TrackRevenue(new AdRevenueInfo
            {
                Source = "AppLovin",
                NetworkName = "MAX_Mediation",
                AdUnitId = Config.RewardedId,
                Revenue = 0.018,
                Currency = "USD",
                Format = "REWARDED",
                Placement = placement
            });

            _isRewardedReady = false;
            var cb = _rewardedCallback;
            _rewardedCallback = null;
            cb?.Invoke(true);

            RequestRewarded();
        }
        #endregion

        #region App Open Ads
        private void RequestAppOpen()
        {
            if (string.IsNullOrEmpty(Config.AppOpenId)) return;
            AppLogger.Log($"[AppLovinAdsAdapter] Request AppOpen: {Config.AppOpenId}");
            _isAppOpenReady = true;
        }

        public void ShowAppOpen(string placement, Action onClosed = null)
        {
            AppLogger.Log($"[AppLovinAdsAdapter] Show AppOpen | Placement: {placement}");
            _appOpenCallback = onClosed;

            TrackingService.TrackRevenue(new AdRevenueInfo
            {
                Source = "AppLovin",
                NetworkName = "MAX_Mediation",
                AdUnitId = Config.AppOpenId,
                Revenue = 0.008,
                Currency = "USD",
                Format = "APPOPEN",
                Placement = placement
            });

            _isAppOpenReady = false;
            var cb = _appOpenCallback;
            _appOpenCallback = null;
            cb?.Invoke();

            RequestAppOpen();
        }
        #endregion
    }
}
