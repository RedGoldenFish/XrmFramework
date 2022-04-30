﻿using AutoMapper;
using Microsoft.Xrm.Sdk;
using System;
using XrmFramework.Definitions;
using XrmFramework.DeployUtils.Context;
using XrmFramework.DeployUtils.Model;

namespace XrmFramework.DeployUtils.Configuration
{
    public class AutoMapperLocalToLocalProfile : Profile
    {
        public AutoMapperLocalToLocalProfile()
        {
            CreateMap<AssemblyInfo, AssemblyInfo>();
            CreateMap<Plugin, Plugin>()
                .ForMember(dest => dest.Children, opt => opt.Ignore());
            CreateMap<Step, Step>();
            CreateMap<StepImage, StepImage>();


            CreateMap<CustomApi, CustomApi>()
                .ForMember(dest => dest.Children, opt => opt.Ignore());

            CreateMap<CustomApiRequestParameter, CustomApiRequestParameter>();
            CreateMap<CustomApiResponseProperty, CustomApiResponseProperty>();

            ShouldMapField = fi => fi.IsPublic || fi.Name is "_internalList" or "_inArguments" or "_outArguments";

            CreateMap<StepCollection, StepCollection>();

            CreateMap<IAssemblyContext, IAssemblyContext>()
                .ConstructUsing(_ => new AssemblyContext())
                .ForMember(dest => dest.AssemblyInfo,
                    opt => opt.MapFrom(src => src.AssemblyInfo))
                .ForMember(dest => dest.Children, opt => opt.Ignore());
        }
    }

    public class AutoMapperRemoteToLocalProfile : Profile
    {
        public AutoMapperRemoteToLocalProfile()
        {
            CreateMap<Deploy.PluginAssembly, AssemblyInfo>();

            CreateMap<AssemblyInfo, IAssemblyContext>()
                .ConstructUsing(src => new AssemblyContext())
                .ForMember(dest => dest.AssemblyInfo,
                    opt => opt.MapFrom(src => src));

            CreateMap<Deploy.CustomApi, CustomApi>()
            .ForMember(p => p.ParentId, opt => opt.MapFrom(d => d.PluginTypeId.Id));

            CreateMap<Deploy.CustomApiRequestParameter, CustomApiRequestParameter>()
                .ForMember(dest => dest.UniqueName,
                    opt => opt.MapFrom(src => src.Name))
                .ForMember(dest => dest.Name,
                    opt => opt.MapFrom(src => src.UniqueName))
                .ForMember(p => p.ParentId,
                    opt => opt.MapFrom(d => d.CustomApiId.Id));


            CreateMap<Deploy.CustomApiResponseProperty, CustomApiResponseProperty>()
                .ForMember(dest => dest.UniqueName,
                    opt => opt.MapFrom(src => src.Name))
                .ForMember(dest => dest.Name,
                    opt => opt.MapFrom(src => src.UniqueName))
                .ForMember(p => p.ParentId,
            opt => opt.MapFrom(d => d.CustomApiId.Id));

        }
    }

    public class AutoMapperLocalToRemoteProfile : Profile
    {
        public AutoMapperLocalToRemoteProfile()
        {
            CreateMap<AssemblyInfo, Deploy.PluginAssembly>()
                .ForMember(dest => dest.Content,
                    opt => opt.MapFrom(src => Convert.ToBase64String(src.Content)));

            CreateMap<CustomApi, Deploy.CustomApi>()
                .ForMember(p => p.PluginTypeId,
                    opt => opt.MapFrom(c => new EntityReference(PluginTypeDefinition.EntityName, c.ParentId)));

            CreateMap<CustomApiRequestParameter, Deploy.CustomApiRequestParameter>()
                .ForMember(dest => dest.UniqueName,
                    opt => opt.MapFrom(src => src.Name))
                .ForMember(dest => dest.Name,
                    opt => opt.MapFrom(src => src.UniqueName))
                .ForMember(p => p.CustomApiId,
                    opt => opt.MapFrom(c => new EntityReference(CustomApiDefinition.EntityName, c.ParentId)))
                .ForMember(dest => dest.LogicalEntityName,
                    opt => opt.Ignore());


            CreateMap<CustomApiResponseProperty, Deploy.CustomApiResponseProperty>()
                .ForMember(dest => dest.UniqueName,
                    opt => opt.MapFrom(src => src.Name))
                .ForMember(dest => dest.Name,
                    opt => opt.MapFrom(src => src.UniqueName))
                .ForMember(p => p.CustomApiId,
                    opt => opt.MapFrom(c => new EntityReference(CustomApiDefinition.EntityName, c.ParentId)))
                .ForMember(dest => dest.LogicalEntityName,
                    opt => opt.Ignore());

        }
    }
}