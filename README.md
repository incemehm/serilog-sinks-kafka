# serilog-sinks-nextkafka

[![Nuget](https://img.shields.io/nuget/v/Serilog.Sinks.NextKafka)](https://www.nuget.org/packages/Serilog.Sinks.NextKafka/) [![NuGet Downloads](https://img.shields.io/nuget/dt/Serilog.Sinks.NextKafka.svg)](https://www.nuget.org/packages/Serilog.Sinks.NextKafka/)
 
A Serilog sink that writes events to Kafka Endpoints (Including Azure Event Hubs).

## Dependencies

This sink works with the following packages

* Serilog >v2.10.0
* Serilog.Sinks.PeriodicBatching >v2.3.0
* Confluent.Kafka >v2.3.0

## Usage

```
Log.Logger = new LoggerConfiguration()
    .WriteTo.Kafka()
    .CreateLogger();
```

### Parameters
* **batchSizeLimit** - Maximum number of logs to batch. Defaults to 50
* **period** - The period in seconds to send batches of logs. Defaults to 5 seconds
* **bootstrapServers** - Comma separated list of Kafka Bootstrap Servers. Defaults to "localhost:9092"
* **topic** - Name of the Kafka topic. Defaults to "logs"
* **enableDeliveryReports** - (Optional) EnableDeliveryReports setting for Producer, defaults to false
* **enableIdempotence** - (Optional) EnableIdempotence setting for Producer, defaults to false
* **acks** - (Optional) Acks setting for Producer, defaults to Acks.None
* **lingerMs** - (Optional) LingerMs setting for Producer, defaults to 5
* **batchSize** - (Optional) BatchSize setting for Producer, defaults to 1000000
* **messageSendMaxRetries** - (Optional) MessageSendMaxRetries setting for Producer, defaults to 2147483647
* **messageTimeoutMs** - (Optional) MessageTimeoutMs setting for Producer, defaults to 300000
* **retryBackoffMs** - (Optional) RetryBackoffMs setting for Producer, defaults to 100
* **securityProtocol** - (Optional) SecurityProtocol.Plaintext
* **saslMechanism** - The SASL Mecahnism. Defaults to SaslMechanism.Plain
* **saslUsername** - (Optional) Username for SASL. This is required for Azure Event Hubs and should be set to `$ConnectionString`
* **saslPassword** - (Optional) Password for SASL. This is required for Azure Event Hubs and is your entire Connection String.
* **sslCaLocation** - (Optional) Location of the SSL CA Certificates This is required for Azure Event Hubs and should be set to `./cacert.pem` as this package includes the Azure carcert.pem file which is copied into your binary output directory.
* **formatter** - optional `ITextFormatter` you can specify to format log entries.  Defaults to the standard `JsonFormatter` with `renderMessage` set to `true`.


## Configuration for a local Kafka instance using appsettings
```
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Kafka",
        "Args": {
          "batchSizeLimit": "50",
          "period": "5",
          "bootstrapServers": "localhost:9092",
          "topic": "logs",
          "messageTimeoutMs": "2000",
          "acks": "-1",
          "lingerMs": "0",
          "batchSize": "1"
        }
      }
    ]
  }
}

```

## Configuration with a custom formatter

```csharp
Log.Logger = new LoggerConfiguration()
              .WriteTo.Kafka("logs", "localhost:9092",
                    formatter: new CustomElasticsearchFormatter("LogEntry"));
              .CreateLogger();
```


The above code references a `CustomElasticSearchFormatter` that uses the whole `LogEntry` as the input to the formatter.  This is a custom formatter that inherits from `ElasticsearchJsonFormatter` in the `Serilog.Sinks.Elasticsearch` NuGet package, but can be any `ITextFormatter` that you want to use when sending the log entry to Kafka.  Note that if you omit the formatter param (which is fine), the standard `JsonFormatter` will be used (with the `renderMessage` parameter set to `true`).


## Configuration for Azure Event Hubs instance

You will need to ensure you have a copy of the Azure CA Certificates and define the location of this cert in the `sslCaLocation`.

You can download a copy of the Azure CA Certificate [here](./certs/cacert.pem).

Place this in you projects root directory and ensure it is copied to the build output in your csproj.

```
  <ItemGroup>
    <None Include="cacert.pem">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

### Configuration for Azure Event Hubs.
```
Log.Logger = new LoggerConfiguration()
    .WriteTo.Kafka(
      batchSizeLimit: 50,
      period: 5,
      bootstrapServers: "my-event-hub-instance.servicebus.windows.net:9093",
      saslUsername: "$ConnectionString",
      saslPassword: "my-event-hub-instance-connection-string",
      topic: "logs",
      sslCaLocation: "./cacert.pem",
      saslMechanism: SaslMechanism.Plain,
      securityProtocol: SecurityProtocol.SaslSsl)
    .CreateLogger();
```

### Or using appsettings...
```
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Kafka",
        "Args": {
          "batchSizeLimit": "50",
          "period": "5",
          "bootstrapServers": "my-event-hub-instance.servicebus.windows.net:9093",
          "saslUsername": "$ConnectionString",
          "saslPassword": "my-event-hub-instance-connection-string",
          "topic": "logs",
          "sslCaLocation": "./cacert.pem",
          "saslMechanism": "Plain",
          "securityProtocol": "SaslSsl"
        }
      }
    ]
  }
}

```

## Extra Configuration using Environment Variables

You can also specify `ProducerConfig` configuration using EnvironmentVariables.
These settings can be specified as the following EnvironmentVariables...

`SERILOG__KAFKA__ProducerConfigPropertyName`

or

`SERILOG__KAFKA__PRODUCER_CONFIG_PROPERTY_NAME`.

`SERILOG__KAFKA__` is first stripped from the Environment Variable Name and the remaining name is lowered and single `_` is replaced with `string.Empty`.

The `ProducerConfig` is first loaded from any specified Environment Variables. Then any of the configuration passed into the KafkaSink constructor will override the Environment Variables.
This is to ensure backwards compatability at the moment but passing this configuration into the KafkaSink constructor will be removed in the future.

You can check what properties are supported at the following github https://github.com/confluentinc/confluent-kafka-dotnet/blob/6128bdf65fa79fbb14210d73970fbd4f7940d4b7/src/Confluent.Kafka/Config_gen.cs#L830


## Azure EventHubs Recommended Configuration
If you are running against an Azure EventHub, the following configuration is recommended. 

https://github.com/Azure/azure-event-hubs-for-kafka/blob/master/CONFIGURATION.md

These EnvironmentVariables can be set...

```
SERILOG__KAFKA__SocketKeepaliveEnable=true
SERILOG__KAFKA__MetadataMaxAgeMs=180000
SERILOG__KAFKA__RequestTimeoutMs=30000
SERILOG__KAFKA__Partitioner=ConsistentRandom
SERILOG__KAFKA__EnableIdempotence=false
SERILOG__KAFKA__CompressionType=None
```        
