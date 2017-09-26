﻿using System;
using StackExchange.Profiling.Storage;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Profiling.Tests.Storage
{
    public class SqlServerStorageTests : StorageBaseTest, IClassFixture<SqlServerStorageFixture>
    {
        public SqlServerStorageTests(SqlServerStorageFixture fixture, ITestOutputHelper output) : base(fixture, output)
        {
        }
    }

    public class SqlServerStorageFixture : StorageFixtureBase<SqlServerStorage>, IDisposable
    {
        public SqlServerStorageFixture()
        {
            Skip.IfNoConfig(nameof(TestConfig.Current.SQLServerConnectionString), TestConfig.Current.SQLServerConnectionString);

            Storage = new SqlServerStorage(
                TestConfig.Current.SQLServerConnectionString,
                "MPTest" + TestId,
                "MPTimingsTest" + TestId,
                "MPClientTimingsTest" + TestId);
            try
            {
                Storage.CreateSchema();
            }
            catch (Exception e)
            {
                ShouldSkip = true;
                SkipReason = e.Message;
            }
        }

        public void Dispose()
        {
            if (!ShouldSkip)
            {
                Storage.DropSchema();
            }
        }
    }
}
