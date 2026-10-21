using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;

using Ctr2.Assets;
using Ctr2.Desktop;
using Ctr2.Engine;
using Ctr2.Engine.Physics;

using SDL3;

using SkiaSharp;

try
{
    if (args.Length > 0 && args[0] == "audit-levels")
    {
        return LevelAuditCommand.Run(args);
    }
    if (args.Length > 0 && args[0] == "replay-baseline")
    {
        if (args.Length is not (2 or 4) || (args.Length == 4 && args[2] != "--write"))
        {
            throw new ArgumentException("replay-baseline requires a cache and optionally --write <file>.");
        }
        string[] lines = [.. JournalBaseline.RunAll(args[1]).Select(o => o.Line)];
        if (args.Length == 4)
        {
            File.WriteAllLines(args[3], ["# pack\tentry\tvariant\tstatus\tfirstInexactFrame\texactSamples\tsamples", .. lines]);
        }
        else
        {
            Array.ForEach(lines, Console.WriteLine);
        }
        return 0;
    }
    if (args.Length > 0 && args[0] == "replay-outcomes")
    {
        return ReplayOutcomesCommand.Run(args);
    }
    if (args.Length > 0 && args[0] == "replay-check")
    {
        return ReplayCheckCommand.Run(args);
    }
    if (args.Length > 0 && args[0] == "play-level")
    {
        return PlayLevelCommand.Run(args);
    }
    AtlasFrame? selectedFrame = null;

    if (args.Length >= 5 && args[0] == "preview-resource")
    {
        SelectedResource selected = ResourcePreview.Load(args[1], int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]));
        selectedFrame = selected.Frame;
        args = ["preview", selected.TexturePath, .. args[5..]];
    }

    if (args.Length == 3 && args[0] == "import")
    {
        using CancellationTokenSource cancel = new();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };
        ImportManifest manifest = GameImport.ImportWindows(args[1], args[2], Console.WriteLine, cancel.Token);
        Console.WriteLine($"Imported {manifest.Textures.Count} RAW textures into {Path.GetFullPath(args[2])}");
        return 0;
    }

    if (args.Length >= 4 && args[0] is "preview-animation" or "preview-level" or "simulate-level")
    {
        int width = 960, height = 720, frames = 0, resolution = 2, ticks = 0, releaseAt = -1;
        bool simulate = args[0] == "simulate-level";
        List<RopeCut> scheduledCuts = [];
        double seconds = 0;
        string? snapshot = null;
        for (int i = 4; i < args.Length; i += 2)
        {
            if (i + 1 == args.Length)
            {
                throw new ArgumentException("Missing option value.");
            }
            switch (args[i])
            {
                case "--width":
                    width = int.Parse(args[i + 1]);
                    break;
                case "--height":
                    height = int.Parse(args[i + 1]);
                    break;
                case "--frames":
                    frames = int.Parse(args[i + 1]);
                    break;
                case "--resolution":
                    resolution = int.Parse(args[i + 1]);
                    break;
                case "--cut" when simulate:
                    if (scheduledCuts.Count >= 1024)
                    {
                        throw new ArgumentException("Too many cuts.");
                    }

                    string[] fields = args[i + 1].Split(':');
                    if (fields.Length is not (5 or 7))
                    {
                        throw new ArgumentException("Cut format: tick:rope:segment:x:y[:impulseX:impulseY].");
                    }

                    int cutTick = int.Parse(fields[0], CultureInfo.InvariantCulture);
                    if (cutTick is < 0 or > 36000)
                    {
                        throw new ArgumentException("Cut tick must be 0–36000.");
                    }

                    scheduledCuts.Add(new(cutTick, int.Parse(fields[1], CultureInfo.InvariantCulture), int.Parse(fields[2], CultureInfo.InvariantCulture),
                        new(float.Parse(fields[3], CultureInfo.InvariantCulture), float.Parse(fields[4], CultureInfo.InvariantCulture)),
                        fields.Length == 7 ? new(float.Parse(fields[5], CultureInfo.InvariantCulture), float.Parse(fields[6], CultureInfo.InvariantCulture)) : Vector2.Zero));
                    break;
                case "--ticks" when simulate:
                    ticks = int.Parse(args[i + 1]);
                    break;
                case "--release-at" when simulate:
                    releaseAt = int.Parse(args[i + 1]);
                    break;
                case "--time":
                    seconds = double.Parse(args[i + 1], CultureInfo.InvariantCulture);
                    break;
                case "--snapshot":
                    snapshot = args[i + 1];
                    break;
                default:
                    throw new ArgumentException("Unknown option: " + args[i]);
            }
        }

        if (width < 1 || height < 1 || width > 8192 || height > 8192 || frames < 0 || !double.IsFinite(seconds) || seconds < 0)
        {
            throw new ArgumentException("Preview dimensions must be 1–8192; frames and time must be nonnegative and finite.");
        }

        if (ticks < 0 || ticks > 36000 || releaseAt < -1)
        {
            throw new ArgumentException("Physics ticks must be 0–36000; release tick must be -1 or nonnegative.");
        }

        bool isLevel = args[0] != "preview-animation";
        using AnimationPreview? animation = isLevel ? null : AnimationPreview.Load(args[1], int.Parse(args[2]), args[3], resolution);
        (LevelDefinition Definition, LevelPreview Preview)? selectedLevel = isLevel
            ? LevelPreview.Load(args[1], int.Parse(args[2]), int.Parse(args[3]), resolution, simulate) : null;
        using LevelPreview? levelPreview = selectedLevel?.Preview;
        Action<SKCanvas, int, int, double> draw = isLevel ? levelPreview!.Draw : animation!.Draw;
        string selection = isLevel ? selectedLevel!.Value.Definition.FileName : args[3];

        if (levelPreview != null)
        {
            Console.WriteLine($"Desktop level {args[2]}/{args[3]}: {selection}; logical canvas {levelPreview.Layout.Size.X}×{levelPreview.Layout.Size.Y}.");
            Console.WriteLine($"Diagnostic markers: {(simulate ? "candy uses original artwork" : "coral=candy")}, gold=star, wheat=grab/link, brown=terrain outline, magenta=unsupported object. Om Nom uses IDLE/default costume.");
            foreach (string diagnostic in levelPreview.Layout.Diagnostics)
            {
                if (!simulate || !diagnostic.StartsWith("Static layout only", StringComparison.Ordinal))
                {
                    Console.WriteLine(diagnostic);
                }
            }
        }

        SimulationClock physicsClock = new();
        RopeInput? ropeInput = simulate ? new(levelPreview!.Simulation!, levelPreview.Layout.Size) : null;
        SdlRopeInput? mouse = ropeInput != null ? new(ropeInput) : null;
        Queue<RopeCut> replay = new(scheduledCuts.OrderBy(c => c.Tick));
        void fixedStep()
        {
            CandySimulation scene = levelPreview!.Simulation!;
            while (replay.TryPeek(out RopeCut? cut) && cut.Tick == scene.Tick)
            {
                _ = replay.Dequeue();
                _ = scene.Cut(cut.Rope, cut.Segment, cut.Point, cut.Impulse);
            }
            ropeInput!.ApplyPending();
            int collected = scene.StarCollections.Count;
            scene.Step(releaseAt);
            for (int i = collected; i < scene.StarCollections.Count; i++)
            {
                StarCollection star = scene.StarCollections[i];
                Console.WriteLine($"Star collected: tick {star.Tick}, star {star.Star}, candy {star.Candy}.");
            }
        }
        if (simulate)
        {
            Console.WriteLine("Candy/rope diagnostic: fixed dt=0.0167, 15/30 solver iterations; static terrain contacts and physical cut fragments enabled. Drag the primary mouse button across a rope. Swept circle/static-terrain contacts, compound character/static-terrain contacts and stationary candy/star collection are enabled. Dynamic terrain, sleeping, polygon CCD, character/candy collision, intro, consumption and level completion are pending. --release-at removes candy attachments only.");
            for (int i = 0; i < ticks; i++)
            {
                fixedStep();
            }
            Console.WriteLine($"Physics tick {levelPreview!.Simulation!.Tick}.");
            Console.WriteLine($"Stars collected: {levelPreview.Simulation.StarCollections.Count}/{levelPreview.Simulation.Stars.Count}.");
        }

        if (snapshot != null)
        {
            using SKBitmap bitmap = new(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            using SKCanvas canvas = new(bitmap);
            draw(canvas, width, height, seconds);
            using SKData encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream file = new(snapshot, FileMode.CreateNew);
            encoded.SaveTo(file);
            Console.WriteLine($"Rendered scene {selection} at {seconds.ToString(CultureInfo.InvariantCulture)} seconds: {snapshot}");
            return 0;
        }

        int result = DesktopPreview.Run(width, height, frames, (canvas, w, h, elapsed) =>
        {
            if (simulate)
            {
                _ = physicsClock.AdvanceTo(elapsed, fixedStep);
            }
            draw(canvas, w, h, seconds + elapsed);
        }, mouse == null ? null : mouse.Handle);

        if (simulate)
        {
            Console.WriteLine($"Physics tick {levelPreview!.Simulation!.Tick}.");
            Console.WriteLine($"Stars collected: {levelPreview.Simulation.StarCollections.Count}/{levelPreview.Simulation.Stars.Count}.");
            foreach (RopeCut cut in ropeInput!.Cuts)
            {
                Console.WriteLine(FormattableString.Invariant($"Cut replay: --cut {cut.Tick}:{cut.Rope}:{cut.Segment}:{cut.Point.X:R}:{cut.Point.Y:R}:{cut.Impulse.X:R}:{cut.Impulse.Y:R}"));
            }
        }

        return result;
    }

    if (args.Length >= 2 && args[0] == "preview")
    {
        int width = 960, height = 720, frames = 0;
        string? snapshot = null, framePath = null;
        for (int i = 2; i < args.Length; i += 2)
        {
            if (i + 1 == args.Length)
            {
                throw new ArgumentException("Missing option value.");
            }

            switch (args[i])
            {
                case "--width":
                    width = int.Parse(args[i + 1]);
                    break;
                case "--height":
                    height = int.Parse(args[i + 1]);
                    break;
                case "--frames":
                    frames = int.Parse(args[i + 1]);
                    break;
                case "--snapshot":
                    snapshot = args[i + 1];
                    break;
                case "--frame":
                    framePath = args[i + 1];
                    break;
                default:
                    throw new ArgumentException("Unknown option: " + args[i]);
            }
        }
        if (width < 1 || height < 1 || width > 8192 || height > 8192 || frames < 0)
        {
            throw new ArgumentException("Preview dimensions must be 1–8192; frames must be nonnegative.");
        }

        using SKBitmap image = SKBitmap.Decode(args[1]) ?? throw new InvalidDataException("Cannot decode preview image.");
        if (selectedFrame != null && framePath != null)
        {
            throw new ArgumentException("A resource selection already supplies frame metadata; omit --frame.");
        }
        AtlasFrame? frame = framePath == null ? selectedFrame : (JsonSerializer.Deserialize<PreviewFrame>(File.ReadAllText(framePath))
            ?? throw new InvalidDataException("Empty atlas frame metadata.")).ToFrame();
        if (snapshot != null)
        {
            using SKBitmap bitmap = new(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            using SKCanvas canvas = new(bitmap);
            AssetPreview.Draw(canvas, image, width, height, frame);
            using SKData encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream file = new(snapshot, FileMode.CreateNew);
            encoded.SaveTo(file);
            Console.WriteLine($"Rendered and saved {width}×{height} preview: {snapshot}");
            return 0;
        }
        return DesktopPreview.Run(width, height, frames, (canvas, w, h, _) => AssetPreview.Draw(canvas, image, w, h, frame));
    }

    Console.WriteLine("CTR2 PC port foundation — local assets required.\n" +
        "  import <Windows dump folder | .appx | .msix | .zip> <new cache directory>\n" +
        "  audit-levels <cache>\n" +
        "  replay-baseline <cache> [--write file]\n" +
        "  replay-outcomes <cache> [--corrected]\n" +
        "  replay-check <cache> <pack> <entry> [--variant challenge|fruit_challenge] [--threshold meters] [--trace] [--corrected] [--follow journalIndex]\n" +
        "  play-level <cache> <one-based pack> <one-based level> [--ticks N] [--cut tick:rope:segment:x:y[:impulseX:impulseY]] [--variant challenge|fruit_challenge] [--replay] [preview options]\n" +
        "  simulate-level <cache> <one-based pack> <one-based level> [--ticks N] [--release-at tick] [--cut tick:rope:segment:x:y] [preview options]\n" +
        "  preview-level <cache> <one-based pack> <one-based level> [--resolution N] [--time seconds] [preview options]\n" +
        "  preview-animation <cache> <animation ID> <scene name> [--resolution N] [--time seconds] [preview options]\n" +
        "  preview-resource <cache> <resource ID> <resolution enum> <quad index> [preview options]\n" +
        "  preview <cached PNG> [--width N] [--height N] [--snapshot new.png] [--frames N] [--frame metadata.json]\n" +
        "Supply a dump from your own Windows game. Early play of supported levels is available; full game mechanics and presentation remain pending.");
    return args.Length == 0 || args is ["--help"] ? 0 : 1;
}

catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or InvalidOperationException or JsonException or OperationCanceledException or FormatException or OverflowException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

/// <summary>Owns the SDL window and presentation resources for the desktop texture preview.</summary>
internal static class DesktopPreview
{
    /// <summary>Runs the blocking SDL preview loop until quit or the requested frame limit.</summary>
    /// <param name="width">Initial window width in SDL window coordinates.</param>
    /// <param name="height">Initial window height in SDL window coordinates.</param>
    /// <param name="frameLimit">Number of frames to present, or zero to run until the window closes.</param>
    /// <param name="draw">Draw callback receiving the canvas, pixel dimensions, and elapsed seconds.</param>
    /// <returns>Zero after normal shutdown or completion of the frame limit.</returns>
    /// <remarks>
    /// Drawing uses the actual window pixel size after resizing or DPI changes. SDL resources created here are
    /// destroyed on exit; the draw callback retains ownership of its resources.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// SDL initialization, resource creation, or presentation fails.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Preview frame metadata is incompatible with the texture or canvas.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The drawing dimensions are invalid.</exception>
    /// <param name="input">Optional event callback receiving window and pixel dimensions.</param>
    /// <param name="title">Desktop window title.</param>
    public static int Run(int width, int height, int frameLimit, Action<SKCanvas, int, int, double> draw, Action<SDL.Event, int, int, int, int>? input = null, string title = "Cut the Rope 2 — asset preview")
    {
        if (!SDL.Init(SDL.InitFlags.Video))
        {
            throw new InvalidOperationException(SDL.GetError());
        }

        nint window = 0, renderer = 0, texture = 0;
        SKBitmap? pixels = null;
        SKCanvas? canvas = null;
        try
        {
            window = SDL.CreateWindow(title, width, height, SDL.WindowFlags.Resizable | SDL.WindowFlags.HighPixelDensity);
            if (window == 0)
            {
                throw new InvalidOperationException(SDL.GetError());
            }

            renderer = SDL.CreateRenderer(window, null!);
            if (renderer == 0)
            {
                throw new InvalidOperationException(SDL.GetError());
            }

            Stopwatch clock = Stopwatch.StartNew();
            int count = 0;
            bool quit = false;
            while (!quit && (frameLimit == 0 || count < frameLimit))
            {
                while (SDL.PollEvent(out SDL.Event evt))
                {
                    if (input != null)
                    {
                        Check(SDL.GetWindowSize(window, out int windowWidth, out int windowHeight));
                        Check(SDL.GetWindowSizeInPixels(window, out int pixelWidth, out int pixelHeight));
                        input(evt, windowWidth, windowHeight, pixelWidth, pixelHeight);
                    }
                    if (evt.Type == (uint)SDL.EventType.Quit || (evt.Type == (uint)SDL.EventType.KeyDown && evt.Key.Key == SDL.Keycode.Escape))
                    {
                        quit = true;
                    }
                }

                if (quit)
                {
                    break;
                }

                Check(SDL.GetWindowSizeInPixels(window, out int w, out int h));
                if (w <= 0 || h <= 0)
                {
                    SDL.Delay(16);
                    continue;
                }
                if (pixels == null || pixels.Width != w || pixels.Height != h)
                {
                    canvas?.Dispose();
                    pixels?.Dispose();
                    pixels = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
                    canvas = new SKCanvas(pixels);
                    if (texture != 0)
                    {
                        SDL.DestroyTexture(texture);
                        texture = 0;
                    }
                    texture = SDL.CreateTexture(renderer, SDL.PixelFormat.ABGR8888, SDL.TextureAccess.Streaming, w, h);
                    if (texture == 0)
                    {
                        throw new InvalidOperationException(SDL.GetError());
                    }

                    Check(SDL.SetTextureBlendMode(texture, SDL.BlendMode.None));
                }
                draw(canvas!, w, h, clock.Elapsed.TotalSeconds);
                Check(SDL.UpdateTexture(texture, 0, pixels.GetPixels(), pixels.RowBytes));
                Check(SDL.RenderTexture(renderer, texture, 0, 0));
                Check(SDL.RenderPresent(renderer));
                count++;
                SDL.Delay(16);
            }
            Console.WriteLine($"Presented {count} preview frames.");
            return 0;
        }
        finally
        {
            canvas?.Dispose();
            pixels?.Dispose();
            if (texture != 0)
            {
                SDL.DestroyTexture(texture);
            }

            if (renderer != 0)
            {
                SDL.DestroyRenderer(renderer);
            }

            if (window != 0)
            {
                SDL.DestroyWindow(window);
            }

            SDL.Quit();
        }
    }

    /// <summary>Converts a failed SDL operation into an exception containing the SDL error.</summary>
    /// <param name="result">True when the preceding SDL operation succeeded.</param>
    /// <exception cref="InvalidOperationException">The operation failed.</exception>
    private static void Check(bool result)
    {
        if (!result)
        {
            throw new InvalidOperationException(SDL.GetError());
        }
    }
}
