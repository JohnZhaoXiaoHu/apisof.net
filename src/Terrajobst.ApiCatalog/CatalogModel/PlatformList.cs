using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Terrajobst.ApiCatalog;

public sealed class PlatformList
{
    public static PlatformList Empty { get; } = new PlatformList(PlatformListKind.ExclusionList, new Dictionary<string, IReadOnlyList<(Version, bool)>>());

    public static PlatformList ForSinglePlatform(string platform)
    {
        return For(new[] { (platform, true) });
    }

    public static PlatformList For(IEnumerable<PlatformSupportModel> platformSupport)
    {
        return For(platformSupport.Select(ps => (ps.PlatformName, ps.IsSupported)));
    }

    public static PlatformList For(IEnumerable<(string Platform, bool IsSupported)> platformSupport)
    {
        return For(platformSupport.Select(t =>
        {
            var (n, v) = ParsePlatform(t.Platform);
            return (n, v, t.IsSupported);
        }));
    }

    public static PlatformList For(IEnumerable<(string PlatformName, Version PlatformVersion, bool IsSupported)> platformSupport)
    {
        var versionsByName = platformSupport.GroupBy(ps => ps.PlatformName, ps => (ps.PlatformVersion, ps.IsSupported), StringComparer.OrdinalIgnoreCase)
                                            .ToDictionary(g => g.Key, g => (IReadOnlyList<(Version Version, bool IsSupported)>) g.OrderBy(t => t.PlatformVersion).ToArray());

        if (versionsByName.Count == 0)
            return Empty;

        // TODO: Clarify behavior of our analyzer.
        //
        // Logical, an given API is either using an inclusion list of an exclusion list.
        // Some APIs, such as the System.IO.FileSystemWatcher, are marked with Unsupported(A, B, C) and Supported(D).
        // D is implicitly supported.
        //
        // Right now, I assume an exclusion if any platforms starts with unsupported.

        var isExclusionList = versionsByName.Any(p => !p.Value[0].IsSupported);
        if (isExclusionList)
        {
            var toBeRemoved = versionsByName.Where(kv => kv.Value[0].IsSupported).ToArray();
            foreach (var (k, _) in toBeRemoved)
                versionsByName.Remove(k);
        }

        var kind = isExclusionList ? PlatformListKind.ExclusionList : PlatformListKind.InclusionList;
        return new PlatformList(kind, versionsByName);
    }

    private static (string PlatformName, Version PlatformVersion) ParsePlatform(string platform)
    {
        var match = Regex.Match(platform, "(?<name>.*)((?<major>[0-9]+)(.(?<minor>[0-9]+))?(.(?<build>[0-9]+))?(.(?<revision>[0-9]+))?)?");
        if (!match.Success)
            throw new FormatException($"'{platform}' isn't a valid platform and version.");

        var name = match.Groups["name"].Value;
        var major = GetVersion(match, "major");
        var minor = GetVersion(match, "minor");
        var build = GetVersion(match, "build");
        var revision = GetVersion(match, "revision");
        var version = build == 0 && revision == 0
                        ? new Version(major, minor)
                        : new Version(major, minor, build, revision);

        return (name, version);

        static int GetVersion(Match match, string groupName)
        {
            var text = match.Groups[groupName].Value;
            return string.IsNullOrEmpty(text) ? 0 : int.Parse(text);
        }
    }

    private readonly IReadOnlyDictionary<string, IReadOnlyList<(Version Version, bool IsSupported)>> _platforms;

    private PlatformList(PlatformListKind kind,
                         IReadOnlyDictionary<string, IReadOnlyList<(Version Version, bool IsSupported)>> platforms)
    {
        Kind = kind;
        _platforms = platforms;
    }

    public PlatformListKind Kind { get; }

    public IEnumerable<string> Names => _platforms.Keys;

    public IReadOnlyList<(Version Version, bool IsSupported)> GetRanges(string platformName)
    {
        if (_platforms.TryGetValue(platformName, out var result))
            result = Array.Empty<(Version, bool)>();

        return result;
    }

    public bool IsSupported(string platformName, Version version)
    {
        if (_platforms.TryGetValue(platformName, out var versions))
        {
            var previous = new Version(0, 0, 0, 0);

            foreach (var (c, isSupported) in versions)
            {
                if (previous > version)
                    break;

                if (version >= c)
                    return isSupported;

                previous = c;
            }
        }

        return Kind == PlatformListKind.InclusionList;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();

        if (Kind == PlatformListKind.InclusionList)
            sb.Append("Only supported on ");
        else
            sb.Append("Not supported on ");

        var isFirstPlatform = true;

        foreach (var (platform, versions) in _platforms)
        {
            if (isFirstPlatform)
                isFirstPlatform = false;
            else
                sb.Append(", ");

            if (versions.Count == 1 && versions[0].Version == new Version(0, 0))
            {
                sb.Append(platform);
            }
            else
            {
                var isFirstVersion = true;

                foreach (var (version, isSupported) in versions)
                {
                    if (isFirstVersion)
                    {
                        isFirstVersion = false;
                        sb.Append(platform);
                        sb.Append(" (");
                    }
                    else
                    {
                        sb.Append(", ");
                    }

                    sb.Append(version);
                }

                sb.Append(')');
            }
        }

        sb.Append('.');

        return sb.ToString();
    }
}
