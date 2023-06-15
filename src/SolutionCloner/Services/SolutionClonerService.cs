using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Deploy;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using XrmFramework;
using XrmFramework.Definitions;
using XrmFramework.DeployUtils.Configuration;
using XrmFramework.DeployUtils.Context;
using XrmFramework.DeployUtils.Service;

namespace SolutionCloner.Services;

public class SolutionClonerService : SolutionContext
{
	private static readonly (componenttype ComponentType, string EntityName)[] TypeMapper =
	{
		(componenttype.Entity, "entity"),
		// (componenttype.Attribute, "attribute"),
		// (componenttype.OptionSet, "optionset"),
		// (componenttype.EntityRelationship, "entityrelationship"),
		(componenttype.Role, "role"),
		(componenttype.Workflow, "workflow"),
		// (componenttype.EmailTemplate, "template"),
		// (componenttype.RibbonCustomization, "ribboncustomization"),
		(componenttype.WebResource, "webresource"),
		// (componenttype.SystemForm, "systemform"),
		// (componenttype.SiteMap, "sitemap"),
		(componenttype.FieldSecurityProfile, "fieldsecurityprofile"),
		(componenttype.PluginAssembly, PluginAssemblyDefinition.EntityName),
	};

	private readonly Dictionary<Guid, string> _idsToNames = new();
	private readonly Dictionary<string, Guid> _namesToIds = new();
	private readonly Dictionary<Guid, string> _visibleSolutionNames = new();
	public static readonly string StashNameField = "custom_name";
	public static readonly string StashSolutionIdsField = "custom_solutionids";
	public static readonly string StashSolutionNamesField = "custom_solutionnames";

	public SolutionClonerService(IRegistrationService service, IOptions<DeploySettings> settings) : base(service, settings) { }

	public void InitCloning()
	{
		InitSolution(false);
		InitComponents();
		InitVisibleSolutionNames();
	}

	private void InitVisibleSolutionNames()
	{
		var query = new QueryExpression(SolutionDefinition.EntityName);
		
		query.ColumnSet.AddColumns(SolutionDefinition.Columns.UniqueName);
		query.Criteria.AddCondition(SolutionDefinition.Columns.IsVisible, ConditionOperator.Equal, true);
		
		var solutions = _service.RetrieveMultiple(query).Entities;
		
		foreach (var solution in solutions)
		{
			var uniqueName = solution.GetAttributeValue<string>(SolutionDefinition.Columns.UniqueName);
			var id = solution.Id;
			
			_visibleSolutionNames[id] = uniqueName;
		}
	}

	public void FetchAllUniqueNames()
	{
		foreach (var (componentType, entityName) in TypeMapper)
		{
			Console.WriteLine($"Fetching all {entityName} names...");
			FetchAllNamesForType(componentType, entityName);
		}
	}

	private void FetchAllNamesForType(componenttype componentType, string entityName)
	{
		var entityIdField = $"{entityName}id";
		var componentTypeValue = componentType.ToInt();

		var query = new QueryExpression(entityName);

		query.ColumnSet.AddColumns("name");

		var componentsWithType = _components.Where(c => c.ComponentType.Value == componentTypeValue).ToList();

		while (componentsWithType.Any())
		{
			var componentsBatch = componentsWithType.Take(199);

			FetchAllNamesForBatch(query, componentsBatch, entityIdField);

			componentsWithType.RemoveRange(0, componentsBatch.Count());
		}
	}

	private void FetchAllNamesForBatch(QueryExpression query, IEnumerable<SolutionComponent> componentsBatch, string entityIdField)
	{
		query.Criteria = new FilterExpression(LogicalOperator.Or);

		foreach (var component in componentsBatch)
		{
			query.Criteria.AddCondition(entityIdField, ConditionOperator.Equal, component.ObjectId);
		}

		var results = _service.RetrieveMultiple(query);

		foreach (var result in results.Entities)
		{
			var name = result.GetAttributeValue<string>("name");
			var id = result.Id;

			_idsToNames[id] = name;
			_namesToIds[name] = id;
		}
	}

	public void AbsorbOtherComponents(SolutionClonerService source)
	{
		var sourceComponentsToAdd = EnumerateComponentsOnlyContainedInSource(source);

		foreach (var sourceComponent in sourceComponentsToAdd)
		{

			var targetEntity = GetCorrespondingComponent(sourceComponent);

			AddComponentToSolution(sourceComponent, targetEntity.Id);
		}
	}

	public IEnumerable<SolutionComponent> EnumerateComponentsOnlyContainedInSource(SolutionClonerService source)
	{
		return source.EnumerateComponentsWithNames()
		   .Where(c =>
			{
				var name = c[StashNameField] as string;
				return !_namesToIds.ContainsKey(name);
			})
		   .OrderBy(c => c.ComponentType.Value);
	}
	
	public IEnumerable<Entity> FetchComponentsOnlyContainedInSource(SolutionClonerService source)
	{
		return EnumerateComponentsOnlyContainedInSource(source)
			.Select(GetCorrespondingComponent);
	}

	private void AddComponentToSolution(SolutionComponent sourceComponent, Guid targetId)
	{
		var res = new AddSolutionComponentRequest
		{
			AddRequiredComponents = false,
			ComponentId = targetId,
			SolutionUniqueName = SolutionName,
			ComponentType = sourceComponent.ComponentType.Value
		};

		_service.Execute(res);
	}

	private Entity GetCorrespondingComponent(SolutionComponent sourceComponent)
	{
		var entityName = TypeMapper.First(t => t.ComponentType.ToInt() == sourceComponent.ComponentType.Value).EntityName;

		var query = new QueryExpression(entityName)
		{
			ColumnSet = new ColumnSet("name")
		};

		query.Criteria.AddCondition("name", ConditionOperator.Equal, sourceComponent[StashNameField]);
		
		var entity_componentSolution_link = query.AddLink(
			SolutionComponentDefinition.EntityName,
			$"{entityName}id",
			"objectid",
			JoinOperator.LeftOuter);
		
		entity_componentSolution_link.Columns.AddColumns("solutionid");
		
		entity_componentSolution_link.EntityAlias = "e_cs";

		var response = _service.RetrieveMultiple(query).Entities.ToList();

		if (!response.Any())
		{
			return new Entity(entityName)
			{
				["name"] = sourceComponent[StashNameField],
				[StashSolutionIdsField] = new List<Guid>(),
				[StashSolutionNamesField] = new List<string>(),
			};
		}
		
		var result = response[0];

		var visibleSolutions = response
		   .Where(e =>
			
				e.TryGetAttributeValue<AliasedValue>("e_cs.solutionid", out var aliasedSolution)
				&& aliasedSolution.Value is EntityReference solutionRef
				&& _visibleSolutionNames.ContainsKey(solutionRef.Id)
			)
		   .Select(e => e.GetAliasedValue<EntityReference>("e_cs.solutionid").Id)
		   .ToList();

		var solutionIds = visibleSolutions;
		var solutionNames = visibleSolutions.Select(s => _visibleSolutionNames[s]).ToList();
		
		result[StashSolutionIdsField] = solutionIds;
		result[StashSolutionNamesField] = solutionNames;

		return result;
	}

	private IEnumerable<SolutionComponent> EnumerateComponentsWithNames()
	{
		foreach (var component in _components)
		{
			if (!component.ObjectId.HasValue || !_idsToNames.TryGetValue(component.ObjectId.Value, out var name))
			{
				continue;
			}

			component[StashNameField] = name;
			yield return component;
		}
	}
}
