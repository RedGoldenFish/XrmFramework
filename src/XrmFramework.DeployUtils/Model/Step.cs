﻿// Copyright (c) Christophe Gondouin (CGO Conseils). All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using XrmFramework.Definitions;

namespace XrmFramework.DeployUtils.Model
{
    public class Step : ISolutionComponent
    {
        private Guid _id;

        public Step(string pluginTypeName, Messages message, Stages stage, Modes mode, string entityName)
        {
            PluginTypeName = pluginTypeName;
            Message = message;
            Stage = stage;
            Mode = mode;
            EntityName = entityName;
            PreImage = new StepImage(Message, true, stage);
            PostImage = new StepImage(Message, false, stage);
        }


        public Guid Id
        {
            get => _id;
            set
            {
                PreImage.ParentId = value;
                PostImage.ParentId = value;
                _id = value;
            }
        }
        public string PluginTypeName { get; }
        public Messages Message { get; }
        public Stages Stage { get; }
        public Modes Mode { get; }
        public string EntityName { get; }

        public Guid ParentId { get; set; }
        public string PluginTypeFullName { get; set; }

        public Guid MessageId { get; set; }

        public bool DoNotFilterAttributes { get; set; }

        public List<string> FilteringAttributes { get; } = new List<string>();

        public StepImage PreImage { get; set; }

        public StepImage PostImage { get; set; }

        public string UnsecureConfig { get; set; }

        public int Order { get; set; }

        public string ImpersonationUsername { get; set; }

        public List<string> MethodNames { get; } = new List<string>();
        public string MethodsDisplayName => string.Join(",", MethodNames);

        public StepConfiguration StepConfiguration => JsonConvert.DeserializeObject<StepConfiguration>(UnsecureConfig);


        public RegistrationState RegistrationState { get; set; } = RegistrationState.NotComputed;

        public void Merge(Step step)
        {
            if (!step.FilteringAttributes.Any())
            {
                DoNotFilterAttributes = true;
            }

            FilteringAttributes.AddRange(step.FilteringAttributes);

            if (step.PreImage.AllAttributes)
            {
                PreImage.AllAttributes = true;
                PreImage.Attributes.Clear();
            }
            else
            {
                PreImage.Attributes.AddRange(step.PreImage.Attributes);
            }

            if (step.PostImage.AllAttributes)
            {
                PostImage.AllAttributes = true;
                PostImage.Attributes.Clear();
            }
            else
            {
                PostImage.Attributes.AddRange(step.PostImage.Attributes);
            }

            MethodNames.AddRange(step.MethodNames);
        }

        public string Description => $"{PluginTypeName} : {Stage} {Message} of {EntityName} ({MethodsDisplayName})";

        public string EntityTypeName => SdkMessageProcessingStepDefinition.EntityName;
        public string UniqueName => PluginTypeFullName;
        public IEnumerable<ISolutionComponent> Children
        {
            get
            {
                var res = new List<ISolutionComponent>();
                if (PreImage.IsUsed) res.Add(PreImage);
                if (PostImage.IsUsed) res.Add(PostImage);
                return res;
            }
        }
        public void AddChild(ISolutionComponent child)
        {
            if (!child.GetType().IsAssignableFrom(typeof(StepImage))) throw new ArgumentException("Step doesn't take this type of children");
            var stepChild = (StepImage)child;
            if (stepChild.IsPreImage)
            {
                PreImage = stepChild;
            }
            else
            {
                PostImage = stepChild;
            }
        }
    }

}
