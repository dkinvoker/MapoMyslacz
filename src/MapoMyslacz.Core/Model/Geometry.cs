using System.Text.Json.Serialization;

namespace MapoMyslacz.Core.Model;

/// <summary>Punkt/wektor 2D w układzie mapy.</summary>
public readonly record struct Vec(double X, double Y)
{
    public static readonly Vec Zero = new(0, 0);

    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec operator -(Vec a) => new(-a.X, -a.Y);
    public static Vec operator *(Vec a, double k) => new(a.X * k, a.Y * k);
    public static Vec operator *(double k, Vec a) => new(a.X * k, a.Y * k);
    public static Vec operator /(Vec a, double k) => new(a.X / k, a.Y / k);

    [JsonIgnore] public double Length => Math.Sqrt(X * X + Y * Y);
    [JsonIgnore] public double LengthSquared => X * X + Y * Y;

    public Vec Normalized()
    {
        var l = Length;
        return l < 1e-9 ? Zero : new Vec(X / l, Y / l);
    }

    /// <summary>Wektor prostopadły (obrót o 90° zgodnie z ruchem wskazówek w układzie ekranu).</summary>
    [JsonIgnore] public Vec Perpendicular => new(-Y, X);

    public static double Dot(Vec a, Vec b) => a.X * b.X + a.Y * b.Y;
    public static double Cross(Vec a, Vec b) => a.X * b.Y - a.Y * b.X;
    public static double Distance(Vec a, Vec b) => (a - b).Length;
    public static Vec Lerp(Vec a, Vec b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    public override string ToString() => FormattableString.Invariant($"({X:0.##}, {Y:0.##})");
}

/// <summary>Prostokąt (lewy górny róg + rozmiar).</summary>
public readonly record struct Box(double X, double Y, double Width, double Height)
{
    public static Box FromCenter(double cx, double cy, double w, double h) => new(cx - w / 2, cy - h / 2, w, h);
    public static Box FromCorners(Vec a, Vec b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public Vec Center => new(X + Width / 2, Y + Height / 2);

    public bool Contains(Vec p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;
    public bool Intersects(Box o) => o.X <= Right && o.Right >= X && o.Y <= Bottom && o.Bottom >= Y;
    public bool ContainsBox(Box o) => o.X >= X && o.Right <= Right && o.Y >= Y && o.Bottom <= Bottom;
    public Box Inflate(double d) => new(X - d, Y - d, Width + 2 * d, Height + 2 * d);
    public Box Offset(Vec d) => new(X + d.X, Y + d.Y, Width, Height);

    public static Box Union(Box a, Box b)
    {
        double l = Math.Min(a.X, b.X), t = Math.Min(a.Y, b.Y);
        return new Box(l, t, Math.Max(a.Right, b.Right) - l, Math.Max(a.Bottom, b.Bottom) - t);
    }

    public static Box? Union(IEnumerable<Box> boxes)
    {
        Box? r = null;
        foreach (var b in boxes) r = r is { } x ? Union(x, b) : b;
        return r;
    }
}

/// <summary>Odcinek krzywej Béziera trzeciego stopnia.</summary>
public readonly record struct Cubic(Vec P0, Vec P1, Vec P2, Vec P3)
{
    public Vec PointAt(double t)
    {
        double u = 1 - t;
        double a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
        return new Vec(a * P0.X + b * P1.X + c * P2.X + d * P3.X, a * P0.Y + b * P1.Y + c * P2.Y + d * P3.Y);
    }

    /// <summary>Kierunek styczny w punkcie t (nieznormalizowany).</summary>
    public Vec TangentAt(double t)
    {
        double u = 1 - t;
        var v = 3 * u * u * (P1 - P0) + 6 * u * t * (P2 - P1) + 3 * t * t * (P3 - P2);
        // zdegenerowane punkty kontrolne (pokrywają się z końcami) — bierzemy kierunek cięciwy
        return v.LengthSquared < 1e-12 ? P3 - P0 : v;
    }

    public static Cubic Line(Vec a, Vec b) => new(a, Vec.Lerp(a, b, 1.0 / 3), Vec.Lerp(a, b, 2.0 / 3), b);

    /// <summary>Przybliżenie łamaną (do trafiania myszką i obliczeń).</summary>
    public IEnumerable<Vec> Flatten(int segments = 24)
    {
        for (int i = 0; i <= segments; i++) yield return PointAt(i / (double)segments);
    }
}

public static class GeometryMath
{
    /// <summary>Odległość punktu od odcinka.</summary>
    public static double DistanceToSegment(Vec p, Vec a, Vec b)
    {
        var ab = b - a;
        double len2 = ab.LengthSquared;
        if (len2 < 1e-12) return Vec.Distance(p, a);
        double t = Math.Clamp(Vec.Dot(p - a, ab) / len2, 0, 1);
        return Vec.Distance(p, a + ab * t);
    }

    /// <summary>Odległość punktu od ciągu krzywych.</summary>
    public static double DistanceToCurves(Vec p, IReadOnlyList<Cubic> curves)
    {
        double best = double.MaxValue;
        foreach (var c in curves)
        {
            Vec? prev = null;
            foreach (var q in c.Flatten())
            {
                if (prev is { } pr) best = Math.Min(best, DistanceToSegment(p, pr, q));
                prev = q;
            }
        }
        return best;
    }

    /// <summary>
    /// Krzywa Catmulla-Roma przez podane punkty, zamieniona na odcinki Béziera (gładka linia przez wszystkie punkty).
    /// </summary>
    public static List<Cubic> CatmullRom(IReadOnlyList<Vec> pts)
    {
        var result = new List<Cubic>();
        if (pts.Count < 2) return result;
        if (pts.Count == 2)
        {
            result.Add(Cubic.Line(pts[0], pts[1]));
            return result;
        }
        for (int i = 0; i < pts.Count - 1; i++)
        {
            var p0 = pts[Math.Max(0, i - 1)];
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var p3 = pts[Math.Min(pts.Count - 1, i + 2)];
            result.Add(new Cubic(p1, p1 + (p2 - p0) / 6, p2 - (p3 - p1) / 6, p2));
        }
        return result;
    }

    /// <summary>
    /// Punkt na obrysie kształtu kafelka w kierunku od jego środka do <paramref name="toward"/>.
    /// Dzięki temu strzałki i gałęzie zaczynają się dokładnie na krawędzi (także rombu i elipsy).
    /// </summary>
    public static Vec BoundaryPoint(NodeShape shape, Box box, Vec toward)
    {
        var c = box.Center;
        var d = toward - c;
        if (d.LengthSquared < 1e-9) d = new Vec(1, 0);
        double a = box.Width / 2, b = box.Height / 2;
        if (a <= 0 || b <= 0) return c;
        switch (shape)
        {
            case NodeShape.Ellipse:
            {
                double t = 1 / Math.Sqrt(d.X * d.X / (a * a) + d.Y * d.Y / (b * b));
                return c + d * t;
            }
            case NodeShape.Diamond:
            {
                double t = 1 / (Math.Abs(d.X) / a + Math.Abs(d.Y) / b);
                return c + d * t;
            }
            case NodeShape.Hexagon:
                return RayPolygon(c, d, HexagonPoints(box)) ?? RectBoundary(c, d, a, b);
            case NodeShape.Pill:
            {
                // prostokąt z półkolami po bokach
                var r = RectBoundary(c, d, a, b);
                double rad = b, cx = a - rad;
                var local = r - c;
                if (Math.Abs(local.X) > cx && cx >= 0)
                {
                    // trafiliśmy w zaokrąglony koniec — przecięcie z okręgiem o środku (±cx, 0)
                    var oc = new Vec(Math.Sign(local.X) * cx, 0);
                    var dir = d.Normalized();
                    double bq = Vec.Dot(dir, -oc), cq = oc.LengthSquared - rad * rad;
                    double disc = bq * bq - cq;
                    if (disc >= 0) return c + dir * (-bq + Math.Sqrt(disc));
                }
                return r;
            }
            default:
                return RectBoundary(c, d, a, b);
        }
    }

    private static Vec RectBoundary(Vec c, Vec d, double a, double b)
    {
        double tx = Math.Abs(d.X) < 1e-12 ? double.MaxValue : a / Math.Abs(d.X);
        double ty = Math.Abs(d.Y) < 1e-12 ? double.MaxValue : b / Math.Abs(d.Y);
        return c + d * Math.Min(tx, ty);
    }

    /// <summary>Wierzchołki sześciokąta wpisanego w prostokąt (ostre rogi po lewej i prawej).</summary>
    public static Vec[] HexagonPoints(Box box)
    {
        double inset = Math.Min(box.Height * 0.45, box.Width / 3);
        return
        [
            new(box.Left, box.Center.Y), new(box.Left + inset, box.Top), new(box.Right - inset, box.Top),
            new(box.Right, box.Center.Y), new(box.Right - inset, box.Bottom), new(box.Left + inset, box.Bottom),
        ];
    }

    private static Vec? RayPolygon(Vec origin, Vec dir, Vec[] poly)
    {
        double best = double.MaxValue;
        for (int i = 0; i < poly.Length; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Length];
            var e = b - a;
            double den = Vec.Cross(dir, e);
            if (Math.Abs(den) < 1e-12) continue;
            double t = Vec.Cross(a - origin, e) / den;
            double u = Vec.Cross(a - origin, dir) / den;
            if (t > 0 && u >= -1e-9 && u <= 1 + 1e-9) best = Math.Min(best, t);
        }
        return best < double.MaxValue ? origin + dir * best : null;
    }

    /// <summary>Czy punkt leży wewnątrz kształtu kafelka.</summary>
    public static bool ShapeContains(NodeShape shape, Box box, Vec p)
    {
        if (!box.Contains(p)) return false;
        var c = box.Center;
        double a = box.Width / 2, b = box.Height / 2;
        double dx = (p.X - c.X) / a, dy = (p.Y - c.Y) / b;
        return shape switch
        {
            NodeShape.Ellipse => dx * dx + dy * dy <= 1.0001,
            NodeShape.Diamond => Math.Abs(dx) + Math.Abs(dy) <= 1.0001,
            _ => true,
        };
    }
}
