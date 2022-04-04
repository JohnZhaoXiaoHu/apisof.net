using System;
using System.Buffers.Binary;

namespace Terrajobst.ApiCatalog;

public struct ObsoletionModel : IEquatable<ObsoletionModel>
{
    private readonly ApiCatalogModel _catalog;
    private readonly int _offset;

    internal ObsoletionModel(ApiCatalogModel catalog, int offset)
    {
        _catalog = catalog;
        _offset = offset;
    }

    public string Message
    {
        get
        {
            var span = _catalog.ObsoletionTable.Slice(_offset + 8);
            var stringOffset = BinaryPrimitives.ReadInt32LittleEndian(span);
            return _catalog.GetString(stringOffset);
        }
    }

    public bool IsError
    {
        get
        {
            var span = _catalog.ObsoletionTable.Slice(_offset + 12);
            return span[0] == 1;
        }
    }

    public string DiagnosticId
    {
        get
        {
            var span = _catalog.ObsoletionTable.Slice(_offset + 13);
            var stringOffset = BinaryPrimitives.ReadInt32LittleEndian(span);
            return _catalog.GetString(stringOffset);
        }
    }

    public string UrlFormat
    {
        get
        {
            var span = _catalog.ObsoletionTable.Slice(_offset + 17);
            var stringOffset = BinaryPrimitives.ReadInt32LittleEndian(span);
            return _catalog.GetString(stringOffset);
        }
    }

    public string Url
    {
        get
        {
            return UrlFormat.Length > 0 && DiagnosticId.Length > 0
                        ? string.Format(UrlFormat, DiagnosticId)
                        : UrlFormat;
        }
    }

    public override bool Equals(object obj)
    {
        return obj is ObsoletionModel model && Equals(model);
    }

    public bool Equals(ObsoletionModel other)
    {
        return ReferenceEquals(_catalog, other._catalog) &&
               _offset == other._offset;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_catalog, _offset);
    }

    public static bool operator ==(ObsoletionModel left, ObsoletionModel right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ObsoletionModel left, ObsoletionModel right)
    {
        return !(left == right);
    }
}