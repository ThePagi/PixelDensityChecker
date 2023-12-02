using nifly;
using System.Diagnostics;
using CommandLine;

internal class Program
{
    public class Args
    {
        [Option('f', "filter", Required = false, Default = "*.nif", HelpText = "A pattern that filters input files. Default is '*.nif'. Only supports literals, * for any sequence of characters, ? for zero or one character. Example to read only grounds: *ground*.nif")]
        public String FileFilter { get; set; }
        [Option('e', "exclude", Required = false, Default = new string[] { "_lod", "lod.", "\\sky\\" }, HelpText = "Input one or more names. Excludes files that the names in their path. By default contains '_lod', 'lod.' and '\\sky\\'.")]
        public IEnumerable<string> Exclude { get; set; }
    }

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

    static (double, double) AnalyzeNif(string path, TextWriter w, Dictionary<string, (int, double, double, double)>  texVals)
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
            var texSize = 0;
            var texPath = nif.GetTexturePathByIndex(shape, 0);
            try
            {
                var tex = DirectXTexNet.TexHelper.Instance.LoadFromDDSFile(texPath, DirectXTexNet.DDS_FLAGS.NONE);
                texSize = Math.Max(tex.GetMetadata().Width, tex.GetMetadata().Height);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Can't load texture {texPath}.");
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
            w.Write(",{0}", texPath);
            if (texSize > 0)
            {
                min *= texSize;
                med *= texSize;
                max *= texSize;
                texTotalMin = Math.Min(min, texTotalMin);
                texTotalMax = Math.Max(max, texTotalMax);
                texTotalAvg += med;
                w.Write(",{0}", min.ToString("0.00e+0"));
                w.Write(",{0}", med.ToString("0.00e+0"));
                w.Write(",{0}", max.ToString("0.00e+0"));
                (int, double, double, double) vals;
                if (texVals.TryGetValue(texPath, out vals))
                {
                    vals.Item1 += 1;
                    vals.Item2 = Math.Min(min, vals.Item2);
                    vals.Item3 += med;
                    vals.Item4 = Math.Max(max, vals.Item4);
                    texVals[texPath] = vals;
                }
                else {
                    vals.Item1 = 1;
                    vals.Item2 = min;
                    vals.Item3 = med;
                    vals.Item4 = max;
                    texVals.Add(texPath, vals);
                }
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
    private static void Main(string[] rawArgs)
    {
        var args = Parser.Default.ParseArguments<Args>(rawArgs).Value;

        var results = new List<(String, double, double)>();
        var out_path = "pixel_density.csv";
        var out_path_sorted = "pixel_density_sorted.csv";
        var out_path_sorted2 = "pixel_density_sorted_textures.csv";
        var out_path_tex = "pixel_density_by_texture.csv";
        var texVals = new Dictionary<string, (int, double, double, double)>();
        using (TextWriter writer = new StreamWriter(out_path))
        {
            Console.WriteLine(">>> Starting search for all .nif recursively from {0}", Directory.GetCurrentDirectory());
            Console.WriteLine(">>> Raw results values are calculated only using UVs, results with textures multiple by texture resolution too...");
            writer.WriteLine("File,MeshShape,Minimum,Median,Maximum,Texture,TexMinimum,TexMedian,TexMaximum");
            string[] paths;
            try
            {
                paths = Directory.GetFiles(".", args.FileFilter, SearchOption.AllDirectories);
            }
            catch (Exception e)
            {
                Console.WriteLine($"{e.Message}");
                return;
            }
            foreach (var path in paths)
            {
                if (args.Exclude.Any(str => path.Contains(str)))
                {
                    Console.WriteLine("Ignoring {0}", path);
                }
                else
                {
                    Console.WriteLine("Processing {0}", path);
                    var (avg, tex_avg) = AnalyzeNif(path, writer, texVals);
                    results.Add((path, avg, tex_avg));
                }
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
        using (TextWriter writer = new StreamWriter(out_path_tex))
        {
            writer.WriteLine("Texture,Shape Count,Minimum,Average,Maximum");
            var texLst = texVals.Select(kv => (kv.Key, (kv.Value.Item1, kv.Value.Item2, kv.Value.Item3/kv.Value.Item1, kv.Value.Item4))).OrderBy(kv => kv.Item2.Item3).ToList();
            foreach (var (k, v) in texLst) {
                writer.WriteLine("{0},{1},{2},{3},{4}", k, v.Item1, v.Item2.ToString("0.00e+0"), (v.Item3 / v.Item1).ToString("0.00e+0"), v.Item4.ToString("0.00e+0"));
            }
        }
        Console.WriteLine(">>> Result files available in {0}, {1}, {2}, {3}, opening {2} now!", out_path, out_path_sorted, out_path_sorted2, out_path_tex);
        ProcessStartInfo psi = new ProcessStartInfo();
        //psi.WorkingDirectory = Directory.GetCurrentDirectory();
        psi.FileName = out_path_sorted2;
        psi.UseShellExecute = true;
        Process.Start(psi);
    }
}

