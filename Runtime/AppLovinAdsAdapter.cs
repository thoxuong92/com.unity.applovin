using System;
using System.Collections.Generic;
using System.Linq;
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
    public class AppLovinAdsAdapter : IAdsService, IAdsMrec, IAdsBannerLayout
    {
        public AppLovinConfig Config { get; private set; }

        private bool _isInitialized;
        public bool IsInitialized => _isInitialized;

        /// <summary>Chế độ giả: chưa có MAX SDK, chưa có SDK Key, hoặc khởi tạo lỗi (mọi loại quảng cáo dùng bản giả của core).</summary>
        private bool _mock;

        public bool CanShowBanner => _isInitialized && (_mock || !string.IsNullOrEmpty(Config?.BannerId));

        private bool _isInterstitialReady;
        public bool CanShowInterstitial => _isInitialized && (!_mock ? CheckInterstitialReady() : _isInterstitialReady);

        private bool _isRewardedReady;
        public bool CanShowRewarded => _isInitialized && (!_mock ? CheckRewardedReady() : _isRewardedReady);

        private bool _isAppOpenReady;
        public bool CanShowAppOpen => _isInitialized && (!_mock ? CheckAppOpenReady() : _isAppOpenReady);

        // MREC: created at the first ShowMrec (it needs a place), moved with UpdateMRecPosition after that
        public bool CanShowMrec => _isInitialized && (_mock || !string.IsNullOrEmpty(Config?.MrecId));
        private bool _mrecCreated, _mrecWanted;
        private Rect _mrecRect;
        private bool _bannerCreated, _bannerWanted;

        /// <summary>Chiều cao banner MAX (pixel): 50 dp, 90 dp trên máy tính bảng; 0 khi chưa có SDK (AdsService tự tính).</summary>
        public float BannerHeightPx
        {
            get
            {
                if (!_hasMaxSdk) return 0f;
                var utils = _maxSdkType.Assembly.GetType("MaxSdkUtils");
                bool tablet = AdsService.IsTablet;
                try
                {
                    var isTablet = utils?.GetMethod("IsTablet", Type.EmptyTypes);
                    if (isTablet != null) tablet = (bool)isTablet.Invoke(null, null);
                }
                catch (Exception) { /* keep AdsService's guess */ }
                return (tablet ? 90f : 50f) * Density();
            }
        }

        private Action _interstitialCallback;
        private Action<bool> _rewardedCallback;
        private Action _appOpenCallback;

        private int _retryCountInterstitial;
        private int _retryCountRewarded;
        private int _retryCountAppOpen;

        private bool _initStarted;
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
            // (AdsService.Register gọi, rồi ServiceRegistry.Register gọi lần nữa: khởi tạo hai lần thì mỗi callback chạy hai lần)
            if (_initStarted) return;
            _initStarted = true;
            AppLogger.Log("[AppLovinAdsAdapter] Khởi tạo AppLovin MAX Service...");
            Config = AppLovinConfig.LoadFromResources();

            DetectMaxSdk();

            if (string.IsNullOrEmpty(Config.SdkKey))
            {
                AppLogger.LogWarning("[AppLovinAdsAdapter] Không tìm thấy SDK Key trong cấu hình Info.json. Chạy ở chế độ Mock.");
                _isInitialized = true;
                _mock = true;
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
                _mock = true;
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
                CallMax("SetSdkKey", Config.SdkKey);
                // (bản development: log đầy đủ của MAX trong logcat, tag AppLovinSdk)
                if (Debug.isDebugBuild) CallMax("SetVerboseLogging", true);

                // MaxSdk.InitializeSdk(string[] adUnitIds = null): MAX 8 có tham số tùy chọn
                if (CallMax("InitializeSdk") == null)
                    AppLogger.LogError("[AppLovinAdsAdapter] Không tìm thấy MaxSdk.InitializeSdk.");
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"[AppLovinAdsAdapter] Lỗi khởi tạo MaxSdk: {ex.Message}. Chuyển sang chế độ Mock.");
                _isInitialized = true;
                _mock = true;
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
            if (_mrecWanted) ShowMrec("pending", _mrecRect);
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

            // Bind MREC Events
            Type mrecCallbacks = _maxCallbacksType.GetNestedType("MRec") ?? Type.GetType("MaxSdkCallbacks+MRec, MaxSdk.Scripts");
            if (mrecCallbacks != null)
            {
                BindEvent(mrecCallbacks, "OnAdRevenuePaidEvent", nameof(OnAdRevenuePaid));
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
            _bannerWanted = true;
            if (string.IsNullOrEmpty(Config?.BannerId)) return;

            if (_hasMaxSdk && _bannerCreated)
            {
                CallMax("ShowBanner", Config.BannerId);
            }
            AppLogger.Log($"[AppLovinAdsAdapter] Show Banner | Placement: {placement}");
        }

        public void HideBanner()
        {
            _bannerWanted = false;
            if (string.IsNullOrEmpty(Config?.BannerId)) return;

            if (_hasMaxSdk)
            {
                CallMax("HideBanner", Config.BannerId);
            }
            AppLogger.Log("[AppLovinAdsAdapter] Hide Banner");
        }

        public void ShowInterstitial(string placement, Action onClosed = null)
        {
            _interstitialCallback = onClosed;
            AppLogger.Log($"[AppLovinAdsAdapter] Show Interstitial | Placement: {placement}");

            if (!_mock && CheckInterstitialReady())
            {
                CallMax("ShowInterstitial", Config.InterstitialId, placement);
            }
            else if (!_mock)
            {
                // SDK thật chưa có quảng cáo: không hiện gì, tải lại
                var cb = _interstitialCallback;
                _interstitialCallback = null;
                cb?.Invoke();
                RequestInterstitial();
            }
            else
            {
                // Fallback Mock (quảng cáo giả có hình trong Editor)
                _isInterstitialReady = false;
                var cb = _interstitialCallback;
                _interstitialCallback = null;
                MockAdsVisual.ShowFullscreen("INTERSTITIAL", AdsService.Settings.MockInterstitialSeconds, false, _ => cb?.Invoke());
                RequestInterstitial();
            }
        }

        public void ShowRewarded(string placement, Action<bool> onReward = null)
        {
            _rewardedCallback = onReward;
            AppLogger.Log($"[AppLovinAdsAdapter] Show Rewarded | Placement: {placement}");

            if (!_mock && CheckRewardedReady())
            {
                CallMax("ShowRewardedAd", Config.RewardedId, placement);
            }
            else if (!_mock)
            {
                // SDK thật chưa có quảng cáo: không có thưởng, tải lại
                var cb = _rewardedCallback;
                _rewardedCallback = null;
                cb?.Invoke(false);
                RequestRewarded();
            }
            else
            {
                // Fallback Mock (quảng cáo giả có hình trong Editor: đóng sớm thì không có thưởng)
                _isRewardedReady = false;
                var cb = _rewardedCallback;
                _rewardedCallback = null;
                MockAdsVisual.ShowFullscreen("REWARDED VIDEO", AdsService.Settings.MockRewardedSeconds, true, earned => cb?.Invoke(earned));
                RequestRewarded();
            }
        }

        public void ShowAppOpen(string placement, Action onClosed = null)
        {
            _appOpenCallback = onClosed;
            AppLogger.Log($"[AppLovinAdsAdapter] Show App Open | Placement: {placement}");

            if (!_mock && CheckAppOpenReady())
            {
                CallMax("ShowAppOpenAd", Config.AppOpenId, placement);
            }
            else if (!_mock)
            {
                // SDK thật chưa có quảng cáo: không hiện gì, tải lại
                var cb = _appOpenCallback;
                _appOpenCallback = null;
                cb?.Invoke();
                RequestAppOpen();
            }
            else
            {
                // Fallback Mock (quảng cáo giả có hình trong Editor)
                _isAppOpenReady = false;
                var cb = _appOpenCallback;
                _appOpenCallback = null;
                MockAdsVisual.ShowFullscreen("APP OPEN AD", AdsService.Settings.MockAppOpenSeconds, false, _ => cb?.Invoke());
                RequestAppOpen();
            }
        }

        private void RequestInterstitial()
        {
            if (_mock) { _isInterstitialReady = true; return; }
            if (string.IsNullOrEmpty(Config?.InterstitialId)) return;
            if (_hasMaxSdk)
            {
                CallMax("LoadInterstitial", Config.InterstitialId);
            }
            else
            {
                _isInterstitialReady = true;
            }
        }

        private void RequestRewarded()
        {
            if (_mock) { _isRewardedReady = true; return; }
            if (string.IsNullOrEmpty(Config?.RewardedId)) return;
            if (_hasMaxSdk)
            {
                CallMax("LoadRewardedAd", Config.RewardedId);
            }
            else
            {
                _isRewardedReady = true;
            }
        }

        private void RequestAppOpen()
        {
            if (_mock) { _isAppOpenReady = true; return; }
            if (string.IsNullOrEmpty(Config?.AppOpenId)) return;
            if (_hasMaxSdk)
            {
                CallMax("LoadAppOpenAd", Config.AppOpenId);
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
                try
                {
                    string pos = Config.BannerPosition.ToString();
                    // MAX 6-7: CreateBanner(string, BannerPosition) — the position by its name (the enums differ in order)
                    var byEnum = FindMaxMethod("CreateBanner", 2, ps => ps[1].ParameterType.IsEnum);
                    if (byEnum != null)
                    {
                        byEnum.Invoke(null, new object[] { Config.BannerId, Enum.Parse(byEnum.GetParameters()[1].ParameterType, pos) });
                        _bannerCreated = true;
                    }
                    else
                    {
                        // MAX 8: CreateBanner(string, AdViewConfiguration(AdViewPosition))
                        var byConfig = FindMaxMethod("CreateBanner", 2, ps => ps[1].ParameterType.Name == "AdViewConfiguration");
                        var ctor = byConfig?.GetParameters()[1].ParameterType.GetConstructors()
                            .FirstOrDefault(c => c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType.IsEnum);
                        if (ctor != null)
                        {
                            var config = ctor.Invoke(new[] { Enum.Parse(ctor.GetParameters()[0].ParameterType, pos) });
                            byConfig.Invoke(null, new object[] { Config.BannerId, config });
                            _bannerCreated = true;
                        }
                    }
                    if (!_bannerCreated) AppLogger.LogWarning("[AppLovinAdsAdapter] Không tìm thấy MaxSdk.CreateBanner phù hợp.");
                    else if (_bannerWanted) ShowBanner(AdsService.ActiveBannerPlacement);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"[AppLovinAdsAdapter] CreateBanner: {ex.Message}");
                }
            }
        }

        public void ShowMrec(string placement, Rect screenRectPx)
        {
            _mrecWanted = true;
            _mrecRect = screenRectPx;
            AppLogger.Log($"[AppLovinAdsAdapter] Show MREC | Placement: {placement} | {screenRectPx}");
            if (!_hasMaxSdk || !_isInitialized || string.IsNullOrEmpty(Config?.MrecId)) return;
            try
            {
                // MAX places ad views in dp from the top-left corner
                float d = Density(), x = screenRectPx.x / d, y = screenRectPx.y / d;
                string id = Config.MrecId;
                if (!_mrecCreated)
                {
                    var byXY = FindMaxMethod("CreateMRec", 3, ps => ps[1].ParameterType == typeof(float));
                    if (byXY != null) byXY.Invoke(null, new object[] { id, x, y });
                    else
                    {
                        // MAX 8: CreateMRec(string, AdViewConfiguration(float x, float y))
                        var byConfig = FindMaxMethod("CreateMRec", 2, ps => ps[1].ParameterType.Name == "AdViewConfiguration");
                        if (byConfig == null) { AppLogger.LogWarning("[AppLovinAdsAdapter] Không tìm thấy MaxSdk.CreateMRec."); return; }
                        var config = Activator.CreateInstance(byConfig.GetParameters()[1].ParameterType, new object[] { x, y });
                        byConfig.Invoke(null, new object[] { id, config });
                    }
                    _mrecCreated = true;
                }
                else
                {
                    FindMaxMethod("UpdateMRecPosition", 3, ps => ps[1].ParameterType == typeof(float))?.Invoke(null, new object[] { id, x, y });
                }
                CallMax("ShowMRec", id);
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"[AppLovinAdsAdapter] ShowMrec: {ex.Message}");
            }
        }

        public void HideMrec()
        {
            _mrecWanted = false;
            AppLogger.Log("[AppLovinAdsAdapter] Hide MREC");
            if (!_hasMaxSdk || !_mrecCreated || string.IsNullOrEmpty(Config?.MrecId)) return;
            CallMax("HideMRec", Config.MrecId);
        }

        /// <summary>Pixel cho mỗi dp, theo MaxSdkUtils.GetScreenDensity (AdsService.ScreenDensity nếu không có).</summary>
        private float Density()
        {
            try
            {
                var m = _maxSdkType?.Assembly.GetType("MaxSdkUtils")?.GetMethod("GetScreenDensity", Type.EmptyTypes);
                if (m != null) return Mathf.Max(0.5f, Convert.ToSingle(m.Invoke(null, null)));
            }
            catch (Exception) { /* below */ }
            return AdsService.ScreenDensity;
        }

        /// <summary>
        /// Gọi một hàm public static của MaxSdk theo tên với các đối số đã cho; các tham số tùy chọn phía sau lấy giá trị
        /// mặc định (MAX 8: InitializeSdk(string[] = null), ShowInterstitial(string, string = null, string = null)...).
        /// Trả về kết quả của hàm, hoặc null khi không có hàm hợp.
        /// </summary>
        private object CallMax(string name, params object[] args)
        {
            if (_maxSdkType == null) return null;
            foreach (var m in _maxSdkType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (m.Name != name) continue;
                var ps = m.GetParameters();
                if (ps.Length < args.Length) continue;
                bool ok = true;
                for (int i = 0; i < ps.Length && ok; i++)
                {
                    if (i < args.Length) ok = args[i] == null ? !ps[i].ParameterType.IsValueType : ps[i].ParameterType.IsInstanceOfType(args[i]);
                    else ok = ps[i].IsOptional;
                }
                if (!ok) continue;
                var full = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++) full[i] = i < args.Length ? args[i] : ps[i].DefaultValue;
                return m.Invoke(null, full) ?? true;
            }
            return null;
        }

        /// <summary>A public static MaxSdk method by name, its number of parameters (-1: any) and their types.</summary>
        private MethodInfo FindMaxMethod(string name, int count, Func<ParameterInfo[], bool> match)
        {
            if (_maxSdkType == null) return null;
            foreach (var m in _maxSdkType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (m.Name != name) continue;
                var ps = m.GetParameters();
                if ((count < 0 || ps.Length == count) && match(ps)) return m;
            }
            return null;
        }

        private bool CheckInterstitialReady()
        {
            if (!_hasMaxSdk || string.IsNullOrEmpty(Config?.InterstitialId)) return false;
            return CallMax("IsInterstitialReady", Config.InterstitialId) is bool ready && ready;
        }

        private bool CheckRewardedReady()
        {
            if (!_hasMaxSdk || string.IsNullOrEmpty(Config?.RewardedId)) return false;
            return CallMax("IsRewardedAdReady", Config.RewardedId) is bool ready && ready;
        }

        private bool CheckAppOpenReady()
        {
            if (!_hasMaxSdk || string.IsNullOrEmpty(Config?.AppOpenId)) return false;
            return CallMax("IsAppOpenAdReady", Config.AppOpenId) is bool ready && ready;
        }
        #endregion
    }
}
