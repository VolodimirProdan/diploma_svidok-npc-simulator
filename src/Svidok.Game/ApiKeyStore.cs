using System.Runtime.InteropServices;
using System.Text;
using Svidok.Core.Util;

namespace Svidok.Game;

/// <summary>
/// Ключ ШІ между запусками. Хранится не в папке проекта (чтобы не попал в git или архив), а в профиле
/// пользователя Windows (%APPDATA%\Svidok\apikey.bin), зашифрованный DPAPI: расшифровать его может
/// только эта учётная запись на этом компьютере. На других системах — файл с правами только для владельца.
/// </summary>
public static class ApiKeyStore
{
    private const uint CryptProtectUiForbidden = 0x1;

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Svidok.ApiKey.v1");

    private static string FilePath
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(root))
            {
                root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
            return Path.Combine(root, "Svidok", "apikey.bin");
        }
    }

    public static bool HasSaved => File.Exists(FilePath);

    /// <summary>При запуске: если в переменной окружения ключа нет, а сохранённый есть — подставляем его в процесс.</summary>
    public static void ApplyToEnvironment(string envName)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(envName)))
        {
            return;
        }
        var key = Load();
        if (!string.IsNullOrWhiteSpace(key))
        {
            Environment.SetEnvironmentVariable(envName, key);
        }
    }

    public static void Save(string key)
    {
        try
        {
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var plain = Encoding.UTF8.GetBytes(key.Trim());
            if (OperatingSystem.IsWindows())
            {
                File.WriteAllBytes(path, Protect(plain));
            }
            else
            {
                File.WriteAllBytes(path, plain);
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Warn("Ключ не збережено: " + ex.Message);
        }
    }

    public static void Delete()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Збережений ключ не видалено: " + ex.Message);
        }
    }

    private static string? Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path))
            {
                return null;
            }
            var data = File.ReadAllBytes(path);
            var plain = OperatingSystem.IsWindows() ? Unprotect(data) : data;
            return plain is null ? null : Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Warn("Збережений ключ не прочитано: " + ex.Message);
            return null;
        }
    }

    // ---------------------------------------------------------------- DPAPI (crypt32), без лишних пакетов

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, uint flags, ref DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, uint flags, ref DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static byte[] Protect(byte[] plain)
    {
        var result = Transform(plain, protect: true);
        return result ?? throw new InvalidOperationException("DPAPI не зашифрував ключ");
    }

    private static byte[]? Unprotect(byte[] cipher) => Transform(cipher, protect: false);

    private static byte[]? Transform(byte[] source, bool protect)
    {
        var input = new DataBlob();
        var entropy = new DataBlob();
        var output = new DataBlob();
        var inputHandle = GCHandle.Alloc(source, GCHandleType.Pinned);
        var entropyHandle = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
        try
        {
            input.Size = source.Length;
            input.Data = inputHandle.AddrOfPinnedObject();
            entropy.Size = Entropy.Length;
            entropy.Data = entropyHandle.AddrOfPinnedObject();

            var ok = protect
                ? CryptProtectData(ref input, "Svidok", ref entropy, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output)
                : CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output);
            if (!ok || output.Data == IntPtr.Zero)
            {
                return null;
            }
            var bytes = new byte[output.Size];
            Marshal.Copy(output.Data, bytes, 0, output.Size);
            return bytes;
        }
        finally
        {
            inputHandle.Free();
            entropyHandle.Free();
            if (output.Data != IntPtr.Zero)
            {
                LocalFree(output.Data);
            }
        }
    }
}
