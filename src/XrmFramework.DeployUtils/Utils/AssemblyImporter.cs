﻿using AutoMapper;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using XrmFramework.DeployUtils.Context;
using XrmFramework.DeployUtils.Model;

namespace XrmFramework.DeployUtils.Utils
{
    /// <summary>
    /// Base implementation of <see cref="IAssemblyImporter"/>
    /// </summary>
    public class AssemblyImporter : IAssemblyImporter
    {
        private readonly ISolutionContext _solutionContext;
        private readonly IMapper _mapper;
        public AssemblyImporter(ISolutionContext solutionContext, IMapper mapper)
        {
            _solutionContext = solutionContext;
            _mapper = mapper;
        }

        public IAssemblyContext CreateAssemblyFromLocal(Assembly assembly)
        {
            var fullNameSplit = assembly.FullName.Split(',');

            var name = fullNameSplit[0];
            var version = fullNameSplit[1].Substring(fullNameSplit[1].IndexOf('=') + 1);
            var culture = fullNameSplit[2].Substring(fullNameSplit[2].IndexOf('=') + 1);
            var publicKeyToken = fullNameSplit[3].Substring(fullNameSplit[3].IndexOf('=') + 1);
            var description = string.Format("{0} plugin assembly", name);

            var t = new AssemblyInfo()
            {
                Name = name,
                SourceType = new OptionSetValue((int)Deploy.pluginassembly_sourcetype.Database),
                IsolationMode = new OptionSetValue((int)Deploy.pluginassembly_isolationmode.Sandbox),
                Culture = culture,
                PublicKeyToken = publicKeyToken,
                Version = version,
                Description = description,
                Content = File.ReadAllBytes(assembly.Location)
            };

            return _mapper.Map<IAssemblyContext>(t);
        }

        public IAssemblyContext CreateAssemblyFromRemote(Deploy.PluginAssembly assembly)
        {
            var info = assembly != null ? _mapper.Map<AssemblyInfo>(assembly) : null;
            return _mapper.Map<IAssemblyContext>(info);
        }

        public Plugin CreatePluginFromType(Type type)
        {
            dynamic instance;
            if (type.GetConstructor(new[] { typeof(string), typeof(string) }) != null)
            {
                instance = Activator.CreateInstance(type, new object[] { null, null });
            }
            else
            {
                instance = Activator.CreateInstance(type, new object[] { });
            }
            return FromXrmFrameworkPlugin(instance);
        }

        public Plugin CreateWorkflowFromType(Type type)
        {
            dynamic instance = Activator.CreateInstance(type, new object[] { });
            return FromXrmFrameworkPlugin(instance, true);
        }

        public CustomApi CreateCustomApiFromType(Type type)
        {
            dynamic instance;
            if (type.GetConstructor(new[] { typeof(string), typeof(string) }) != null)
            {
                instance = Activator.CreateInstance(type, new object[] { null, null });
            }
            else
            {
                instance = Activator.CreateInstance(type, new object[] { });
            }
            return FromXrmFrameworkCustomApi(instance);
        }

        public Step CreateStepFromRemote(Deploy.SdkMessageProcessingStep sdkStep, IEnumerable<Deploy.SdkMessageProcessingStepImage> sdkImages)
        {
            var entityName = sdkStep.EntityName;
            var pluginFullName = sdkStep.EventHandler.Name;
            var pluginName = pluginFullName.Split('.').Last();

#pragma warning disable CS0612 // Type or member is obsolete
            var step = new Step(pluginName,
                                Messages.GetMessage(sdkStep.SdkMessageId.Name),
                                (Stages)(int)sdkStep.StageEnum,
                                (Modes)(int)sdkStep.ModeEnum,
                                entityName);
#pragma warning restore CS0612 // Type or member is obsolete
            step.Id = sdkStep.Id;

            step.PluginTypeFullName = pluginFullName;
            step.ParentId = sdkStep.EventHandler.Id;

            step.FilteringAttributes.Add(sdkStep.FilteringAttributes);
            step.ImpersonationUsername = sdkStep.ImpersonatingUserId?.Name ?? "";
            step.Order = (int)sdkStep.Rank;
            if (!string.IsNullOrWhiteSpace(sdkStep.Configuration))
            {
                step.StepConfiguration = JsonConvert.DeserializeObject<StepConfiguration>(sdkStep.Configuration);
            }


            CreateStepImageFromRemote(step, true, sdkImages);
            CreateStepImageFromRemote(step, false, sdkImages);

            return step;
        }

        public void CreateStepImageFromRemote(Step step, bool isPreImage,
            IEnumerable<Deploy.SdkMessageProcessingStepImage> stepImages)
        {
            var imageType = isPreImage
                ? Deploy.sdkmessageprocessingstepimage_imagetype.PreImage
                : Deploy.sdkmessageprocessingstepimage_imagetype.PostImage;
            var existingImage = stepImages.FirstOrDefault(i => i.ImageTypeEnum == imageType
                                                          && i.SdkMessageProcessingStepId.Id == step.Id);

            if (existingImage != null)
            {
                step.PreImage.Id = existingImage.Id;
                step.PreImage.ParentId = step.Id;
                step.PreImage.AllAttributes = existingImage.Attributes1 == null;
                step.PreImage.Attributes.Add(existingImage.Attributes1);
            }
        }

        public Plugin CreatePluginFromRemote(Deploy.PluginType pluginType, IEnumerable<Step> steps)
        {
            if (pluginType.WorkflowActivityGroupName != null)
            {
                return new Plugin(pluginType.TypeName, pluginType.Name)
                {
                    Id = pluginType.Id,
                    ParentId = pluginType.PluginAssemblyId.Id
                };
            }

            var plugin = new Plugin(pluginType.TypeName)
            {
                Id = pluginType.Id,
                ParentId = pluginType.PluginAssemblyId.Id
            };

            foreach (var s in steps.Where(s => s.ParentId == plugin.Id))
            {
                plugin.Steps.Add(s);
            }
            return plugin;
        }


        public CustomApi CreateCustomApiFromRemote(Deploy.CustomApi customApi,
                                                   IEnumerable<Deploy.CustomApiRequestParameter> registeredRequestParameters,
                                                   IEnumerable<Deploy.CustomApiResponseProperty> registeredResponseProperties)
        {

            var parsedCustomApi = _mapper.Map<CustomApi>(customApi);

            registeredRequestParameters
                .Where(r => r.CustomApiId.Id == customApi.Id)
                .Select(_mapper.Map<CustomApiRequestParameter>)
                .ToList()
                .ForEach(parsedCustomApi.AddChild);

            registeredResponseProperties
                .Where(r => r.CustomApiId.Id == customApi.Id)
                .Select(_mapper.Map<CustomApiResponseProperty>)
                .ToList()
                .ForEach(parsedCustomApi.AddChild);

            return parsedCustomApi;
        }

        private Plugin FromXrmFrameworkPlugin(dynamic plugin, bool isWorkflow = false)
        {
            var pluginTemp = !isWorkflow ? new Plugin(plugin.GetType().FullName) : new Plugin(plugin.GetType().FullName, plugin.DisplayName);
            if (!isWorkflow)
            {
                foreach (var step in plugin.Steps)
                {
                    pluginTemp.Steps.Add(FromXrmFrameworkStep(step));
                }
            }

            return pluginTemp;
        }

        private Step FromXrmFrameworkStep(dynamic s)
        {
            var step = new Step(s.Plugin.GetType().Name, Messages.GetMessage(s.Message.ToString()), (Stages)(int)s.Stage, (Modes)(int)s.Mode, s.EntityName);

            step.PluginTypeFullName = s.Plugin.GetType().FullName;
            step.FilteringAttributes.UnionWith(s.FilteringAttributes);
            step.ImpersonationUsername = s.ImpersonationUsername ?? "";
            step.Order = s.Order;

            step.PreImage.AllAttributes = s.PreImageAllAttributes;
            step.PreImage.Attributes.UnionWith(s.PreImageAttributes);

            step.PostImage.AllAttributes = s.PostImageAllAttributes;
            step.PostImage.Attributes.UnionWith(s.PostImageAttributes);

            if (!string.IsNullOrWhiteSpace(s.UnsecureConfig))
            {
                step.StepConfiguration = JsonConvert.DeserializeObject<StepConfiguration>(s.UnsecureConfig);
            }

            step.MethodNames.UnionWith(s.MethodNames);
            return step;
        }

        private CustomApi FromXrmFrameworkCustomApi(dynamic record)
        {
            var type = (Type)record.GetType();

            dynamic customApiAttribute = type.GetCustomAttributes().FirstOrDefault(a => a.GetType().FullName == "XrmFramework.CustomApiAttribute");

            if (customApiAttribute == null)
            {
                throw new Exception($"The custom api type {type.FullName} must have a CustomApiAttribute defined");
            }

            var name = string.IsNullOrWhiteSpace(customApiAttribute.Name) ? type.Name : customApiAttribute.Name;

            var customApi = new CustomApi
            {
                DisplayName = string.IsNullOrWhiteSpace(customApiAttribute.DisplayName) ? name : customApiAttribute.DisplayName,
                Name = name,
                AllowedCustomProcessingStepType = new OptionSetValue((int)customApiAttribute.AllowedCustomProcessing),
                BindingType = new OptionSetValue((int)customApiAttribute.BindingType),
                BoundEntityLogicalName = customApiAttribute.BoundEntityLogicalName,
                Description = string.IsNullOrWhiteSpace(customApiAttribute.Description) ? name : customApiAttribute.Description,
                ExecutePrivilegeName = customApiAttribute.ExecutePrivilegeName,
                IsFunction = customApiAttribute.IsFunction,
                IsPrivate = customApiAttribute.IsPrivate,
                Prefix = _solutionContext.Publisher.CustomizationPrefix,
                WorkflowSdkStepEnabled = customApiAttribute.WorkflowSdkStepEnabled,
                FullName = type.FullName,
            };

            foreach (var argument in record.Arguments)
            {
                if (argument.IsInArgument)
                {
                    customApi.AddChild(FromXrmFrameworkArgument<CustomApiRequestParameter>(customApi.Name, argument));
                }
                else
                {
                    customApi.AddChild(FromXrmFrameworkArgument<CustomApiResponseProperty>(customApi.Name, argument));
                }
            }

            return customApi;
        }

        private T FromXrmFrameworkArgument<T>(string customApiName, dynamic argument) where T : ICustomApiComponent, new()
        {
            var res = new T()
            {
                Description = string.IsNullOrWhiteSpace(argument.Description) ? $"{customApiName}.{argument.ArgumentName}" : argument.Description,
                UniqueName = $"{customApiName}.{argument.ArgumentName}",
                DisplayName = string.IsNullOrWhiteSpace(argument.DisplayName) ? $"{customApiName}.{argument.ArgumentName}" : argument.DisplayName,
                Type = new OptionSetValue((int)argument.ArgumentType),
                Name = argument.ArgumentName,
            };

            if (res is CustomApiRequestParameter)
            {
                res.IsOptional = argument.IsOptional;
            }
            return res;
        }

    }
}
