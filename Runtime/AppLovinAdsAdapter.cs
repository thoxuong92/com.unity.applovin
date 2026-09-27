using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Core.Logging;
using Unity.Core.Services.Ads;
using Unity.Core.Services.Analytics;
using Unity.Core.Services.Tracking;

namespace Unity.AppLovin
{
    /// <summary>
    /// Adapter tích hợp AppLovin MAX Mediation với hệ thống AdsService & TrackingService của Unity Core Framework.
    /// Quản lý toàn bộ vòng đời SDK, tự động tải quảng cáo với Exponential Backoff,
    /// gửi Impression-level Revenue (ILRD) sang TrackingService/AnalyticsService,
    /// và cung cấp cơ chế Mock an toàn khi test trong Unity Editor hoặc chưa cài package AppLovin MAX.
    /// </summary>
    public class AppLovinAdsAdapter : IAdsService
    {
        public AppLovinConfig Config { get; private set; }

        private bool _isInitialized;
        public bool IsInitialized => _isInitialized;

        public bool CanShowBanner => _isInitialized && !string.IsNullOrEmpty(Config?.BannerId);

        private bool _isInterstitialReady;
        public bool CanShowInterstitial => _isInitialized && (_hasMaxSdk ? CheckInterstitialReady() : _isInterstitialReady);

        private bool _isRewardedReady;
        public bool CanShowRewarded => _isInitialized && (_hasMaxSdk ? CheckRewardedReady() : _isRewardedReady);

        private bool _isAppOpenReady;
        public bool CanShowAppOpen => _isInitialized && (_hasMaxSdk ? CheckAppOpenReady() : _isAppOpenReady);

        private Action _interstitialCallback;
        private Action<bool> _rewardedCallback;
        private Action _appOpenCallback;

        private int _retryCountInterstitial;
        private int _retryCountRewarded;
        private int _retryCountAppOpen;

        private bool _hasMaxSdk;
        private Type _maxSdkType;
        private Type _maxCallbacksType;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoRegister()
        {
            AdsService.Register(new AppLovinAdsAdapter());
        }

        public void Initialize()
        {
            AppLogger.Log("[AppLovinAdsAdapter] Khởi tạo AppLovin MAX Service...");
            Config = AppLovinConfig.LoadFromResources();

            DetectMaxSdk();

            if (string.IsNullOrEmpty(Config.SdkKey))
            {
                AppLogger.LogWarning("[AppLovinAdsAdapter] Không tìm thấy SDK Key trong cấu hình Info.json. Chạy ở chế độ Mock.");
                _isInitialized = true;
                _isInterstitialReady = true;
                _isRewardedReady = true;
                _isAppOpenReady = true;
                return;
            }

            if (_hasMaxSdk)
            {
                InitRealSdk();
            }
            else
            {
                AppLogger.LogWarning("[AppLovinAdsAdapter] Chưa tìm thấy MaxSdk assembly. Tự động chuyển sang chế độ Mock.");
                _isInitialized = true;
                _isInterstitialReady = true;
                _isRewardedReady = true;
                _isAppOpenReady = true;
            }
        }

        public void Shutdown()
        {
            AppLogger.Log("[AppLovinAdsAdapter] Dừng AppLovin MAX Service.");
            _isInitialized = false;
        }

        private void DetectMaxSdk()
        {
            _maxSdkType = Type.GetType("MaxSdk, MaxSdk.Scripts") ?? Type.GetType("MaxSdk");
            _maxCallbacksType = Type.GetType("MaxSdkCallbacks, MaxSdk.Scripts") ?? Type.GetType("MaxSdkCallbacks");
            _hasMaxSdk = _maxSdkType != null && _maxCallbacksType != null;
        }

        #region Real SDK Integration
        private void InitRealSdk()
        {
            try
            {
                AppLogger.Log($"[AppLovinAdsAdapter] Gắn kết AppLovin MAX SDK Key: {Config.SdkKey}");

                // Bind SDK Initialized event
                var initEvent = _maxCallbacksType.GetEvent("OnSdkInitializedEvent");
                if (initEvent != null)
                {
                    Action<object> onInitAction = OnSdkInitialized;
                    var delegateMethod = Delegate.CreateDelegate(initEvent.EventHandlerType, onInitAction.Target, onInitAction.Method);
                    initEvent.AddEventHandler(null, delegateMethod);
                }

                // Bind Interstitial, Rewarded, Banner, AppOpen events
                BindCallbacks();

                // MaxSdk.SetSdkKey(Config.SdkKey);
                var setKeyMethod = _maxSdkType.GetMethod("SetSdkKey", new[] { typeof(string) });
                setKeyMethod?.Invoke(null, new object[] { Config.SdkKey });

                // MaxSdk.InitializeSdk();
                var initMethod = _maxSdkType.GetMethod("InitializeSdk", Type.EmptyTypes);
                initMethod?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"[AppLovinAdsAdapter] Lỗi khởi tạo MaxSdk: {ex.Message}. Chuyển sang chế độ Mock.");
                _isInitialized = true;
                _isInterstitialReady = true;
                _isRewardedReady = true;
                _isAppOpenReady = true;
            }
        }

        private void OnSdkInitialized(object sdkConfiguration)
        {
            _isInitialized = true;
            AppLogger.Log("[AppLovinAdsAdapter] AppLovin MAX SDK đã khởi tạo thành công!");

            RequestInterstitial();
            RequestRewarded();
            RequestAppOpen();
            RequestBanner();
        }

        private void BindCallbacks()
        {
            // Bind Interstitial Events
            Type interCallbacks = _maxCallbacksType.GetNestedType("Interstitial") ?? Type.GetType("MaxSdkCallbacks+Interstitial, MaxSdk.Scripts");
            if (interCallbacks != null)
            {
                BindEvent(interCallbacks, "OnAdLoadedEvent", nameof(OnInterstitialLoaded));
                BindEvent(interCallbacks, "OnAdLoadFailedEvent", nameof(OnInterstitialLoadFailed));
                BindEvent(interCallbacks, "OnAdHiddenEvent", nameof(OnInterstitialHidden));
                BindEvent(interCallbacks, "OnAdRevenuePaidEvent", nameof(OnAdRevenuePaid));
            }

            // Bind Rewarded Events
            Type rewardedCallbacks = _maxCallbacksType.GetNestedType("Rewarded") ?? Type.GetType("MaxSdkCallbacks+Rewarded, MaxSdk.Scripts");
            if (rewardedCallbacks != null)
            {
                BindEvent(rewardedCallbacks, "OnAdLoadedEvent", nameof(OnRewardedLoaded));
                BindEvent(rewardedCallbacks, "OnAdLoadFailedEvent", nameof(OnRewardedLoadFailed));
                BindEvent(rewardedCallbacks, "OnAdHiddenEvent", nameof(OnRewardedHidden));
                BindEvent(rewardedCallbacks, "OnAdReceivedRewardEvent", nameof(OnRewardedReceivedReward));
                BindEvent(rewardedCallbacks, "OnAdRevenuePaidEvent", nameof(OnAdRevenuePaid));
            }

            // Bind Banner Events
            Type bannerCallbacks = _maxCallbacksType.GetNestedType("Banner") ?? Type.GetType("MaxSdkCallbacks+Banner, MaxSdk.Scripts");
            if (bannerCallbacks != null)
            {
                BindEvent(bannerCallbacks, "OnAdRevenuePaidEvent", nameof(OnAdRevenuePaid));
            }

            // Bind AppOpen Events
            Type appOpenCallbacks = _maxCallbacksType.GetNestedType("AppOpen") ?? Type.GetType("MaxSdkCallbacks+AppOpen, MaxSdk.Scripts");
            if (appOpenCallbacks != null)
            {
                BindEvent(appOpenCallbacks, "OnAdLoadedEvent", nameof(OnAppOpenLoadedInternal));
                BindEvent(appOpenCallbacks, "OnAdLoadFailedEvent", nameof(OnAppOpenLoadFailed));
                BindEvent(appOpenCallbacks, "OnAdHiddenEvent", nameof(OnAppOpenHidden));
                BindEvent(appOpenCallbacks, "OnAdRevenuePaidEvent", nameof(OnAdRevenuePaid));
            }
        }

        private void BindEvent(Type containerType, string eventName, string handlerMethodName)
        {
            try
            {
                var ev = containerType.GetEvent(eventName, BindingFlags.Public | BindingFlags.Static);
                var method = GetType().GetMethod(handlerMethodName, BindingFlags.NonPublic | BindingFlags.Instance);
                if (ev != null && method != null)
                {
                    var del = Delegate.CreateDelegate(ev.EventHandlerType, this, method);
                    ev.AddEventHandler(null, del);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[AppLovinAdsAdapter] BindEvent '{eventName}' warning: {ex.Message}");
            }
        }
        #endregion

        #region Callback Handlers
        private void OnInterstitialLoaded(string adUnitId, object adInfo)
        {
            _isInterstitialReady = true;
            _retryCountInterstitial = 0;
            AppLogger.Log($"[AppLovinAdsAdapter] Interstitial đã tải thành công: {adUnitId}");
        }

        private async void OnInterstitialLoadFailed(string adUnitId, object errorInfo)
        {
            _isInterstitialReady = false;
            _retryCountInterstitial++;
            float retryDelay = Mathf.Pow(2, Mathf.Min(5, _retryCountInterstitial));
            AppLogger.LogWarning($"[AppLovinAdsAdapter] Tải Interstitial thất bại. Thử lại sau {retryDelay}s (Lần {_retryCountInterstitial})...");
            await Task.Delay(TimeSpan.FromSeconds(retryDelay));
            RequestInterstitial();
        }

        private void OnInterstitialHidden(string adUnitId, object adInfo)
        {
            _isInterstitialReady = false;
            var cb = _interstitialCallback;
            _interstitialCallback = null;
            cb?.Invoke();

            RequestInterstitial();
        }

        private void OnRewardedLoaded(string adUnitId, object adInfo)
        {
            _isRewardedReady = true;
            _retryCountRewarded = 0;
            AppLogger.Log($"[AppLovinAdsAdapter] Rewarded Video đã tải thành công: {adUnitId}");
        }

        private async void OnRewardedLoadFailed(string adUnitId, object errorInfo)
        {
            _isRewardedReady = false;
            _retryCountRewarded++;
            float retryDelay = Mathf.Pow(2, Mathf.Min(5, _retryCountRewarded));
            AppLogger.LogWarning($"[AppLovinAdsAdapter] Tải Rewarded thất bại. Thử lại sau {retryDelay}s (Lần {_retryCountRewarded})...");
            await Task.Delay(TimeSpan.FromSeconds(retryDelay));
            RequestRewarded();
        }

        private void OnRewardedHidden(string adUnitId, object adInfo)
        {
            _isRewardedReady = false;
            var cb = _rewardedCallback;
            _rewardedCallback = null;
            cb?.Invoke(false);

            RequestRewarded();
        }

        private void OnRewardedReceivedReward(string adUnitId, object reward, object adInfo)
        {
            AppLogger.Log($"[AppLovinAdsAdapter] Người chơi đã nhận phần thưởng từ Rewarded Ad: {adUnitId}");
            var cb = _rewardedCallback;
            _rewardedCallback = null;
            cb?.Invoke(true);
        }

        private void OnAppOpenLoadedInternal(string adUnitId, object adInfo)
        {
            _isAppOpenReady = true;
            _retryCountAppOpen = 0;
            AppLogger.Log($"[AppLovinAdsAdapter] App Open Ad đã sẵn sàng: {adUnitId}");
        }

        private async void OnAppOpenLoadFailed(string adUnitId, object errorInfo)
        {
            _isAppOpenReady = false;
            _retryCountAppOpen++;
            float retryDelay = Mathf.Pow(2, Mathf.Min(5, _retryCountAppOpen));
            await Task.Delay(TimeSpan.FromSeconds(retryDelay));
            RequestAppOpen();
        }

        private void OnAppOpenHidden(string adUnitId, object adInfo)
        {
            _isAppOpenReady = false;
            var cb = _appOpenCallback;
            _appOpenCallback = null;
            cb?.Invoke();

            RequestAppOpen();
        }

        private void OnAdRevenuePaid(string adUnitId, object adInfo)
        {
            if (adInfo == null) return;

            try
            {
                Type infoType = adInfo.GetType();
                double revenue = Convert.ToDouble(infoType.GetProperty("Revenue")?.GetValue(adInfo) ?? 0.0);
                string networkName = infoType.GetProperty("NetworkName")?.GetValue(adInfo)?.ToString() ?? "AppLovin";
                string format = infoType.GetProperty("AdFormat")?.GetValue(adInfo)?.ToString() ?? "UNKNOWN";
                string placement = infoType.GetProperty("Placement")?.GetValue(adInfo)?.ToString() ?? "";
                string country = infoType.GetProperty("CountryCode")?.GetValue(adInfo)?.ToString() ?? "US";

                var revInfo = new AdRevenueInfo
                {
                    Source = "AppLovin",
                    NetworkName = networkName,
                    AdUnitId = adUnitId,
                    Revenue = revenue,
                    Currency = "USD",
                    Format = format,
                    Placement = placement,
                    CountryCode = country
                };

                TrackingService.TrackRevenue(revInfo);
                AnalyticsService.LogEvent("ad_impression", new Dictionary<string, object>
                {
                    { "ad_platform", "AppLovin" },
                    { "ad_source", networkName },
                    { "ad_unit_name", adUnitId },
                    { "ad_format", format },
                    { "currency", "USD" },
                    { "value", revenue }
                });
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"[AppLovinAdsAdapter] Lỗi phân tích AdRevenue: {ex.Message}");
            }
        }
        #endregion

        #region Ads Control Interface
        public void ShowBanner(string placement = "")
        {
            if (string.IsNullOrEmpty(Config?.BannerId)) return;

            if (_hasMaxSdk)
            {
                _maxSdkType.GetMethod("ShowBanner", new[] { typeof(string) })?.Invoke(null, new object[] { Config.BannerId });
            }
            AppLogger.Log($"[AppLovinAdsAdapter] Show Banner | Placement: {placement}");
        }

        public void HideBanner()
        {
            if (string.IsNullOrEmpty(Config?.BannerId)) return;

            if (_hasMaxSdk)
            {
                _maxSdkType.GetMethod("HideBanner", new[] { typeof(string) })?.Invoke(null, new object[] { Config.BannerId });
            }
            AppLogger.Log("[AppLovinAdsAdapter] Hide Banner");
        }

        public void ShowInterstitial(string placement, Action onClosed = null)
        {
            _interstitialCallback = onClosed;
            AppLogger.Log($"[AppLovinAdsAdapter] Show Interstitial | Placement: {placement}");

            if (_hasMaxSdk && CheckInterstitialReady())
            {
                _maxSdkType.GetMethod("ShowInterstitial", new[] { typeof(string) })?.Invoke(null, new object[] { Config.InterstitialId });
            }
            else
            {
                // Fallback Mock
                _isInterstitialReady = false;
                var cb = _interstitialCallback;
                _interstitialCallback = null;
                cb?.Invoke();
                RequestInterstitial();
            }
        }

        public void ShowRewarded(string placement, Action<bool> onReward = null)
        {
            _rewardedCallback = onReward;
            AppLogger.Log($"[AppLovinAdsAdapter] Show Rewarded | Placement: {placement}");

            if (_hasMaxSdk && CheckRewardedReady())
            {
                _maxSdkType.GetMethod("ShowRewardedAd", new[] { typeof(string) })?.Invoke(null, new object[] { Config.RewardedId });
            }
            else
            {
                // Fallback Mock
                _isRewardedReady = false;
                var cb = _rewardedCallback;
                _rewardedCallback = null;
                cb?.Invoke(true);
                RequestRewarded();
            }
        }

        public void ShowAppOpen(string placement, Action onClosed = null)
        {
            _appOpenCallback = onClosed;
            AppLogger.Log($"[AppLovinAdsAdapter] Show App Open | Placement: {placement}");

            if (_hasMaxSdk && CheckAppOpenReady())
            {
                _maxSdkType.GetMethod("ShowAppOpenAd", new[] { typeof(string) })?.Invoke(null, new object[] { Config.AppOpenId });
            }
            else
            {
                // Fallback Mock
                _isAppOpenReady = false;
                var cb = _appOpenCallback;
                _appOpenCallback = null;
                cb?.Invoke();
                RequestAppOpen();
            }
        }

        private void RequestInterstitial()
        {
            if (string.IsNullOrEmpty(Config?.InterstitialId)) return;
            if (_hasMaxSdk)
            {
                _maxSdkType.GetMethod("LoadInterstitial", new[] { typeof(string) })?.Invoke(null, new object[] { Config.InterstitialId });
            }
            else
            {
                _isInterstitialReady = true;
            }
        }

        private void RequestRewarded()
        {
            if (string.IsNullOrEmpty(Config?.RewardedId)) return;
            if (_hasMaxSdk)
            {
                _maxSdkType.GetMethod("LoadRewardedAd", new[] { typeof(string) })?.Invoke(null, new object[] { Config.RewardedId });
            }
            else
            {
                _isRewardedReady = true;
            }
        }

        private void RequestAppOpen()
        {
            if (string.IsNullOrEmpty(Config?.AppOpenId)) return;
            if (_hasMaxSdk)
            {
                _maxSdkType.GetMethod("LoadAppOpenAd", new[] { typeof(string) })?.Invoke(null, new object[] { Config.AppOpenId });
            }
            else
            {
                _isAppOpenReady = true;
            }
        }

        private void RequestBanner()
        {
            if (string.IsNullOrEmpty(Config?.BannerId)) return;
            if (_hasMaxSdk)
            {
                // MaxSdk.CreateBanner(Config.BannerId, BannerPosition.BottomCenter)
                var createBanner = _maxSdkType.GetMethod("CreateBanner", new[] { typeof(string), typeof(Enum) });
                if (createBanner != null)
                {
                    Type posType = _maxSdkType.Assembly.GetType("MaxSdkBase+BannerPosition");
                    object posVal = posType != null ? Enum.ToObject(posType, (int)Config.BannerPosition) : 1;
                    createBanner.Invoke(null, new object[] { Config.BannerId, posVal });
                }
            }
        }

        private bool CheckInterstitialReady()
        {
            if (!_hasMaxSdk || string.IsNullOrEmpty(Config?.InterstitialId)) return false;
            var m = _maxSdkType.GetMethod("IsInterstitialReady", new[] { typeof(string) });
            return m != null && (bool)m.Invoke(null, new object[] { Config.InterstitialId });
        }

        private bool CheckRewardedReady()
        {
            if (!_hasMaxSdk || string.IsNullOrEmpty(Config?.RewardedId)) return false;
            var m = _maxSdkType.GetMethod("IsRewardedAdReady", new[] { typeof(string) });
            return m != null && (bool)m.Invoke(null, new object[] { Config.RewardedId });
        }

        private bool CheckAppOpenReady()
        {
            if (!_hasMaxSdk || string.IsNullOrEmpty(Config?.AppOpenId)) return false;
            var m = _maxSdkType.GetMethod("IsAppOpenAdReady", new[] { typeof(string) });
            return m != null && (bool)m.Invoke(null, new object[] { Config.AppOpenId });
        }
        #endregion
    }
}
