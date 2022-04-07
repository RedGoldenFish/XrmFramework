﻿using Deploy;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using XrmFramework.DeployUtils.Model;

namespace XrmFramework.DeployUtils.Context
{
    public interface ISolutionContext
    {
        string SolutionName { get; }
        Solution Solution { get; }
        Publisher Publisher { get; }

        List<SolutionComponent> Components { get; }
        List<SdkMessageFilter> Filters { get; }
        Dictionary<Messages, EntityReference> Messages { get; }
        List<KeyValuePair<string, Guid>> Users { get; }

        void InitMetadata();

        void InitExportMetadata(IEnumerable<Step> steps);



    }
}
