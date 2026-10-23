using System.Windows;

namespace TikzEdt.Modern;

internal sealed record LaboPathStep(Point End,Point? C1=null,Point? C2=null);
internal static class LaboTubePath
{
    public static IReadOnlyList<LaboPathStep> Steps(LaboScene scene,LaboTube tube)
    {
        var points=scene.TubePoints(tube);
        if(tube.Route=="Curve") return [new(points[0]),new(points[3],points[1],points[2])];
        var clean=new List<Point>();
        foreach(var point in points)
        {
            if(clean.Count>0 && (point-clean[^1]).Length<1e-8)continue;
            if(clean.Count>1)
            {
                var a=clean[^1]-clean[^2];var b=point-clean[^1];
                if(Math.Abs(Vector.CrossProduct(a,b))<1e-8 && Vector.Multiply(a,b)>=0)clean.RemoveAt(clean.Count-1);
            }
            clean.Add(point);
        }
        var result=new List<LaboPathStep>{new(clean[0])};
        for(var i=1;i<clean.Count-1;i++)
        {
            var a=clean[i-1]-clean[i];var b=clean[i+1]-clean[i];var trim=Math.Min(tube.Bend,Math.Min(a.Length,b.Length)/2);
            if(trim<1e-8){result.Add(new(clean[i]));continue;}
            a.Normalize();b.Normalize();var before=clean[i]+a*trim;var after=clean[i]+b*trim;
            result.Add(new(before));result.Add(new(after,before+(clean[i]-before)*(2d/3),after+(clean[i]-after)*(2d/3)));
        }
        if(clean.Count>1)result.Add(new(clean[^1]));return result;
    }
}
