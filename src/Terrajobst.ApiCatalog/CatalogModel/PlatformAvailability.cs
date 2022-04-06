using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using NuGet.Frameworks;

namespace Terrajobst.ApiCatalog;

public sealed class PlatformAvailability
{
    private static PlatformAvailability Unknown { get; } = new PlatformAvailability(PlatformAvailabilityKind.Unknown,
                                                                                    null,
                                                                                    PlatformList.Empty);

    private static PlatformAvailability Unlimited { get; } = new PlatformAvailability(PlatformAvailabilityKind.Unlimited,
                                                                                      null,
                                                                                      PlatformList.Empty);

    private static PlatformAvailability FrameworkLimited(FrameworkModel framework, string platformName)
    {
        var platformList = PlatformList.Supported(platformName);
        return new PlatformAvailability(PlatformAvailabilityKind.FrameworkLimited, framework, platformList);
    }

    private static PlatformAvailability AssemblyLimited(AssemblyModel assembly, IEnumerable<PlatformSupportModel> platformSupport)
    {
        var platformList = PlatformList.For(platformSupport);
        return new PlatformAvailability(PlatformAvailabilityKind.AssemblyLimited, assembly, platformList);
    }

    private static PlatformAvailability ApiLimited(ApiModel api, IEnumerable<PlatformSupportModel> platformSupport)
    {
        var platformList = PlatformList.For(platformSupport);
        return new PlatformAvailability(PlatformAvailabilityKind.ApiLimited, api, platformList);
    }

    public static PlatformAvailability Create(ApiModel api)
    {
        // We don't want to associate platform information with namespaces, just members and containing types.
        if (api.Kind == ApiKind.Namespace)
            return null;

        var frameworksByAssembly = api.Catalog.Frameworks.SelectMany(fx => fx.Assemblies, (fx, a) => (Framework: fx, Assembly: a))
                                                         .ToLookup(t => t.Assembly, t => t.Framework);

        var apiAvailability = ApiAvailability.Create(api);
        var latestPlatformNeutralAnnotation = apiAvailability.Frameworks.Where(a => IsAnnotated(a.Framework) && !a.Framework.HasPlatform)
                                                                        .MaxBy(a => a.Framework.Version);

        if (latestPlatformNeutralAnnotation is not null)
            return Create(latestPlatformNeutralAnnotation.Declaration);

        var frameworkPlatforms = apiAvailability.Frameworks.Where(a => IsAnnotated(a.Framework) && a.Framework.HasPlatform)
                                                           .Select(a => GetFrameworkPlatform(a.Framework))
                                                           .Where(p => !string.IsNullOrEmpty(p))
                                                           .Distinct()
                                                           .ToArray();

        if (frameworkPlatforms.Any())
        {
            var list = PlatformList.Supported(frameworkPlatforms);
            return new PlatformAvailability(PlatformAvailabilityKind.ApiLimited, api, list);
        }

        return Unknown;
    }

    public static PlatformAvailability Create(ApiModel api, FrameworkModel framework)
    {
        var frameworkAssemblies = framework.Assemblies.ToHashSet();
        return Create(api, framework, frameworkAssemblies);
    }

    public static IEnumerable<(ApiModel Api, PlatformAvailability Availability)> Create(IEnumerable<ApiModel> apis, FrameworkModel framework)
    {
        var frameworkAssemblies = framework.Assemblies.ToHashSet();

        foreach (var api in apis)
        {
            PlatformAvailability availability;
            try
            {
                availability = Create(api, framework, frameworkAssemblies);
            }
            catch (Exception ex) when (!Debugger.IsAttached)
            {
                Console.WriteLine($"error: {api.GetFullName()}: {ex.Message}");
                continue;
            }

            yield return (api, availability);
        }
    }

    private static PlatformAvailability Create(ApiModel api, FrameworkModel framework, HashSet<AssemblyModel> frameworkAssemblies)
    {
        var declaration = api.Declarations.FirstOrDefault(d => frameworkAssemblies.Contains(d.Assembly));
        if (declaration == default)
            return null;

        return Create(declaration, framework);
    }

    private static PlatformAvailability Create(ApiDeclarationModel declaration, FrameworkModel framework)
    {
        var annotatedAvailability = Create(declaration);

        if (annotatedAvailability.Kind == PlatformAvailabilityKind.Unlimited)
        {
            var parsedFramework = NuGetFramework.Parse(framework.Name);

            if (!IsAnnotated(parsedFramework))
                return Unknown;

            var frameworkPlatform = GetFrameworkPlatform(parsedFramework);
            if (frameworkPlatform is not null)
                return FrameworkLimited(framework, frameworkPlatform);
        }

        return annotatedAvailability;
    }

    public static PlatformAvailability Create(ApiDeclarationModel declaration)
    {
        // We don't want to associate platform information with namespaces, just members and containing types.
        if (declaration.Api.Kind == ApiKind.Namespace)
            return null;

        foreach (var a in declaration.Api.AncestorsAndSelf())
        {
            var aDeclaration = a.Declarations.FirstOrDefault(d => d.Assembly == declaration.Assembly);
            if (aDeclaration.PlatformSupport.Any())
                return ApiLimited(a, declaration.PlatformSupport);
        }

        var assembly = declaration.Assembly;
        if (assembly.PlatformSupport.Any())
            return AssemblyLimited(assembly, assembly.PlatformSupport);

        return Unlimited;
    }

    private static string GetFrameworkPlatform(NuGetFramework framework)
    {
        if (framework.HasPlatform)
        {
            return framework.PlatformVersion > new Version(0, 0, 0, 0)
                    ? framework.Platform + framework.PlatformVersion
                    : framework.Platform;
        }

        switch (framework.Framework)
        {
            case ".NETFramework":
            case ".NETPortable":
                return "windows";
            case "MonoAndroid":
                return "android";
            case "MonoTouch":
            case "Xamarin.iOS":
                return "ios";
            case "Xamarin.Mac":
                return "macos";
            case "Xamarin.TVOS":
                return "tvos";
            case "Xamarin.WatchOS":
                return "watchos";
        }

        return null;
    }

    private static bool IsAnnotated(NuGetFramework framework)
    {
        return string.Equals(framework.Framework, ".NETCoreApp", StringComparison.OrdinalIgnoreCase) &&
               framework.Version >= new Version(5, 0, 0, 0);
    }

    private PlatformAvailability(PlatformAvailabilityKind kind,
                                 object limitationSource,
                                 PlatformList platformList)
    {
        Kind = kind;
        LimitationSource = limitationSource;
        PlatformList = platformList;
    }

    public PlatformAvailabilityKind Kind { get; }

    public object LimitationSource { get; }

    public PlatformList PlatformList { get; }

    public PlatformAvailability Union(PlatformAvailability other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (other.Kind == PlatformAvailabilityKind.Unknown)
            return this;

        if (other.Kind == PlatformAvailabilityKind.Unlimited)
            return other;

        switch (Kind)
        {
            case PlatformAvailabilityKind.Unknown:
                return other;
            case PlatformAvailabilityKind.Unlimited:
                return this;
            case PlatformAvailabilityKind.FrameworkLimited:
            case PlatformAvailabilityKind.AssemblyLimited:
            case PlatformAvailabilityKind.ApiLimited:
                var list = PlatformList.Union(other.PlatformList);
                var source = Kind == PlatformAvailabilityKind.ApiLimited ? LimitationSource : null;
                return list.IsUnlimited
                        ? Unlimited
                        : new PlatformAvailability(PlatformAvailabilityKind.ApiLimited, source, list);
            default:
                throw new Exception($"Unknown kind {Kind}");
        }
    }

    public override string ToString()
    {
        switch (Kind)
        {
            case PlatformAvailabilityKind.Unknown:
                return "The platform information isn't known.";
            case PlatformAvailabilityKind.Unlimited:
                return "Supported on all platforms.";
            case PlatformAvailabilityKind.FrameworkLimited:
                var framework = (FrameworkModel)LimitationSource;
                return $"The framework {framework.Name} has limited platform support: {PlatformList}";
            case PlatformAvailabilityKind.AssemblyLimited:
                var assembly = (AssemblyModel)LimitationSource;
                return $"The assembly {assembly.Name} has limited platform support. {PlatformList}";
            case PlatformAvailabilityKind.ApiLimited:
                var api = (ApiModel?)LimitationSource;
                return api is null
                    ? $"The API has limited platform support. {PlatformList}"
                    : $"The API {api.Value.GetFullName()} has limited platform support. {PlatformList}";
            default:
                throw new Exception($"Unexpected kind: {Kind}");
        }
    }
}