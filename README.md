# Unity AppLovin MAX Service (UPM Package)

Package module tích hợp giải pháp trung gian quảng cáo (**Mediation Ads Service**) từ **AppLovin MAX** (hỗ trợ Google AdMob, Unity Ads, Mintegral, IronSource, Vungle, InMobi,...) cho **Unity Core Framework**.

Được tích hợp sẵn với kiến trúc **Pluggable Service Bridge** (`Unity.Core`), tự động bắt doanh thu quảng cáo (Impression-level Ad Revenue) và gửi sang các dịch vụ Tracking (`Adjust`, `Firebase`, `AppsFlyer`) một cách mượt mà và an toàn.

---

## 🚀 Các Tính Năng Nổi Bật

1. **Đầy Đủ Định Dạng Quảng Cáo (Ad Formats)**:
   - App Open Ads (AOA)
   - Banner Ads & MRec
   - Interstitial Ads
   - Rewarded Ads

2. **Tự Động Bắn Doanh Thu Doanh Số (ILR)**:
   - Tự động bắt sự kiện `OnAdRevenuePaid` của MAX và đóng gói vào `AdRevenueInfo` của `Unity.Core.Services.Ads`.
   - Chuyển tiếp tới tất cả `ITrackingProvider` đã đăng ký (`Adjust`, `Firebase`, `AppsFlyer`).

3. **Unity Editor Setup Tool & Scoped Registries**:
   - Menu: **`Unity Core > AppLovin MAX > Integration & Mediation Setup`**.
   - Tự động cấu hình Scoped Registries cho AppLovin và OpenUPM trong `Packages/manifest.json`.

4. **IL2CPP Stripping Safe**:
   - Kèm file `link.xml` bảo vệ các symbol của MAX SDK khi build release với Managed Stripping Level = High.

---

## 📦 Cài Đặt Vào Dự Án

### Cách 1: Cài đặt qua Git URL trong Unity Package Manager
1. Mở Unity Editor: **Window** > **Package Manager**.
2. Nhấn vào dấu **`+`** > chọn **Add package from git URL...**
3. Nhập:
   ```text
   https://github.com/thoxuong92/com.unity.applovin.git
   ```

### Cách 2: Qua file `Packages/manifest.json`
Thêm dependency trỏ tới kho lưu trữ GitHub:
```json
{
  "dependencies": {
    "com.unity.core": "https://github.com/thoxuong92/com.unity.core.git",
    "com.unity.applovin": "https://github.com/thoxuong92/com.unity.applovin.git"
  }
}
```

---

## 🛠️ Hướng Dẫn Gọi Quảng Cáo

```csharp
using Unity.Core.Services.Ads;

// Hiển thị Interstitial
if (AdsService.CanShowInterstitial)
{
    AdsService.ShowInterstitial("level_complete", () => {
        // Tiếp tục gameplay
    });
}

// Hiển thị Rewarded
AdsService.ShowRewarded("free_coins", (rewardReceived) => {
    if (rewardReceived) {
        // Cộng xu cho người chơi
    }
});
```

---

## 👨‍💻 Tác Giả & Bản Quyền
- **Repository**: [thoxuong92/com.unity.applovin](https://github.com/thoxuong92/com.unity.applovin.git)
