using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace AutomatizacionSOT.Config;

/// <summary>
/// Cifrado en reposo de secretos en appsettings.secrets.json (DPAPI Windows, prefijo dpapi:).
/// </summary>
public static class SecretProtector
{
    public const string DpapiPrefix = "dpapi:";
    public const string EncPrefix = "enc:";

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AutomatizacionSOT.Secrets.v1");

    private static readonly HashSet<string> Placeholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "***", "********", "****", "[SECRETO]", "[SECRET]", "[OCULTO]", "[MASKED]"
    };

    public static bool IsDpapiAvailable =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>Indica si un valor en disco ya está protegido (dpapi) o es máscara UI.</summary>
    public static bool IsProtectedOrMasked(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var v = value.Trim();
        return v.StartsWith(DpapiPrefix, StringComparison.OrdinalIgnoreCase)
               || Placeholders.Contains(v);
    }

    /// <summary>¿Debe cifrarse al persistir en appsettings.secrets.json?</summary>
    public static bool IsSensitiveSecretsPath(string section, string key) =>
        section switch
        {
            "Aplicacion" => string.Equals(key, "Contrasena", StringComparison.OrdinalIgnoreCase),
            "Supervision" => string.Equals(key, "Password", StringComparison.OrdinalIgnoreCase),
            "SqlSot" => string.Equals(key, "Contrasena", StringComparison.OrdinalIgnoreCase),
            "Cobis" => string.Equals(key, "Contrasena", StringComparison.OrdinalIgnoreCase),
            "CatalogoTfs" => string.Equals(key, "Pat", StringComparison.OrdinalIgnoreCase),
            "RunnerAuth" => string.Equals(key, "Pin", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    /// <summary>Toda la sección UsuariosSot son contraseñas por usuario.</summary>
    public static bool IsUsuariosSotSection(string section) =>
        string.Equals(section, "UsuariosSot", StringComparison.OrdinalIgnoreCase);

    /// <summary>Cifra un valor plano para guardarlo en secrets (dpapi: en Windows).</summary>
    public static string ProtectForStorage(string plain)
    {
        if (string.IsNullOrEmpty(plain))
            return plain;

        if (plain.StartsWith(DpapiPrefix, StringComparison.OrdinalIgnoreCase))
            return plain;

        if (!IsDpapiAvailable)
            return plain;

        var bytes = Encoding.UTF8.GetBytes(plain);
        var protectedBytes = ProtectBytes(bytes);
        return DpapiPrefix + Convert.ToBase64String(protectedBytes);
    }

    [SupportedOSPlatform("windows")]
    private static byte[] ProtectBytes(byte[] bytes) =>
        ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);

    /// <summary>Resuelve dpapi:, enc: o texto plano (legacy).</summary>
    public static string UnprotectFromStorage(string stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return stored.Trim();

        var v = stored.Trim();

        if (v.StartsWith(DpapiPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (!IsDpapiAvailable)
            {
                throw new InvalidOperationException(
                    "Este secreto está cifrado con dpapi: (Windows). Solo puede usarse en la misma máquina y usuario Windows.");
            }

            var blob = v[DpapiPrefix.Length..].Trim();
            if (string.IsNullOrWhiteSpace(blob))
                throw new InvalidOperationException("El valor dpapi: no contiene datos cifrados.");

            try
            {
                var cipher = Convert.FromBase64String(blob);
                var plain = UnprotectBytes(cipher);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                throw new InvalidOperationException(
                    "No se pudo descifrar el secreto dpapi:. Verificá que sea la misma PC y usuario Windows.", ex);
            }
        }

        if (v.StartsWith(EncPrefix, StringComparison.OrdinalIgnoreCase))
            return DecodeEncValue(v);

        return v;
    }

    /// <summary>Recorre el JSON de secrets y cifra campos sensibles que aún estén en claro o enc:.</summary>
    public static int EncryptSensitiveFieldsInPlace(JsonObject root)
    {
        if (!IsDpapiAvailable)
            return 0;

        var count = 0;
        foreach (var (section, node) in root.ToList())
        {
            if (section.StartsWith('_')) continue;

            if (IsUsuariosSotSection(section) && node is JsonObject usuarios)
            {
                foreach (var (user, val) in usuarios.ToList())
                {
                    if (val is not JsonValue jv) continue;
                    var s = jv.ToString();
                    if (string.IsNullOrWhiteSpace(s) || IsProtectedOrMasked(s)) continue;
                    usuarios[user] = ProtectForStorage(UnprotectFromStorage(s));
                    count++;
                }
                continue;
            }

            if (!string.Equals(section, "RunnerAuth", StringComparison.OrdinalIgnoreCase) || node is not JsonObject runner)
                continue;

            foreach (var (key, val) in runner.ToList())
            {
                if (string.Equals(key, "Smtp", StringComparison.OrdinalIgnoreCase) && val is JsonObject smtp)
                {
                    if (smtp["Password"] is JsonValue pw)
                    {
                        var s = pw.ToString();
                        if (!string.IsNullOrWhiteSpace(s) && !IsProtectedOrMasked(s))
                        {
                            smtp["Password"] = ProtectForStorage(UnprotectFromStorage(s));
                            count++;
                        }
                    }
                    continue;
                }

                if (!IsSensitiveSecretsPath("RunnerAuth", key) || val is not JsonValue jv2) continue;
                var raw = jv2.ToString();
                if (string.IsNullOrWhiteSpace(raw) || IsProtectedOrMasked(raw)) continue;
                runner[key] = ProtectForStorage(UnprotectFromStorage(raw));
                count++;
            }
        }

        foreach (var (section, node) in root.ToList())
        {
            if (section.StartsWith('_') || IsUsuariosSotSection(section)
                || string.Equals(section, "RunnerAuth", StringComparison.OrdinalIgnoreCase))
                continue;

            if (node is not JsonObject sec) continue;
            foreach (var (key, val) in sec.ToList())
            {
                if (!IsSensitiveSecretsPath(section, key) || val is not JsonValue jv) continue;
                var s = jv.ToString();
                if (string.IsNullOrWhiteSpace(s) || IsProtectedOrMasked(s)) continue;
                sec[key] = ProtectForStorage(UnprotectFromStorage(s));
                count++;
            }
        }

        return count;
    }

    /// <summary>Migra appsettings.secrets.json: cifra campos sensibles y deja backup .bak.</summary>
    public static (int Encrypted, string Path) MigrateSecretsFile(string secretsPath)
    {
        if (!File.Exists(secretsPath))
            throw new FileNotFoundException("No se encontró appsettings.secrets.json.", secretsPath);

        if (!IsDpapiAvailable)
            throw new PlatformNotSupportedException("DPAPI solo está disponible en Windows.");

        var root = JsonNode.Parse(File.ReadAllText(secretsPath)) as JsonObject
                   ?? new JsonObject();
        var n = EncryptSensitiveFieldsInPlace(root);
        if (n > 0)
        {
            File.Copy(secretsPath, secretsPath + ".bak", overwrite: true);
            File.WriteAllText(
                secretsPath,
                root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
        }

        return (n, secretsPath);
    }

    private static string DecodeEncValue(string value)
    {
        var encoded = value[EncPrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(encoded))
            throw new InvalidOperationException("El valor enc: no contiene datos codificados.");
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("El valor enc: no es Base64 válido.", ex);
        }
    }

    [SupportedOSPlatform("windows")]
    private static byte[] UnprotectBytes(byte[] cipher) =>
        ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
}
