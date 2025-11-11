using Confluent.Kafka;
using Microsoft.IO;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Sinks.PeriodicBatching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Serilog.Sinks.Kafka
{
    public class KafkaSink : IBatchedLogEventSink, IDisposable
    {
        private const int FlushTimeoutSecs = 10;
        const string SKIP_KEY = "skip-kafka";

        private readonly TopicPartition _globalTopicPartition;
        private readonly ITextFormatter _formatter;
        private readonly Action<IProducer<string, byte[]>, Error> _errorHandler;
        private readonly Action<DeliveryReport<string, byte[]>> _deliveryHandler;
        private readonly ProducerConfig _producerConfig;
        private readonly IProducer<string, byte[]> _producer;
        private readonly string _messageKey;

        private static readonly RecyclableMemoryStreamManager _streamManager = new RecyclableMemoryStreamManager();

        public KafkaSink(
            ProducerConfig producerConfig,
             string topic = null,
             ITextFormatter formatter = null, 
             string messageKey = null, 
             Action<IProducer<string, byte[]>, Error> errorHandler = null)
        {
            Console.WriteLine($"[Kafka] new topic={topic}");

            _formatter = formatter ?? new Formatting.Json.JsonFormatter(renderMessage: true);

            if (topic != null)
                _globalTopicPartition = new TopicPartition(topic, Partition.Any);

            if (_errorHandler != null)
                _errorHandler = errorHandler;
            else
            {
                _errorHandler = (pro, msg) =>
                {
                    Log.ForContext(SKIP_KEY, string.Empty).Error($"[Kafka] Error {pro.Name} {msg.Code} {msg.Reason}");
                };
            }

            if (producerConfig.EnableDeliveryReports.GetValueOrDefault())
                _deliveryHandler = HandleDeliveryReport;
            else
                _deliveryHandler = null;

            _messageKey = messageKey;
            _producerConfig = producerConfig;
            _producer = ConfigureKafkaConnection();

            Console.WriteLine($"[Kafka] Producer OK");
        }

        public Task OnEmptyBatchAsync() => Task.CompletedTask;

        public Task EmitBatchAsync(IEnumerable<LogEvent> batch)
        {
            try
            {
                foreach (var logEvent in batch)
                {
                    if (logEvent.Properties.ContainsKey(SKIP_KEY))
                        continue;

                    string key = null;
                    if (!string.IsNullOrEmpty(_messageKey) && logEvent.Properties.TryGetValue(_messageKey, out var value))
                        key = value.ToString();


                    byte[] logBytes;

                    using (var stream = _streamManager.GetStream())
                    {
                        using (var writer = new StreamWriter(stream, Encoding.UTF8, 1024, true))
                        {
                            _formatter.Format(logEvent, writer);
                            writer.Flush();
                        }

                        logBytes = stream.ToArray();

                    }

                    var message = new Message<string, byte[]>
                    {
                        Key = key,
                        Value = logBytes
                    };

                    _producer.Produce(_globalTopicPartition, message, _deliveryHandler);
                }
            }
            catch (Exception ex)
            {
                Log.ForContext(SKIP_KEY, string.Empty).Error(ex, "[Kafka][EmitBatchAsync Error]");
                Log.ForContext(SKIP_KEY, string.Empty).Information($"[Kafka][batchInfo] {batch.First().RenderMessage()} ~ {batch.Last().RenderMessage()}");
            }

            return Task.CompletedTask;
        }

        private void HandleDeliveryReport(DeliveryReport<string, byte[]> report)
        {
            if (report.Error.IsError)
            {
                Log.ForContext(SKIP_KEY, string.Empty)
                   .Error($"[Kafka] Mesaj iletilemedi. Topic: {report.Topic}, Hata: {report.Error.Reason}");
            }
        }

        private IProducer<string, byte[]> ConfigureKafkaConnection()
        {
            return new ProducerBuilder<string, byte[]>(_producerConfig)
                    .SetErrorHandler(_errorHandler)
                    .SetLogHandler((pro, msg) =>
                    {
                        var level = msg.Level <= SyslogLevel.Error ? LogEventLevel.Error : LogEventLevel.Information;
                        Log.ForContext(SKIP_KEY, string.Empty).Write(level, $"[Kafka] Producer Log: {msg.Level} | {msg.Message}");
                    })
                    .Build();
        }

        public void Dispose()
        {
            Console.WriteLine("[Kafka] Disposing sink. Flushing producer...");
            try
            {
                var outstandingMessages = _producer.Flush(TimeSpan.FromSeconds(FlushTimeoutSecs));
                if (outstandingMessages > 0)
                {
                    Log.ForContext(SKIP_KEY, string.Empty)
                       .Warning($"[Kafka] {outstandingMessages} mesaj flush edilirken timeout oldu.");
                }
            }
            catch (Exception ex)
            {
                Log.ForContext(SKIP_KEY, string.Empty)
                       .Error(ex, $"[Kafka] Producer flush edilirken hata.");
            }
            finally
            {
                _producer.Dispose();
                Console.WriteLine("[Kafka] Sink disposed.");
            }
        }
    }
}