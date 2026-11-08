using System.Text;

namespace GrimoireCli.Api;

/// <summary>
/// A multipart/form-data body whose file part is read from disk as it is sent,
/// so a library-sized file never has to fit in memory. Kiota's MultipartBody
/// serializes the whole body into a MemoryStream, which caps it at 2 GiB.
/// The part headers are byte-for-byte what MultipartBody writes, so the
/// server sees the same request. Seekable, because the token-refresh retry
/// rewinds the body and sends it again.
/// </summary>
public sealed class StreamedMultipartBody : Stream
{
    private readonly Stream[] _segments;
    private long _position;

    public string Boundary { get; }

    public string ContentType => $"multipart/form-data; boundary={Boundary}";

    /// <summary>
    /// The file goes in a part named "file", the name every Grimoire upload
    /// route binds. Takes ownership of <paramref name="file"/>.
    /// </summary>
    public StreamedMultipartBody(IEnumerable<(string Name, string Value)> fields, string fileName, string fileContentType, Stream file)
    {
        Boundary = Guid.NewGuid().ToString("N");
        var head = new StringBuilder();
        foreach (var (name, value) in fields)
            head.Append($"--{Boundary}\r\nContent-Type: text/plain\r\nContent-Disposition: form-data; name=\"{name}\"\r\n\r\n{value}\r\n");
        head.Append($"--{Boundary}\r\nContent-Type: {fileContentType}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{fileName}\"\r\n\r\n");
        _segments =
        [
            new MemoryStream(Encoding.UTF8.GetBytes(head.ToString()), writable: false),
            file,
            new MemoryStream(Encoding.UTF8.GetBytes($"\r\n--{Boundary}--\r\n"), writable: false),
        ];
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _segments.Sum(s => s.Length);

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var (segment, local) = Locate(_position);
        while (segment < _segments.Length)
        {
            _segments[segment].Position = local;
            var read = _segments[segment].Read(buffer);
            if (read > 0)
            {
                _position += read;
                return read;
            }
            segment++;
            local = 0;
        }
        return 0;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var (segment, local) = Locate(_position);
        while (segment < _segments.Length)
        {
            _segments[segment].Position = local;
            var read = await _segments[segment].ReadAsync(buffer, cancellationToken);
            if (read > 0)
            {
                _position += read;
                return read;
            }
            segment++;
            local = 0;
        }
        return 0;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => Length + offset,
        };
        if (target < 0) throw new IOException("Cannot seek before the start of the body.");
        return _position = target;
    }

    /// <summary>The segment holding <paramref name="position"/>, and the offset within it.</summary>
    private (int Segment, long Local) Locate(long position)
    {
        for (var i = 0; i < _segments.Length; i++)
        {
            if (position < _segments[i].Length) return (i, position);
            position -= _segments[i].Length;
        }
        return (_segments.Length, 0);
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            foreach (var segment in _segments)
                segment.Dispose();
        base.Dispose(disposing);
    }
}
