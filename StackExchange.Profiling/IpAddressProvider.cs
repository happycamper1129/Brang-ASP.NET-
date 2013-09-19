﻿using System.Web;

namespace StackExchange.Profiling
{
    /// <summary>
    /// Identifies users based on ip address.
    /// </summary>
    public class IpAddressIdentity : IUserProvider
    {
        /// <summary>
        /// Returns the paramter HttpRequest's client ip address.
        /// </summary>
        public string GetUser(HttpRequest request)
        {
            return request.ServerVariables["REMOTE_ADDR"] ?? "";
        }
    }
}
