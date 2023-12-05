using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FAFileUpload
{
    internal static class LoggingConstants
    {
        internal const string FileInfoTemplate = "{EventId}, {CorrelationId}, {FileName}, {FileSize}";
        internal const string LoggingTemplate = "{EventId}, {CorrelationId}, {Description}";

        internal enum EventId
        {
            UploadBegin = 1000,
            UploadSuccess = 1001,
            UploadFailure = 1002,
            UploadEnd = 1003,
            UploadEndNoFiles = 1004,
            UploadInfo = 1005,
            HandleScanResultBegin = 2000,
            HandleScanResultTrusted = 2001,
            HandleScanResultMalicious = 2002,
            HandleScanResultEnd = 2003,
        }
    }
}
