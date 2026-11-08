namespace MapoMyslacz.Tests;

/// <summary>Geometria: obrysy kształtów, krzywe, prowadzenie strzałek i gałęzi.</summary>
public class GeometryTests
{
    private static readonly Box B = new(-50, -20, 100, 40);

    [Theory]
    [InlineData(NodeShape.Rectangle)]
    [InlineData(NodeShape.Rounded)]
    [InlineData(NodeShape.Ellipse)]
    [InlineData(NodeShape.Diamond)]
    [InlineData(NodeShape.Hexagon)]
    [InlineData(NodeShape.Pill)]
    public void BoundaryPoint_LiesOnOutline(NodeShape shape)
    {
        foreach (var target in new[] { new Vec(500, 0), new Vec(0, -500), new Vec(300, 300), new Vec(-400, 100) })
        {
            var p = GeometryMath.BoundaryPoint(shape, B, target);
            // tuż przed punktem jest wnętrze, tuż za nim — zewnętrze
            var dir = (target - B.Center).Normalized();
            Assert.True(Inside(shape, p - dir * 0.5), $"{shape}: {p} powinien być na krawędzi (od środka)");
            Assert.False(Inside(shape, p + dir * 0.5), $"{shape}: {p} powinien być na krawędzi (na zewnątrz)");
        }
    }

    private static bool Inside(NodeShape shape, Vec p) => shape switch
    {
        NodeShape.Hexagon => PointInPolygon(p, GeometryMath.HexagonPoints(B)),
        NodeShape.Pill => InPill(p),
        _ => GeometryMath.ShapeContains(shape, B, p),
    };

    private static bool InPill(Vec p)
    {
        double r = B.Height / 2, cx = B.Width / 2 - r;
        if (Math.Abs(p.X) <= cx) return Math.Abs(p.Y) <= r;
        var c = new Vec(Math.Sign(p.X) * cx, 0);
        return Vec.Distance(p, c) <= r;
    }

    private static bool PointInPolygon(Vec p, Vec[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        return inside;
    }

    [Fact]
    public void ShapeContains_DiamondCornersAreOutside()
    {
        Assert.True(GeometryMath.ShapeContains(NodeShape.Diamond, B, B.Center));
        Assert.False(GeometryMath.ShapeContains(NodeShape.Diamond, B, new Vec(45, 18)));
        Assert.True(GeometryMath.ShapeContains(NodeShape.Rectangle, B, new Vec(45, 18)));
    }

    [Fact]
    public void Cubic_EndpointsAndMidpoint()
    {
        var c = new Cubic(new Vec(0, 0), new Vec(0, 100), new Vec(100, 100), new Vec(100, 0));
        Assert.Equal(new Vec(0, 0), c.PointAt(0));
        Assert.Equal(new Vec(100, 0), c.PointAt(1));
        Assert.Equal(new Vec(50, 75), c.PointAt(0.5));
        Assert.True(c.TangentAt(0).Y > 0);
    }

    [Fact]
    public void CatmullRom_PassesThroughPoints()
    {
        var pts = new[] { new Vec(0, 0), new Vec(100, 50), new Vec(200, -20), new Vec(300, 10) };
        var curves = GeometryMath.CatmullRom(pts);
        Assert.Equal(3, curves.Count);
        for (int i = 0; i < curves.Count; i++)
        {
            Assert.Equal(pts[i], curves[i].P0);
            Assert.Equal(pts[i + 1], curves[i].P3);
        }
    }

    [Fact]
    public void DistanceToSegment()
    {
        Assert.Equal(5, GeometryMath.DistanceToSegment(new Vec(5, 5), new Vec(0, 0), new Vec(10, 0)), 6);
        Assert.Equal(5, GeometryMath.DistanceToSegment(new Vec(-3, 4), new Vec(0, 0), new Vec(10, 0)), 6);
    }

    [Fact]
    public void Route_StartsAndEndsOnNodes()
    {
        var from = new NodeOutline(NodeShape.Rounded, Box.FromCenter(0, 0, 100, 40));
        var to = new NodeOutline(NodeShape.Diamond, Box.FromCenter(400, 200, 120, 60));
        var link = new Link { From = "a", To = "b" };
        var path = LinkRouting.Route(link, LinkCurve.Curved, from, to);
        Assert.True(Math.Abs(path.Start.X - 50) < 0.01, "start na prawej krawędzi");
        Assert.True(GeometryMath.ShapeContains(NodeShape.Diamond, to.Box.Inflate(1), path.End));
        Assert.True(path.EndDir.X > 0, "grot skierowany w stronę celu");
        Assert.NotNull(path.Handle1);

        var straight = LinkRouting.Route(link, LinkCurve.Straight, from, to);
        Assert.Single(straight.Curves);
        Assert.Null(straight.Handle1);
    }

    [Fact]
    public void Route_DefaultControls_FollowDominantAxis()
    {
        var (c1, c2) = LinkRouting.DefaultControls(new Vec(0, 0), new Vec(0, 300));
        Assert.Equal(0, c1.X);
        Assert.True(c1.Y > 0 && c2.Y < 0);
        var (h1, h2) = LinkRouting.DefaultControls(new Vec(0, 0), new Vec(-300, 50));
        Assert.True(h1.X < 0 && h2.X > 0);
        Assert.Equal(0, h1.Y);
    }

    [Fact]
    public void Route_WithWaypoints_PassesThroughThem()
    {
        var from = new NodeOutline(NodeShape.Rounded, Box.FromCenter(0, 0, 100, 40));
        var to = new NodeOutline(NodeShape.Rounded, Box.FromCenter(400, 0, 100, 40));
        var link = new Link { From = "a", To = "b", Via = [new Vec(200, 300)] };
        var path = LinkRouting.Route(link, LinkCurve.Curved, from, to);
        Assert.Equal(2, path.Curves.Count);
        Assert.Equal(new Vec(200, 300), path.Curves[0].P3);
    }

    [Fact]
    public void Bend_MovesCurveMidpointByDelta()
    {
        var from = new NodeOutline(NodeShape.Rectangle, Box.FromCenter(0, 0, 10, 10));
        var to = new NodeOutline(NodeShape.Rectangle, Box.FromCenter(600, 0, 10, 10));
        var link = new Link { From = "a", To = "b" };
        var before = LinkRouting.Route(link, LinkCurve.Curved, from, to).Curves[0];
        LinkRouting.Bend(link, from.Center, to.Center, new Vec(0, 90));
        var after = LinkRouting.Route(link, LinkCurve.Curved, from, to).Curves[0];
        // środek krzywej (t = 0,5) przesuwa się o tyle, o ile przeciągnięto (końce zmieniają się nieznacznie)
        var moved = after.PointAt(0.5) - before.PointAt(0.5);
        Assert.InRange(moved.Y, 80, 100);
    }

    [Fact]
    public void Branch_LeavesParentOnTheChildSide()
    {
        var parent = new NodeOutline(NodeShape.Rounded, Box.FromCenter(0, 0, 100, 40));
        var right = LinkRouting.Branch(parent, new NodeOutline(NodeShape.Rounded, Box.FromCenter(300, 80, 80, 30)));
        Assert.Equal(50, right.P0.X, 3);
        Assert.Equal(260, right.P3.X, 3);
        var left = LinkRouting.Branch(parent, new NodeOutline(NodeShape.Rounded, Box.FromCenter(-300, 0, 80, 30)));
        Assert.Equal(-50, left.P0.X, 3);
        var below = LinkRouting.Branch(parent, new NodeOutline(NodeShape.Rounded, Box.FromCenter(10, 200, 80, 30)));
        Assert.Equal(20, below.P0.Y, 3);
        Assert.Equal(185, below.P3.Y, 3);
    }

    [Fact]
    public void BoxUnionAndIntersection()
    {
        var u = Box.Union([new Box(0, 0, 10, 10), new Box(20, -5, 5, 5)])!.Value;
        Assert.Equal(new Box(0, -5, 25, 15), u);
        Assert.Null(Box.Union([]));
        Assert.True(new Box(0, 0, 10, 10).Intersects(new Box(5, 5, 10, 10)));
        Assert.False(new Box(0, 0, 10, 10).Intersects(new Box(11, 0, 5, 5)));
        Assert.Equal(new Box(0, 0, 10, 20), Box.FromCorners(new Vec(10, 20), new Vec(0, 0)));
    }
}
