﻿namespace StackExchange.Profiling
{
    partial class WebRequestProfilerProvider
    {
        /// <summary>
        /// WebRequestProfilerProvider specific configurations
        /// </summary>
        public static class Settings
        {
            /// <summary>
            /// Provides user identification for a given profiling request.
            /// </summary>
            public static IUserProvider UserProvider { get; set; } = new IpAddressIdentity();
        }
    }
}
