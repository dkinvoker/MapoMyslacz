using MapoMyslacz.Core.Model;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using XCanvas = Microsoft.UI.Xaml.Controls.Canvas;

namespace MapoMyslacz.Editor;

/// <summary>Eksport całej mapy do PNG (niezależnie od tego, co widać w oknie).</summary>
public sealed partial class MapCanvas
{
    /// <summary>
    /// Buduje osobną kopię mapy (bez zaznaczeń i uchwytów) w ukrytym miejscu drzewa XAML, renderuje ją
    /// <see cref="RenderTargetBitmap"/> i zapisuje jako PNG. <paramref name="scale"/> 2 = ostry wydruk/ekran HiDPI.
    /// </summary>
    public async Task ExportPngAsync(string path, double scale = 2, double margin = 40)
    {
        CommitEdit();
        if (ContentBounds() is not { } content) throw new InvalidOperationException("Mapa jest pusta — nie ma czego eksportować.");
        var b = content.Inflate(margin);
        // ograniczenie rozmiaru bitmapy (pamięć i limit tekstur karty graficznej)
        scale = Math.Min(scale, 8000 / Math.Max(b.Width, b.Height));
        int pw = (int)Math.Ceiling(b.Width * scale), ph = (int)Math.Ceiling(b.Height * scale);

        var root = new XCanvas { Width = pw, Height = ph, Background = Xaml.Brush(_canvasColor) };
        var content2 = new XCanvas
        {
            RenderTransform = new CompositeTransform { ScaleX = scale, ScaleY = scale, TranslateX = -b.X * scale, TranslateY = -b.Y * scale },
        };
        var branchLayer = new XCanvas();
        var linkLayer = new XCanvas();
        var nodeLayer = new XCanvas();
        content2.Children.Add(branchLayer);
        content2.Children.Add(linkLayer);
        content2.Children.Add(nodeLayer);
        root.Children.Add(content2);

        var idx = _session.Index;
        var copies = new Dictionary<string, NodeVisual>();
        foreach (var n in _session.Map.Nodes)
        {
            if (!_nodes.ContainsKey(n.Id)) continue;
            var v = new NodeVisual(n.Id);
            nodeLayer.Children.Add(v.Root);
            UpdateNode(idx, n, v);
            copies[n.Id] = v;
        }
        foreach (var n in _session.Map.Nodes)
        {
            if (n.Parent is null || !copies.TryGetValue(n.Id, out var cv) || !copies.TryGetValue(n.Parent, out var pv)) continue;
            branchLayer.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = Xaml.Geometry([LinkRouting.Branch(pv.Outline, cv.Outline)]),
                Stroke = Xaml.Brush(cv.Style!.BranchColor),
                StrokeThickness = idx.Get(n.Parent)?.Parent is null ? 3 : 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });
        }
        foreach (var l in _session.Map.Links)
        {
            if (!copies.TryGetValue(l.From, out var f) || !copies.TryGetValue(l.To, out var t)) continue;
            var lv = new LinkVisual(l.Id);
            lv.AddTo(linkLayer);
            var s = Styles.Resolve(l.Style);
            lv.Update(l, s, LinkRouting.Route(l, s.Curve, f.Outline, t.Outline), _canvasColor);
        }

        _exportHost.Children.Add(root);
        try
        {
            root.UpdateLayout();
            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(root, pw, ph);
            var pixels = await rtb.GetPixelsAsync();
            var bytes = new byte[pixels.Length];
            DataReader.FromBuffer(pixels).ReadBytes(bytes);

            using var ms = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ms);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, 96 * scale, 96 * scale, bytes);
            await encoder.FlushAsync();

            ms.Seek(0);
            var png = new byte[ms.Size];
            using (var reader = new DataReader(ms.GetInputStreamAt(0)))
            {
                await reader.LoadAsync((uint)ms.Size);
                reader.ReadBytes(png);
            }
            await File.WriteAllBytesAsync(path, png);
        }
        finally
        {
            _exportHost.Children.Remove(root);
        }
    }
}
