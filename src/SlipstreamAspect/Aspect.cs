// Races that fill screens taller than 16:9.
//
// The game draws into a 480x270 canvas that Canvas.End letterboxes. The patched game
// (src/AspectPatch) reads the canvas height from here instead, so the canvas
// takes the screen's shape. Menus and overlays keep their 16:9 layout in a band across
// the middle of that canvas; a race and its HUD use the whole of it. A race view reaches
// past the canvas bottom, which lowers its horizon and car and gives most of the added
// height to the sky.
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Slipstream.Core;
using Slipstream.Elements;

namespace SlipstreamPort;

/// <summary>The canvas sized to the screen, and the draw areas over it.</summary>
/// <remarks>Every public member is called by name from the patched game: a rename here
/// needs the same one in AspectPatch.</remarks>
public static class Aspect
{
    public const int BaseWidth = 480;
    public const int BaseHeight = 270;
    // The tallest canvas is a square one; a taller screen letterboxes it.
    private const int MaxHeight = BaseWidth;
    private const string Override = "SLIPSTREAM_ASPECT";
    private const string OriginalAspect = "16:9";
    private const string HorizonOverride = "SLIPSTREAM_HORIZON";
    // Percent of the added height that goes to the sky: 0 centres the horizon; this share
    // keeps the car at about its 16:9 fraction of the screen height.
    private const int SkyPercent = 60;
    private const int Percent = 100;

    private static WeakReference<Road> raceRoad = new(null);
    private static int raceRoadFovHeight = BaseHeight;

    /// <summary>Height of the canvas; BaseHeight until Configure runs.</summary>
    public static int Height { get; private set; } = BaseHeight;

    public static float HeightF => Height;

    public static int HalfHeight => Height / 2;

    /// <summary>Height Road.SetScreenSize starts from: the canvas during a race, BaseHeight
    /// for every other road.</summary>
    public static int RoadHeight { get; private set; } = BaseHeight;

    /// <summary>Share of the added height given to the sky, in percent.</summary>
    public static int Sky { get; private set; } = SkyPercent;

    /// <summary>Sizes the canvas to the back buffer, keeping it even so the band centres on
    /// whole pixels; SLIPSTREAM_ASPECT=16:9 keeps the original canvas.</summary>
    public static void Configure()
    {
        PresentationParameters screen = Canvas.Device.PresentationParameters;
        Sky = int.TryParse(Environment.GetEnvironmentVariable(HorizonOverride), out int sky)
            ? Math.Clamp(sky, 0, Percent)
            : SkyPercent;
        if (Environment.GetEnvironmentVariable(Override) == OriginalAspect)
        {
            Height = BaseHeight;
            return;
        }
        int even = (int)Math.Round((double)BaseWidth * screen.BackBufferHeight / screen.BackBufferWidth / 2) * 2;
        Height = Math.Clamp(even, BaseHeight, MaxHeight);
    }

    /// <summary>Draws into the 16:9 band across the middle of the canvas.</summary>
    public static void Band()
    {
        Canvas.SetDrawArea(0, (Height - BaseHeight) / 2, BaseWidth, BaseHeight);
    }

    /// <summary>Draws into the whole canvas.</summary>
    public static void Full()
    {
        Canvas.SetDrawArea(0, 0, BaseWidth, Height);
    }

    /// <summary>Stands in for the race's full-screen SetDrawArea before its overlays; takes
    /// the original arguments and draws into the band.</summary>
    public static void OverlayArea(int x, int y, int width, int height)
    {
        Band();
    }

    /// <summary>Stands in for a Canvas.Draw that covered the 16:9 canvas: covers the whole
    /// canvas instead, then returns to the band.</summary>
    public static void DrawFull(Texture2D texture, float x, float y, float width, float height, float scale, float rotation)
    {
        Full();
        Canvas.Draw(texture, x, y, width, HeightF, scale, rotation);
        Band();
    }

    /// <summary>Draws a HUD laid out over the whole canvas, then returns to the band.</summary>
    public static void HudFull(HUD hud, float lerp, float alpha)
    {
        Full();
        hud.Draw(lerp, alpha);
        Band();
    }

    /// <summary>Stands in for the race's Road.SetScreenSize: the road takes the height of a
    /// race view, other roads keep BaseHeight.</summary>
    public static void SetRoadScreenSize(Road road, bool halfWidth, bool halfHeight)
    {
        RoadHeight = halfHeight ? Extend(Height / 2) * 2 : Extend(Height);
        raceRoad = new WeakReference<Road>(road);
        raceRoadFovHeight = halfHeight ? BaseHeight / 2 : BaseHeight;
        try
        {
            road.SetScreenSize(halfWidth, halfHeight);
        }
        finally
        {
            RoadHeight = BaseHeight;
        }
    }

    /// <summary>Stands in for Road.SetFOV's reads of the road's height: the 16:9 height,
    /// so a taller race view keeps the original vertical scale.</summary>
    public static int FovHeight(Road road)
    {
        return raceRoad.TryGetTarget(out Road race) && race == road ? raceRoadFovHeight : BaseHeight;
    }

    /// <summary>Stands in for a race view's bounds: the view keeps its place and grows
    /// downward past its visible height, which lowers the horizon at its middle.</summary>
    public static Rectangle ViewBounds(int x, int y, int width, int height)
    {
        return new Rectangle(x, y, width, Extend(height));
    }

    /// <summary>Height of a race view shown height rows tall; returns height itself when
    /// the sky takes nothing.</summary>
    private static int Extend(int height)
    {
        int original = height * BaseHeight / Height;
        return height + (height - original) * Sky / Percent;
    }
}
