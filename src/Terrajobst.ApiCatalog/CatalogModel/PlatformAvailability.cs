using System;
using System.Collections.Generic;
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
        var platformList = PlatformList.ForSinglePlatform(platformName);
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
            catch (Exception ex)
            {
                Console.WriteLine($"error: {api.GetFullName()}: {ex.Message}");
                continue;
            }

            yield return (api, availability);
        }
    }

    private static PlatformAvailability Create(ApiModel api, FrameworkModel framework, HashSet<AssemblyModel> frameworkAssemblies)
    {
        // We don't want to associate platform information with namespaces, just members and containing types.
        if (api.Kind == ApiKind.Namespace)
            return null;

        if (!frameworkAssemblies.Overlaps(api.Declarations.Select(d => d.Assembly)))
            return null;

        var parsedFramework = NuGetFramework.Parse(framework.Name);
        var frameworkPlatform = GetFrameworkPlatform(parsedFramework);
        if (frameworkPlatform is not null)
            return FrameworkLimited(framework, frameworkPlatform);

        if (parsedFramework.Framework == ".NETStandard" ||
            parsedFramework.Framework == ".NETCoreApp" && parsedFramework.Version < new Version(5, 0))
        {
            return Unknown;
        }

        var platformSupport = Enumerable.Empty<PlatformSupportModel>();
        var limitationSource = (object)api;

        foreach (var a in api.AncestorsAndSelf())
        {
            var declaration = a.Declarations.FirstOrDefault(d => frameworkAssemblies.Contains(d.Assembly));
            if (declaration.PlatformSupport.Any())
                return ApiLimited(a, declaration.PlatformSupport);
        }

        if (!platformSupport.Any())
        {
            var declaration = api.Declarations.First(d => frameworkAssemblies.Contains(d.Assembly));
            var assembly = declaration.Assembly;
            if (assembly.PlatformSupport.Any())
                return AssemblyLimited(assembly, assembly.PlatformSupport);
        }

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
                var api = (ApiModel)LimitationSource;
                return $"The API {api.GetFullName()} has limited platform support. {PlatformList}";
            default:
                throw new Exception($"Unexpected kind: {Kind}");
        }
    }
}