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
            var curves = curve == LinkCurve.Curved
                ? GeometryMath.CatmullRom(pts)
                : pts.Zip(pts.Skip(1), Cubic.Line).ToList();
            return Finish(curves, null, null);
        }

        if (curve == LinkCurve.Elbow) return Finish(Elbow(link, from, to), null, null);

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

    /// <summary>
    /// Czy łamana idzie najpierw w poziomie (z boku kafelka na bok): gdy odstęp między krawędziami kafelków jest
    /// większy w poziomie niż w pionie. Kafelek wyraźnie pod drugim dostaje więc łącznik „jak w drzewie
    /// genealogicznym” (z dołu do góry), a kafelek obok — z boku na bok.
    /// </summary>
    public static bool ElbowHorizontal(Box from, Box to)
    {
        double gapX = Math.Max(to.Left - from.Right, from.Left - to.Right);
        double gapY = Math.Max(to.Top - from.Bottom, from.Top - to.Bottom);
        return gapX >= gapY;
    }

    /// <summary>
    /// Łamana pod kątem prostym: bok → środek → bok (albo góra/dół), z zaokrąglonymi narożnikami.
    /// Środkowy odcinek leży w połowie drogi, przesunięty o <see cref="Link.C1"/> (X albo Y — zależnie od kierunku).
    /// </summary>
    public static List<Cubic> Elbow(Link link, NodeOutline from, NodeOutline to)
    {
        const double far = 1e5;
        var fc = from.Center;
        var tc = to.Center;
        var offset = link.C1 ?? Vec.Zero;
        Vec s, e, m1, m2;
        if (ElbowHorizontal(from.Box, to.Box))
        {
            int side = tc.X >= fc.X ? 1 : -1;
            s = from.BoundaryToward(fc + new Vec(side * far, 0));
            e = to.BoundaryToward(tc - new Vec(side * far, 0));
            double mx = (s.X + e.X) / 2 + offset.X;
            m1 = new Vec(mx, s.Y);
            m2 = new Vec(mx, e.Y);
        }
        else
        {
            int side = tc.Y >= fc.Y ? 1 : -1;
            s = from.BoundaryToward(fc + new Vec(0, side * far));
            e = to.BoundaryToward(tc - new Vec(0, side * far));
            double my = (s.Y + e.Y) / 2 + offset.Y;
            m1 = new Vec(s.X, my);
            m2 = new Vec(e.X, my);
        }
        if (Vec.Distance(m1, m2) < 1) return [Cubic.Line(s, e)]; // w jednej linii — po prostu prosta
        return RoundedPolyline([s, m1, m2, e], 10);
    }

    /// <summary>Łamana z zaokrąglonymi narożnikami (promień nie większy niż połowa krótszego odcinka).</summary>
    public static List<Cubic> RoundedPolyline(IReadOnlyList<Vec> pts, double radius)
    {
        var result = new List<Cubic>();
        var cur = pts[0];
        for (int i = 1; i < pts.Count - 1; i++)
        {
            var corner = pts[i];
            var a = corner - pts[i - 1];
            var b = pts[i + 1] - corner;
            double r = Math.Min(radius, Math.Min(a.Length, b.Length) / 2);
            if (r < 0.5)
            {
                result.Add(Cubic.Line(cur, corner));
                cur = corner;
                continue;
            }
            var da = a.Normalized();
            var db = b.Normalized();
            var p0 = corner - da * r;
            var p3 = corner + db * r;
            if (Vec.Distance(cur, p0) > 0.01) result.Add(Cubic.Line(cur, p0));
            result.Add(new Cubic(p0, corner - da * (r * 0.45), corner + db * (r * 0.45), p3));
            cur = p3;
        }
        result.Add(Cubic.Line(cur, pts[^1]));
        return result;
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

    /// <summary>Odległość wspólnego „pnia” gałęzi łamanych od rodzica (połowa zwykłego odstępu dziecka od rodzica).</summary>
    public const double TrunkOffset = 28;

    /// <summary>
    /// Gałąź drzewa w wybranym kształcie: łuk (<see cref="Branch(NodeOutline, NodeOutline)"/>), prosta od krawędzi do
    /// krawędzi albo łamana pod kątem prostym. Łamana: gdy dziecko jest bardziej obok niż pod rodzicem — z boku
    /// rodzica krótki odcinek do pnia, wzdłuż pnia do wysokości dziecka i do jego boku; gdy jest bardziej pod (nad) —
    /// z dołu (góry) rodzica, wzdłuż poziomej belki i do góry (dołu) dziecka, jak w drzewie genealogicznym. Pień stoi
    /// w stałej odległości od rodzica, więc wszystkie dzieci dzielą jeden.
    /// </summary>
    public static List<Cubic> BranchPath(NodeOutline parent, NodeOutline child, LinkCurve shape)
    {
        if (shape != LinkCurve.Elbow)
        {
            var curve = Branch(parent, child);
            return shape == LinkCurve.Straight ? [Cubic.Line(curve.P0, curve.P3)] : [curve];
        }
        var pb = parent.Box;
        var cb = child.Box;
        const double far = 1e5;
        double gapX = Math.Max(cb.Left - pb.Right, pb.Left - cb.Right);
        double gapY = Math.Max(cb.Top - pb.Bottom, pb.Top - cb.Bottom);
        if (gapY > gapX)
        {
            int dir = cb.Center.Y >= pb.Center.Y ? 1 : -1;
            var s = parent.BoundaryToward(pb.Center + new Vec(0, dir * far));
            var e = child.BoundaryToward(cb.Center - new Vec(0, dir * far));
            if (Math.Abs(e.X - s.X) < 0.5) return [Cubic.Line(s, e)];
            double my = s.Y + dir * Math.Min(Math.Abs(e.Y - s.Y) / 2, TrunkOffset);
            return RoundedPolyline([s, new Vec(s.X, my), new Vec(e.X, my), e], 8);
        }
        else
        {
            int side = cb.Center.X >= pb.Center.X ? 1 : -1;
            var s = parent.BoundaryToward(pb.Center + new Vec(side * far, 0));
            var e = child.BoundaryToward(cb.Center - new Vec(side * far, 0));
            if (Math.Abs(e.Y - s.Y) < 0.5) return [Cubic.Line(s, e)];
            double mx = s.X + side * Math.Min(Math.Abs(e.X - s.X) / 2, TrunkOffset);
            return RoundedPolyline([s, new Vec(mx, s.Y), new Vec(mx, e.Y), e], 8);
        }
    }

    /// <summary>Czy gałąź wychodzi z boku rodzica (dziecko obok), a nie z góry albo z dołu (dziecko nad / pod nim).</summary>
    private static bool BranchSideways(Box pb, Box cb) => cb.Left >= pb.Right + 2 || cb.Right <= pb.Left - 2;

    /// <summary>Gałąź drzewa od rodzica do dziecka: z boku (gdy dziecko jest obok) albo z góry/dołu.</summary>
    public static Cubic Branch(NodeOutline parent, NodeOutline child)
    {
        var pb = parent.Box;
        var cb = child.Box;
        const double far = 1e5;
        if (BranchSideways(pb, cb))
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
