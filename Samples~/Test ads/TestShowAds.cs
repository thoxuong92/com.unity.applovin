using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestShowAds : MonoBehaviour
{
    public TMPro.TextMeshProUGUI statusAOA, statusBanner, statusInter, statusRewarded;

    private void Awake()
    {
        statusAOA.text = "AOA";
        statusBanner.text = "Banner";
        statusInter.text = "Inter";
        statusRewarded.text = "Rewarded";
    }
    public void ShowAppOpen()
    {
        API.Get<ServiceAds>().ShowAppOpen("");
    }

    public void ShowBanner()
    {
        API.Get<ServiceAds>().ShowBanner();
    }
    public void ShowInter()
    {
        API.Get<ServiceAds>().ShowInterstitial("");
    }
    public void ShowRewarded()
    {
        API.Get<ServiceAds>().ShowRewarded("", _ => { });
    }
}
