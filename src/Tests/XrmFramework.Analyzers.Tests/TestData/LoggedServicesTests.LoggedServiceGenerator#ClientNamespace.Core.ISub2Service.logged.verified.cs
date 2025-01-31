﻿//HintName: ClientNamespace.Core.ISub2Service.logged.cs
#if !DISABLE_SERVICES
using ClientNamespace.Core;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using XrmFramework;
using XrmFramework.Definitions;

namespace ClientNamespace.Core
{
    [DebuggerStepThrough, CompilerGenerated]
    public class LoggedISub2Service : LoggedIService, ISub2Service
    {
        protected new ISub2Service Service => (ISub2Service) base.Service;

        #region .ctor
        public LoggedISub2Service(IServiceContext context, ISub2Service service) : base(context, service)
        {
        }
        #endregion
    }
}
#endif