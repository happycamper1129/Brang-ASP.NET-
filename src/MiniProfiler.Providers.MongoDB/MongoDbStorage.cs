﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using StackExchange.Profiling;
using StackExchange.Profiling.Storage;

namespace Afisha.Frontend.Infrastructure.Profiling
{
    /// <summary>
    /// Understands how to store a <see cref="MiniProfiler"/> to a MongoDb database.
    /// </summary>
    public class MongoDbStorage : IAsyncStorage, IDisposable
    {
        /// <summary>
        /// Gets or sets how we connect to the database used to save/load MiniProfiler results.
        /// </summary>
        protected string ConnectionString { get; set; }

        private IMongoDatabase _db;
        private IMongoDatabase Db
        {
            get
            {
                if (_db == null)
                {
                    var client = new MongoClient(ConnectionString);
                    _db = client.GetDatabase("MiniProfiler");
                }
                return _db;
            }
        }

        private IMongoCollection<MiniProfiler> _profilers;
        private IMongoCollection<MiniProfiler> Profilers => _profilers ?? (_profilers = Db.GetCollection<MiniProfiler>("profilers"));

        /// <summary>
        /// Returns a new <see cref="MongoDbStorage"/>. MongoDb connection string will default to "mongodb://localhost"
        /// </summary>
        /// <param name="connectionString">The MongoDB connection string.</param>
        public MongoDbStorage(string connectionString)
        {
            ConnectionString = connectionString;

            BsonClassMap.RegisterClassMap<MiniProfiler>(
                map =>
                {
                    map.MapIdField(c => c.Id);
                    map.MapField(c => c.Name);
                    map.MapField(c => c.Started);
                    map.MapField(c => c.DurationMilliseconds);
                    map.MapField(c => c.MachineName);
                    map.MapField(c => c.CustomLinks);
                    map.MapField(c => c.Root);
                    map.MapField(c => c.ClientTimings);
                    map.MapField(c => c.User);
                    map.MapField(c => c.HasUserViewed);
                });

            BsonClassMap.RegisterClassMap<ClientTiming>(
                map =>
                {
                    map.MapField(x => x.Name);
                    map.MapField(x => x.Start);
                    map.MapField(x => x.Duration);
                });

            BsonClassMap.RegisterClassMap<CustomTiming>(
                map =>
                {
                    map.MapField(x => x.Id);
                    map.MapField(x => x.CommandString);
                    map.MapField(x => x.ExecuteType);
                    map.MapField(x => x.StackTraceSnippet);
                    map.MapField(x => x.StartMilliseconds);
                    map.MapField(x => x.DurationMilliseconds);
                    map.MapField(x => x.FirstFetchDurationMilliseconds);
                    map.MapField(x => x.Errored);
                });

            BsonClassMap.RegisterClassMap<Timing>(
                map =>
                {
                    map.MapField(x => x.Id);
                    map.MapField(x => x.Name);
                    map.MapField(x => x.DurationMilliseconds);
                    map.MapField(x => x.StartMilliseconds);
                    map.MapField(x => x.Children);
                    map.MapField(x => x.CustomTimings);
                });
        }

        /// <summary>
        /// Returns a list of <see cref="MiniProfiler.Id"/>s that haven't been seen by <paramref name="user"/>.
        /// </summary>
        /// <param name="user">User identified by the current <see cref="MiniProfiler.User"/>.</param>
        public List<Guid> GetUnviewedIds(string user) => Profilers.Find(p => p.User == user && !p.HasUserViewed).Project(p => p.Id).ToList();

        /// <summary>
        /// Asynchronously returns a list of <see cref="MiniProfiler.Id"/>s that haven't been seen by <paramref name="user"/>.
        /// </summary>
        /// <param name="user">User identified by the current <see cref="MiniProfiler.User"/>.</param>
        public async Task<List<Guid>> GetUnviewedIdsAsync(string user)
        {
            var guids = new List<Guid>();
            using (var cursor = await Profilers.FindAsync(p => p.User == user && !p.HasUserViewed).ConfigureAwait(false))
            {
                await cursor.ForEachAsync(profiler => guids.Add(profiler.Id)).ConfigureAwait(false);
            }
            return guids;
        }

        /// <summary>
        /// List the MiniProfiler Ids for the given search criteria.
        /// </summary>
        /// <param name="maxResults">The max number of results</param>
        /// <param name="start">Search window start</param>
        /// <param name="finish">Search window end</param>
        /// <param name="orderBy">Result order</param>
        /// <returns>The list of GUID keys</returns>
        public IEnumerable<Guid> List(int maxResults, DateTime? start = null, DateTime? finish = null, ListResultsOrder orderBy = ListResultsOrder.Descending)
        {
            var query = FilterDefinition<MiniProfiler>.Empty;

            if (start != null)
            {
                query = Builders<MiniProfiler>.Filter.And(Builders<MiniProfiler>.Filter.Gt(poco => poco.Started, (DateTime)start));
            }
            if (finish != null)
            {
                query = Builders<MiniProfiler>.Filter.And(Builders<MiniProfiler>.Filter.Gt(poco => poco.Started, (DateTime)finish));
            }

            var profilers = Profilers.Find(query).Limit(maxResults);

            profilers = orderBy == ListResultsOrder.Descending
                ? profilers.SortByDescending(p => p.Started)
                : profilers.SortBy(p => p.Started);

            return profilers.Project(p => p.Id).ToList();
        }

        /// <summary>
        /// Asynchronously returns the MiniProfiler Ids for the given search criteria.
        /// </summary>
        /// <param name="maxResults">The max number of results</param>
        /// <param name="start">Search window start</param>
        /// <param name="finish">Search window end</param>
        /// <param name="orderBy">Result order</param>
        /// <returns>The list of GUID keys</returns>
        public async Task<IEnumerable<Guid>> ListAsync(int maxResults, DateTime? start = null, DateTime? finish = null, ListResultsOrder orderBy = ListResultsOrder.Descending)
        {
            var query = FilterDefinition<MiniProfiler>.Empty;

            if (start != null)
            {
                query = Builders<MiniProfiler>.Filter.And(Builders<MiniProfiler>.Filter.Gt(poco => poco.Started, (DateTime)start));
            }
            if (finish != null)
            {
                query = Builders<MiniProfiler>.Filter.And(Builders<MiniProfiler>.Filter.Gt(poco => poco.Started, (DateTime)finish));
            }

            var profilers = Profilers.Find(query).Limit(maxResults);

            profilers = orderBy == ListResultsOrder.Descending
                ? profilers.SortByDescending(p => p.Started)
                : profilers.SortBy(p => p.Started);

            return await profilers.Project(p => p.Id).ToListAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Loads the <c>MiniProfiler</c> identified by 'id' from the database.
        /// </summary>
        /// <param name="id">The profiler ID to load.</param>
        /// <returns>The loaded <see cref="MiniProfiler"/>.</returns>
        public MiniProfiler Load(Guid id)
        {
            return Profilers.Find(p => p.Id == id).FirstOrDefault();
        }

        /// <summary>
        /// Loads the <c>MiniProfiler</c> identified by 'id' from the database.
        /// </summary>
        /// <param name="id">The profiler ID to load.</param>
        /// <returns>The loaded <see cref="MiniProfiler"/>.</returns>
        public async Task<MiniProfiler> LoadAsync(Guid id)
        {
            return (await Profilers.FindAsync(p => p.Id == id).ConfigureAwait(false)).FirstOrDefault();
        }

        /// <summary>
        /// Stores to <c>profilers</c> under its <see cref="MiniProfiler.Id"/>;
        /// </summary>
        /// <param name="profiler">The <see cref="MiniProfiler"/> to save.</param>
        public void Save(MiniProfiler profiler)
        {
            Profilers.ReplaceOne(
                p => p.Id == profiler.Id,
                profiler,
                new UpdateOptions
                {
                    IsUpsert = true
                });
        }

        /// <summary>
        /// Asynchronously stores to <c>profilers</c> under its <see cref="MiniProfiler.Id"/>.
        /// </summary>
        /// <param name="profiler">The <see cref="MiniProfiler"/> to save.</param>
        public Task SaveAsync(MiniProfiler profiler)
        {
            return Profilers.ReplaceOneAsync(
                p => p.Id == profiler.Id,
                profiler,
                new UpdateOptions
                {
                    IsUpsert = true
                });
        }

        /// <summary>
        /// Sets a particular profiler session so it is considered "unviewed"  
        /// </summary>
        /// <param name="user">The user to set this profiler ID as unviewed for.</param>
        /// <param name="id">The profiler ID to set unviewed.</param>
        public void SetUnviewed(string user, Guid id)
        {
            var set = Builders<MiniProfiler>.Update.Set(poco => poco.HasUserViewed, false);
            Profilers.UpdateOne(p => p.Id == id, set);
        }

        /// <summary>
        /// Asynchronously sets a particular profiler session so it is considered "unviewed"  
        /// </summary>
        /// <param name="user">The user to set this profiler ID as unviewed for.</param>
        /// <param name="id">The profiler ID to set unviewed.</param>
        public async Task SetUnviewedAsync(string user, Guid id)
        {
            var set = Builders<MiniProfiler>.Update.Set(poco => poco.HasUserViewed, false);
            await Profilers.UpdateOneAsync(p => p.Id == id, set).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets a particular profiler session to "viewed"
        /// </summary>
        /// <param name="user">The user to set this profiler ID as viewed for.</param>
        /// <param name="id">The profiler ID to set viewed.</param>
        public void SetViewed(string user, Guid id)
        {
            var set = Builders<MiniProfiler>.Update.Set(poco => poco.HasUserViewed, true);
            Profilers.UpdateOne(p => p.Id == id, set);
        }

        /// <summary>
        /// Asynchronously sets a particular profiler session to "viewed"
        /// </summary>
        /// <param name="user">The user to set this profiler ID as viewed for.</param>
        /// <param name="id">The profiler ID to set viewed.</param>
        public async Task SetViewedAsync(string user, Guid id)
        {
            var set = Builders<MiniProfiler>.Update.Set(poco => poco.HasUserViewed, true);
            await Profilers.UpdateOneAsync(p => p.Id == id, set).ConfigureAwait(false);
        }

        /// <summary>
        /// Returns a client to MongoDB Server.
        /// </summary>
        public MongoClient GetClient() => new MongoClient(ConnectionString);

        /// <summary>
        /// Disposes the database connection, if present.
        /// </summary>
        public void Dispose() { }
    }
}