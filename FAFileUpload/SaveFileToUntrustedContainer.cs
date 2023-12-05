using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs;
using Azure;
using Azure.Storage.Blobs.Models;
using System.Collections.Generic;

namespace FAFileUpload
{
    public static class SaveFileToUntrustedContainer
    {
        [FunctionName("SaveFileToUntrustedContainer")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            var correlationId = Guid.NewGuid().ToString("N").ToUpper();

            if (req.Form.Files.Count == 0)
            {
                log.LogInformation(
                    new EventId((int)LoggingConstants.EventId.UploadEndNoFiles),
                    LoggingConstants.LoggingTemplate,
                    LoggingConstants.EventId.UploadEndNoFiles.ToString(),
                    correlationId,
                    "No files found to save");
                
                return new OkResult();
            }

            var file = req.Form.Files[0];

            log.LogInformation(
                new EventId((int)LoggingConstants.EventId.UploadBegin),
                LoggingConstants.LoggingTemplate,
                LoggingConstants.EventId.UploadBegin.ToString(),
                correlationId, file.FileName);

            var uploaded = await UploadFileToUntrustedContainer(file, correlationId, log);

            log.LogInformation(
                new EventId((int)LoggingConstants.EventId.UploadEnd),
                LoggingConstants.LoggingTemplate,
                LoggingConstants.EventId.UploadEnd.ToString(),
                correlationId, file.FileName);

            return uploaded
                ? new OkResult()
                : new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }

        private static async Task<bool> UploadFileToUntrustedContainer(IFormFile file, string correlationId, ILogger log)
        {
            try
            {
                var storageAccountConnectionString = Environment.GetEnvironmentVariable("StorageAccountConnectionString");
                var UntrustedContainerName = Environment.GetEnvironmentVariable("UntrustedContainerName");

                var blobContainerClient = new BlobContainerClient(storageAccountConnectionString, UntrustedContainerName);

                var timestamp = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds();
                var blobName = $"pocplayback/{timestamp}-{correlationId}";

                var blobUploadOptions = new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = file.ContentType },
                    Metadata = new Dictionary<string, string>()
                    {
                        { "correlationId", correlationId },
                        { "originalFileName", file.FileName },
                        { "contentType", file.ContentType },
                        { "contentDisposition", file.ContentDisposition }
                    }
                };

                using var fileStream = file.OpenReadStream();

                var fileSize = $"{fileStream.Length} bytes";
                log.LogInformation(
                    new EventId((int)LoggingConstants.EventId.UploadInfo),
                    LoggingConstants.FileInfoTemplate,
                    LoggingConstants.EventId.UploadInfo.ToString(),
                    correlationId,
                    file.FileName,
                    fileSize);

                var blobClient = blobContainerClient.GetBlobClient(blobName);
                var result = await blobClient.UploadAsync(fileStream, blobUploadOptions);

                log.LogInformation(
                    new EventId((int)LoggingConstants.EventId.UploadSuccess),
                    LoggingConstants.LoggingTemplate,
                    LoggingConstants.EventId.UploadSuccess.ToString(),
                    correlationId, file.FileName);

                return true;
            }
            catch (RequestFailedException ex)
            {
                log.LogError(ex, "Failed to upload file to storage: request failed");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to upload file to storage: an unexpected error occurred");
            }

            log.LogInformation(
                new EventId((int)LoggingConstants.EventId.UploadFailure),
                LoggingConstants.LoggingTemplate,
                LoggingConstants.EventId.UploadFailure.ToString(),
                correlationId, file.FileName);

            return false;
        }
    }
}
