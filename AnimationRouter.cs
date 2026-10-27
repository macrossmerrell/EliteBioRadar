using System;
using System.Collections.Generic;

namespace EliteBioRadar
{
    // Where an animation may be shown: the Elite Bio Radar window, the System Notifications window, both, or neither (off).
    [Flags]
    public enum AnimationSurfaces { None = 0, Main = 1, Notifications = 2, Both = Main | Notifications }

    // One entry per animation the app can play. Group is the heading in the settings list; BackdropOpacity is how much of whatever lies
    // underneath is hidden while it plays (1 = fully covered).
    // PreviewSubject/PreviewDetail are the sample text the Notification Settings window's Play button uses.
    public sealed record AnimationDef(string Id, string Name, string Group, AnimationSurfaces DefaultSurfaces, double BackdropOpacity,
                                      string PreviewSubject, string PreviewDetail);

    public static class AnimationCatalog
    {
        public static readonly AnimationDef FirstDiscovery =
            new("first-discovery", "First discovery", "Discovery", AnimationSurfaces.Main, 1.0, "Sample System", "M-Type");

        public static readonly IReadOnlyList<AnimationDef> All = new[] { FirstDiscovery };

        // The user's choice from the settings file, else the animation's own default.
        public static AnimationSurfaces SurfacesFor(AnimationDef def, AppSettingsData settings) =>
            settings.AnimationTargets.TryGetValue(def.Id, out var v) ? (AnimationSurfaces)v : def.DefaultSurfaces;
    }

    // What to play and what to say. Subject/Detail are the animation's own text (First discovery: system name, star class).
    public sealed record AnimationRequest(AnimationDef Def, string Subject, string Detail)
    {
        public DateTime At { get; } = DateTime.UtcNow;
    }

    // A place that can play animations. Play must call done exactly once when the animation has finished and the surface is clear again.
    public interface IAnimationSurface
    {
        AnimationSurfaces Kind { get; }
        bool IsAvailable { get; }
        void Play(AnimationRequest request, Action done);
    }

    // Events are detected once, then handed to Raise. The router looks up where that animation is switched on and gives it to each of those
    // surfaces. Every surface plays one animation at a time and queues the rest; a request still waiting after MaxWait is dropped, since
    // a stale notification (a system the commander has already left) is worse than none. All calls are on the UI thread.
    public static class AnimationRouter
    {
        private const double MaxWaitSeconds = 10;

        private sealed class Lane
        {
            public IAnimationSurface Surface = null!;
            public readonly Queue<AnimationRequest> Queue = new();
            public bool Busy;
        }

        private static readonly Dictionary<AnimationSurfaces, Lane> _lanes = new();

        public static void Register(IAnimationSurface surface) => _lanes[surface.Kind] = new Lane { Surface = surface };

        public static void Unregister(IAnimationSurface surface)
        {
            if (_lanes.TryGetValue(surface.Kind, out var lane) && ReferenceEquals(lane.Surface, surface)) _lanes.Remove(surface.Kind);
        }

        public static void Raise(AnimationRequest request)
        {
            var targets = AnimationCatalog.SurfacesFor(request.Def, AppSettings.Load());
            foreach (var kv in _lanes)
            {
                if ((targets & kv.Key) == 0) continue;
                kv.Value.Queue.Enqueue(request);
                Pump(kv.Value);
            }
        }

        private static void Pump(Lane lane)
        {
            if (lane.Busy) return;
            while (lane.Queue.Count > 0)
            {
                var req = lane.Queue.Dequeue();
                if ((DateTime.UtcNow - req.At).TotalSeconds > MaxWaitSeconds)
                {
                    Log.Write($"Animation '{req.Def.Id}' dropped on {lane.Surface.Kind}: waited too long");
                    continue;
                }
                if (!lane.Surface.IsAvailable)
                {
                    Log.Write($"Animation '{req.Def.Id}' skipped on {lane.Surface.Kind}: surface not available");
                    continue;
                }
                lane.Busy = true;
                lane.Surface.Play(req, () => { lane.Busy = false; Pump(lane); });
                return;
            }
        }
    }
}
