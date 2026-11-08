using MapoMyslacz.Core.Import;
using MapoMyslacz.Core.Model;

// XmindConvert <plik.xmind>... [--out folder]
// Każdy arkusz zapisuje jako osobny plik .mapo (nazwa pliku, a przy kilku arkuszach: „plik - arkusz.mapo”).

var inputs = new List<string>();
string? outDir = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--out" && i + 1 < args.Length) outDir = args[++i];
    else inputs.Add(args[i]);
}
if (inputs.Count == 0)
{
    Console.Error.WriteLine("Użycie: XmindConvert <plik.xmind>... [--out folder]");
    return 1;
}

int code = 0;
foreach (var input in inputs)
{
    try
    {
        var maps = XmindImporter.Import(input);
        var dir = outDir ?? Path.GetDirectoryName(Path.GetFullPath(input))!;
        var baseName = Path.GetFileNameWithoutExtension(input);
        for (int i = 0; i < maps.Count; i++)
        {
            var name = maps.Count == 1 ? baseName : $"{baseName} - {Safe(maps[i].Title ?? $"arkusz {i + 1}")}";
            var path = Path.Combine(dir, name + MindMap.FileExtension);
            MapSerializer.Save(maps[i], path);
            Console.WriteLine($"{input} → {path}  ({maps[i].Nodes.Count} kafelków, {maps[i].Links.Count} strzałek)");
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{input}: {(ex is InvalidDataException or IOException ? ex.Message : ex.ToString())}");
        code = 2;
    }
}
return code;

static string Safe(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
