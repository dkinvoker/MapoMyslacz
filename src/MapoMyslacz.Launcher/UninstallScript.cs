using System.Text;

namespace MapoMyslacz.Launcher;

/// <summary>
/// Skrypt odinstalowania kładziony obok exe przy pierwszym uruchomieniu (i odtwarzany, jeśli zniknie).
/// .cmd zamiast .ps1, bo uruchamia się dwuklikiem (PowerShell otworzyłby się w Notatniku albo zablokowała go
/// polityka wykonywania). Usuwa: rozpakowaną aplikację i ustawienia (%LOCALAPPDATA%\MapoMyslacz),
/// skojarzenie plików .mapo, sam program i na końcu siebie. Mapy użytkownika zostają nietknięte.
/// </summary>
internal static class UninstallScript
{
    public const string FileName = "MapoMyslacz - odinstaluj.cmd";

    /// <summary>Treść skryptu dla programu o nazwie pliku <paramref name="exeName"/> (CRLF — cmd.exe tego wymaga).</summary>
    public static string Build(string exeName)
    {
        // nazwa exe trafia do skryptu w cudzysłowach — znaki specjalne cmd i tak nie mogą wystąpić w nazwie pliku,
        // ale % trzeba podwoić
        var exe = exeName.Replace("%", "%%");
        return Template.Replace("{EXE}", exe).ReplaceLineEndings("\r\n");
    }

    /// <summary>Kładzie skrypt obok exe (jeśli go nie ma albo ma inną treść). Błędy są ignorowane.</summary>
    public static bool TryWrite(string? exePath)
    {
        try
        {
            if (string.IsNullOrEmpty(exePath)) return false;
            var dir = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(dir)) return false;
            var path = Path.Combine(dir, FileName);
            var content = Build(Path.GetFileName(exePath));
            if (File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == content) return false;
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return true;
        }
        catch
        {
            // folder tylko do odczytu itp. — aplikacja i tak działa
            return false;
        }
    }

    private const string Template = """
        @echo off
        setlocal EnableExtensions
        chcp 65001 >nul
        title MapoMyślacz - odinstalowanie
        for /f %%a in ('echo prompt $E^| cmd') do set "ESC=%%a"
        set "C0=%ESC%[0m"
        set "CH=%ESC%[1;38;5;141m"
        set "CW=%ESC%[1;97m"
        set "CD=%ESC%[90m"
        set "CY=%ESC%[93m"
        set "CG=%ESC%[92m"
        set "CR=%ESC%[91m"
        set "APPDIR=%LOCALAPPDATA%\MapoMyslacz"
        set "EXE=%~dp0{EXE}"

        echo.
        echo   %CH%╭──────────────────────────────────────────╮%C0%
        echo   %CH%│        MapoMyślacz — odinstalowanie      │%C0%
        echo   %CH%╰──────────────────────────────────────────╯%C0%
        echo.

        tasklist /fi "imagename eq MapoMyslacz.exe" /nh <nul 2>nul | find /i "MapoMyslacz.exe" >nul
        if not errorlevel 1 goto :running

        echo   %CW%Zostaną usunięte:%C0%
        if exist "%EXE%" echo     %CD%•%C0% program               %CD%%EXE%%C0%
        if exist "%APPDIR%" echo     %CD%•%C0% pliki programu i ustawienia  %CD%%APPDIR%%C0%
        echo     %CD%•%C0% skojarzenie plików .mapo (przestaną się otwierać dwuklikiem)
        echo     %CD%•%C0% ten skrypt
        echo.
        echo   %CG%Twoje mapy (*.mapo) i wyeksportowane obrazy zostaną nietknięte.%C0%
        echo.
        set "ANSWER="
        set /p "ANSWER=  Odinstalować MapoMyślacza? %CW%[T/N]%C0% "
        if /i "%ANSWER%"=="t" goto :remove
        if /i "%ANSWER%"=="tak" goto :remove
        goto :cancel

        :remove
        echo.
        <nul set /p "=  Usuwam skojarzenie plików .mapo... "
        reg delete "HKCU\Software\Classes\MapoMyslacz.Mapa" /f >nul 2>nul
        reg delete "HKCU\Software\Classes\.mapo\OpenWithProgids" /v "MapoMyslacz.Mapa" /f >nul 2>nul
        reg query "HKCU\Software\Classes\.mapo" /ve 2>nul | find /i "MapoMyslacz.Mapa" >nul
        if not errorlevel 1 reg delete "HKCU\Software\Classes\.mapo" /f >nul 2>nul
        ie4uinit.exe -show >nul 2>nul
        echo %CG%gotowe%C0%

        <nul set /p "=  Usuwam pliki programu i ustawienia... "
        if exist "%APPDIR%" rmdir /s /q "%APPDIR%" 2>nul
        if exist "%APPDIR%" goto :failed
        echo %CG%gotowe%C0%

        <nul set /p "=  Usuwam program... "
        if exist "%EXE%" del /f /q "%EXE%" 2>nul
        if exist "%EXE%.old" del /f /q "%EXE%.old" 2>nul
        if exist "%EXE%" goto :failed
        echo %CG%gotowe%C0%

        echo.
        echo   %CG%MapoMyślacz został odinstalowany. Dzięki za wspólne myślenie!%C0%
        echo   %CD%(Ten skrypt też zaraz zniknie.)%C0%
        echo.
        echo   %CD%Naciśnij dowolny klawisz, żeby zamknąć…%C0%
        pause >nul
        (goto) 2>nul & del "%~f0"

        :running
        echo   %CY%MapoMyślacz jest teraz uruchomiony.%C0%
        echo   Zamknij go — najpierw zapisz mapy! — i uruchom ten plik jeszcze raz.
        echo.
        echo   %CD%Naciśnij dowolny klawisz, żeby zamknąć…%C0%
        pause >nul
        exit /b 1

        :cancel
        echo.
        echo   Anulowano — nic nie zostało usunięte.
        echo.
        echo   %CD%Naciśnij dowolny klawisz, żeby zamknąć…%C0%
        pause >nul
        exit /b 0

        :failed
        echo %CR%nie udało się%C0%
        echo.
        echo   %CR%Któryś plik jest zajęty.%C0% Zamknij MapoMyślacza i spróbuj ponownie
        echo   albo usuń ręcznie:  %APPDIR%
        echo                       %EXE%
        echo.
        echo   %CD%Naciśnij dowolny klawisz, żeby zamknąć…%C0%
        pause >nul
        exit /b 1

        """;
}
