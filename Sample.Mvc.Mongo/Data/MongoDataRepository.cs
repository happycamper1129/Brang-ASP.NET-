﻿using MongoDB.Driver;
using StackExchange.Profiling.MongoDB;

namespace SampleWeb.Data
{
    public class MongoDataRepository
    {
        public string MongoUrl { get; private set; }
        public string DbName { get; set; }

        public MongoDataRepository(string mongoUrl, string dbName)
        {
            MongoUrl = mongoUrl;
            DbName = dbName;
        }

        private MongoClient _client;
        public MongoClient Client
        {
            get
            {
                if (_client == null)
                {
                    _client = new MongoClient(MongoUrl);
                }

                return _client;
            }
        }

        private MongoServer _server;
        public MongoServer Server
        {
            get
            {
                if (_server == null)
                {
                    _server = new ProfiledMongoServer(Client.GetServer());
                }
                return _server;
            }
        }

        private MongoDatabase _database;
        public MongoDatabase Database
        {
            get
            {
                if (_database == null)
                {
                    _database = Server.GetDatabase(DbName);
                }
                return _database;
            }
        }

        private MongoCollection _fooCollection;
        public MongoCollection FooCollection
        {
            get
            {
                if (_fooCollection == null)
                {
                    _fooCollection = Database.GetCollection("foo");
                }
                return _fooCollection;
            }
        }
    }
}
