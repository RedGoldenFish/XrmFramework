using System.Configuration;
using Deploy;
using Microsoft.Extensions.Options;
using SolutionCloner.Services;
using XrmFramework;
using XrmFramework.DeployUtils.Configuration;
using XrmFramework.DeployUtils.Service;


var sourceConnectionString = ConfigurationManager.ConnectionStrings["source"]?.ConnectionString;
var targetConnectionString = ConfigurationManager.ConnectionStrings["target"]?.ConnectionString;

var sourceRegistrationService = new RegistrationService(sourceConnectionString);
var targetRegistrationService = new RegistrationService(targetConnectionString);

var sourceDeploySettings = new DeploySettings()
{
	ConnectionString = sourceConnectionString,
	PluginSolutionUniqueName = "All"
};
var targetDeploySettings = new DeploySettings()
{
	ConnectionString = targetConnectionString,
	PluginSolutionUniqueName = "All"
};

var sourceOptions = new OptionsWrapper<DeploySettings>(sourceDeploySettings);
var targetOptions = new OptionsWrapper<DeploySettings>(targetDeploySettings);

var sourceSolutionCloner = new SolutionClonerService(sourceRegistrationService, sourceOptions);
var targetSolutionCloner = new SolutionClonerService(targetRegistrationService, targetOptions);

Console.WriteLine("Initializing...");
sourceSolutionCloner.InitCloning();
targetSolutionCloner.InitCloning();

Console.WriteLine("Fetching all unique names for source...");
sourceSolutionCloner.FetchAllUniqueNames();

Console.WriteLine("Fetching all unique names for target...");
targetSolutionCloner.FetchAllUniqueNames();

Console.WriteLine("Components only contained in source:");

var diff = targetSolutionCloner.FetchComponentsOnlyContainedInSource(sourceSolutionCloner);

foreach (var entity in diff)
{
	var name = entity.GetAttributeValue<string>("name");
	var entityName = entity.LogicalName;
	var solutions = entity.GetAttributeValue<IEnumerable<string>>(SolutionClonerService.StashSolutionNamesField);

	if (solutions.Count() > 1)
	{
		continue;
	}

	Console.WriteLine($"{entityName} {name}. Solutions : {string.Join(", ", solutions)}");
}



Console.WriteLine("Hello Tuktuk !");
Console.ReadKey();