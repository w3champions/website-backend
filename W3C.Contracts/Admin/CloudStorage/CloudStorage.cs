using System;

namespace W3C.Contracts.Admin.CloudStorage;

public class CloudFile
{
    public string Name { get; set; }
    public long Size { get; set; }
    public long? SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    public string PreviewPath { get; set; }
}

public class UploadFileRequest
{
    public string Name { get; set; }
    public string Content { get; set; }
    public string ContentType { get; set; }

    public string ImageContentType() => ContentType switch
    {
        "image/jpeg" or "image/png" or "image/webp" or "image/gif" => ContentType,
        _ => "application/octet-stream"
    };
}
