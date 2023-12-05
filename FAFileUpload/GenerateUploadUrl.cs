using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.WindowsAzure.Storage.Blob;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.Collections.Generic;
using Azure.Storage.Sas;

namespace FAFileUpload
{
    public static class GenerateUploadUrl
    {
        [FunctionName("GenerateUploadUrl")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req,
            ILogger log)
        {
            var storageAccountConnectionString = Environment.GetEnvironmentVariable("StorageAccountConnectionString");
            var untrustedContainerName = Environment.GetEnvironmentVariable("UntrustedContainerName");

            var guid = Guid.NewGuid().ToString("N").ToUpper();
            var timestamp = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds();
            var blobName = $"testfolder/{timestamp}-{guid}";

            var untrustedBlobClient = new BlobClient(
                storageAccountConnectionString,
                untrustedContainerName,
                blobName);

            var blobSasBuilder = new BlobSasBuilder();
            blobSasBuilder.BlobName = blobName;
            blobSasBuilder.BlobContainerName = untrustedContainerName;
            blobSasBuilder.StartsOn = DateTime.UtcNow.AddMinutes(-5);
            blobSasBuilder.ExpiresOn = DateTime.UtcNow.AddMinutes(15);
            blobSasBuilder.SetPermissions(BlobSasPermissions.Write);
            blobSasBuilder.Resource = "b";

            var sasUri = untrustedBlobClient.GenerateSasUri(blobSasBuilder);

            return new OkObjectResult(sasUri);
        }
    }
}
