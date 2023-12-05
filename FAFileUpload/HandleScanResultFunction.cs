using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Azure.Messaging.EventGrid;
using Azure.Identity;
using Azure.Storage.Blobs;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Storage.Blobs.Models;

namespace FAFileUpload
{
    public static class HandleScanResultFunction
    {
        private const string AntimalwareScanEventType = "Microsoft.Security.MalwareScanningResult";
       
        private const string MaliciousVerdict = "Malicious";
        private const string CleanVerdict = "No threats found";

        [FunctionName("HandleScanResultFunction")]
        public static async Task RunAsync([EventGridTrigger] EventGridEvent eventGridEvent, ILogger log)
        {
            // Filter events that are not a Malware Scanning Result
            if (eventGridEvent.EventType != AntimalwareScanEventType)
            {
                log.LogInformation("Event type is not a '{0}' event, event type: {1}", AntimalwareScanEventType, eventGridEvent.EventType);
                return;
            }

            var untrustedContainerName = Environment.GetEnvironmentVariable("UntrustedContainerName");
            var trustedContainerName = Environment.GetEnvironmentVariable("TrustedContainerName");

            var storageAccountName = eventGridEvent?.Subject?.Split("/")[^1];
            log.LogInformation("Received new scan result for storage {0}", storageAccountName);
            
            var eventData = JsonDocument.Parse(eventGridEvent.Data).RootElement;
            var verdict = eventData.GetProperty("scanResultType").GetString();
            var blobUriString = eventData.GetProperty("blobUri").GetString();
           
            var blobUri = new Uri(blobUriString);

            // Filter events from untrusted container
            var blobUriBuilder = new BlobUriBuilder(blobUri);
            if (blobUriBuilder.BlobContainerName != untrustedContainerName)
            {
                log.LogInformation("Event is not from the untrusted container, ignoring");
                return;
            }

            if (verdict == null || blobUriString == null)
            {
                log.LogError("Event data doesn't contain 'verdict' or 'blobUri' fields");
                throw new ArgumentException("Event data doesn't contain 'verdict' or 'blobUri' fields");
            }

            var correlationId = blobUriBuilder.BlobName.Split("-")[^1];

            log.LogInformation(
                new EventId((int)LoggingConstants.EventId.HandleScanResultBegin),
                LoggingConstants.LoggingTemplate,
                LoggingConstants.EventId.HandleScanResultBegin.ToString(),
                correlationId, "");

            if (verdict == MaliciousVerdict)
            {
                log.LogInformation("Blob {0} is malicious, deleting it from '{1}' container", blobUri, untrustedContainerName);
                try
                {
                    await DeleteMaliciousBlobAsync(blobUri, log);
                    log.LogInformation(
                        new EventId((int)LoggingConstants.EventId.HandleScanResultMalicious),
                        LoggingConstants.LoggingTemplate,
                        LoggingConstants.EventId.HandleScanResultMalicious.ToString(),
                        correlationId, "");
                }
                catch (Exception e)
                {
                    log.LogError(e, "Couldn't delete blob from '{0}' container", untrustedContainerName);
                    log.LogInformation(
                        new EventId((int)LoggingConstants.EventId.HandleScanResultEnd),
                        LoggingConstants.LoggingTemplate,
                        LoggingConstants.EventId.HandleScanResultEnd.ToString(),
                        correlationId, "Failed to delete blob from untrusted container");
                    throw;
                }
            }

            // If no threats found, move blob from untrusted to trusted container
            if (verdict == CleanVerdict)
            {
                log.LogInformation("blob {0} is not malicious, moving it to '{1}' container", blobUri, trustedContainerName);
                try
                {
                    await MoveCleanBlobAsync(blobUri, trustedContainerName, log);
                    log.LogInformation(
                        new EventId((int)LoggingConstants.EventId.HandleScanResultTrusted),
                        LoggingConstants.LoggingTemplate,
                        LoggingConstants.EventId.HandleScanResultTrusted.ToString(),
                        correlationId, "");
                }
                catch (Exception e)
                {
                    log.LogError(e, "Can't move blob to '{0}' container from '{1}' container", trustedContainerName, untrustedContainerName);

                    log.LogInformation(
                        new EventId((int)LoggingConstants.EventId.HandleScanResultEnd),
                        LoggingConstants.LoggingTemplate,
                        LoggingConstants.EventId.HandleScanResultEnd.ToString(),
                        correlationId, "Failed to move blob to trusted container");
                    throw;
                }
            }

        }

        private static async Task DeleteMaliciousBlobAsync(Uri maliciousBlobUri, ILogger log)
        {
            var storageAccountConnectionString = Environment.GetEnvironmentVariable("StorageAccountConnectionString");

            var untrustedBlobUriBuilder = new BlobUriBuilder(maliciousBlobUri);
            var untrustedBlobClient = new BlobClient(
                storageAccountConnectionString,
                untrustedBlobUriBuilder.BlobContainerName,
                untrustedBlobUriBuilder.BlobName);

            // Delete blob from untrusted container
            log.LogInformation("DeleteBlob: Deleting source blob {0}", maliciousBlobUri);

            await untrustedBlobClient.DeleteAsync();

            log.LogInformation("DeleteBlob: Blob deleted successfully");
        }

        private static async Task MoveCleanBlobAsync(Uri cleanBlobUri, string trustedContainerName, ILogger log)
        {
            // Check the blob isn't already in the trusted container
            var trustedBlobUriBuilder = new BlobUriBuilder(cleanBlobUri);
            if (trustedBlobUriBuilder.BlobContainerName == trustedContainerName)
            {
                log.LogInformation("MoveBlob: Blob {0} is already in {1} container, skipping", trustedBlobUriBuilder.BlobName, trustedContainerName);
                return;
            }

            var storageAccountConnectionString = Environment.GetEnvironmentVariable("StorageAccountConnectionString");

            // Ensure the blob to copy exists
            var untrustedBlobClient = new BlobClient(
                storageAccountConnectionString,
                trustedBlobUriBuilder.BlobContainerName,
                trustedBlobUriBuilder.BlobName);

            if (!await untrustedBlobClient.ExistsAsync())
            {
                log.LogError("MoveBlob: Blob {0} doesn't exist", cleanBlobUri);
                return;
            }

            trustedBlobUriBuilder.BlobContainerName = trustedContainerName;

            var trustedBlobClient = new BlobClient(
                storageAccountConnectionString,
                trustedBlobUriBuilder.BlobContainerName,
                trustedBlobUriBuilder.BlobName);

            // Copy blob from untruted to trusted container
            log.LogInformation("MoveBlob: Copying blob to {0}", trustedBlobClient.Uri);
            var copyFromUriOperation = await trustedBlobClient.StartCopyFromUriAsync(untrustedBlobClient.Uri);
            await copyFromUriOperation.WaitForCompletionAsync();
            
            // Delete blob from untrusted container
            log.LogInformation("MoveBlob: Deleting source blob {0}", untrustedBlobClient.Uri);
            var blobProperties = await untrustedBlobClient.GetPropertiesAsync();
            await untrustedBlobClient.DeleteAsync();

            var createdTime = blobProperties.Value.CreatedOn;
            var timeSinceCreated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - createdTime.ToUnixTimeMilliseconds();
            var filesize = blobProperties.Value.ContentLength;
            log.LogInformation($"MoveBlob: Blob moved successfully. Time taken: {timeSinceCreated}ms. BlobSize: {filesize} bytes");
        }
    }
}
