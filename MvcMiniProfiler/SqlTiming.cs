﻿using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using MvcMiniProfiler.Data;
using System.Text.RegularExpressions;
using System.Runtime.Serialization;
using System.Web.Script.Serialization;

namespace MvcMiniProfiler
{
    /// <summary>
    /// Profiles a single sql execution.
    /// </summary>
    [DataContract]
    public class SqlTiming
    {

        /// <summary>
        /// Category of sql statement executed.
        /// </summary>
        [DataMember(Order = 1)]
        public ExecuteType ExecuteType { get; set; }

        /// <summary>
        /// The sql that was executed.
        /// </summary>
        [DataMember(Order = 2)]
        public string CommandString { get; set; }

        /// <summary>
        /// Roughly where in the calling code that this sql was executed.
        /// </summary>
        [DataMember(Order = 3)]
        public string StackTraceSnippet { get; set; }

        /// <summary>
        /// Offset from main MiniProfiler start that this sql began.
        /// </summary>
        [DataMember(Order = 4)]
        public decimal StartMilliseconds { get; set; }

        /// <summary>
        /// How long this sql statement took to execute.
        /// </summary>
        [DataMember(Order = 5)]
        public decimal DurationMilliseconds { get; set; }

        /// <summary>
        /// When executing readers, how long it took to come back initially from the database, 
        /// before all records are fetched and reader is closed.
        /// </summary>
        [DataMember(Order = 6)]
        public decimal FirstFetchDurationMilliseconds { get; set; }

        /// <summary>
        /// Id of the Timing this statement was executed in.
        /// </summary>
        /// <remarks>
        /// Needed for database deserialization.
        /// </remarks>
        public Guid? ParentTimingId { get; set; }

        private Timing _parentTiming;
        /// <summary>
        /// The Timing step that this sql execution occurred in.
        /// </summary>
        [ScriptIgnore]
        public Timing ParentTiming
        {
            get { return _parentTiming; }
            set
            {
                _parentTiming = value;

                if (value != null && ParentTimingId != value.Id)
                    ParentTimingId = value.Id;
            }
        }

        /// <summary>
        /// True when other identical sql statements have been executed during this MiniProfiler session.
        /// </summary>
        public bool IsDuplicate { get; set; }

        private long _startTicks;
        private MiniProfiler _profiler;

        /// <summary>
        /// Creates a new SqlTiming to profile 'command'.
        /// </summary>
        public SqlTiming(DbCommand command, ExecuteType type, MiniProfiler profiler)
        {
            CommandString = AddSpacesToParameters(command.CommandText);
            ExecuteType = type;
            StackTraceSnippet = Helpers.StackTraceSnippet.Get();

            _profiler = profiler;
            _profiler.AddSqlTiming(this);

            _startTicks = _profiler.ElapsedTicks;
            StartMilliseconds = MiniProfiler.GetRoundedMilliseconds(_startTicks);
        }

        /// <summary>
        /// Obsolete - used for serialization.
        /// </summary>
        [Obsolete("Used for serialization")]
        public SqlTiming()
        {
        }

        /// <summary>
        /// Called when command execution is finished to determine this SqlTiming's duration.
        /// </summary>
        public void ExecutionComplete(bool isReader)
        {
            if (isReader)
            {
                FirstFetchDurationMilliseconds = GetDurationMilliseconds();
            }
            else
            {
                DurationMilliseconds = GetDurationMilliseconds();
            }
        }

        /// <summary>
        /// Called when database reader is closed, ending profiling for <see cref="MvcMiniProfiler.ExecuteType.Reader"/> SqlTimings.
        /// </summary>
        public void ReaderFetchComplete()
        {
            DurationMilliseconds = GetDurationMilliseconds();
        }

        private decimal GetDurationMilliseconds()
        {
            return MiniProfiler.GetRoundedMilliseconds(_profiler.ElapsedTicks - _startTicks);
        }

        /// <summary>
        /// To help with display, put some space around sammiched commas
        /// </summary>
        private string AddSpacesToParameters(string commandString)
        {
            return Regex.Replace(commandString, @",([^\s])", ", $1");
        }

    }
}