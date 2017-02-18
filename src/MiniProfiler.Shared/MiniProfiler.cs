﻿using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Profiling.Helpers;
using StackExchange.Profiling.Storage;
#if NET46
using System.IO;
using System.Web.Script.Serialization;
#else
// TODO: Factor these extensions out? That'd be more breaks...
using Newtonsoft.Json;
#endif

namespace StackExchange.Profiling
{
    /// <summary>
    /// A single MiniProfiler can be used to represent any number of steps/levels in a call-graph, via Step()
    /// </summary>
    /// <remarks>Totally baller.</remarks>
    [DataContract]
    public partial class MiniProfiler
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="MiniProfiler"/> class. 
        /// Obsolete - used for serialization.
        /// </summary>
        [Obsolete("Used for serialization")]
        public MiniProfiler() { /* serialization only */ }

        /// <summary>
        /// Initialises a new instance of the <see cref="MiniProfiler"/> class.  Creates and starts a new MiniProfiler 
        /// for the root <paramref name="name"/>.
        /// </summary>
        /// <param name="name">The name of this <see cref="MiniProfiler"/>, typically a URL.</param>
        public MiniProfiler(string name)
        {
            Id = Guid.NewGuid();
            SqlProfiler = new SqlProfiler(this);
            MachineName = Environment.MachineName;
            Started = DateTime.UtcNow;

            // stopwatch must start before any child Timings are instantiated
            Stopwatch = Settings.StopwatchProvider();
            Root = new Timing(this, null, name);
        }

        /// <summary>
        /// Whether the profiler is currently profiling
        /// </summary>
        internal bool IsActive { get; set; }

        /// <summary>
        /// Gets or sets the profiler id.
        /// Identifies this Profiler so it may be stored/cached.
        /// </summary>
        [DataMember(Order = 1)]
        public Guid Id { get; set; }

        /// <summary>
        /// Gets or sets a display name for this profiling session.
        /// </summary>
        [DataMember(Order = 2)]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets when this profiler was instantiated, in UTC time.
        /// </summary>
        [DataMember(Order = 3)]
        public DateTime Started { get; set; }

        /// <summary>
        /// Gets the milliseconds, to one decimal place, that this MiniProfiler ran.
        /// </summary>
        [DataMember(Order = 4)]
        public decimal DurationMilliseconds { get; set; }

        /// <summary>
        /// Gets or sets where this profiler was run.
        /// </summary>
        [DataMember(Order = 5)]
        public string MachineName { get; set; }

        /// <summary>
        /// Keys are names, values are URLs, allowing additional links to be added to a profiler result, e.g. perhaps a deeper
        /// diagnostic page for the current request.
        /// </summary>
        /// <remarks>
        /// Use <see cref="MiniProfilerExtensions.AddCustomLink"/> to easily add a name/url pair to this dictionary.
        /// </remarks>
        [DataMember(Order = 6)]
        public Dictionary<string, string> CustomLinks { get; set; }

        /// <summary>
        /// Json used to store Custom Links. Do not touch.
        /// </summary>
        public string CustomLinksJson
        {
            get => CustomLinks?.ToJson();
            set
            {
                if (value.HasValue())
                {
                    CustomLinks = value.FromJson<Dictionary<string, string>>();
                }
            }
        }

        private Timing _root;
        /// <summary>
        /// Gets or sets the root timing.
        /// The first <see cref="Timing"/> that is created and started when this profiler is instantiated.
        /// All other <see cref="Timing"/>s will be children of this one.
        /// </summary>
        [DataMember(Order = 7)]
        public Timing Root
        {
            get => _root;
            set
            {
                _root = value;
                RootTimingId = value.Id;

                // TODO: remove this shit

                // when being deserialized, we need to go through and set all child timings' parents
                if (_root.HasChildren)
                {
                    var timings = new Stack<Timing>();
                    timings.Push(_root);
                    while (timings.Count > 0)
                    {
                        var timing = timings.Pop();

                        if (timing.HasChildren)
                        {
                            var children = timing.Children;

                            for (int i = children.Count - 1; i >= 0; i--)
                            {
                                children[i].ParentTiming = timing;
                                timings.Push(children[i]); // FLORIDA!  TODO: refactor this and other stack creation methods into one 
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Id of Root Timing. Used for Sql Storage purposes.
        /// </summary>
        public Guid? RootTimingId { get; set; }

        /// <summary>
        /// Gets or sets timings collected from the client
        /// </summary>
        [DataMember(Order = 8)]
        public ClientTimings ClientTimings { get; set; }

        /// <summary>
        /// RedirectCount in ClientTimings. Used for sql storage.
        /// </summary>
        public int? ClientTimingsRedirectCount { get; set; }

        /// <summary>
        /// Gets or sets a string identifying the user/client that is profiling this request.
        /// </summary>
        /// <remarks>
        /// If this is not set manually at some point, the IUserProvider implementation will be used;
        /// by default, this will be the current request's IP address.
        /// </remarks>
        [DataMember(Order = 9)]
        public string User { get; set; }

        /// <summary>
        /// Returns true when this MiniProfiler has been viewed by the <see cref="User"/> that recorded it.
        /// </summary>
        /// <remarks>
        /// Allows POSTs that result in a redirect to be profiled. <see cref="MiniProfiler.Settings.Storage"/> implementation
        /// will keep a list of all profilers that haven't been fetched down.
        /// </remarks>
        [DataMember(Order = 10)]
        public bool HasUserViewed { get; set; }

        // Allows async to properly track the attachment point
        private readonly AsyncLocal<Timing> _head = new AsyncLocal<Timing>();

        /// <summary>
        /// Gets or sets points to the currently executing Timing. 
        /// </summary>
        public Timing Head
        {
            get => _head.Value;
            set => _head.Value = value;
        }

        /// <summary>
        /// Gets the ticks since this MiniProfiler was started.
        /// </summary>
        internal long ElapsedTicks => Stopwatch.ElapsedTicks;

        /// <summary>
        /// Gets the timer, for unit testing, returns the timer.
        /// </summary>
        public IStopwatch Stopwatch { get; }

        /// <summary>
        /// Gets the currently running MiniProfiler for the current HttpContext; null if no MiniProfiler was <see cref="Start(string)"/>ed.
        /// </summary>
        public static MiniProfiler Current => Settings.ProfilerProvider.GetCurrentProfiler();

        /// <summary>
        /// A <see cref="IAsyncStorage"/> strategy to use for the current profiler. 
        /// If null, then the <see cref="IAsyncStorage"/> set in <see cref="Settings.Storage"/> will be used.
        /// </summary>
        /// <remarks>Used to set custom storage for an individual request</remarks>
        public IAsyncStorage Storage { get; set; }

        /// <summary>
        /// Starts a new MiniProfiler based on the current <see cref="IAsyncProfilerProvider"/>. This new profiler can be accessed by
        /// <see cref="Current"/>.
        /// </summary>
        /// <param name="sessionName">
        /// Allows explicit naming of the new profiling session; when null, an appropriate default will be used, e.g. for
        /// a web request, the url will be used for the overall session name.
        /// </param>
        public static MiniProfiler Start(string sessionName = null) =>
            Settings.ProfilerProvider.Start(sessionName);

        /// <summary>
        /// Ends the current profiling session, if one exists.
        /// </summary>
        /// <param name="discardResults">
        /// When true, clears the <see cref="MiniProfiler.Current"/>, allowing profiling to 
        /// be prematurely stopped and discarded. Useful for when a specific route does not need to be profiled.
        /// </param>
        public static void Stop(bool discardResults = false) =>
            Settings.ProfilerProvider.Stop(discardResults);

        /// <summary>
        /// Asynchronously ends the current profiling session, if one exists. 
        /// This invokves async saving all the way down if th providers support it.
        /// </summary>
        /// <param name="discardResults">
        /// When true, clears the <see cref="MiniProfiler.Current"/>, allowing profiling to 
        /// be prematurely stopped and discarded. Useful for when a specific route does not need to be profiled.
        /// </param>
        public static Task StopAsync(bool discardResults = false) =>
            Settings.ProfilerProvider.StopAsync(discardResults);

        /// <summary>
        /// Returns an <see cref="IDisposable"/> that will time the code between its creation and disposal. Use this method when you
        /// do not wish to include the StackExchange.Profiling namespace for the <see cref="MiniProfilerExtensions.Step(MiniProfiler,string)"/> extension method.
        /// </summary>
        /// <param name="name">A descriptive name for the code that is encapsulated by the resulting IDisposable's lifetime.</param>
        /// <returns>the static step.</returns>
        public static IDisposable StepStatic(string name) => Current.Step(name);

        /// <summary>
        /// Renders the parameter <see cref="MiniProfiler"/> to JSON.
        /// </summary>
        /// <param name="profiler">The <see cref="MiniProfiler"/> to serialize.</param>
        public static string ToJson(MiniProfiler profiler)
        {
            return profiler != null
#if NET46
                ? GetJsonSerializer().Serialize(profiler) : null;
#else
                ? JsonConvert.SerializeObject(profiler) : null;
#endif
        }

        /// <summary>
        /// Deserializes the JSON string parameter to a <see cref="MiniProfiler"/>.
        /// </summary>
        /// <param name="json">The string to deserialize int a <see cref="MiniProfiler"/>.</param>
        public static MiniProfiler FromJson(string json)
        {
            return json.HasValue()
#if NET46
                ? GetJsonSerializer().Deserialize<MiniProfiler>(json) : null;
#else
                ? JsonConvert.DeserializeObject<MiniProfiler>(json) : null;
#endif
        }

#if NET46
        private static JavaScriptSerializer GetJsonSerializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = Settings.MaxJsonResponseSize };
        }
#endif

        /// <summary>
        /// Returns the <see cref="Root"/>'s <see cref="Timing.Name"/> and <see cref="DurationMilliseconds"/> this profiler recorded.
        /// </summary>
        /// <returns>a string containing the recording information</returns>
        public override string ToString()
        {
            return Root != null ? Root.Name + " (" + DurationMilliseconds + " ms)" : "";
        }

        /// <summary>
        /// Returns true if Ids match.
        /// </summary>
        /// <param name="other">The <see cref="object"/> to compare to.</param>
        public override bool Equals(object other)
        {
            return other is MiniProfiler && Id.Equals(((MiniProfiler)other).Id);
        }

        /// <summary>
        /// Returns hash code of Id.
        /// </summary>
        public override int GetHashCode() => Id.GetHashCode();

        /// <summary>
        /// Walks the <see cref="Timing"/> hierarchy contained in this profiler, starting with <see cref="Root"/>, and returns each Timing found.
        /// </summary>
        public IEnumerable<Timing> GetTimingHierarchy()
        {
            var timings = new Stack<Timing>();

            timings.Push(_root);

            while (timings.Count > 0)
            {
                var timing = timings.Pop();

                yield return timing;

                if (timing.HasChildren)
                {
                    var children = timing.Children;
                    for (int i = children.Count - 1; i >= 0; i--) timings.Push(children[i]);
                }
            }
        }

#if NET46 // TODO: Revisit in .NET Standard 2.0
        /// <summary>
        /// Create a DEEP clone of this MiniProfiler.
        /// </summary>
        public MiniProfiler Clone()
        {
            var serializer = new DataContractSerializer(typeof(MiniProfiler), null, int.MaxValue, false, true, null);
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, this);
                ms.Position = 0;
                return (MiniProfiler)serializer.ReadObject(ms);
            }
        }
#endif

        internal Timing StepImpl(string name, decimal? minSaveMs = null, bool? includeChildrenWithMinSave = false)
        {
            return new Timing(this, Head, name, minSaveMs, includeChildrenWithMinSave);
        }

        internal IDisposable IgnoreImpl() => new Suppression(this);

        internal bool StopImpl()
        {
            if (!Stopwatch.IsRunning)
                return false;

            Stopwatch.Stop();
            DurationMilliseconds = GetRoundedMilliseconds(ElapsedTicks);

            foreach (var timing in GetTimingHierarchy())
            {
                timing.Stop();
            }

            return true;
        }

        /// <summary>
        /// Returns milliseconds based on Stopwatch's Frequency, rounded to one decimal place.
        /// </summary>
        /// <param name="ticks">The tick count to round.</param>
        internal decimal GetRoundedMilliseconds(long ticks)
        {
            long z = 10000 * ticks;
            decimal timesTen = (int)(z / Stopwatch.Frequency);
            return timesTen / 10;
        }

        /// <summary>
        /// Returns how many milliseconds have elapsed since <paramref name="startTicks"/> was recorded.
        /// </summary>
        /// <param name="startTicks">The start tick count.</param>
        internal decimal GetDurationMilliseconds(long startTicks) =>
            GetRoundedMilliseconds(ElapsedTicks - startTicks);
    }
}