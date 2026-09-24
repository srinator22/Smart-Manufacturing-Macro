// Purpose: Render every ribbon icon master in the workspace to the theme- and size-specific PNGs the
//   add-ins embed, verify that the committed PNGs still match their masters, or draw a preview sheet.
// Inputs: projects/<project>/assets/icons/<icon>-16.xaml and <icon>-32.xaml masters; the command line
//   (no argument renders, --check verifies, --preview <path> writes a contact sheet).
// Outputs: projects/<project>/assets/ribbon/<icon>-<theme>-<size>.png; exit code 0 on success, 1 on a
//   stale, missing, extra, or invalid icon, 2 on a usage error.
// Dependencies: WPF XamlReader, DrawingVisual and RenderTargetBitmap; WmpRibbon.RibbonIconSelector for
//   the sizes, themes, and file names the add-ins request, so the two cannot drift apart.
// Assumptions: RenderTargetBitmap rasterizes in software, so the same .NET and WPF version produces the
//   same pixels on a developer machine and on the CI runner; --check therefore compares decoded pixels
//   exactly. Sizes below 32 px come from the 16-unit master and 32 px and above from the 32-unit
//   master, because a 16 px icon needs simplified shapes rather than a scaled-down 32; a 32 px small
//   icon (200% scale) therefore shares the large master's 32 px file.
// Validation source: palette sampled from Inventor 2027 dark-theme ribbon icons on 2026-09-23
//   (bodies #D8D8D8, accents #70C0F8-#88C8F8, gold #F8D080).

using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WmpRibbon;

namespace WmpIconRenderer;

internal static partial class Program
{
    private const int SmallGrid = 16;
    private const int LargeGrid = 32;
    private const double MinimumLongerFill = 28.0 / 32.0;
    private const double MinimumShorterFill = 26.0 / 32.0;

    private static readonly IReadOnlyList<int> RenderedSizes =
        [.. RibbonIconSelector.SmallSizes.Union(RibbonIconSelector.LargeSizes).Order()];

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string root = FindRepositoryRoot();
            IReadOnlyList<IconMaster> masters = IconMaster.Discover(root);
            if (args.Length == 0)
            {
                Render(masters);
                return 0;
            }

            if (args.Length == 1 && args[0] == "--check")
            {
                return Check(masters, root);
            }

            if (args.Length == 2 && args[0] == "--preview")
            {
                WritePng(Preview(masters), Path.GetFullPath(args[1]));
                Console.WriteLine($"icons: preview written to {Path.GetFullPath(args[1])}");
                return 0;
            }

            Console.Error.WriteLine("usage: WmpIconRenderer [--check | --preview <path>]");
            return 2;
        }
        catch (IconMasterException exception)
        {
            Console.Error.WriteLine($"CHECK FAILED: {exception.Message}");
            return 1;
        }
    }

    private static void Render(IReadOnlyList<IconMaster> masters)
    {
        int written = 0;
        foreach (IconMaster master in masters)
        {
            Directory.CreateDirectory(master.OutputDirectory);
            foreach ((string path, BitmapSource image) in master.Outputs())
            {
                WritePng(image, path);
                written++;
            }
        }

        Console.WriteLine($"icons: rendered {written} PNGs from {masters.Count} masters");
    }

    private static int Check(IReadOnlyList<IconMaster> masters, string root)
    {
        List<string> problems = [];
        HashSet<string> expected = new(StringComparer.OrdinalIgnoreCase);
        foreach (IconMaster master in masters)
        {
            foreach ((string path, BitmapSource image) in master.Outputs())
            {
                expected.Add(path);
                if (!File.Exists(path))
                {
                    problems.Add($"{Relative(root, path)} is missing");
                }
                else if (!Pixels(image).AsSpan().SequenceEqual(Pixels(ReadPng(path))))
                {
                    problems.Add($"{Relative(root, path)} does not match its master");
                }
            }
        }

        foreach (string ribbonDirectory in Directory.GetDirectories(Path.Combine(root, "projects"))
            .Select(project => Path.Combine(project, "assets", "ribbon"))
            .Where(Directory.Exists))
        {
            foreach (string png in Directory.GetFiles(ribbonDirectory, "*.png"))
            {
                if (!expected.Contains(Path.GetFullPath(png)))
                {
                    problems.Add($"{Relative(root, png)} has no master");
                }
            }
        }

        if (problems.Count > 0)
        {
            foreach (string problem in problems)
            {
                Console.Error.WriteLine($"CHECK FAILED: {problem}");
            }

            Console.Error.WriteLine("Regenerate with: dotnet run --project shared/WmpIconRenderer");
            return 1;
        }

        Console.WriteLine($"icons: OK ({expected.Count} PNGs match {masters.Count} masters)");
        return 0;
    }

    private static RenderTargetBitmap Preview(IReadOnlyList<IconMaster> masters)
    {
        const int Pad = 12;
        int rowHeight = RenderedSizes.Max() + (2 * Pad);
        int width = RenderedSizes.Sum() + ((RenderedSizes.Count + 1) * Pad);
        int height = rowHeight * masters.Count * RibbonIconSelector.Themes.Count;

        DrawingVisual visual = new();
        using (DrawingContext context = visual.RenderOpen())
        {
            int y = 0;
            foreach (RibbonTheme theme in RibbonIconSelector.Themes)
            {
                Brush background = new SolidColorBrush(Palette.RibbonBackground(theme));
                foreach (IconMaster master in masters)
                {
                    context.DrawRectangle(background, null, new Rect(0, y, width, rowHeight));
                    int x = Pad;
                    foreach (int size in RenderedSizes)
                    {
                        context.DrawImage(master.Render(theme, size), new Rect(x, y + Pad, size, size));
                        x += size + Pad;
                    }

                    y += rowHeight;
                }
            }
        }

        RenderTargetBitmap sheet = new(width, height, 96, 96, PixelFormats.Pbgra32);
        sheet.Render(visual);
        return sheet;
    }

    internal static BitmapSource Rasterize(DrawingGroup drawing, int grid, int size)
    {
        DrawingVisual visual = new();
        using (DrawingContext context = visual.RenderOpen())
        {
            double scale = size / (double)grid;
            context.PushTransform(new ScaleTransform(scale, scale));
            context.PushClip(new RectangleGeometry(new Rect(0, 0, grid, grid)));
            context.DrawDrawing(drawing);
        }

        RenderTargetBitmap bitmap = new(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] Pixels(BitmapSource image)
    {
        FormatConvertedBitmap converted = new(image, PixelFormats.Bgra32, null, 0);
        int stride = converted.PixelWidth * 4;
        byte[] pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static BitmapFrame ReadPng(string path)
    {
        using FileStream stream = File.OpenRead(path);
        BitmapDecoder decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
    }

    private static void WritePng(BitmapSource image, string path)
    {
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0)));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "InventorScripts.sln")))
            {
                return directory.FullName;
            }
        }

        throw new IconMasterException("InventorScripts.sln was not found above the current directory; run from the repository.");
    }

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private sealed class IconMaster
    {
        private readonly Dictionary<RibbonTheme, DrawingGroup> small = [];
        private readonly Dictionary<RibbonTheme, DrawingGroup> large = [];

        private IconMaster(string name, string outputDirectory)
        {
            Name = name;
            OutputDirectory = outputDirectory;
        }

        public string Name { get; }

        public string OutputDirectory { get; }

        public static List<IconMaster> Discover(string root)
        {
            List<IconMaster> masters = [];
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            foreach (string project in Directory.GetDirectories(Path.Combine(root, "projects")).Order(StringComparer.Ordinal))
            {
                string iconDirectory = Path.Combine(project, "assets", "icons");
                if (!Directory.Exists(iconDirectory))
                {
                    continue;
                }

                string[] files = Directory.GetFiles(iconDirectory, "*.xaml");
                foreach (IGrouping<string, string> icon in files
                    .GroupBy(file => MasterName(root, file), StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal))
                {
                    if (!names.Add(icon.Key))
                    {
                        throw new IconMasterException($"icon name '{icon.Key}' is defined by more than one project");
                    }

                    string smallPath = Path.Combine(iconDirectory, $"{icon.Key}-{SmallGrid}.xaml");
                    string largePath = Path.Combine(iconDirectory, $"{icon.Key}-{LargeGrid}.xaml");
                    foreach (string required in new[] { smallPath, largePath }.Where(path => !File.Exists(path)))
                    {
                        throw new IconMasterException($"{Relative(root, required)} is missing; every icon needs a 16- and a 32-unit master");
                    }

                    IconMaster master = new(icon.Key, Path.GetFullPath(Path.Combine(project, "assets", "ribbon")));
                    foreach (RibbonTheme theme in RibbonIconSelector.Themes)
                    {
                        master.small[theme] = Load(root, smallPath, SmallGrid, theme);
                        master.large[theme] = Load(root, largePath, LargeGrid, theme);
                    }

                    masters.Add(master);
                }
            }

            if (masters.Count == 0)
            {
                throw new IconMasterException("no icon masters were found under projects/*/assets/icons");
            }

            return masters;
        }

        public IEnumerable<(string Path, BitmapSource Image)> Outputs()
        {
            foreach (RibbonTheme theme in RibbonIconSelector.Themes)
            {
                foreach (int size in RenderedSizes)
                {
                    yield return (Path.Combine(OutputDirectory, RibbonIconSelector.FileName(Name, theme, size)), Render(theme, size));
                }
            }
        }

        public BitmapSource Render(RibbonTheme theme, int size) => size < LargeGrid
            ? Rasterize(small[theme], SmallGrid, size)
            : Rasterize(large[theme], LargeGrid, size);

        private static string MasterName(string root, string file)
        {
            Match match = MasterFileName().Match(Path.GetFileName(file));
            if (!match.Success)
            {
                throw new IconMasterException($"{Relative(root, file)} must be named <icon>-16.xaml or <icon>-32.xaml with a lowercase kebab-case icon name");
            }

            return match.Groups["name"].Value;
        }

        private static DrawingGroup Load(string root, string path, int grid, RibbonTheme theme)
        {
            string xaml = File.ReadAllText(path);
            string filled = Token().Replace(xaml, match => Palette.Color(theme, match.Groups["slot"].Value)
                ?? throw new IconMasterException($"{Relative(root, path)} uses unknown palette slot '{match.Groups["slot"].Value}'"));
            if (filled.Contains("{{", StringComparison.Ordinal) || filled.Contains("}}", StringComparison.Ordinal))
            {
                throw new IconMasterException($"{Relative(root, path)} has a malformed palette token");
            }

            if (XamlReader.Parse(filled) is not DrawingGroup drawing)
            {
                throw new IconMasterException($"{Relative(root, path)} must have a DrawingGroup root");
            }

            Rect bounds = drawing.Bounds;
            const double Tolerance = 0.01;
            if (bounds.IsEmpty || bounds.Left < -Tolerance || bounds.Top < -Tolerance
                || bounds.Right > grid + Tolerance || bounds.Bottom > grid + Tolerance)
            {
                throw new IconMasterException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Relative(root, path)} draws at {bounds}, outside its 0,0 to {grid},{grid} grid"));
            }

            // Inventor's own ribbon icons use nearly the whole square; a drawing that leaves wide
            // margins reads as undersized next to them (the first WMP icons spanned 20 to 25 of 32).
            double longer = Math.Max(bounds.Width, bounds.Height);
            double shorter = Math.Min(bounds.Width, bounds.Height);
            if (longer < grid * MinimumLongerFill - Tolerance || shorter < grid * MinimumShorterFill - Tolerance)
            {
                throw new IconMasterException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Relative(root, path)} fills {bounds.Width:0.##} x {bounds.Height:0.##} of its {grid}-unit grid; the longer side must reach {grid * MinimumLongerFill:0.##} and the shorter side {grid * MinimumShorterFill:0.##}"));
            }

            drawing.Freeze();
            return drawing;
        }
    }

    [GeneratedRegex(@"^(?<name>[a-z0-9]+(-[a-z0-9]+)*)-(16|32)\.xaml$", RegexOptions.CultureInvariant)]
    private static partial Regex MasterFileName();

    [GeneratedRegex(@"\{\{(?<slot>[a-z]+)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Token();
}

internal sealed class IconMasterException(string message) : Exception(message);
