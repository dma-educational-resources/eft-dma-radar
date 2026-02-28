using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace eft_dma_radar.Tarkov.API
{
    public static class ApiKeyStore
    {
        private sealed class ApiKeyFile
        {
            public string ApiKeyProtected { get; set; }   // base64 of DPAPI-protected bytes
            public DateTime CreatedUtc { get; set; }
        }

        public static string StoreDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "eft-dma-radar");

        public static string StorePath => Path.Combine(StoreDir, "api.json");

        public static void SaveApiKey(string apiKey)
        {
            Directory.CreateDirectory(StoreDir);
            HardenDirectory(StoreDir);

            var raw = Encoding.UTF8.GetBytes(apiKey);
            var protectedBytes = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
            var payload = new ApiKeyFile
            {
                ApiKeyProtected = Convert.ToBase64String(protectedBytes),
                CreatedUtc = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StorePath, json, Encoding.UTF8);
        }

        public static bool TryLoadApiKey(out string apiKey)
        {
            apiKey = null;
            if (!File.Exists(StorePath)) return false;

            try
            {
                var json = File.ReadAllText(StorePath, Encoding.UTF8);
                var payload = JsonSerializer.Deserialize<ApiKeyFile>(json);
                if (payload?.ApiKeyProtected is null) return false;

                var protectedBytes = Convert.FromBase64String(payload.ApiKeyProtected);
                var raw = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                apiKey = Encoding.UTF8.GetString(raw);
                return !string.IsNullOrWhiteSpace(apiKey);
            }
            catch
            {
                return false;
            }
        }

        private static void HardenDirectory(string dir)
        {
            try
            {
                var dirInfo = new DirectoryInfo(dir);
                var sec = dirInfo.GetAccessControl();
                var sid = WindowsIdentity.GetCurrent().User;
                if (sid == null) return;

                // Remove inheritance & give current user FullControl
                sec.SetAccessRuleProtection(true, false);
                var rule = new FileSystemAccessRule(
                    sid,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow);

                sec.ResetAccessRule(rule);
                dirInfo.SetAccessControl(sec);
            }
            catch
            {
                // best effort, not fatal if ACL hardening fails
            }
        }

    }
}
