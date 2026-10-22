using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;
using Ctr2.Engine;
using Ctr2.Engine.Physics;

using SDL3;

using SkiaSharp;

namespace Ctr2.Desktop
{
    /// <summary>Hosts the supported gameplay slice using the existing desktop renderer.</summary>
    internal static class PlayLevelCommand
    {
        internal static int Run(string[] args)
        {
            if (args.Length < 4)
            {
                throw new ArgumentException("play-level requires a cache, pack and level entry.");
            }
            int width = 640, height = 960, frames = 0, resolution = 2, ticks = 0;
            string? snapshot = null;
            List<RopeCut> cuts = [];
            List<SceneTap> taps = [];
            List<BalloonPop> pops = [];
            LevelVariant variant = LevelVariant.Main;
            bool replayHint = false;
            for (int i = 4; i < args.Length; i += 2)
            {
                if (args[i] == "--replay")
                {
                    replayHint = true;
                    i--;
                    continue;
                }
                if (i + 1 == args.Length)
                {
                    throw new ArgumentException("Missing option value.");
                }
                string value = args[i + 1];
                switch (args[i])
                {
                    case "--width":
                        width = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "--height":
                        height = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "--frames":
                        frames = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "--resolution":
                        resolution = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "--ticks":
                        ticks = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "--snapshot":
                        snapshot = value;
                        break;
                    case "--variant":
                        variant = LevelVariants.Parse(value);
                        break;
                    case "--tap":
                        if (taps.Count >= 1024)
                        {
                            throw new ArgumentException("Too many taps.");
                        }
                        taps.Add(ParseTap(value));
                        break;
                    case "--pop":
                        if (pops.Count >= 1024)
                        {
                            throw new ArgumentException("Too many pops.");
                        }
                        string[] pop = value.Split(':');
                        pops.Add(pop.Length == 2 && int.TryParse(pop[0], NumberStyles.None, CultureInfo.InvariantCulture, out int popTick) && popTick <= 36000 &&
                            int.TryParse(pop[1], NumberStyles.None, CultureInfo.InvariantCulture, out int popIndex)
                            ? new(popTick, popIndex) : throw new ArgumentException("Invalid pop tick or balloon index."));
                        break;
                    case "--cut":
                        if (cuts.Count >= 1024)
                        {
                            throw new ArgumentException("Too many cuts.");
                        }
                        cuts.Add(ParseCut(value));
                        break;
                    default:
                        throw new ArgumentException("Unknown play option: " + args[i]);
                }
            }
            if (width is < 1 or > 8192 || height is < 1 or > 8192 || frames < 0 || ticks is < 0 or > 36000)
            {
                throw new ArgumentException("Dimensions must be 1–8192; frames nonnegative; ticks 0–36000.");
            }
            using LevelPreview preview = LevelPreview.Load(args[1], int.Parse(args[2], CultureInfo.InvariantCulture),
                int.Parse(args[3], CultureInfo.InvariantCulture), resolution, play: true, variant: variant).Preview;
            GameSession session = preview.Session!;
            ReplayJournal? journal = null;
            JournalInputPlayer? hint = null;
            if (replayHint)
            {
                journal = JournalInputPlayer.LoadHint(args[1], LevelReader.LoadWindows(args[1], int.Parse(args[2], CultureInfo.InvariantCulture),
                    int.Parse(args[3], CultureInfo.InvariantCulture)), variant);
                hint = new(journal);
                Console.WriteLine($"Replaying the {variant.Name()} hint journal: {hint.LastFrame} frames of recorded input, applied open loop; R restarts it.");
            }
            Queue<RopeCut> replay = new(cuts.OrderBy(c => c.Tick));
            Queue<SceneTap> tapReplay = new(taps.OrderBy(t => t.Tick));
            Queue<BalloonPop> popReplay = new(pops.OrderBy(p => p.Tick));
            session.Stepping += () =>
            {
                _ = hint?.Apply(session.Simulation);
                while (tapReplay.TryPeek(out SceneTap? tap) && tap.Tick == session.Simulation.Tick)
                {
                    _ = tapReplay.Dequeue();
                    _ = session.Simulation.Tap(tap.Point);
                }
                while (popReplay.TryPeek(out BalloonPop? pop) && pop.Tick == session.Simulation.Tick)
                {
                    _ = popReplay.Dequeue();
                    _ = session.Simulation.PopBalloon(pop.Balloon);
                }
                while (replay.TryPeek(out RopeCut? cut) && cut.Tick == session.Simulation.Tick)
                {
                    _ = replay.Dequeue();
                    if (session.Simulation.Cut(cut.Rope, cut.Segment, cut.Point, cut.Impulse))
                    {
                        session.Simulation.Gameplay!.NotifyTouchDown();
                    }
                }
            };
            Console.WriteLine("Play: swipe to cut or pop, tap objects to interact; R restart; Space pause/resume; Escape exit. Terrain and ropes are diagnostic; broader mechanics are pending.");
            for (int i = 0; i < ticks && session.Simulation.Gameplay!.Result == LevelResult.Running; i++)
            {
                session.Step();
            }
            void Draw(SKCanvas canvas, int w, int h)
            {
                preview.Draw(canvas, w, h, 0);
                DrawStatus(canvas, w, session);
            }
            if (snapshot != null)
            {
                using SKBitmap bitmap = new(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
                using SKCanvas canvas = new(bitmap);
                Draw(canvas, width, height);
                using SKData encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                using FileStream file = new(snapshot, FileMode.CreateNew);
                encoded.SaveTo(file);
                Console.WriteLine("Rendered playable scene: " + snapshot);
            }
            else
            {
                WaterAudioPlayback? audio = null;
                if (session.Simulation.WaterEffects != null)
                {
                    try
                    {
                        audio = WaterAudioPlayback.Create(args[1]);
                        audio.Update(session.Simulation, session.Paused);
                    }
                    catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException)
                    {
                        audio?.Dispose();
                        audio = null;
                        Console.Error.WriteLine("Water audio unavailable: " + error.Message);
                    }
                }
                using WaterAudioPlayback? ownedAudio = audio;
                _ = DesktopPreview.Run(width, height, frames, (canvas, w, h, seconds) =>
                {
                    audio?.Update(session.Simulation, session.Paused);
                    session.AdvanceTo(seconds);
                    audio?.Update(session.Simulation, session.Paused);
                    Draw(canvas, w, h);
                }, (evt, windowWidth, windowHeight, pixelWidth, pixelHeight) =>
                {
                    if ((SDL.EventType)evt.Type == SDL.EventType.KeyDown && !evt.Key.Repeat)
                    {
                        if (evt.Key.Key == SDL.Keycode.R)
                        {
                            session.Restart();
                            replay = new(cuts.OrderBy(c => c.Tick));
                            tapReplay = new(taps.OrderBy(t => t.Tick));
                            popReplay = new(pops.OrderBy(p => p.Tick));
                            hint = journal == null ? null : new(journal);
                        }
                        else if (evt.Key.Key == SDL.Keycode.Space)
                        {
                            session.Paused = !session.Paused;
                        }
                    }
                    if ((SDL.EventType)evt.Type is SDL.EventType.WindowFocusLost or SDL.EventType.WindowMinimized)
                    {
                        session.Paused = true;
                    }
                    if (!session.Paused && session.Simulation.Gameplay!.Result == LevelResult.Running)
                    {
                        if ((SDL.EventType)evt.Type == SDL.EventType.MouseButtonDown && evt.Button.Button == 1 && evt.Button.Which != uint.MaxValue)
                        {
                            session.Simulation.Gameplay.NotifyTouchDown();
                        }
                        new SdlRopeInput(session.Input).Handle(evt, windowWidth, windowHeight, pixelWidth, pixelHeight);
                    }
                }, title: "Cut the Rope 2 — play", beforeShutdown: () => audio?.Dispose());
            }
            LevelGameplay game = session.Simulation.Gameplay!;
            foreach (string skipped in hint?.Unsupported ?? [])
            {
                Console.WriteLine("Replay input not applied: " + skipped);
            }
            Console.WriteLine($"Result: {game.Result}; tick {session.Simulation.Tick}; stars {session.Simulation.StarCollections.Count}/{session.Simulation.Stars.Count}; candy {game.CandyState}.");
            foreach (CandyConsumption eaten in game.Consumptions)
            {
                Console.WriteLine($"Candy consumed: tick {eaten.Tick}.");
            }
            foreach (BalloonPop pop in session.Simulation.BalloonPops)
            {
                Console.WriteLine(FormattableString.Invariant($"Pop replay: --pop {pop.Tick}:{pop.Balloon}"));
            }
            foreach (SceneTap tap in session.Input.Taps)
            {
                Console.WriteLine(FormattableString.Invariant($"Tap replay: --tap {tap.Tick}:{tap.Point.X:R}:{tap.Point.Y:R}"));
            }
            foreach (RopeCut cut in session.Input.Cuts)
            {
                Console.WriteLine(FormattableString.Invariant($"Cut replay: --cut {cut.Tick}:{cut.Rope}:{cut.Segment}:{cut.Point.X:R}:{cut.Point.Y:R}:{cut.Impulse.X:R}:{cut.Impulse.Y:R}"));
            }
            return 0;
        }

        private static SceneTap ParseTap(string value)
        {
            string[] parts = value.Split(':');
            return parts.Length != 3 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int tick) || tick > 36000 ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) || !float.IsFinite(x) || MathF.Abs(x) > 1000000 ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) || !float.IsFinite(y) || MathF.Abs(y) > 1000000
                ? throw new ArgumentException("Invalid tap tick or position.")
                : new(tick, new(x, y));
        }

        private static RopeCut ParseCut(string value)
        {
            string[] fields = value.Split(':');
            if (fields.Length is not (5 or 7))
            {
                throw new ArgumentException("Cut format: tick:rope:segment:x:y[:impulseX:impulseY].");
            }
            int tick = int.Parse(fields[0], CultureInfo.InvariantCulture), rope = int.Parse(fields[1], CultureInfo.InvariantCulture), segment = int.Parse(fields[2], CultureInfo.InvariantCulture);
            Vector2 point = new(float.Parse(fields[3], CultureInfo.InvariantCulture), float.Parse(fields[4], CultureInfo.InvariantCulture));
            Vector2 impulse = fields.Length == 7 ? new(float.Parse(fields[5], CultureInfo.InvariantCulture), float.Parse(fields[6], CultureInfo.InvariantCulture)) : Vector2.Zero;
            return tick is < 0 or > 36000 || rope < 0 || segment < 0 || !float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(impulse.X) || !float.IsFinite(impulse.Y)
                ? throw new ArgumentException("Invalid cut tick, index or vector.")
                : new(tick, rope, segment, point, impulse);
        }

        private static void DrawStatus(SKCanvas canvas, int width, GameSession session)
        {
            using SKPaint panel = new()
            {
                Color = new SKColor(0, 0, 0, 200)
            };
            using SKPaint text = new()
            {
                Color = SKColors.White,
                IsAntialias = true
            };
            using SKTypeface face = SKTypeface.FromFamilyName("Arial");
            using SKFont font = new(face, Math.Clamp(width / 32f, 12, 24));
            canvas.DrawRect(0, 0, width, 66, panel);
            string status = session.Paused ? "Paused" : session.Simulation.Gameplay!.Result switch
            {
                LevelResult.Won => "Level complete",
                LevelResult.Lost => "Try again",
                LevelResult.Running => "Feed Om Nom",
                _ => "Feed Om Nom"
            };
            canvas.DrawText($"{status}   Stars: {session.Simulation.StarCollections.Count}/{session.Simulation.Stars.Count}", 12, 27, SKTextAlign.Left, font, text);
            font.Size *= .75f;
            canvas.DrawText("Swipe to cut/pop, tap objects | R: restart | Space: pause | Esc: exit", 12, 52, SKTextAlign.Left, font, text);
        }
    }
}
