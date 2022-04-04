using System;
using System.Buffers.Binary;

namespace Terrajobst.ApiCatalog;

public struct PlatformSupportModel : IEquatable<PlatformSupportModel>
{
    private readonly ApiCatalogModel _catalog;
    private readonly int _offset;

    internal PlatformSupportModel(ApiCatalogModel catalog, int offset)
    {
        _catalog = catalog;
        _offset = offset;
    }

    public string PlatformName
    {
        get
        {
            var span = _catalog.PlatformSupportTable.Slice(_offset + 8);
            var stringOffset = BinaryPrimitives.ReadInt32LittleEndian(span);
            return _catalog.GetString(stringOffset);
        }
    }

    public bool IsSupported
    {
        get
        {
            var span = _catalog.PlatformSupportTable.Slice(_offset + 12);
            return span[0] == 1;
        }
    }

    public override bool Equals(object obj)
    {
        return obj is ApiUsageModel model && Equals(model);
    }

    public bool Equals(PlatformSupportModel other)
    {
        return ReferenceEquals(_catalog, other._catalog) &&
               _offset == other._offset;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_catalog, _offset);
    }

    public static bool operator ==(PlatformSupportModel left, PlatformSupportModel right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(PlatformSupportModel left, PlatformSupportModel right)
    {
        return !(left == right);
    }
}