﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Terrajobst.ApiCatalog.Tests;

internal sealed class FluentCatalogBuilder
{
    private readonly List<XDocument> _documents = new();

    public FluentCatalogBuilder AddFramework(string name, Action<FrameworkBuilder> action)
    {
        var builder = new FrameworkBuilder(name);
        action(builder);
        var entry = builder.Build();
        var doc = entry.ToDocument();
        _documents.Add(doc);
        return this;
    }

    public FluentCatalogBuilder AddPackage(string id, string version, Action<PackageBuilder> action)
    {
        var builder = new PackageBuilder(id, version);
        action(builder);
        var entry = builder.Build();
        var doc = entry.ToDocument();
        _documents.Add(doc);
        return this;
    }

    public async Task<ApiCatalogModel> BuildAsync()
    {
        var fileName = Path.GetTempFileName();
        using (var builder = CatalogBuilder.Create(fileName))
        {
            foreach (var doc in _documents)
                builder.IndexDocument(doc);
        }

        using (var stream = new MemoryStream())
        {
            await ApiCatalogModel.ConvertAsync(fileName, stream);
            stream.Position = 0;
            return ApiCatalogModel.Load(stream);
        }
    }

    public sealed class FrameworkBuilder
    {
        private readonly string _frameworkName;
        private readonly List<AssemblyEntry> _assemblyEntries = new();

        public FrameworkBuilder(string frameworkName)
        {
            _frameworkName = frameworkName;
        }

        public FrameworkBuilder AddAssembly(string name, string source)
        {
            var referencePaths = new[] {
                typeof(object).Assembly.Location,
            };

            var references = referencePaths.Select(p => MetadataReference.CreateFromFile(p)).ToArray();

            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true,
                optimizationLevel: OptimizationLevel.Release
            );

            var compilation = CSharpCompilation.Create(name,
                new[] { CSharpSyntaxTree.ParseText(source) },
                references,
                options
            );

            using var peStream = new MemoryStream();

            var result = compilation.Emit(peStream);
            if (!result.Success)
            {
                var diagnostics = string.Join(Environment.NewLine, result.Diagnostics);
                var message = $"Compilation has errors{Environment.NewLine}{diagnostics}";
                throw new Exception(message);
            }

            peStream.Position = 0;

            var reference = MetadataReference.CreateFromStream(peStream, filePath: $"{name}.dll");
            var context = MetadataContext.Create(new[] { reference }, references);
            var entry = AssemblyEntry.Create(context.Assemblies.Single());
            _assemblyEntries.Add(entry);

            return this;
        }

        public FrameworkEntry Build()
        {
            var assemblies = _assemblyEntries.ToArray();
            return FrameworkEntry.Create(_frameworkName, assemblies);
        }
    }

    public sealed class PackageBuilder
    {
        private readonly string _id;
        private readonly string _version;
        private readonly List<FrameworkEntry> _frameworks = new();

        public PackageBuilder(string id, string version)
        {
            _id = id;
            _version = version;
        }

        public PackageBuilder AddFramework(string name, Action<FrameworkBuilder> action)
        {
            var builder = new FrameworkBuilder(name);
            action(builder);
            _frameworks.Add(builder.Build());
            return this;
        }

        public PackageEntry Build()
        {
            var frameworks = _frameworks.ToArray();
            return PackageEntry.Create(_id, _version, frameworks);
        }
    }
}