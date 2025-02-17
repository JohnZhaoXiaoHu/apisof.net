﻿using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace Terrajobst.ApiCatalog;

public sealed partial class ApiCatalogModel
{
    private static IReadOnlyList<byte> MagicHeader { get; } = Encoding.ASCII.GetBytes("APICATFB");
    private const int FormatVersion = 2;

    private readonly int _sizeOnDisk;
    private readonly byte[] _buffer;
    private readonly int _stringTableLength;
    private readonly int _frameworkTableOffset;
    private readonly int _frameworkTableLength;
    private readonly int _packageTableOffset;
    private readonly int _packageTableLength;
    private readonly int _assemblyTableOffset;
    private readonly int _assemblyTableLength;
    private readonly int _usageSourcesTableOffset;
    private readonly int _usageSourcesTableLength;
    private readonly int _apiTableOffset;
    private readonly int _apiTableLength;

    private Dictionary<int, int> _forwardedApis;

    private ApiCatalogModel(int sizeOnDisk, byte[] buffer, int[] tableSizes)
    {
        Debug.Assert(tableSizes.Length == 6);

        _stringTableLength = tableSizes[0];

        _frameworkTableOffset = _stringTableLength;
        _frameworkTableLength = tableSizes[1];

        _packageTableOffset = _frameworkTableOffset + _frameworkTableLength;
        _packageTableLength = tableSizes[2];

        _assemblyTableOffset = _packageTableOffset + _packageTableLength;
        _assemblyTableLength = tableSizes[3];

        _usageSourcesTableOffset = _assemblyTableOffset + _assemblyTableLength;
        _usageSourcesTableLength = tableSizes[4];

        _apiTableOffset = _usageSourcesTableOffset + _usageSourcesTableLength;
        _apiTableLength = tableSizes[5];

        _buffer = buffer;
        _sizeOnDisk = sizeOnDisk;
    }

    internal ReadOnlySpan<byte> StringTable => new(_buffer, 0, _stringTableLength);

    internal ReadOnlySpan<byte> FrameworkTable => new(_buffer, _frameworkTableOffset, _frameworkTableLength);

    internal ReadOnlySpan<byte> PackageTable => new(_buffer, _packageTableOffset, _packageTableLength);

    internal ReadOnlySpan<byte> AssemblyTable => new(_buffer, _assemblyTableOffset, _assemblyTableLength);

    internal ReadOnlySpan<byte> UsageSourcesTable => new(_buffer, _usageSourcesTableOffset, _usageSourcesTableLength);

    internal ReadOnlySpan<byte> ApiTable => new(_buffer, _apiTableOffset, _apiTableLength);

    public IEnumerable<FrameworkModel> Frameworks
    {
        get
        {
            var count = GetFrameworkTableInt32(0);

            for (var i = 0; i < count; i++)
            {
                var offset = GetFrameworkTableInt32(4 + 4 * i);
                yield return new FrameworkModel(this, offset);
            }
        }
    }

    public IEnumerable<PackageModel> Packages
    {
        get
        {
            var count = GetPackageTableInt32(0);

            for (var i = 0; i < count; i++)
            {
                var offset = GetPackageTableInt32(4 + 4 * i);
                yield return new PackageModel(this, offset);
            }
        }
    }

    public IEnumerable<AssemblyModel> Assemblies
    {
        get
        {
            var count = GetAssemblyTableInt32(0);

            for (var i = 0; i < count; i++)
            {
                var offset = GetAssemblyTableInt32(4 + 4 * i);
                yield return new AssemblyModel(this, offset);
            }
        }
    }

    public IEnumerable<UsageSourceModel> UsageSources
    {
        get
        {
            var count = GetUsageSourcesTableInt32(0);
            var offset = 4;

            for (var i = 0; i < count; i++, offset += 8)
                yield return new UsageSourceModel(this, offset);
        }
    }

    public IEnumerable<ApiModel> RootApis => GetApis(0);

    public IEnumerable<ApiModel> GetAllApis()
    {
        return RootApis.SelectMany(r => r.DescendantsAndSelf());
    }

    public ApiModel GetApiById(int id)
    {
        return new ApiModel(this, id);
    }

    internal IEnumerable<ApiModel> GetApis(int offset)
    {
        var childCount = GetApiTableInt32(offset);

        for (var i = 0; i < childCount; i++)
        {
            var childOffset = GetApiTableInt32(offset + 4 + 4 * i);
            yield return new ApiModel(this, childOffset);
        }
    }

    internal string GetString(int offset)
    {
        var stringSpan = StringTable.Slice(offset);
        var nameLength = BinaryPrimitives.ReadInt32LittleEndian(stringSpan);
        var nameSpan = stringSpan.Slice(4, nameLength);
        var name = Encoding.UTF8.GetString(nameSpan);
        return name;
    }

    internal Markup GetMarkup(int offset)
    {
        var span = StringTable.Slice(offset);
        var partsCount = BinaryPrimitives.ReadInt32LittleEndian(span);
        span = span.Slice(4);

        var parts = new List<MarkupPart>(partsCount);

        for (var i = 0; i < partsCount; i++)
        {
            var kind = (MarkupPartKind)span[0];
            span = span.Slice(1);
            var textOffset = BinaryPrimitives.ReadInt32LittleEndian(span);
            var text = GetString(textOffset);
            span = span.Slice(4);

            Guid? reference;

            if (kind == MarkupPartKind.Reference)
            {
                var apiOffset = BinaryPrimitives.ReadInt32LittleEndian(span);
                if (apiOffset < 0)
                    reference = null;
                else
                    reference = new ApiModel(this, apiOffset).Guid;
                span = span.Slice(4);
            }
            else
            {
                reference = null;
            }

            var part = new MarkupPart(kind, text, reference);
            parts.Add(part);
        }

        return new Markup(parts);
    }

    internal int GetFrameworkTableInt32(int offset)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(FrameworkTable.Slice(offset));
    }

    internal int GetPackageTableInt32(int offset)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(PackageTable.Slice(offset));
    }

    internal int GetAssemblyTableInt32(int offset)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(AssemblyTable.Slice(offset));
    }

    internal int GetUsageSourcesTableInt32(int offset)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(UsageSourcesTable.Slice(offset));
    }

    internal int GetApiTableInt32(int offset)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(ApiTable.Slice(offset));
    }

    internal float GetApiTableSingle(int offset)
    {
        return BinaryPrimitives.ReadSingleLittleEndian(ApiTable.Slice(offset));
    }

    public ApiCatalogStatistics GetStatistics()
    {
        var allApis = RootApis.SelectMany(a => a.DescendantsAndSelf());
        return new ApiCatalogStatistics(
            sizeOnDisk: _sizeOnDisk,
            sizeInMemory: _buffer.Length,
            numberOfApis: allApis.Count(),
            numberOfDeclarations: allApis.SelectMany(a => a.Declarations).Count(),
            numberOfAssemblies: Assemblies.Count(),
            numberOfFrameworks: Frameworks.Count(),
            numberOfFrameworkAssemblies: Assemblies.SelectMany(a => a.Frameworks).Count(),
            numberOfPackages: Packages.Select(p => p.Name).Distinct().Count(),
            numberOfPackageVersions: Packages.Count(),
            numberOfPackageAssemblies: Assemblies.SelectMany(a => a.Packages).Count()
        );
    }

    public ApiModel? GetForwardedApi(ApiModel api)
    {
        if (_forwardedApis is null)
        {
            var forwardedApis = ComputeForwardedApis();
            Interlocked.CompareExchange(ref _forwardedApis, forwardedApis, null);
        }

        if (_forwardedApis.TryGetValue(api.Id, out var forwardedId))
            return GetApiById(forwardedId);

        return null;
    }

    private Dictionary<int, int> ComputeForwardedApis()
    {
        var result = new Dictionary<int, int>();
        ForwardTypeInfoApis(result, this);
        return result;

        static void ForwardTypeInfoApis(Dictionary<int, int> receiver, ApiCatalogModel catalog)
        {
            var typeFullName = "System.Type";
            var typeInfoFullName = "System.Reflection.TypeInfo";

            var typeApi = catalog.GetAllApis().Single(a => a.GetFullName() == typeFullName);
            var typeInfoApi = catalog.GetAllApis().Single(a => a.GetFullName() == typeInfoFullName);

            var memberByRelativeName = typeApi.Descendants()
                                              .Select(a => (Name: a.GetFullName()[(typeFullName.Length + 1)..], Api: a))
                                              .ToDictionary(t => t.Name, t => t.Api);

            var typeInfoMembers = typeInfoApi.Descendants()
                                             .Select(a => (Name: a.GetFullName()[(typeInfoFullName.Length + 1)..], Api: a));

            foreach (var (name, typeInfoMember) in typeInfoMembers)
            {
                if (memberByRelativeName.TryGetValue(name, out var typeMember))
                    receiver.TryAdd(typeInfoMember.Id, typeMember.Id);
            }
        }
    }

    public static ApiCatalogModel Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    public static ApiCatalogModel Load(Stream stream)
    {
        var start = stream.Position;

        using (var reader = new BinaryReader(stream))
        {
            var magicHeader = reader.ReadBytes(8);
            if (!magicHeader.SequenceEqual(MagicHeader))
                throw new InvalidDataException();

            var formatVersion = reader.ReadInt32();
            if (formatVersion != FormatVersion)
                throw new InvalidDataException();

            var numberOfTables = reader.ReadInt32();
            var tableSizes = new int[numberOfTables];
            for (var i = 0; i < tableSizes.Length; i++)
                tableSizes[i] = reader.ReadInt32();

            var bufferSize = tableSizes.Sum();

            using (var decompressedStream = new DeflateStream(stream, CompressionMode.Decompress))
            using (var decompressedReader = new BinaryReader(decompressedStream))
            {
                var buffer = decompressedReader.ReadBytes(bufferSize);
                var end = stream.Position;
                var sizeOnDisk = (int)(end - start);
                return new ApiCatalogModel(sizeOnDisk, buffer, tableSizes);
            }
        }
    }

    public static async Task ConvertAsync(string sqliteDbPath, string outputPath)
    {
        using (var stream = new MemoryStream())
        {
            await ConvertAsync(sqliteDbPath, stream);

            stream.Position = 0;

            using (var fileStream = File.Create(outputPath))
                await stream.CopyToAsync(fileStream);
        }
    }

    public static Task ConvertAsync(string sqliteDbPath, Stream stream)
    {
        return Converter.ConvertAsync(sqliteDbPath, stream);
    }
}