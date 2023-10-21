using System;

namespace W3C.Contracts.Admin.CloudStorage
{
    public class CloudFile
    {
        public string Name { get; set; }
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
    }

    public class UploadFileRequest
    {
        public string Name { get; set; }
        public string Content { get; set; }
    }
}
