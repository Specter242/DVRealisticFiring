using System;
using System.Collections.Generic;
using System.Linq;
using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class CoalRockTests
{
    private static IEnumerable<(CoalRock.Kind Kind, CoalRock.Shape Shape)> Many()
    {
        var random = new Random(588030);
        foreach (var kind in new[] { CoalRock.Kind.Block, CoalRock.Kind.Broken, CoalRock.Kind.Slab })
            for (var i = 0; i < 200; i++)
                yield return (kind, CoalRock.Make(random, kind));
    }

    private static (double X, double Y, double Z) Point(CoalRock.Shape s, int i) =>
        (s.Points[3 * i], s.Points[3 * i + 1], s.Points[3 * i + 2]);

    [Fact]
    public void EveryEdgeIsSharedByTwoFacesRunningOppositeWays()
    {
        foreach (var (kind, shape) in Many())
        {
            var edges = new Dictionary<(int, int), int>();
            var t = shape.Triangles;
            for (var k = 0; k < t.Length; k += 3)
                foreach (var e in new[] { (t[k], t[k + 1]), (t[k + 1], t[k + 2]), (t[k + 2], t[k]) })
                    edges[e] = edges.TryGetValue(e, out var n) ? n + 1 : 1;
            Assert.All(edges, e => Assert.Equal(1, e.Value));
            Assert.All(edges.Keys, e => Assert.True(edges.ContainsKey((e.Item2, e.Item1)), $"{kind}: an open edge"));
            // A closed solid of genus 0: V - E + F = 2.
            Assert.Equal(2, shape.PointCount - edges.Count / 2 + t.Length / 3);
        }
    }

    [Fact]
    public void FacesTurnOutward()
    {
        foreach (var (kind, shape) in Many())
        {
            var volume = 0.0;
            var t = shape.Triangles;
            for (var k = 0; k < t.Length; k += 3)
            {
                var a = Point(shape, t[k]);
                var b = Point(shape, t[k + 1]);
                var c = Point(shape, t[k + 2]);
                volume += a.X * (b.Y * c.Z - b.Z * c.Y) - a.Y * (b.X * c.Z - b.Z * c.X) + a.Z * (b.X * c.Y - b.Y * c.X);
            }
            Assert.True(volume / 6 > 0.1, $"{kind}: volume {volume / 6:F3}");
        }
    }

    [Fact]
    public void SizedToItsLongestWayAboutItsMiddle()
    {
        foreach (var (kind, shape) in Many())
        {
            var extents = Enumerable.Range(0, 3).Select(axis =>
            {
                var values = Enumerable.Range(0, shape.PointCount).Select(i => shape.Points[3 * i + axis]).ToArray();
                Assert.InRange(values.Max() + values.Min(), -1e-4, 1e-4);
                return values.Max();
            }).ToArray();
            Assert.InRange(extents.Max(), 1 - 1e-4, 1 + 1e-4);
        }
    }

    [Fact]
    public void LighterThanTheRocksBefore()
    {
        foreach (var (kind, shape) in Many())
            Assert.InRange(shape.Triangles.Length / 3, 12, 60);
    }

    [Fact]
    public void SlabsAreThinAndMostLieFlat()
    {
        var slabs = Many().Where(s => s.Kind == CoalRock.Kind.Slab).Select(s => s.Shape).ToList();
        var flat = 0;
        foreach (var slab in slabs)
        {
            var heights = Enumerable.Range(0, slab.PointCount).Select(i => slab.Points[3 * i + 1]).ToArray();
            if (heights.Max() < 0.55)
                flat++;
        }
        Assert.InRange(flat / (double)slabs.Count, 0.75, 0.95);
    }

    [Fact]
    public void TheSameSeedMakesTheSameRock()
    {
        var a = CoalRock.Make(new Random(7), CoalRock.Kind.Broken);
        var b = CoalRock.Make(new Random(7), CoalRock.Kind.Broken);
        Assert.Equal(a.Points, b.Points);
        Assert.Equal(a.Triangles, b.Triangles);
    }
}
