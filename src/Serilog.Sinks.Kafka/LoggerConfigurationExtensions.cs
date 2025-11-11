using Confluent.Kafka;
using Serilog.Configuration;
using Serilog.Formatting;
using Serilog.Sinks.PeriodicBatching;
using System;
using System.Net;

namespace Serilog.Sinks.Kafka
{
    public static class LoggerConfigurationExtensions
    {
        /// <summary>
        /// Adds a sink that writes log events to a Kafka topic in the broker endpoints.
        /// </summary>
        /// <param name="loggerConfiguration">The logger configuration.</param>
        /// <param name="batchSizeLimit">The maximum number of events to include in a single batch.</param>
        /// <param name="period">The time in seconds to wait between checking for event batches.</param>
        /// <param name="bootstrapServers">The list of bootstrapServers separated by comma.</param>
        /// <param name="errorHandler">kafka errorHandler</param>
        /// <param name="topic">The topic name.</param>
        /// <returns></returns>
        public static LoggerConfiguration Kafka(
            this LoggerSinkConfiguration loggerConfiguration,
            int batchSizeLimit = 50,
            int period = 5,
            string bootstrapServers = "localhost:9092",
            bool enableDeliveryReports = true,
            bool enableIdempotence = false,
            Acks acks = Acks.None,
            double lingerMs = 5,
            int batchSize = 1000000,
            int messageSendMaxRetries = 2147483647,
            int messageTimeoutMs = 300000,
            int retryBackoffMs = 100,
            SecurityProtocol securityProtocol = SecurityProtocol.Plaintext,
            SaslMechanism? saslMechanism = null,
            string saslUsername = null,
            string saslPassword = null,
            string sslCaLocation = null,
            string topic = "logs",
            string messageKey = null,
            Action<IProducer<string, byte[]>, Error> errorHandler = null,
            ITextFormatter formatter = null)
        {
            return loggerConfiguration.KafkaInit(new ProducerConfig()
            {
                BootstrapServers = bootstrapServers,
                EnableDeliveryReports = enableDeliveryReports,
                EnableIdempotence = enableIdempotence,
                CompressionType = CompressionType.Gzip,
                Acks = acks,
                ClientId = Dns.GetHostName(),
                LingerMs = lingerMs,
                BatchSize = batchSize,
                MessageSendMaxRetries = messageSendMaxRetries,
                MessageTimeoutMs = messageTimeoutMs,
                RetryBackoffMs = retryBackoffMs,
                SecurityProtocol = securityProtocol,
                SaslMechanism = saslMechanism,
                SaslUsername = saslUsername,
                SaslPassword = saslPassword,
                SslCaLocation = sslCaLocation
            }, topic, batchSizeLimit, period, messageKey, errorHandler, formatter);
        }

        public static LoggerConfiguration Kafka(
            this LoggerSinkConfiguration loggerConfiguration,
            string topic,
            ProducerConfig producerConfig,
            int batchSizeLimit = 50,
            int period = 5,
            string messageKey = null,
            Action<IProducer<string, byte[]>, Error> errorHandler = null,
            ITextFormatter formatter = null)
        {
            return loggerConfiguration.KafkaInit(producerConfig, topic, batchSizeLimit, period, messageKey, errorHandler, formatter);
        }

        private static LoggerConfiguration KafkaInit(
           this LoggerSinkConfiguration loggerConfiguration,
           ProducerConfig producerConfig,
           string topic,
           int batchSizeLimit,
           int period,
           string messageKey,
           Action<IProducer<string, byte[]>, Error> errorHandler,
           ITextFormatter formatter)
        {
            var kafkaSink = new KafkaSink(
                producerConfig,
                topic,
                formatter, 
                messageKey, 
                errorHandler);

            var batchingOptions = new PeriodicBatchingSinkOptions
            {
                BatchSizeLimit = batchSizeLimit,
                Period = TimeSpan.FromSeconds(period)
            };

            var batchingSink = new PeriodicBatchingSink(
                kafkaSink,
                batchingOptions);

            return loggerConfiguration
                .Sink(batchingSink);
        }
    }
}
