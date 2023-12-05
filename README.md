# Defender for Cloud PoC

## Prerequisites

Azure Resources:
- Function App
- Storage Account
    - Defender for Storage enabled with On-upload Malware scanning 
- Event Grid
- App Insights for monitoring

## Environment config

3 Function App environment variables need configured:
- `UntrustedContainerName` - the name of the storage container files are saved to
- `TrustedContainerName` - the name of the storage container clean files are moved to once scanned
- `StorageAccountConnectionString` - the connection string of the storage account containing the untrusted and trusted containers

## Azure Resource Configuration

Once your functions have been published, a custom topic needs to be configured in Defender for Cloud, as outlined [here](https://learn.microsoft.com/en-us/azure/defender-for-cloud/advanced-configurations-for-malware-scanning#setting-up-event-grid-for-malware-scanning).

## Monitoring

```kql
// Summarise each scan request, showing file type, size, name, and time taken
// 12/4/2023 10:20-10:34
traces
| project EventId = toint(customDimensions.EventId),
          CorrelationId = tostring(customDimensions.prop__CorrelationId),
          FileName = tostring(customDimensions.prop__FileName),
          FileSize = tostring(customDimensions.prop__FileSize),
          TimeStamp = timestamp
| where EventId >= 1000
| summarize TimeTaken = toint(datetime_diff('millisecond', max(TimeStamp), min(TimeStamp)))/1000, 
            FileSize = split(take_any(FileSize), " ")[0],
            FileName = take_any(FileName),
            FileType = split(take_any(FileName), ".")[-1]
            by CorrelationId
```

