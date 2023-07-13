using System.Xml;
using Newtonsoft.Json;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Readers;

public class Component
{
    public string ID { get; }
    public string URL { get; }
    public string Path { get; }
    public long InstallSize { get; }
    public string Hash { get; }
    public string[] Depends { get; }

    public Component(XmlNode node)
    {
        // ID

        XmlNode workingNode = node.ParentNode;
        string id = node.Attributes["id"].Value;

        while (workingNode != null && workingNode.Name != "list")
        {
            if (workingNode.Attributes != null && workingNode.Name != "list")
            {
                id = $"{workingNode.Attributes["id"].Value}-{id}";
            }

            workingNode = workingNode.ParentNode;
        }

        ID = id;

        // URL

        string url = node.OwnerDocument.SelectSingleNode("/list").Attributes["url"].Value;
        if (!url.EndsWith("/")) url += "/";

        URL = url + $"{ID}.zip";

        // Path

        try
        {
            Path = node.Attributes["path"].Value;
        }
        catch
        {
            Path = "";
        }

        // InstallSize

        InstallSize = long.Parse(node.Attributes["install-size"].Value);

        // Hash

        Hash = node.Attributes["hash"].Value;

        try
        {
            Depends = node.Attributes["depends"].Value.Split(' ');
        }
        catch
        {
            Depends = Array.Empty<string>();
        }
    }
}

class Config
{
    public string XmlSource { get; set; }
    public string OutputUnzipped { get; set; }
    public string OutputZipped { get; set; }
    public bool LogFile { get; set; }
}

namespace fpz
{
    public static partial class Program
    {
        static readonly Config config = JsonConvert.DeserializeObject<Config>(File.ReadAllText("config.json"));

        async static Task Main(string[] args)
        {
            Write("Process started");

            HttpClient client = new();
            XmlDocument xml = new();

            xml.LoadXml(await client.GetStringAsync(config.XmlSource));

            try { Directory.Delete(config.OutputUnzipped, true); } catch { }
            Directory.CreateDirectory(config.OutputUnzipped);

            foreach (var node in xml.SelectNodes("/list/category[@id='core']//component").OfType<XmlNode>())
            {
                Component component = new(node);
                if (component.InstallSize == 0) continue;

                Write($"Downloading {component.ID}...");

                MemoryStream stream = new(await client.GetByteArrayAsync(component.URL));

                Write($"Extracting {component.ID}...");

                List<string> infoContents = new()
                {
                    string.Join(" ", new[] { component.Hash, $"{component.InstallSize}" }.Concat(component.Depends).ToArray())
                };

                using (var reader = ZipArchive.Open(stream).ExtractAllEntries())
                {
                    while (reader.MoveToNextEntry())
                    {
                        if (reader.Entry.IsDirectory) continue;

                        string destPath = Path.Combine(config.OutputUnzipped, component.Path.Replace('/', Path.DirectorySeparatorChar));

                        Directory.CreateDirectory(destPath);

                        reader.WriteEntryToDirectory(destPath, new ExtractionOptions
                        {
                            ExtractFullPath = true,
                            Overwrite = true,
                            PreserveFileTime = true
                        });

                        infoContents.Add(Path.Combine(component.Path, reader.Entry.Key).Replace('/', Path.DirectorySeparatorChar));
                    }
                }

                string infoDir = Path.Combine(config.OutputUnzipped, "Components");

                Directory.CreateDirectory(infoDir);
                File.WriteAllLines(Path.Combine(infoDir, component.ID), infoContents);
            }

            Write("Creating zipped file...");

            try { File.Delete(config.OutputZipped); } catch { }

            using (var archive = ZipArchive.Create())
            {
                archive.AddAllFromDirectory(config.OutputUnzipped);
                archive.SaveTo(config.OutputZipped, CompressionType.Deflate);
            }

            Write("Process finished");
        }

        public static void Write(string text)
        {
            var time = DateTime.Now;
            text = $"[{time.ToShortDateString()} {time.ToShortTimeString()}] {text}";

            Console.WriteLine(text);

            if (config.LogFile)
            {
                using StreamWriter writer = File.AppendText("fpz.log");
                writer.WriteLine(text);
            }
        }
    }
}