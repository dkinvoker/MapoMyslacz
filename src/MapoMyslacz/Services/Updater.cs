using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MapoMyslacz.Core.Updates;

namespace MapoMyslacz.Services;

/// <summary>
/// Aktualizacje z wydań na GitHubie. Sprawdzenie to jedno zapytanie do API GitHuba (bez żadnych danych
/// o użytkowniku). Instalacja działa dla jednoplikowego exe (startera): nowy plik jest pobierany obok,
/// sprawdzany sumą SHA-256 z wydania i podmieniany — starter po ponownym uruchomieniu sam rozpakuje nową wersję.
/// </summary>
internal static class Updater
{
    /// <summary>Do testów: inny adres z opisem wydania (np. lokalny serwer).</summary>
    private const string FeedOverrideVariable = "MAPOMYSLACZ_UPDATE_FEED";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // GitHub wymaga nagłówka User-Agent — sama nazwa i wersja programu
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"MapoMyslacz/{AppInfo.Version}");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    private static bool IsArm64 => RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

    public static Version CurrentVersion => UpdateLogic.ParseVersion(AppInfo.Version) ?? new Version(0, 0, 0);

    /// <summary>Exe, który można podmienić: jednoplikowy starter (przekazuje swoją ścieżkę przy uruchomieniu).</summary>
    public static string? LauncherPath =>
        Environment.GetEnvironmentVariable(FileAssociation.LauncherVariable) is { Length: > 0 } p && File.Exists(p) ? p : null;

    /// <summary>Czy da się zainstalować samemu (starter w folderze z prawem zapisu).</summary>
    public static bool CanSelfUpdate
    {
        get
        {
            if (LauncherPath is not { } exe) return false;
            try
            {
                var probe = exe + ".write-test";
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Najnowsze wydanie, jeśli jest nowsze niż ta wersja (null: brak nowszego albo brak wydań).</summary>
    public static async Task<ReleaseInfo?> CheckAsync(CancellationToken ct = default)
    {
        var url = Environment.GetEnvironmentVariable(FeedOverrideVariable) is { Length: > 0 } o ? o : UpdateLogic.LatestReleaseApi;
        using var response = await Http.GetAsync(url, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // jeszcze żadnego wydania
        response.EnsureSuccessStatusCode();
        var info = UpdateLogic.Parse(await response.Content.ReadAsStringAsync(ct), IsArm64);
        return info is not null && UpdateLogic.IsNewer(info.Version, CurrentVersion) ? info : null;
    }

    /// <summary>
    /// Pobiera nowy exe obok obecnego, sprawdza SHA-256 i podmienia plik (stary zostaje jako .old —
    /// starter usuwa go przy następnym uruchomieniu). Wyjątek = nic nie zostało zmienione.
    /// </summary>
    public static async Task InstallAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct = default)
    {
        var exe = LauncherPath ?? throw new InvalidOperationException("Tę kopię programu można zaktualizować tylko ręcznie.");
        if (!release.CanInstall) throw new InvalidOperationException("Wydanie nie ma pliku exe albo jego sumy SHA-256.");
        var download = exe + ".download";
        var old = exe + ".old";
        try
        {
            using (var response = await Http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? release.AssetSize;
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var target = File.Create(download);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    sha.AppendData(buffer, 0, read);
                    done += read;
                    if (total > 0) progress?.Report(Math.Min(1, done / (double)total));
                }
                var hash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                if (hash != release.Sha256)
                    throw new InvalidDataException("Pobrany plik ma inną sumę kontrolną niż podana w wydaniu.");
            }
            using (var fs = File.OpenRead(download))
            {
                if (fs.ReadByte() != 'M' || fs.ReadByte() != 'Z') throw new InvalidDataException("Pobrany plik nie jest programem.");
            }

            // podmiana: działa tylko rozpakowany program, nie starter — jego plik można przenieść
            File.Move(exe, old, overwrite: true);
            try
            {
                File.Move(download, exe);
            }
            catch
            {
                File.Move(old, exe, overwrite: true); // przywróć poprzednią wersję
                throw;
            }
        }
        finally
        {
            try { if (File.Exists(download)) File.Delete(download); } catch { }
        }
    }

    /// <summary>Uruchamia (nowy) starter — opcjonalnie z mapą do otwarcia. Okno trzeba potem zamknąć.</summary>
    public static void Restart(string? mapPath)
    {
        var psi = new ProcessStartInfo(LauncherPath ?? Environment.ProcessPath!) { UseShellExecute = false };
        if (mapPath is not null && File.Exists(mapPath)) psi.ArgumentList.Add(mapPath);
        Process.Start(psi);
    }

    /// <summary>Strona wydania w przeglądarce (gdy nie da się zainstalować samemu).</summary>
    public static void OpenReleasePage(ReleaseInfo? release) =>
        Process.Start(new ProcessStartInfo(release?.PageUrl ?? UpdateLogic.ReleasesPage) { UseShellExecute = true });
}
