﻿using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Internal;
using XrmFramework.Analyzers.Extensions;
using XrmFramework.DeployUtils.Generators;

namespace XrmFramework.Analyzers.Helpers;

public class LoggedServiceCodeGenerator : CodeGeneratorBase
{
	public string Generate(ITypeSymbol serviceType)
	{
		var namespaces = new List<string> {"System.Diagnostics", "System", "System.Runtime.CompilerServices", "XrmFramework.Definitions"};

		namespaces.AddRange(serviceType.GetNamespaces());

		if (serviceType.BaseType != default)
			namespaces.Add(serviceType.BaseType.ContainingNamespace.GetFullMetadataName());

		namespaces.AddRange(serviceType.AllInterfaces.Select(i => i.ContainingNamespace.GetFullMetadataName()));

		namespaces.AddRange(GetNamespaces(serviceType.GetMembers().OfType<IMethodSymbol>()));

		namespaces.AddRange(serviceType.GetMembers().OfType<IPropertySymbol>()
			.SelectMany(p => p.Type.GetNamespaces()
				.Concat(GetAttributeNamespaces(p.GetAttributes()))));

		var isIService = serviceType.GetFullMetadataName() == "XrmFramework.IService";

		var builder = new IndentedStringBuilder();

		builder.AppendLine("#if !DISABLE_SERVICES");

		foreach (var ns in namespaces.Where(n => !string.IsNullOrEmpty(n)).OrderBy(n => n).Distinct())
			builder
				.Append("using ")
				.Append(ns)
				.AppendLine(";");

		builder
			.AppendLine()
			.Append("namespace ")
			.AppendLine(serviceType.ContainingNamespace.GetFullMetadataName() ??
			            throw new NotSupportedException($"Service type {serviceType} has no namespace"))
			.AppendLine("{");

		using (builder.Indent())
		{
			var className = $"Logged{serviceType.Name}";

			builder
				.AppendLine("[DebuggerStepThrough, CompilerGenerated]")
				.Append("public class ")
				.Append(className)
				.Append(" : ")
				.Append(!isIService ? "LoggedIService, " : "LoggedServiceBase, ")
				.AppendLine(serviceType.Name)
				.AppendLine("{");

			using (builder.Indent())
			{
				if (!isIService)
					builder
						.Append("protected new ")
						.Append(serviceType.Name)
						.Append(" Service => (")
						.Append(serviceType.Name)
						.AppendLine(") base.Service;");

				builder
					.AppendLine()
					.AppendLine("#region .ctor")
					.Append("public ").Append(className)
					.Append("(IServiceContext context, ")
					.Append(serviceType.Name)
					.AppendLine(" service) : base(context, service)");

				builder
					.AppendLine("{")
					.AppendLine("}")
					.AppendLine("#endregion");

				foreach (var method in serviceType.GetMembers().OfType<IMethodSymbol>())
					GenerateMethod(method, builder);
			}

			builder.AppendLine("}");
		}

		builder.AppendLine("}");

		builder.Append("#endif");

		return builder.ToString();
	}

	private bool IsAssignableFrom(ITypeSymbol symbol, string targetTypeFullName)
	{
		if (symbol.GetFullMetadataName() == targetTypeFullName) return true;

		return (symbol.BaseType != null && IsAssignableFrom(symbol.BaseType, targetTypeFullName))
		       || symbol.AllInterfaces.Any(i => IsAssignableFrom(i, targetTypeFullName));
	}

	private void GenerateMethod(IMethodSymbol m, IndentedStringBuilder builder)
	{
		var isAsyncMethod = IsAssignableFrom(m.ReturnType, typeof(Task).FullName);

		var isObsoleteMethod = m.GetAttributes()
			.Any(a => a.AttributeClass.GetFullMetadataName() == "System.ObsoleteAttribute");

		if (isObsoleteMethod) builder.AppendLine("#pragma warning disable CS0612");

		builder
			.AppendLine()
			.Append("public ");

		if (isAsyncMethod) builder.Append("async ");

		GenerateMethodSignature(m, builder, true);

		builder
			.AppendLine()
			.AppendLine("{");

		using (builder.Indent())
		{
			GetParametersCheck(m, builder);

			builder
				.AppendLine()
				.AppendLine("var sw = new Stopwatch();")
				.AppendLine("sw.Start();")
				.AppendLine();

			GetMethodLog(m, true, builder);

			builder
				.AppendLine()
				;


			if (m.ReturnType.SpecialType != SpecialType.System_Void &&
			    m.ReturnType.GetFullMetadataName() != typeof(Task).FullName)
				builder
					.Append("var returnValue = ");

			if (isAsyncMethod) builder.Append("await ");

			builder
				.Append("Service.");

			GenerateMethodSignature(m, builder, false);

			builder
				.AppendLine(";")
				.AppendLine();


			GetMethodLog(m, false, builder);

			if (m.ReturnType.SpecialType != SpecialType.System_Void &&
			    m.ReturnType.GetFullMetadataName() != typeof(Task).FullName)
				builder
					.AppendLine()
					.AppendLine("return returnValue;");
		}

		builder.AppendLine("}");
		if (isObsoleteMethod) builder.AppendLine("#pragma warning restore CS0612");
	}

	private void GenerateMethodSignature(IMethodSymbol m, IndentedStringBuilder builder, bool displayTypes)
	{
		var formatFull = SymbolDisplayFormat.FullyQualifiedFormat
			.WithMemberOptions(SymbolDisplayMemberOptions.IncludeAccessibility |
			                   SymbolDisplayMemberOptions.IncludeModifiers |
			                   SymbolDisplayMemberOptions.IncludeParameters |
			                   SymbolDisplayMemberOptions.IncludeType |
			                   SymbolDisplayMemberOptions.IncludeConstantValue)
			.WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeVariance |
			                     SymbolDisplayGenericsOptions.IncludeTypeParameters |
			                     SymbolDisplayGenericsOptions.IncludeTypeConstraints)
			.WithParameterOptions(SymbolDisplayParameterOptions.IncludeDefaultValue |
			                      SymbolDisplayParameterOptions.IncludeName |
			                      SymbolDisplayParameterOptions.IncludeType |
			                      SymbolDisplayParameterOptions.IncludeParamsRefOut |
			                      SymbolDisplayParameterOptions.IncludeExtensionThis);

		if (!displayTypes)
			formatFull = formatFull
				.RemoveMemberOptions(SymbolDisplayMemberOptions.IncludeType)
				.RemoveMemberOptions(SymbolDisplayMemberOptions.IncludeParameters)
				.RemoveGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeConstraints);

		builder.Append(m.ToDisplayString(formatFull));

		if (!displayTypes)
		{
			builder
				.Append("(");

			var isFirst = true;

			foreach (var parameter in m.Parameters)
			{
				if (isFirst) isFirst = false;
				else
					builder
						.Append(", ");

				if (parameter.RefKind == RefKind.Out)
					builder
						.Append("out ");
				else if (parameter.RefKind == RefKind.Ref)
					builder
						.Append("ref ");

				builder
					.Append(parameter.Name);
			}

			builder.Append(")");
		}
	}

	private void GetMethodLog(IMethodSymbol method, bool start, IndentedStringBuilder builder)
	{
		builder
			.Append("Log(")
			.Append("nameof(")
			.Append(method.Name)
			.Append("), \"")
			.Append(start ? "Start" : "End : duration = {0}");

		var parameters = method.Parameters;
		var i = start ? 0 : 1;

		var sb2 = new StringBuilder();
		if (!start) sb2.AppendFormat("sw.Elapsed");

		if (parameters.Any(p => (start && p.RefKind != RefKind.Out) || (!start && p.RefKind == RefKind.Out)))
		{
			builder.Append(": ");
			foreach (var param in parameters)
				if ((start && param.RefKind != RefKind.Out) || (!start && param.RefKind == RefKind.Out))
				{
					if (i > 0)
					{
						builder.Append(", ");
						sb2.Append(", ");
					}

					builder.Append(param.Name).Append(" = {").Append(i++).Append("}");
					sb2.Append(param.Name);
				}
		}

		if (!start && method.ReturnType.SpecialType != SpecialType.System_Void &&
		    method.ReturnType.GetFullMetadataName() != typeof(Task).FullName)
		{
			if (!builder.ToString().Contains(": ")) builder.Append(": ");
			if (i > 0)
			{
				builder.Append(", ");
				sb2.Append(", ");
			}

			builder.Append("returnValue = {").Append(i).Append("}");
			sb2.AppendFormat("returnValue");
		}

		builder.Append("\"");
		if (sb2.Length > 0) builder.Append(", ").Append(sb2.ToString());
		builder.AppendLine(");");
	}

	private void GetParametersCheck(IMethodSymbol method, IndentedStringBuilder builder)
	{
		builder.AppendLine("#region Parameters check");
		foreach (var param in method.Parameters)
		{
			var isNullable = param.GetAttributes().Any(a => a.AttributeClass is
			{
				Name: "NullableAttribute",
				ContainingNamespace: {Name: "XrmFramework", ContainingNamespace: {IsGlobalNamespace: true}}
			});

			if (param.RefKind == RefKind.Out) continue;
			if (isNullable) continue;
			if (param.Type.IsPrimitive() && param.Type.SpecialType != SpecialType.System_String) continue;
			if (param.Type.TypeKind == TypeKind.Enum) continue;
			if (param.IsOptional) continue;
			if (param.Type.SpecialType == SpecialType.System_Decimal) continue;

			if (param.Type.SpecialType == SpecialType.System_String)
			{
				builder
					.Append("if (string.IsNullOrWhiteSpace(")
					.Append(param.Name)
					.Append("))");
			}
			else
			{
				var isGenericParam = param.Type.IsDefinition && param.Type.TypeKind == TypeKind.TypeParameter;
				builder.Append("if (");
				if (isGenericParam)
					builder
						.Append("Equals(");
				builder
					.Append(param.Name)
					.Append(isGenericParam ? ", " : " == ")
					.Append("default");

				if (isGenericParam)
					builder
						.Append("(")
						.Append(param.Type.Name)
						.Append("))");

				builder.Append(")");
			}

			builder
				.AppendLine()
				.AppendLine("{");

			using (builder.Indent())
			{
				builder
					.Append("throw new ArgumentNullException(nameof(")
					.Append(param.Name).AppendLine("));");
			}

			builder.AppendLine("}");
		}

		builder.AppendLine("#endregion");
	}
}