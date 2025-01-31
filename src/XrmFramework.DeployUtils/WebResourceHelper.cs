﻿// Copyright (c) Christophe Gondouin (CGO Conseils). All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Tooling.Connector;
using System;
using System.Configuration;
using System.IO;
using System.Linq;
using XrmFramework.Definitions;
using XrmFramework.DeployUtils.Configuration;
using XrmFramework.DeployUtils.Model;

namespace XrmFramework.DeployUtils
{
    public static class WebResourceHelper
    {
        public static void SyncWebResources(string webresourcesPath, string projectName)
        {
            var nbWebresources = 0;

            var xrmFrameworkConfigSection = ConfigHelper.GetSection();

            var solutionName = xrmFrameworkConfigSection.Projects.OfType<ProjectElement>().Single(p => p.Name == projectName).TargetSolution;

            var connectionString = ConfigurationManager.ConnectionStrings[xrmFrameworkConfigSection.SelectedConnection].ConnectionString;

            Console.WriteLine($@"You are about to deploy on {connectionString} organization. If ok press any key.");
            Console.ReadKey();
            Console.WriteLine(@"Connecting to CRM...");

            var service = new CrmServiceClient(connectionString);

            var solution = GetSolutionByName(service, solutionName);
            if (solution == null)
            {
                Console.WriteLine(@$"Error : Solution not found : {solutionName}");
                return;
            }

            if (solution.GetAttributeValue<bool>(SolutionDefinition.Columns.IsManaged))
            {
                Console.WriteLine(@$"Error : Solution {solutionName} is managed, no deployment possible.");
                return;
            }

            var publisherId = solution.GetAttributeValue<EntityReference>(SolutionDefinition.Columns.PublisherId).Id;

            var publisher = GetPublisherById(service, publisherId);
            if (publisher == null)
            {
                Console.WriteLine(@$"Error : Publisher not found : {solutionName}");
                return;
            }
            var prefix = publisher.GetAttributeValue<string>(PublisherDefinition.Columns.CustomizationPrefix);
            Console.WriteLine($" ==> Prefix : {prefix}");

            DirectoryInfo root = new DirectoryInfo(webresourcesPath);
            var resourcesToPublish = string.Empty;

            var files = Directory
                    .GetFiles(webresourcesPath, "*.*", SearchOption.AllDirectories)
                    .Select(file => new FileInfo(file))
                    .Where(fi => IsWebResource(fi.Extension))
                    .Select(fi => new WebResource(fi, root, prefix))
                    .ToList();

            foreach (var fi in files)
            {
                //if (fi.Directory.Name == root.Name)
                //{
                //    continue;
                //}

                var publish = false;

                string webResourceUniqueName = fi.FullName;
                Guid webResourceId;

                var webResource = GetWebResource(webResourceUniqueName, service);
                if (webResource == null)
                {
                    webResourceId = CreateWebResource(webResourceUniqueName, fi, solutionName, service);
                    publish = true;
                }
                else
                {
                    // Web resource exists, check if update is required

                    webResourceId = webResource.Id;

                    if (webResource.Equals(fi))
                    {
                        // Content is identical, no need to update
                    }
                    else
                    {
                        var updatedWr = new Entity(WebResourceDefinition.EntityName, webResource.Id);
                        updatedWr[WebResourceDefinition.Columns.Content] = fi.Base64Content;
                        updatedWr[WebResourceDefinition.Columns.DependencyXml] = fi.GetDependenciesXml();

                        service.Update(updatedWr);
                        publish = true;
                    }
                }
                Console.ForegroundColor = publish ? ConsoleColor.DarkGreen : ConsoleColor.White;
                Console.WriteLine($@"{fi.FullName} => {webResourceUniqueName}");
                Console.ForegroundColor = ConsoleColor.White;

                if (publish)
                {
                    resourcesToPublish += $"<webresource>{webResourceId}</webresource>";
                    nbWebresources++;
                }
            }

            if (!string.IsNullOrEmpty(resourcesToPublish))
            {
                Console.WriteLine();
                Console.WriteLine($@"Publishing {nbWebresources} Resources...");

                var request = new PublishXmlRequest
                {
                    ParameterXml =
                        $"<importexportxml><webresources>{resourcesToPublish}</webresources></importexportxml>"
                };

                service.Execute(request);

            }
        }


        /// <summary>
        /// Gets the web resource.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="service">The service.</param>
        /// <returns></returns>
        private static WebResource GetWebResource(string name, IOrganizationService service)
        {
            var query = new QueryExpression(WebResourceDefinition.EntityName);
            query.ColumnSet.AddColumns(WebResourceDefinition.Columns.Content,
                                       WebResourceDefinition.Columns.DependencyXml,
                                       WebResourceDefinition.Columns.Name);
            query.Criteria.AddCondition(WebResourceDefinition.Columns.Name, ConditionOperator.Equal, name);
            var result = service.RetrieveMultiple(query);

            var webResource = result.Entities.Select(e => new WebResource(e)).FirstOrDefault();
            return webResource;
        }

        /// <summary>
        /// Creates the web resource.
        /// </summary>
        /// <param name="webResourceName">Name of the web resource.</param>
        /// <param name="fi">The fi.</param>
        /// <param name="solutionUniqueName">Name of the solution unique.</param>
        /// <param name="service">The service.</param>
        /// <exception cref="System.Exception">Unsupported extension:  + fi.Extension.Remove(0, 1).ToLower()</exception>
        private static Guid CreateWebResource(string webResourceName, WebResource fi, string solutionUniqueName, IOrganizationService service)
        {
            var wr = new Entity(WebResourceDefinition.EntityName);
            wr[WebResourceDefinition.Columns.Name] = webResourceName;
            wr[WebResourceDefinition.Columns.DisplayName] = webResourceName;
            wr[WebResourceDefinition.Columns.Content] = fi.Base64Content;
            wr[WebResourceDefinition.Columns.DependencyXml] = fi.GetDependenciesXml();


            if (string.IsNullOrEmpty(fi.Extension))
            {
                throw new Exception($"No extension found for the file '{fi.FullName}'!");
            }

            string extension = fi.Extension.Remove(0, 1).ToLower();
            switch (extension)
            {
                case "htm":
                case "html":
                    wr[WebResourceDefinition.Columns.WebResourceType] = new OptionSetValue(1);
                    break;
                case "css":
                    wr[WebResourceDefinition.Columns.WebResourceType] = new OptionSetValue(2);
                    break;
                case "js":
                    wr[WebResourceDefinition.Columns.WebResourceType] = new OptionSetValue(3);
                    break;
                case "xml":
                    wr[WebResourceDefinition.Columns.WebResourceType] = new OptionSetValue(4);
                    break;
                case "png":
                    wr[WebResourceDefinition.Columns.WebResourceType] = new OptionSetValue(5);
                    break;
                case "jpg":
                case "jpeg":
                    wr[WebResourceDefinition.Columns.WebResourceType] = new OptionSetValue(6);
                    break;
                case "gif":
                    wr[WebResourceDefinition.Columns.WebResourceType] = new OptionSetValue(7);
                    break;
                case "xap":
                    wr[WebResourceDefinition.Columns.WebResourceType] = new OptionSetValue(8);
                    break;
                case "xsl":
                    wr[WebResourceDefinition.Columns.WebResourceType] = new OptionSetValue(9);
                    break;
                case "ico":
                    wr[WebResourceDefinition.Columns.WebResourceType] = new OptionSetValue(10);
                    break;
                case "svg":
                    wr[WebResourceDefinition.Columns.WebResourceType] = new OptionSetValue(11);
                    break;
                case "resx":
                    wr[WebResourceDefinition.Columns.WebResourceType] = new OptionSetValue(12);
                    break;
                default:
                    throw new Exception("Unsupported extension: " + fi.Extension.Remove(0, 1).ToLower());
            }

            var id = service.Create(wr);

            // Add current web resource to defined solution
            var request = new AddSolutionComponentRequest { AddRequiredComponents = false, ComponentType = 61, ComponentId = id, SolutionUniqueName = solutionUniqueName };
            service.Execute(request);

            return id;
        }

        /// <summary>
        /// Determines whether [is web resource] [the specified extension].
        /// </summary>
        /// <param name="extension">The extension.</param>
        /// <returns></returns>
        private static bool IsWebResource(string extension)
        {
            switch (extension.ToLower())
            {
                case ".htm":
                case ".html":
                case ".css":
                case ".js":
                case ".xml":
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".gif":
                case ".xap":
                case ".xsl":
                case ".ico":
                case ".svg":
                case ".resx":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Gets the Solution
        /// </summary>
        /// <param name="service"></param>
        /// <param name="solutionName"></param>
        /// <returns></returns>
        private static Entity GetSolutionByName(CrmServiceClient service, string solutionName)
        {
            var query = new QueryExpression(SolutionDefinition.EntityName);
            query.ColumnSet.AddColumns(SolutionDefinition.Columns.UniqueName,
                                       SolutionDefinition.Columns.PublisherId,
                                       SolutionDefinition.Columns.IsManaged);
            query.Criteria.AddCondition(SolutionDefinition.Columns.UniqueName, ConditionOperator.Equal, solutionName);

            return service.RetrieveMultiple(query).Entities.FirstOrDefault();
        }

        /// <summary>
        /// Gets the Publisher
        /// </summary>
        /// <param name="service"></param>
        /// <param name="publisherId"></param>
        /// <returns></returns>
        private static Entity GetPublisherById(CrmServiceClient service, Guid publisherId)
        {
            var query = new QueryExpression(PublisherDefinition.EntityName);
            query.ColumnSet.AddColumn(PublisherDefinition.Columns.CustomizationPrefix);
            query.Criteria.AddCondition(PublisherDefinition.Columns.Id, ConditionOperator.Equal, publisherId);

            return service.RetrieveMultiple(query).Entities.FirstOrDefault();
        }
    }
}
