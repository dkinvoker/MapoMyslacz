namespace MapoMyslacz.Core.Model;

/// <summary>Wyliczony kształt linii: krzywe, końce z kierunkami (do grotów) i miejsce na podpis.</summary>
public sealed record LinkPath(IReadOnlyList<Cubic> Curves, Vec Start, Vec End, Vec StartDir, Vec EndDir, Vec LabelAt,
    Vec? Handle1, Vec? Handle2);

/// <summary>Prostokąt i kształt kafelka — wszystko, czego potrzeba do wyznaczenia końców linii.</summary>
public readonly record struct NodeOutline(NodeShape Shape, Box Box)
{
    public Vec Center => Box.Center;
    public Vec BoundaryToward(Vec p) => GeometryMath.BoundaryPoint(Shape, Box, p);
}

/// <summary>Prowadzenie strzałek (relacji) i gałęzi drzewa.</summary>
public static class LinkRouting
{
    /// <summary>
    /// Domyślna krzywizna (gdy użytkownik jej nie ustawił): punkty kontrolne wzdłuż dominującej osi —
    /// łagodne „S”, jak w popularnych programach do map myśli.
    /// </summary>
    public static (Vec C1, Vec C2) DefaultControls(Vec from, Vec to)
    {
        var d = to - from;
        return Math.Abs(d.X) >= Math.Abs(d.Y)
            ? (new Vec(d.X * 0.45, 0), new Vec(-d.X * 0.45, 0))
            : (new Vec(0, d.Y * 0.45), new Vec(0, -d.Y * 0.45));
    }

    public static (Vec C1, Vec C2) Controls(Link link, Vec from, Vec to)
    {
        var (d1, d2) = DefaultControls(from, to);
        return (link.C1 ?? d1, link.C2 ?? d2);
    }

    public static LinkPath Route(Link link, LinkCurve curve, NodeOutline from, NodeOutline to)
    {
        var fc = from.Center;
        var tc = to.Center;

        if (link.Via is { Count: > 0 } via)
        {
            var start = from.BoundaryToward(via[0]);
            var end = to.BoundaryToward(via[^1]);
            var pts = new List<Vec>(via.Count + 2) { start };
            pts.AddRange(via);
            pts.Add(end);
            var curves = curve == LinkCurve.Straight
                ? pts.Zip(pts.Skip(1), Cubic.Line).ToList()
                : GeometryMath.CatmullRom(pts);
            return Finish(curves, null, null);
        }

        if (curve == LinkCurve.Straight)
        {
            var start = from.BoundaryToward(tc);
            var end = to.BoundaryToward(fc);
            return Finish([Cubic.Line(start, end)], null, null);
        }

        var (c1, c2) = Controls(link, fc, tc);
        var p1 = fc + c1;
        var p2 = tc + c2;
        var s = from.BoundaryToward(p1.Equals(fc) ? tc : p1);
        var e = to.BoundaryToward(p2.Equals(tc) ? fc : p2);
        // punkt kontrolny schowany w kafelku dałby pętelkę przy krawędzi — wypychamy go na zewnątrz
        var q1 = PushOut(fc, s, p1);
        var q2 = PushOut(tc, e, p2);
        return Finish([new Cubic(s, q1, q2, e)], p1, p2);
    }

    private static Vec PushOut(Vec center, Vec boundary, Vec control)
    {
        double rb = Vec.Distance(center, boundary);
        var dir = control - center;
        if (dir.Length >= rb + 12) return control;
        var n = dir.LengthSquared < 1e-9 ? (boundary - center).Normalized() : dir.Normalized();
        return boundary + n * 12;
    }

    private static LinkPath Finish(List<Cubic> curves, Vec? h1, Vec? h2)
    {
        var first = curves[0];
        var last = curves[^1];
        Vec label;
        if (curves.Count % 2 == 1) label = curves[curves.Count / 2].PointAt(0.5);
        else label = curves[curves.Count / 2].P0;
        return new LinkPath(curves, first.P0, last.P3,
            (-first.TangentAt(0)).Normalized(), last.TangentAt(1).Normalized(), label, h1, h2);
    }

    /// <summary>
    /// Przeciągnięcie środka krzywej o <paramref name="delta"/>: środek krzywej Béziera to (P0 + 3·P1 + 3·P2 + P3)/8,
    /// więc przesunięcie obu punktów kontrolnych o 4/3·delta przesuwa go dokładnie o delta.
    /// </summary>
    public static void Bend(Link link, Vec from, Vec to, Vec delta)
    {
        var (c1, c2) = Controls(link, from, to);
        link.C1 = Round(c1 + delta * (4.0 / 3));
        link.C2 = Round(c2 + delta * (4.0 / 3));
    }

    public static Vec Round(Vec v) => new(Math.Round(v.X, 1), Math.Round(v.Y, 1));

    /// <summary>Gałąź drzewa od rodzica do dziecka: z boku (gdy dziecko jest obok) albo z góry/dołu.</summary>
    public static Cubic Branch(NodeOutline parent, NodeOutline child)
    {
        var pb = parent.Box;
        var cb = child.Box;
        const double far = 1e5;
        if (cb.Left >= pb.Right + 2 || cb.Right <= pb.Left - 2)
        {
            int side = cb.Center.X >= pb.Center.X ? 1 : -1;
            var s = parent.BoundaryToward(pb.Center + new Vec(side * far, 0));
            var e = child.BoundaryToward(cb.Center - new Vec(side * far, 0));
            double k = (e.X - s.X) * 0.5;
            return new Cubic(s, s + new Vec(k, 0), e - new Vec(k, 0), e);
        }
        else
        {
            int side = cb.Center.Y >= pb.Center.Y ? 1 : -1;
            var s = parent.BoundaryToward(pb.Center + new Vec(0, side * far));
            var e = child.BoundaryToward(cb.Center - new Vec(0, side * far));
            double k = (e.Y - s.Y) * 0.5;
            return new Cubic(s, s + new Vec(0, k), e - new Vec(0, k), e);
        }
    }
}
