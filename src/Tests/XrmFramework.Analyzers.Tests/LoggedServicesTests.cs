﻿using System.Threading.Tasks;
using VerifyXunit;
using XrmFramework.Analyzers.Generators;
using Xunit;

namespace XrmFramework.Analyzers.Tests;

[UsesVerify]
public class LoggedServicesTests
{
    [Fact]
    public async Task LoggedServiceGenerator()
    {
        // The source code to test
        var source = @"
using System;

namespace Microsoft.Xrm.Sdk
{
    public class Entity
    {
    }
}


namespace XrmFramework {
    using Microsoft.Xrm.Sdk;

    public partial interface IService
    {
        Guid Create(Entity entity);
    }

    public partial class DefaultService : IService
    {
        public Guid Create(Entity entity)
        {
            return Guid.Empty;
        }
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public class NullableAttribute : Attribute
    {
    }
}

namespace XrmFramework {
    using Microsoft.Xrm.Sdk;

    partial interface IService
    {
        void Update(Entity entity);
    }

    partial class DefaultService
    {
        public void Update(Entity entity)
        {
        }
    }
}

namespace ClientNamespace.Core
{
    using XrmFramework;
    using Microsoft.Xrm.Sdk;
    using System.Threading.Tasks;

    public interface ISubService : IService
    {
    }

    abstract partial class SubService
    {
    }

    public partial class SubService : DefaultService, ISubService
    {
    }

    public interface ISub2Service : IService
    {
    }

    internal class Sub2Service : DefaultService, ISub2Service
    {
    }

    public interface ISub3Service : IService
    {
    }

    public class Sub3Service : DefaultService, ISub3Service
    {
    }

    public interface IAccountService : IService
    {
        int GetSubRecordCount(EntityReference recordRef);

        Task AsynchronousCall(IEnumerable<double> list);

        bool TryGetValue(string key, out int value);

        U GetValue<T, U>(T argument) where U: new() where T: Enum;

        void StringParameter([Nullable] string parameter);
        void StringParameterDefaultValue(string parameter = null);
    }
}
";

        // Pass the source code to our helper and snapshot test the output
        await TestHelper.Verify<LoggedServicesSourceGenerator>(source);

    }
}