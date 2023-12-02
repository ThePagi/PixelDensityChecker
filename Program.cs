using nifly;
using System.Diagnostics;

internal class Program
{
    static (double, double, double) AnalyzeShape(vectorTriangle tris, vectorVector3 verts, vectorVector2 uvs)
    {
        var results = new List<double>();
        foreach (var t in tris)
        {
            var v1 = verts[t.p1];
            var v2 = verts[t.p2];
            var v3 = verts[t.p3];
            var uv1 = uvs[t.p1];
            var uv2 = uvs[t.p2];
            var uv3 = uvs[t.p3];
            // make vectors going from one point to the other two
            var cross = v2.opSub(v1).cross(v3.opSub(v1));
            // length of their cross product is 2x the area of the triangle
            var area = cross.length() / 2.0;
            var uv_dir1 = uv2.opSub(uv1);
            var uv_dir2 = uv3.opSub(uv1);
            var uv_cross = new Vector3(uv_dir1.u, uv_dir1.v, 0).cross(new Vector3(uv_dir2.u, uv_dir2.v, 0));
            var uv_area = uv_cross.length() / 2.0;
            results.Add(uv_area / area);
            //Console.WriteLine("{0}, {1}, {2}", area, uv_area, uv_area / area);
        }
        if (results.Count == 0)
            return (double.MaxValue, 0, 0); // ignore the min and max yes
        results.Sort();
        //Console.WriteLine((results[0], results[results.Count / 2], results[results.Count - 1]));
        return (results[0], results[results.Count / 2], results[results.Count - 1]);
    }

    static (double, double) AnalyzeNif(string path, TextWriter w)
    {
        var nif = new NifFile();
        if (nif.Load(path) != 0)
        {
            Console.WriteLine("Error loading file '{0}'", path);
            return (0, 0);
        }

        var tris = new vectorTriangle();
        var totalMin = double.MaxValue;
        var totalAvg = 0.0;
        var totalMax = 0.0;
        var texTotalMin = double.MaxValue;
        var texTotalAvg = 0.0;
        var texTotalMax = 0.0;
        foreach (var shape in nif.GetShapes())
        {
            if (!shape.HasUVs() || !shape.HasVertices())
                continue;
            shape.GetTriangles(tris);
            var verts = nif.GetVertsForShape(shape);
            var uvs = nif.GetUvsForShape(shape);
            var tex_size = 0;
            var tex_path = nif.GetTexturePathByIndex(shape, 0);
            try
            {
                var tex = DirectXTexNet.TexHelper.Instance.LoadFromDDSFile(tex_path, DirectXTexNet.DDS_FLAGS.NONE);
                tex_size = Math.Max(tex.GetMetadata().Width, tex.GetMetadata().Height);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Can't load texture {tex_path}.");
            }
            var (min, med, max) = AnalyzeShape(tris, verts, uvs);
            totalMin = Math.Min(min, totalMin);
            totalMax = Math.Max(max, totalMax);
            totalAvg += med;
            w.Write("{0}", path);
            w.Write(",{0}", shape.name.get());
            w.Write(",{0}", min.ToString("0.00e+0"));
            w.Write(",{0}", med.ToString("0.00e+0"));
            w.Write(",{0}", max.ToString("0.00e+0"));
            w.Write(",{0}", tex_path);
            if (tex_size > 0)
            {
                min *= tex_size;
                med *= tex_size;
                max *= tex_size;
                texTotalMin = Math.Min(min, texTotalMin);
                texTotalMax = Math.Max(max, texTotalMax);
                texTotalAvg += med;
                w.Write(",{0}", min.ToString("0.00e+0"));
                w.Write(",{0}", med.ToString("0.00e+0"));
                w.Write(",{0}", max.ToString("0.00e+0"));
            }
            else {
                w.Write(",,,");
            }
            w.WriteLine();
        }
        if (nif.GetShapes().Count > 0)
        {
            totalAvg /= nif.GetShapes().Count;
            texTotalAvg /= nif.GetShapes().Count;
            w.Write("{0},{1},{2},{3},{4}", path, "ALL_SHAPES", totalMin.ToString("0.00e+0"), totalAvg.ToString("0.00e+0"), totalMax.ToString("0.00e+0"));
            if (texTotalMax > 0.0)
            {
                w.Write(",{0},{1},{2},{3}", "ALL_TEXTURES", texTotalMin.ToString("0.00e+0"), texTotalAvg.ToString("0.00e+0"), texTotalMax.ToString("0.00e+0"));
            }
            else
            {
                w.Write(",,,,");
            }
            w.WriteLine();
        }
        return (totalAvg, texTotalAvg);
    }
    private static void Main(string[] args)
    {
        if (args.Length > 0)
        {
            AnalyzeNif(args[0], Console.Out);
        }
        var results = new List<(String, double, double)>();
        var out_path = "pixel_density.csv";
        var out_path_sorted = "pixel_density_sorted.csv";
        var out_path_sorted2 = "pixel_density_sorted_textures.csv";
        using (TextWriter writer = new StreamWriter(out_path))
        {
            Console.WriteLine(">>> Starting search for all .nif recursively from {0}", Directory.GetCurrentDirectory());
            Console.WriteLine(">>> Raw results values are calculated only using UVs, results with textures multiple by texture resolution too...");
            writer.WriteLine("File,MeshShape,Minimum,Median,Maximum,Texture,TexMinimum,TexMedian,TexMaximum");
            string[] paths;
            try
            {
                paths = Directory.GetFiles(".", "*.nif", SearchOption.AllDirectories);
            }
            catch (Exception e)
            {
                Console.WriteLine($"{e.Message}");
                return;
            }
            foreach (var path in paths)
            {
                Console.WriteLine("Processing {0}", path);
                var (avg, tex_avg) = AnalyzeNif(path, writer);
                results.Add((path, avg, tex_avg));
            }
        }
        results.Sort((one, two) => one.Item2.CompareTo(two.Item2));
        using (TextWriter writer = new StreamWriter(out_path_sorted))
        {
            writer.WriteLine("File,Raw Average,Texture Average");
            foreach (var res in results)
            {
                if (res.Item2 > 0)
                    writer.WriteLine("{0},{1},{2}", res.Item1, res.Item2.ToString("0.00e+0"), res.Item3.ToString("0.00e+0"));
            }
        }
        results.Sort((one, two) => one.Item3.CompareTo(two.Item3));
        using (TextWriter writer = new StreamWriter(out_path_sorted2))
        {
            writer.WriteLine("File,Raw Average,Texture Average");
            foreach (var res in results)
            {
                if (res.Item3 > 0)
                    writer.WriteLine("{0},{1},{2}", res.Item1, res.Item2.ToString("0.00e+0"), res.Item3.ToString("0.00e+0"));
            }
        }
        Console.WriteLine(">>> Result files available in {0}, {1}, {2}, opening {0} now!", out_path, out_path_sorted, out_path_sorted2);
        ProcessStartInfo psi = new ProcessStartInfo();
        //psi.WorkingDirectory = Directory.GetCurrentDirectory();
        psi.FileName = out_path;
        psi.UseShellExecute = true;
        Process.Start(psi);
    }
}

