using System.Runtime.InteropServices;
using Microsoft.UI.Input;

namespace MapoMyslacz.Services;

/// <summary>
/// Kursory „łapek” do przesuwania płótna: otwarta dłoń (można chwycić) i zaciśnięta (przesuwanie trwa).
/// Windows ma wbudowaną tylko dłoń ze wskazującym palcem (od linków), a WinUI 3 wczytuje własne kursory wyłącznie
/// z zasobów modułu — dlatego leżą w Assets\Cursors.dll (biblioteka bez kodu, z samymi zasobami; robi ją
/// tools\make-cursors.ps1). Gdy pliku brakuje, wracamy do kursorów systemowych.
/// </summary>
internal static class HandCursors
{
    private const uint OpenHandId = 1, GrabHandId = 2;
    private static InputCursor? _open, _grab;
    private static bool _loaded;

    public static InputCursor? Get(bool grab)
    {
        if (!_loaded)
        {
            _loaded = true;
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Cursors.dll");
                // moduł musi być załadowany, żeby system znalazł w nim zasoby kursorów
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out _))
                {
                    _open = InputDesktopResourceCursor.CreateFromModule(path, OpenHandId);
                    _grab = InputDesktopResourceCursor.CreateFromModule(path, GrabHandId);
                }
            }
            catch
            {
                _open = _grab = null;
            }
        }
        return grab ? _grab : _open;
    }
}
