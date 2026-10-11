using System;
using System.IO;
using System.Text.Json;
using System.Threading;

using Ctr2.Assets;
using Ctr2.Desktop;
using Ctr2.Engine;

using SDL3;

using SkiaSharp;

try
{
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
        AtlasFrame? frame = framePath == null ? null : (JsonSerializer.Deserialize<PreviewFrame>(File.ReadAllText(framePath))
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
        return DesktopPreview.Run(image, width, height, frames, frame);
    }
    Console.WriteLine("CTR2 PC port foundation — local assets required.\n" +
        "  import <Windows dump folder | .appx | .msix | .zip> <new cache directory>\n" +
        "  preview <cached PNG> [--width N] [--height N] [--snapshot new.png] [--frames N] [--frame metadata.json]\n" +
        "Supply a dump from your own Windows game. This build previews assets; gameplay is not implemented.");
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
    /// <param name="image">Decoded texture retained by the caller for the entire loop.</param>
    /// <param name="width">Initial window width in SDL window coordinates.</param>
    /// <param name="height">Initial window height in SDL window coordinates.</param>
    /// <param name="frameLimit">Number of frames to present, or zero to run until the window closes.</param>
    /// <param name="frame">Optional atlas geometry; null displays the entire image.</param>
    /// <returns>Zero after normal shutdown or completion of the frame limit.</returns>
    /// <remarks>
    /// Drawing uses the actual window pixel size after resizing or DPI changes. SDL resources created here are
    /// destroyed on exit; the caller-owned bitmap is not disposed.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// SDL initialization, resource creation, or presentation fails.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Preview frame metadata is incompatible with the texture or canvas.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The drawing dimensions are invalid.</exception>
    public static int Run(SKBitmap image, int width, int height, int frameLimit, AtlasFrame? frame)
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
            window = SDL.CreateWindow("Cut the Rope 2 — asset preview", width, height, SDL.WindowFlags.Resizable | SDL.WindowFlags.HighPixelDensity);
            if (window == 0)
            {
                throw new InvalidOperationException(SDL.GetError());
            }

            renderer = SDL.CreateRenderer(window, null!);
            if (renderer == 0)
            {
                throw new InvalidOperationException(SDL.GetError());
            }

            int count = 0;
            bool quit = false;
            while (!quit && (frameLimit == 0 || count < frameLimit))
            {
                while (SDL.PollEvent(out SDL.Event evt))
                {
                    if (evt.Type == (uint)SDL.EventType.Quit)
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
                AssetPreview.Draw(canvas!, image, w, h, frame);
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
