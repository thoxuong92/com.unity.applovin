using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Unity.AppLovin.Editor
{
    /// <summary>
    /// Trình quản lý Scoped Registry trong Packages/manifest.json phục vụ cài đặt UPM package của AppLovin MAX và OpenUPM.
    /// </summary>
    public class AppLovinUpmManifest
    {
        private const string KeyUrl = "url";
        private const string KeyName = "name";
        private const string KeyScopes = "scopes";
        private const string KeyScopedRegistry = "scopedRegistries";

        private static string ManifestPath => Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");

        public static void EnsureAppLovinScopedRegistries()
        {
            try
            {
                if (!File.Exists(ManifestPath)) return;

                string content = File.ReadAllText(ManifestPath);
                bool modified = false;

                if (!content.Contains("unity.packages.applovin.com"))
                {
                    content = InsertRegistry(content, "AppLovin MAX Unity", "https://unity.packages.applovin.com/", new[] { "com.applovin.mediation.ads", "com.applovin.mediation.adapters", "com.applovin.mediation.dsp" });
                    modified = true;
                }

                if (!content.Contains("package.openupm.com"))
                {
                    content = InsertRegistry(content, "package.openupm.com", "https://package.openupm.com", new[] { "com.google.external-dependency-manager" });
                    modified = true;
                }

                if (modified)
                {
                    File.WriteAllText(ManifestPath, content);
                    Debug.Log("[AppLovinUpmManifest] Đã thêm AppLovin & OpenUPM Scoped Registries vào Packages/manifest.json.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AppLovinUpmManifest] Lỗi cấu hình manifest.json: {ex.Message}");
            }
        }

        private static string InsertRegistry(string manifestJson, string name, string url, string[] scopes)
        {
            string scopesJson = string.Join(", ", scopes.Select(s => $"\"{s}\""));
            string registryBlock =
                "    {\n" +
                $"      \"name\": \"{name}\",\n" +
                $"      \"url\": \"{url}\",\n" +
                $"      \"scopes\": [ {scopesJson} ]\n" +
                "    }";

            int idx = manifestJson.IndexOf("\"scopedRegistries\"", StringComparison.Ordinal);
            if (idx >= 0)
            {
                int openBracket = manifestJson.IndexOf('[', idx);
                if (openBracket >= 0)
                {
                    return manifestJson.Insert(openBracket + 1, "\n" + registryBlock + ",");
                }
            }
            else
            {
                int firstBrace = manifestJson.IndexOf('{');
                if (firstBrace >= 0)
                {
                    string newSection =
                        $"\n  \"scopedRegistries\": [\n{registryBlock}\n  ],";
                    return manifestJson.Insert(firstBrace + 1, newSection);
                }
            }
            return manifestJson;
        }
    }
}
